namespace MediFlow.Domain.Claims;

using Common;
using Enrollment;
using Fees;

/// <summary>A submission-time validation failure with a staff/provider-facing message.</summary>
public readonly record struct ClaimSubmissionViolation(string Code, string Message);

/// <summary>
/// Intake validation for newly submitted claims — everything checkable before a
/// claim enters the queue. Runs in the API before persistence; returns 400 with
/// these violations rather than accepting unadjudicatable work.
/// </summary>
public static class ClaimSubmissionRules
{
    public static IReadOnlyList<ClaimSubmissionViolation> Validate(
        string renderingProviderNpi,
        int memberId,
        int planId,
        DateOnly serviceDate,
        DateTime receivedAtUtc,
        IReadOnlyList<(string ProcedureCode, int ChargeCents)> lines)
    {
        List<ClaimSubmissionViolation> violations = [];

        if (!Npi.IsValid(renderingProviderNpi))
        {
            violations.Add(new("NPI_INVALID", "Rendering provider NPI failed the check-digit validation."));
        }

        if (memberId <= 0)
        {
            violations.Add(new("MEMBER_REQUIRED", "A member must be selected."));
        }

        if (planId <= 0)
        {
            violations.Add(new("PLAN_REQUIRED", "A plan must be selected."));
        }

        if (serviceDate > DateOnly.FromDateTime(receivedAtUtc.Date))
        {
            violations.Add(new("SERVICE_DATE_FUTURE", "Service date cannot be in the future."));
        }

        if (lines.Count == 0)
        {
            violations.Add(new("LINES_REQUIRED", "At least one service line is required."));
        }

        foreach (var (index, line) in lines.Select((l, i) => (Index: i + 1, Line: l)))
        {
            if (!IsValidProcedureCode(line.ProcedureCode))
            {
                violations.Add(new($"LINE_{index}_CODE_INVALID", $"Line {index}: procedure codes are 4–5 alphanumeric characters (CPT/HCPCS)."));
            }

            if (line.ChargeCents <= 0)
            {
                violations.Add(new($"LINE_{index}_CHARGE_INVALID", $"Line {index}: charge must be greater than zero."));
            }
        }

        return violations;
    }

    /// <summary>CPT is 4 digits; HCPCS is letter + 4 alphanumeric.</summary>
    private static bool IsValidProcedureCode(string code) =>
        code.Length is 4 or 5 && code.All(c => char.IsAsciiLetterOrDigit(c));
}
