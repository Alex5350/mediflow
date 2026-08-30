namespace MediFlow.Claims.Api;

using MediFlow.Contracts.Claims;
using MediFlow.Contracts.Members;
using MediFlow.Contracts.Plans;
using MediFlow.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;

public static class RollupModule
{
    public static IEndpointRouteBuilder MapRollupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rollups").WithTags("Analytics");

        group.MapGet("/denials", async Task<Ok<List<DenialRollupDto>>> (
            IReadStore readStore,
            int? year,
            CancellationToken ct) =>
        {
            var rollup = await readStore.DenialRollupAsync(year ?? DateTime.UtcNow.Year, ct);
            return TypedResults.Ok(rollup.ToList());
        });

        group.MapGet("/plan-enrollment", async Task<Ok<List<PlanEnrollmentSummaryDto>>> (
            IReadStore readStore,
            int? year,
            CancellationToken ct) =>
        {
            var summary = await readStore.PlanEnrollmentSummaryAsync(year ?? DateTime.UtcNow.Year, ct);
            return TypedResults.Ok(summary.ToList());
        });

        group.MapGet("/dashboard", async Task<Ok<DashboardStatsDto>> (
            IReadStore readStore,
            CancellationToken ct) =>
        {
            var stats = await readStore.DashboardStatsAsync(ct);
            return TypedResults.Ok(stats);
        });

        return app;
    }
}
