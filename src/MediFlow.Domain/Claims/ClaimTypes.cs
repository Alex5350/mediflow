namespace MediFlow.Domain.Claims;

/// <summary>Claim lifecycle states.</summary>
public enum ClaimStatus
{
    /// <summary>Submitted, waiting for the adjudication worker to lease it.</summary>
    Received = 0,

    /// <summary>Leased by a worker and currently adjudicating.</summary>
    Adjudicating = 1,

    /// <summary>Adjudicated — plan payment determined.</summary>
    Paid = 2,

    /// <summary>Adjudicated — denied in whole or in part.</summary>
    Denied = 3,

    /// <summary>Held for manual review (e.g. provider mismatch investigation).</summary>
    Pended = 4,

    /// <summary>Exhausted adjudication attempts — requires operator intervention.</summary>
    DeadLettered = 5,
}

/// <summary>Institutional (facility) vs professional (provider) claim forms.</summary>
public enum ClaimType
{
    Professional = 0,
    Institutional = 1,
}
