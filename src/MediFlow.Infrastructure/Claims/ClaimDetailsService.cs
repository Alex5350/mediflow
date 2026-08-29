namespace MediFlow.Infrastructure.Claims;

using MediFlow.Contracts.Claims;
using MediFlow.Domain.Auditing;
using MediFlow.Domain.Claims;
using Microsoft.EntityFrameworkCore;
using Persistence;
using System.Text.Json;

/// <summary>Claim detail assembly and manual queue actions for the Claims API.</summary>
public interface IClaimDetailsService
{
    Task<ClaimDetailDto?> GetClaimDetailAsync(int claimId, CancellationToken ct = default);
    Task<ClaimDetailDto?> GetClaimByNumberAsync(string claimNumber, CancellationToken ct = default);
    Task<bool> PendAsync(int claimId, string note, string actor, CancellationToken ct = default);
}

public sealed class ClaimDetailsService(MediFlowDbContext db, TimeProvider clock) : IClaimDetailsService
{
    public async Task<ClaimDetailDto?> GetClaimDetailAsync(int claimId, CancellationToken ct = default)
    {
        var claim = await LoadClaimAsync(c => c.Id == claimId, ct);
        return claim is null ? null : await ToDtoAsync(claim, ct);
    }

    public async Task<ClaimDetailDto?> GetClaimByNumberAsync(string claimNumber, CancellationToken ct = default)
    {
        var claim = await LoadClaimAsync(c => c.ClaimNumber == claimNumber, ct);
        return claim is null ? null : await ToDtoAsync(claim, ct);
    }

    public async Task<bool> PendAsync(int claimId, string note, string actor, CancellationToken ct = default)
    {
        var claim = await db.Claims.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim is null || claim.Status is not (ClaimStatus.Received or ClaimStatus.Adjudicating))
        {
            return false;
        }

        claim.Status = ClaimStatus.Pended;
        db.AuditEntries.Add(new AuditEntry
        {
            EntityType = "Claim",
            EntityKey = claim.ClaimNumber,
            Action = "Pended",
            DetailJson = JsonSerializer.Serialize(new { Note = note.Trim() }),
            Actor = actor,
            AtUtc = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private Task<Domain.Claims.Claim?> LoadClaimAsync(System.Linq.Expressions.Expression<Func<Domain.Claims.Claim, bool>> predicate, CancellationToken ct) =>
        db.Claims
            .Include(c => c.Lines)
            .Include(c => c.Member)
            .Include(c => c.Plan)
            .AsNoTracking()
            .FirstOrDefaultAsync(predicate, ct);

    private async Task<ClaimDetailDto> ToDtoAsync(Domain.Claims.Claim claim, CancellationToken ct)
    {
        var audit = await db.AuditEntries
            .AsNoTracking()
            .Where(a => a.EntityType == "Claim" && a.EntityKey == claim.ClaimNumber)
            .OrderBy(a => a.AtUtc)
            .Select(a => new ClaimAuditRowDto(a.Action, a.Actor, a.AtUtc, a.DetailJson))
            .ToListAsync(ct);

        return new ClaimDetailDto
        {
            Id = claim.Id,
            ClaimNumber = claim.ClaimNumber,
            MemberId = claim.MemberId,
            MemberName = claim.Member!.DisplayName,
            Mbi = claim.Member.Mbi,
            PlanCode = claim.Plan!.PlanCode,
            PlanName = claim.Plan.Name,
            RenderingProviderNpi = claim.RenderingProviderNpi,
            Type = (int)claim.Type,
            ServiceDate = claim.ServiceDate,
            TotalChargeCents = claim.TotalChargeCents,
            Status = (int)claim.Status,
            ReceivedAtUtc = claim.ReceivedAtUtc,
            AdjudicatedAtUtc = claim.AdjudicatedAtUtc,
            TotalAllowedCents = claim.TotalAllowedCents,
            TotalPlanPaidCents = claim.TotalPlanPaidCents,
            TotalMemberOwesCents = claim.TotalMemberOwesCents,
            DenialCode = (int?)claim.DenialCode,
            Lines = [.. claim.Lines.OrderBy(l => l.Sequence).Select(l => new ClaimLineDto
            {
                Sequence = l.Sequence,
                ProcedureCode = l.ProcedureCode,
                ChargeCents = l.ChargeCents,
                AllowedCents = l.AllowedCents,
                PlanPaidCents = l.PlanPaidCents,
                MemberOwesCents = l.MemberOwesCents,
                DenialCode = (int?)l.DenialCode,
            })],
            Audit = audit,
        };
    }
}
