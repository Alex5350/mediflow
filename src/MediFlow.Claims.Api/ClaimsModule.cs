namespace MediFlow.Claims.Api;

using MediFlow.Contracts.Claims;
using MediFlow.Contracts.Members;
using MediFlow.Domain.Claims;
using MediFlow.Domain.Messaging;
using MediFlow.Infrastructure.Claims;
using MediFlow.Infrastructure.Data;
using MediFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public static class ClaimsModule
{
    public static IEndpointRouteBuilder MapClaimsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/claims").WithTags("Claims");

        group.MapPost("/", async Task<Results<Accepted<ClaimAcceptedDto>, ValidationProblem>> (
            SubmitClaimRequest request,
            IClaimIntakeService intake,
            CancellationToken ct) =>
        {
            var result = await intake.SubmitClaimAsync(request, "provider-portal", ct);
            if (!result.Accepted)
            {
                return TypedResults.ValidationProblem(result.Violations.ToDictionary(
                    v => v.Code, v => new[] { v.Message }));
            }

            return TypedResults.Accepted((string?)null, new ClaimAcceptedDto(result.ClaimId!.Value, result.ClaimNumber!));
        });

        group.MapGet("/queue", async Task<Ok<PagedResult<ClaimQueueItemDto>>> (
            IReadStore readStore,
            string? statuses,
            DateOnly? from,
            DateOnly? to,
            int page = 1,
            int pageSize = 25,
            CancellationToken ct = default) =>
        {
            var parsed = string.IsNullOrWhiteSpace(statuses)
                ? null
                : statuses.Split(',', StringSplitOptions.TrimEntries)
                    .Select(s => Enum.TryParse<ClaimStatus>(s, true, out var status) ? status : (ClaimStatus?)null)
                    .Where(s => s.HasValue)
                    .Select(s => s!.Value)
                    .ToList();
            var queue = await readStore.ClaimsQueueAsync(parsed, from, to, page, pageSize, ct);
            return TypedResults.Ok(queue);
        });

        group.MapGet("/{claimId:int}", async Task<Results<Ok<ClaimDetailDto>, NotFound>> (
            int claimId,
            IClaimDetailsService details,
            CancellationToken ct) =>
        {
            var claim = await details.GetClaimDetailAsync(claimId, ct);
            return claim is null ? TypedResults.NotFound() : TypedResults.Ok(claim);
        });

        group.MapGet("/by-number/{claimNumber}", async Task<Results<Ok<ClaimDetailDto>, NotFound>> (
            string claimNumber,
            IClaimDetailsService details,
            CancellationToken ct) =>
        {
            var claim = await details.GetClaimByNumberAsync(claimNumber, ct);
            return claim is null ? TypedResults.NotFound() : TypedResults.Ok(claim);
        });

        group.MapGet("/{claimId:int}/preview", async Task<Results<Ok<AdjudicationPreviewDto>, NotFound>> (
            int claimId,
            IClaimAdjudicationRunner runner,
            CancellationToken ct) =>
        {
            var preview = await runner.PreviewAsync(claimId, ct);
            return preview is null ? TypedResults.NotFound() : TypedResults.Ok(preview);
        });

        group.MapPost("/{claimId:int}/pend", async Task<Results<NoContent, NotFound, Conflict<string>>> (
            int claimId,
            PendRequest request,
            IClaimDetailsService details,
            CancellationToken ct) =>
        {
            var pended = await details.PendAsync(claimId, request.Note ?? "Manual review", "staff-portal", ct);
            return pended ? TypedResults.NoContent() : TypedResults.Conflict("Only Received or Adjudicating claims can be pended.");
        });

        group.MapPost("/{claimId:int}/adjudicate", async Task<Results<Accepted, Conflict<string>>> (
            int claimId,
            MediFlowDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            // Manual trigger: drop an immediately-available outbox message. The worker's
            // atomic lease makes this safe even if the claim is already being processed.
            var claim = await db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == claimId, ct);
            if (claim is null)
            {
                return TypedResults.Conflict($"Claim {claimId} not found.");
            }
            if (claim.Status is not (ClaimStatus.Received or ClaimStatus.DeadLettered))
            {
                return TypedResults.Conflict($"Claim {claim.ClaimNumber} is {claim.Status}; only Received/DeadLettered claims can be (re)queued.");
            }

            var now = clock.GetUtcNow().UtcDateTime;
            db.Outbox.Add(new OutboxMessage
            {
                Type = OutboxMessage.AdjudicateClaim,
                PayloadJson = JsonSerializer.Serialize(new { claimId }),
                CreatedAtUtc = now,
                AvailableAtUtc = now,
            });
            await db.SaveChangesAsync(ct);
            return TypedResults.Accepted((string?)null);
        });

        return app;
    }

    public sealed record ClaimAcceptedDto(int ClaimId, string ClaimNumber);
    public sealed record PendRequest(string? Note);
}
