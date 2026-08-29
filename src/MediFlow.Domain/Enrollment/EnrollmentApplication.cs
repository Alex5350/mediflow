namespace MediFlow.Domain.Enrollment;

using Members;
using Plans;

/// <summary>An application to enroll a member in a plan product.</summary>
public sealed class EnrollmentApplication
{
    public int Id { get; set; }

    /// <summary>Business key shown to staff, e.g. ENR-2026-000421.</summary>
    public required string ApplicationNumber { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public int PlanId { get; set; }
    public Plan? Plan { get; set; }

    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Draft;

    /// <summary>SEP reason asserted on the application; verified by staff before approval.</summary>
    public SepReason SepReason { get; set; } = SepReason.None;

    /// <summary>Coverage start requested by the applicant.</summary>
    public DateOnly RequestedEffectiveDate { get; set; }

    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>Free-text reason recorded when an application is denied or cancelled.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>Coverage end date when an active enrollment is cancelled/replaced.</summary>
    public DateOnly? CancelledEffectiveDate { get; set; }

    /// <summary>Concurrency token — staff decisions race with the eligibility worker.</summary>
    public byte[]? RowVersion { get; set; }

    public const string SepReasonPrefix = "SEP";

    public static string NextApplicationNumber(int sequence, int year) => $"ENR-{year}-{sequence:D6}";
}
