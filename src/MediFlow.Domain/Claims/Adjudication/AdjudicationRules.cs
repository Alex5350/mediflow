namespace MediFlow.Domain.Claims.Adjudication;

/// <summary>
/// A claim-level rule that runs before benefit math. Rules can deny the whole
/// claim (all lines inherit the denial) or let the pipeline continue. Rules are
/// stateless, run in registration order and are unit-tested in isolation.
/// </summary>
public interface IAdjudicationClaimRule
{
    /// <summary>Rule name for audit entries and structured logs.</summary>
    string Name { get; }

    /// <summary>Evaluates the claim; a returned code denies the entire claim.</summary>
    DenialCode? Evaluate(AdjudicationRequest request);
}

/// <summary>CO-29 — claims must be filed within one year of the service date.</summary>
public sealed class FilingTimelinessRule : IAdjudicationClaimRule
{
    public string Name => "FilingTimeliness";

    public DenialCode? Evaluate(AdjudicationRequest request)
    {
        var deadline = request.Claim.ServiceDate.AddDays(Claim.FilingLimitDays);
        return request.Claim.ReceivedAtUtc.Date > deadline.ToDateTime(TimeOnly.MinValue)
            ? DenialCode.TimelyFiling
            : null;
    }
}

/// <summary>CO-27 — services must fall inside an active enrollment period.</summary>
public sealed class CoverageRule : IAdjudicationClaimRule
{
    public string Name => "Coverage";

    public DenialCode? Evaluate(AdjudicationRequest request) =>
        request.HasCoverageOnServiceDate ? null : DenialCode.CoverageTerminated;
}

/// <summary>
/// CO-18 — the same provider billing the same procedure for the same member on the
/// same service date is an exact duplicate. Fingerprints arrive from the worker
/// (prior claims already on file, excluding the claim being adjudicated).
/// </summary>
public sealed class DuplicateClaimRule : IAdjudicationClaimRule
{
    public string Name => "Duplicate";

    public DenialCode? Evaluate(AdjudicationRequest request)
    {
        foreach (var line in request.Claim.Lines)
        {
            var fingerprint = new PriorClaimFingerprint(
                request.Claim.RenderingProviderNpi,
                request.Claim.ServiceDate,
                line.ProcedureCode);

            if (request.PriorClaims.Contains(fingerprint))
            {
                return DenialCode.DuplicateClaim;
            }
        }

        return null;
    }
}
