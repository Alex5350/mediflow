namespace MediFlow.Worker;

using MediFlow.Domain.Claims;
using MediFlow.Domain.Claims.Adjudication;
using MediFlow.Infrastructure.Claims;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>Custom worker metrics surfaced through OpenTelemetry.</summary>
public static class AdjudicationMetrics
{
    public const string MeterName = "MediFlow.Worker";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ClaimsAdjudicated =
        Meter.CreateCounter<long>("mediflow.claims.adjudicated", unit: "{claim}", description: "Claims adjudicated");

    public static readonly Histogram<double> AdjudicationDuration =
        Meter.CreateHistogram<double>("mediflow.adjudication.duration", unit: "ms", description: "Adjudication engine duration per claim");

    public static readonly Counter<long> AdjudicationFailures =
        Meter.CreateCounter<long>("mediflow.adjudication.failures", unit: "{claim}", description: "Adjudication attempts that failed and were retried");
}

/// <summary>
/// The claims adjudication worker. Drains the outbox in batches: lease claims
/// atomically (SQL), run the rules engine, commit results transactionally with a
/// table-valued parameter, and on failure release the lease with exponential
/// backoff. Five failed attempts dead-letter a claim for operator review (ADR 0005).
/// </summary>
public sealed partial class AdjudicationWorker(
    IServiceProvider services,
    ILogger<AdjudicationWorker> logger) : BackgroundService
{
    private const int LeaseRejectedSqlError = 51002;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adjudicated {ClaimNumber} → {Status} (plan paid {PlanPaidCents}c, member owes {MemberOwesCents}c)")]
    private partial void LogAdjudicated(string claimNumber, ClaimStatus status, int planPaidCents, int memberOwesCents);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lease rejected for claim {ClaimId} — another worker owns it or it expired; leaving it queued")]
    private partial void LogLeaseRejected(int claimId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Adjudication failed for claim {ClaimId} — lease released, retry scheduled with backoff")]
    private partial void LogFailure(int claimId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adjudication worker starting (poll {PollSeconds}s, lease {LeaseSeconds}s)")]
    private static partial void LogStarting(ILogger logger, double pollSeconds, double leaseSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Adjudication batch failed — retrying next poll")]
    private static partial void LogBatchFailed(ILogger logger, Exception ex);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarting(logger, PollInterval.TotalSeconds, LeaseDuration.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessOneBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogBatchFailed(logger, ex);
            }

            await Task.Delay(processed > 0 ? PollInterval : IdlePollInterval, stoppingToken);
        }
    }

    private async Task<int> ProcessOneBatchAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IAdjudicationGateway>();
        var runner = scope.ServiceProvider.GetRequiredService<IClaimAdjudicationRunner>();
        var engine = scope.ServiceProvider.GetRequiredService<AdjudicationEngine>();

        var leaseToken = Guid.NewGuid();
        var claimIds = await gateway.LeaseNextClaimsAsync(batchSize: 10, LeaseDuration, leaseToken, ct);

        foreach (var claimId in claimIds)
        {
            await AdjudicateOneAsync(gateway, runner, engine, claimId, leaseToken, ct);
        }

        return claimIds.Count;
    }

    private async Task AdjudicateOneAsync(
        IAdjudicationGateway gateway,
        IClaimAdjudicationRunner runner,
        AdjudicationEngine engine,
        int claimId,
        Guid leaseToken,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var work = await runner.LoadForAdjudicationAsync(claimId, ct)
                ?? throw new InvalidOperationException($"Claim {claimId} could not be assembled for adjudication.");

            var result = engine.Adjudicate(work.Request);
            await gateway.CommitAdjudicationAsync(claimId, leaseToken, result, ct: ct);

            AdjudicationMetrics.ClaimsAdjudicated.Add(1, new KeyValuePair<string, object?>("status", result.Status.ToString()));
            AdjudicationMetrics.AdjudicationDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            LogAdjudicated(work.Claim.ClaimNumber, result.Status, result.TotalPlanPaidCents, result.TotalMemberOwesCents);
        }
        catch (SqlException ex) when (ex.Number == LeaseRejectedSqlError)
        {
            // usp_RecordAdjudicationResult threw: our lease lapsed. The claim is still
            // queued with its attempt count advanced — nothing else to do here.
            AdjudicationMetrics.AdjudicationFailures.Add(1);
            LogLeaseRejected(claimId);
        }
        catch (Exception ex)
        {
            AdjudicationMetrics.AdjudicationFailures.Add(1);
            LogFailure(claimId, ex);
            await gateway.FailLeaseAsync(claimId, leaseToken, ex.Message, TimeSpan.FromSeconds(30), ct);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}
