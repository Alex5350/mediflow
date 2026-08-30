namespace MediFlow.Domain.Auditing;

/// <summary>
/// Append-only audit trail. Every state change to enrollments and claims writes
/// one row — who, what, when — which staff review on member/claim detail pages.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    /// <summary>Entity type name, e.g. Claim / EnrollmentApplication.</summary>
    public required string EntityType { get; set; }

    /// <summary>Business key (claim number / application number).</summary>
    public required string EntityKey { get; set; }

    /// <summary>Action, e.g. Submitted / Approved / Adjudicated / Leased.</summary>
    public required string Action { get; set; }

    /// <summary>Additional context as JSON (old→new status, denial codes, ...).</summary>
    public string? DetailJson { get; set; }

    /// <summary>Initiator — a staff user id, "worker" or "system".</summary>
    public required string Actor { get; set; }

    public DateTime AtUtc { get; set; }
}
