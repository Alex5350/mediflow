namespace MediFlow.Mcp;

using MediFlow.Contracts.Claims;
using MediFlow.Contracts.Enrollment;
using MediFlow.Contracts.Members;
using MediFlow.Domain.Claims;
using MediFlow.Infrastructure.Claims;
using MediFlow.Infrastructure.Data;
using MediFlow.Infrastructure.Enrollment;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;
using System.ComponentModel;

/// <summary>
/// MediFlow operations tools for MCP clients (GitHub Copilot, VS Code, and other MCP-capable hosts).
/// Read-only by design: the one stateful surface — adjudication — is exposed as a
/// dry-run preview so an agent can explain outcomes without writing to the database.
/// </summary>
[McpServerToolType]
public static class MediFlowTools
{
    // ---- membership ----

    [McpServerTool(Name = "search_members")]
    [Description("Find Medicare members by MBI or name prefix. Returns a page of matches with entitlement dates.")]
    public static async Task<string> SearchMembers(
        [FromServices] IReadStore readStore,
        [Description("MBI prefix or last/first name prefix, e.g. '1EG4' or 'Abernathy'")] string query,
        [Description("1-based page number")] int page = 1)
    {
        var results = await readStore.SearchMembersAsync(query, page, 10);
        var lines = results.Items.Select(m =>
            $"{m.Id}: {m.LastName}, {m.FirstName} · MBI {m.Mbi} · DOB {m.DateOfBirth.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)} · Part B {(m.PartBEffective is { } partB ? partB.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "not entitled")} · {m.StateCode}");
        return $"Page {page} of {results.TotalPages} ({results.Total} total):\n" + string.Join('\n', lines);
    }

    [McpServerTool(Name = "get_member_360")]
    [Description("Member 360: active coverage, enrollment history and recent claims with YTD totals.")]
    public static async Task<string> GetMember360(
        [FromServices] IReadStore readStore,
        [Description("Member id from search_members")] int memberId)
    {
        var view = await readStore.GetMember360Async(memberId);
        if (view?.Header is null)
        {
            return $"Member {memberId} not found.";
        }

        var h = view.Header;
        var summary = $"""
            {h.LastName}, {h.FirstName} · MBI {h.Mbi}
            Active coverage: {(h.PlanCode is null ? "none" : $"{h.PlanCode} {h.PlanName} since {h.RequestedEffectiveDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "?"}")}
            Applications on file: {view.Enrollments.Count}
            Recent claims: {view.Claims.Count}
            YTD plan paid: {Money(view.Claims.Count > 0 ? view.Claims[0].YtdPlanPaidCents : 0)} · member share: {Money(view.Claims.Count > 0 ? view.Claims[0].YtdMemberOwesCents : 0)}
            Recent claim statuses: {(view.Claims.Count == 0 ? "none" : string.Join(", ", view.Claims.Take(5).Select(c => $"{c.ClaimNumber}={((ClaimStatus)c.Status)}")))}
            """;
        return summary;
    }

    // ---- enrollment ----

    [McpServerTool(Name = "check_enrollment_eligibility")]
    [Description("Dry-run Medicare enrollment eligibility (AEP/ICEP/SEP windows, Part B entitlement, dual-coverage) without saving anything.")]
    public static async Task<string> CheckEnrollmentEligibility(
        [FromServices] IEnrollmentService enrollmentService,
        [Description("Member id")] int memberId,
        [Description("Plan id")] int planId,
        [Description("Requested effective date (yyyy-MM-dd)")] string effectiveDate,
        [Description("SEP reason code: 0 none, 1 moved, 2 lost coverage, 3 dual eligible, 4 LIS, 5 five-star switch")] int sepReason = 0)
    {
        if (!DateOnly.TryParse(effectiveDate, out var date))
        {
            return $"Invalid date '{effectiveDate}' — use yyyy-MM-dd.";
        }

        var result = await enrollmentService.CheckEligibilityAsync(new SubmitEnrollmentRequest
        {
            MemberId = memberId,
            PlanId = planId,
            RequestedEffectiveDate = date,
            SepReason = sepReason,
        });

        return result.IsValid
            ? $"ELIGIBLE: member {memberId} may enroll effective {date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)} with SEP reason {sepReason}."
            : "NOT ELIGIBLE:\n" + string.Join('\n', result.Violations.Select(v => $"- [{v.Code}] {v.Message}"));
    }

    // ---- claims ----

    [McpServerTool(Name = "claims_queue")]
    [Description("List the claims work queue, optionally filtered by status.")]
    public static async Task<string> ClaimsQueue(
        [FromServices] IReadStore readStore,
        [Description("Status filter: Received, Adjudicating, Paid, Denied, Pended, DeadLettered (comma-separated for several)")] string? statuses = null,
        [Description("1-based page number")] int page = 1)
    {
        var parsed = statuses?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Enum.TryParse<ClaimStatus>(s, true, out var status) ? status : (ClaimStatus?)null)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        var queue = await readStore.ClaimsQueueAsync(parsed, null, null, page, 10);
        var lines = queue.Items.Select(c =>
            $"{c.ClaimNumber}: {((ClaimStatus)c.Status)} · {c.LastName}, {c.FirstName} · svc {c.ServiceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)} · charged {Money(c.TotalChargeCents)}{(c.DenialCode is { } d ? $" · denial {Describe(d)}" : string.Empty)}");
        return $"Page {page} of {queue.TotalPages} ({queue.Total} total):\n" + string.Join('\n', lines);
    }

    [McpServerTool(Name = "get_claim")]
    [Description("Full claim detail with line-level remittance and audit trail, by claim number.")]
    public static async Task<string> GetClaim(
        [FromServices] IClaimDetailsService details,
        [Description("Claim number, e.g. CLM-2026-000511")] string claimNumber)
    {
        var claim = await details.GetClaimByNumberAsync(claimNumber.Trim().ToUpperInvariant());
        if (claim is null)
        {
            return $"Claim {claimNumber} not found.";
        }

        var lines = claim.Lines.Select(l =>
            $"  {l.Sequence}. {l.ProcedureCode}: charged {Money(l.ChargeCents)}, allowed {Optional(l.AllowedCents)}, plan pays {Optional(l.PlanPaidCents)}, member owes {Optional(l.MemberOwesCents)}{(l.DenialCode is { } d ? $" [{Describe(d)}]" : string.Empty)}");
        return $"""
            {claim.ClaimNumber} — {((ClaimStatus)claim.Status)}
            Member: {claim.MemberName} (MBI {claim.Mbi}) · Plan {claim.PlanCode} · NPI {claim.RenderingProviderNpi}
            Service {claim.ServiceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)} · received {claim.ReceivedAtUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)} UTC
            Totals: charged {Money(claim.TotalChargeCents)}, plan paid {Optional(claim.TotalPlanPaidCents)}, member owes {Optional(claim.TotalMemberOwesCents)}
            {string.Join('\n', lines)}
            Audit: {string.Join(" → ", claim.Audit.Select(a => $"{a.Action} ({a.AtUtc.ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)}, {a.Actor})"))}
            """;
    }

    [McpServerTool(Name = "preview_adjudication")]
    [Description("Dry-run the adjudication engine on a claim and return the decision it WOULD make — no data is written.")]
    public static async Task<string> PreviewAdjudication(
        [FromServices] IClaimAdjudicationRunner runner,
        [Description("Claim id")] int claimId)
    {
        var preview = await runner.PreviewAsync(claimId);
        if (preview is null)
        {
            return $"Claim {claimId} not found.";
        }

        var lines = preview.Lines.Select(l =>
            $"  {l.Sequence}. {l.ProcedureCode}: allowed {Money(l.AllowedCents)}, plan {Money(l.PlanPaidCents)}, member {Money(l.MemberOwesCents)}{(l.DenialCode is not null ? $" [{l.DenialCode}]" : string.Empty)}");
        return $"""
            {preview.ClaimNumber} would adjudicate as {preview.Status}{(preview.ClaimDenialCode is { } why ? $" — {why}" : string.Empty)}
            Plan pays {Money(preview.TotalPlanPaidCents)} · member owes {Money(preview.TotalMemberOwesCents)}
            Deductible met after: {Money(preview.NewDeductibleMetCents)} · OOP met after: {Money(preview.NewOopMetCents)}
            {string.Join('\n', lines)}
            (Dry run — nothing was committed.)
            """;
    }

    // ---- analytics ----

    [McpServerTool(Name = "denial_rollup")]
    [Description("Denial counts and dollars grouped by adjustment code for a plan year.")]
    public static async Task<string> DenialRollup(
        [FromServices] IReadStore readStore,
        [Description("Four-digit service year")] int? year = null)
    {
        var rows = await readStore.DenialRollupAsync(year ?? DateTime.UtcNow.Year);
        if (rows.Count == 0)
        {
            return $"No denials recorded for {year ?? DateTime.UtcNow.Year}.";
        }

        var lines = rows.Select(r => $"{Describe(r.DenialCode)}: {r.ClaimCount} claims · {Money((int)r.ChargedCents)} charged · {Money((int)r.UnpaidCents)} unpaid");
        return string.Join('\n', lines);
    }

    [McpServerTool(Name = "explain_denial_code")]
    [Description("Explain a remittance adjustment code (e.g. CO-18, PR-1) in plain language.")]
    public static string ExplainDenialCode(
        [Description("Adjustment code like CO-18, PR-1, or a description fragment")] string code)
    {
        var normalized = code.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        var map = DenialCodeDescriptions.All();
        var match = normalized switch
        {
            "CO-18" => DenialCode.DuplicateClaim,
            "CO-27" => DenialCode.CoverageTerminated,
            "CO-29" => DenialCode.TimelyFiling,
            "CO-96" => DenialCode.NonCoveredService,
            "PR-1" => DenialCode.Deductible,
            "PR-2" => DenialCode.Coinsurance,
            "PR-3" => DenialCode.Copay,
            _ => ParseByEnumName(normalized),
        };

        return match is { } denial
            ? $"{code}: {DenialCodeDescriptions.Describe(denial)}."
            : $"Unknown code '{code}'. Known codes: " + string.Join(", ", map.Select(kv => $"{kv.Key} = {kv.Value.Split('—')[0].Trim()}"));
    }

    private static DenialCode? ParseByEnumName(string value) =>
        Enum.TryParse<DenialCode>(value, true, out var parsed) && mapHasKey(parsed) ? parsed : null;

    private static bool mapHasKey(DenialCode code) => DenialCodeDescriptions.All().ContainsKey(code);

    private static string Describe(int denialCode) => DenialCodeDescriptions.Describe((DenialCode)denialCode).Split('—')[0].Trim();

    private static string Money(int cents) => Domain.Common.Money.Format(cents);

    private static string Optional(int? cents) => cents is { } value ? Money(value) : "—";
}
