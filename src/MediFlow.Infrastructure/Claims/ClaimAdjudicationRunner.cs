namespace MediFlow.Infrastructure.Claims;

using MediFlow.Contracts.Claims;
using MediFlow.Domain.Accumulators;
using MediFlow.Domain.Claims;
using MediFlow.Domain.Claims.Adjudication;
using MediFlow.Domain.Enrollment;
using MediFlow.Domain.Fees;
using Microsoft.EntityFrameworkCore;
using Persistence;

/// <summary>
/// Assembles the <see cref="AdjudicationRequest"/> for a claim and runs the engine.
/// Shared by the worker (which then commits) and the MCP preview tool (which does not).
/// </summary>
public interface IClaimAdjudicationRunner
{
    Task<AdjudicationPreviewDto?> PreviewAsync(int claimId, CancellationToken ct = default);
    Task<AdjudicateWorkItem?> LoadForAdjudicationAsync(int claimId, CancellationToken ct = default);
}

/// <summary>Claim plus everything the engine needs, ready for adjudication.</summary>
public sealed record AdjudicateWorkItem(Claim Claim, AdjudicationRequest Request);

public sealed class ClaimAdjudicationRunner(MediFlowDbContext db, AdjudicationEngine engine) : IClaimAdjudicationRunner
{
    public async Task<AdjudicateWorkItem?> LoadForAdjudicationAsync(int claimId, CancellationToken ct = default)
    {
        var claim = await db.Claims
            .Include(c => c.Lines)
            .Include(c => c.Member)
            .Include(c => c.Plan)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim?.Member is null || claim.Plan is null)
        {
            return null;
        }

        var request = await BuildRequestAsync(claim, ct);
        return request is null ? null : new AdjudicateWorkItem(claim, request);
    }

    public async Task<AdjudicationPreviewDto?> PreviewAsync(int claimId, CancellationToken ct = default)
    {
        var work = await LoadForAdjudicationAsync(claimId, ct);
        if (work is null)
        {
            return null;
        }

        var result = engine.Adjudicate(work.Request);
        return new AdjudicationPreviewDto
        {
            ClaimNumber = work.Claim.ClaimNumber,
            Status = result.Status.ToString(),
            ClaimDenialCode = result.ClaimDenialCode is { } code ? DenialCodeDescriptions.Describe(code) : null,
            Lines = [.. result.Lines.Select(l => new AdjudicationPreviewLineDto
            {
                Sequence = l.Sequence,
                ProcedureCode = l.ProcedureCode,
                ChargeCents = l.ChargeCents,
                AllowedCents = l.AllowedCents,
                PlanPaidCents = l.PlanPaidCents,
                MemberOwesCents = l.MemberOwesCents,
                DenialCode = l.DenialCode is { } dc ? DenialCodeDescriptions.Describe(dc) : null,
            })],
            TotalAllowedCents = result.TotalAllowedCents,
            TotalPlanPaidCents = result.TotalPlanPaidCents,
            TotalMemberOwesCents = result.TotalMemberOwesCents,
            NewDeductibleMetCents = result.NewDeductibleMetCents,
            NewOopMetCents = result.NewOopMetCents,
        };
    }

    private async Task<AdjudicationRequest?> BuildRequestAsync(Claim claim, CancellationToken ct)
    {
        var benefitYear = claim.ServiceDate.Year;

        // Coverage: the active enrollment (if any) covering the service date.
        var enrollment = await db.Enrollments
            .AsNoTracking()
            .Where(a => a.MemberId == claim.MemberId
                && a.Status == EnrollmentStatus.Active
                && a.RequestedEffectiveDate <= claim.ServiceDate
                && (a.CancelledEffectiveDate == null || claim.ServiceDate <= a.CancelledEffectiveDate))
            .OrderByDescending(a => a.RequestedEffectiveDate)
            .FirstOrDefaultAsync(ct);

        var fees = await db.ProcedureFees
            .AsNoTracking()
            .Where(f => f.EffectiveYear == benefitYear)
            .ToDictionaryAsync(f => f.ProcedureCode, f => f, ct);

        var accumulator = await db.Accumulators
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.MemberId == claim.MemberId && a.BenefitYear == benefitYear, ct)
            ?? new BenefitAccumulator { MemberId = claim.MemberId, BenefitYear = benefitYear };

        // Duplicate fingerprints from prior (non-open) claims for the same member.
        var prior = await db.Claims
            .AsNoTracking()
            .Where(c => c.MemberId == claim.MemberId && c.Id != claim.Id && c.Status != ClaimStatus.Received)
            .SelectMany(c => c.Lines, (c, l) => new { c.RenderingProviderNpi, c.ServiceDate, l.ProcedureCode })
            .ToListAsync(ct);
        var fingerprints = prior
            .Select(p => new PriorClaimFingerprint(p.RenderingProviderNpi, p.ServiceDate, p.ProcedureCode))
            .ToList();

        return new AdjudicationRequest(
            claim, claim.Member!, claim.Plan!, enrollment, fees, accumulator, fingerprints);
    }
}
