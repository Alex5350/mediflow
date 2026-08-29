namespace MediFlow.Domain.Enrollment;

/// <summary>
/// Guarded transitions for <see cref="EnrollmentStatus"/>. Centralising the machine
/// keeps API endpoints, the verification worker and tests agreeing on one definition.
/// </summary>
public static class EnrollmentStateMachine
{
    private static readonly Dictionary<EnrollmentStatus, EnrollmentStatus[]> Transitions = new()
    {
        [EnrollmentStatus.Draft] = [EnrollmentStatus.Submitted, EnrollmentStatus.Cancelled],
        [EnrollmentStatus.Submitted] = [EnrollmentStatus.PendingVerification, EnrollmentStatus.Cancelled],
        [EnrollmentStatus.PendingVerification] = [EnrollmentStatus.Approved, EnrollmentStatus.Denied, EnrollmentStatus.Cancelled],
        [EnrollmentStatus.Approved] = [EnrollmentStatus.Active, EnrollmentStatus.Cancelled],
        // Denied applications are terminal — a re-apply is a new application.
        [EnrollmentStatus.Denied] = [],
        [EnrollmentStatus.Active] = [EnrollmentStatus.Cancelled],
        [EnrollmentStatus.Cancelled] = [],
    };

    public static bool CanTransition(EnrollmentStatus from, EnrollmentStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Attempts the transition, returning false (and leaving the status untouched) when illegal.</summary>
    public static bool TryTransition(EnrollmentApplication application, EnrollmentStatus to)
    {
        if (!CanTransition(application.Status, to))
        {
            return false;
        }

        application.Status = to;
        return true;
    }
}
