namespace MediFlow.Api;

using MediFlow.Contracts.Plans;
using MediFlow.Domain.Plans;
using MediFlow.Infrastructure.Data;
using MediFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public static class PlansModule
{
    public static IEndpointRouteBuilder MapPlansEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/plans").WithTags("Plans");

        group.MapGet("/", async Task<Ok<List<PlanDto>>> (
            MediFlowDbContext db,
            int? year,
            CancellationToken ct) =>
        {
            var query = db.Plans.AsNoTracking().Where(p => p.IsActive);
            if (year.HasValue)
            {
                query = query.Where(p => p.ContractYear == year.Value);
            }

            var plans = await query
                .OrderBy(p => p.ContractYear).ThenBy(p => p.PlanCode)
                .Select(p => ToDto(p))
                .ToListAsync(ct);
            return TypedResults.Ok(plans);
        });

        group.MapGet("/{planId:int}", async Task<Results<Ok<PlanDto>, NotFound>> (
            int planId,
            MediFlowDbContext db,
            CancellationToken ct) =>
        {
            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
            return plan is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(plan));
        });

        group.MapGet("/enrollment-summary", async Task<Ok<List<PlanEnrollmentSummaryDto>>> (
            IReadStore readStore,
            int year,
            CancellationToken ct) =>
        {
            var summary = await readStore.PlanEnrollmentSummaryAsync(year, ct);
            return TypedResults.Ok(summary.ToList());
        });

        return app;
    }

    private static PlanDto ToDto(Plan p) => new()
    {
        Id = p.Id,
        PlanCode = p.PlanCode,
        Name = p.Name,
        Carrier = p.Carrier,
        Type = (int)p.Type,
        ContractYear = p.ContractYear,
        MonthlyPremiumCents = p.MonthlyPremiumCents,
        DeductibleCents = p.DeductibleCents,
        CoinsurancePercent = p.CoinsurancePercent,
        OopMaxCents = p.OopMaxCents,
        IsFiveStar = p.IsFiveStar,
    };
}
