namespace MediFlow.Domain.Claims;

/// <summary>
/// Remittance-advice adjustment codes used by the adjudicator. CO-* codes are
/// contractual/plan adjustments; PR-* codes shift the amount to member responsibility.
/// </summary>
public enum DenialCode
{
    None = 0,

    /// <summary>CO-18: exact duplicate claim/service.</summary>
    DuplicateClaim = 181,

    /// <summary>CO-27: expenses incurred after coverage terminated.</summary>
    CoverageTerminated = 271,

    /// <summary>CO-29: the time limit for filing has expired.</summary>
    TimelyFiling = 291,

    /// <summary>CO-96: non-covered charge per the plan's fee schedule.</summary>
    NonCoveredService = 961,

    /// <summary>PR-1: amount applied to the member's deductible.</summary>
    Deductible = 12,

    /// <summary>PR-2: member coinsurance share.</summary>
    Coinsurance = 22,

    /// <summary>PR-3: member copayment.</summary>
    Copay = 32,
}

public static class DenialCodeDescriptions
{
    private static readonly Dictionary<DenialCode, string> Map = new()
    {
        [DenialCode.DuplicateClaim] = "CO-18 — Exact duplicate claim or service",
        [DenialCode.CoverageTerminated] = "CO-27 — Expenses incurred after coverage terminated",
        [DenialCode.TimelyFiling] = "CO-29 — The time limit for filing has expired",
        [DenialCode.NonCoveredService] = "CO-96 — Non-covered charge (not on the plan fee schedule)",
        [DenialCode.Deductible] = "PR-1 — Deductible amount (member responsibility)",
        [DenialCode.Coinsurance] = "PR-2 — Coinsurance amount (member responsibility)",
        [DenialCode.Copay] = "PR-3 — Copayment (member responsibility)",
    };

    /// <summary>Looks up the remittance description for an adjustment code.</summary>
    public static string Describe(DenialCode code) =>
        Map.TryGetValue(code, out var description) ? description : "No adjustment";

    /// <summary>All codes with descriptions — feeds the MCP explain-denial-code tool.</summary>
    public static IReadOnlyDictionary<DenialCode, string> All() => Map;
}
