namespace MediFlow.Infrastructure.Claims;

using Data;
using MediFlow.Contracts.Claims;
using MediFlow.Domain.Auditing;
using MediFlow.Domain.Claims;
using MediFlow.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Persistence;
using System.Text.Json;

/// <summary>Transactional claim intake: validation, claim + lines, outbox message
/// and audit entry all commit together (ADR 0005).</summary>
public interface IClaimIntakeService
{
    Task<ClaimSubmissionResultDto> SubmitClaimAsync(SubmitClaimRequest request, string actor, CancellationToken ct = default);
}

public sealed class ClaimIntakeService(MediFlowDbContext db, TimeProvider clock) : IClaimIntakeService
{
    public async Task<ClaimSubmissionResultDto> SubmitClaimAsync(SubmitClaimRequest request, string actor, CancellationToken ct = default)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;

        var violations = ClaimSubmissionRules.Validate(
            request.RenderingProviderNpi,
            request.MemberId,
            request.PlanId,
            request.ServiceDate,
            nowUtc,
            [.. request.Lines.Select(l => (l.ProcedureCode, l.ChargeCents))]);

        if (violations.Count > 0)
        {
            return new ClaimSubmissionResultDto(false, null, null,
                [.. violations.Select(v => new ClaimSubmissionViolationDto(v.Code, v.Message))]);
        }

        // SqlServerRetryingExecutionStrategy requires user transactions to run
        // inside an explicit execution strategy (retriable unit).
        Claim claim = null!;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Business keys are derived from the identity column so concurrent submissions
            // can never collide (insert with placeholder, update by the assigned id).
            claim = new Claim
            {
                ClaimNumber = "PENDING",
                MemberId = request.MemberId,
                PlanId = request.PlanId,
                Type = (ClaimType)request.Type,
                RenderingProviderNpi = request.RenderingProviderNpi.Trim(),
                ServiceDate = request.ServiceDate,
                TotalChargeCents = request.Lines.Sum(l => l.ChargeCents),
                Status = ClaimStatus.Received,
                ReceivedAtUtc = nowUtc,
                Lines = [.. request.Lines.Select((l, i) => new ClaimLine
            {
                Sequence = i + 1,
                ProcedureCode = l.ProcedureCode.Trim().ToUpperInvariant(),
                ChargeCents = l.ChargeCents,
            })],
            };
            db.Claims.Add(claim);
            await db.SaveChangesAsync(ct);

            claim.ClaimNumber = Claim.NextClaimNumber(claim.Id, nowUtc.Year);
            db.Outbox.Add(new OutboxMessage
            {
                Type = OutboxMessage.AdjudicateClaim,
                PayloadJson = JsonSerializer.Serialize(new { claimId = claim.Id }),
                CreatedAtUtc = nowUtc,
                AvailableAtUtc = nowUtc,
            });
            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = "Claim",
                EntityKey = claim.ClaimNumber,
                Action = "Submitted",
                DetailJson = JsonSerializer.Serialize(new { claim.MemberId, Lines = claim.Lines.Count }),
                Actor = actor,
                AtUtc = nowUtc,
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return new ClaimSubmissionResultDto(true, claim.Id, claim.ClaimNumber, []);
    }
}
