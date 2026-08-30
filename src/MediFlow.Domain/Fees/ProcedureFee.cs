namespace MediFlow.Domain.Fees;

/// <summary>
/// One CPT/HCPCS code on the plan's in-network fee schedule for a benefit year.
/// Codes absent from the schedule (or <see cref="IsCovered"/> = false) adjudicate
/// as CO-96 non-covered.
/// </summary>
public sealed class ProcedureFee
{
    public int Id { get; set; }

    /// <summary>Five-character CPT/HCPCS code, e.g. 99213 or J1885.</summary>
    public required string ProcedureCode { get; set; }

    public required string Description { get; set; }

    /// <summary>Negotiated in-network allowed amount in cents.</summary>
    public int AllowedCents { get; set; }

    public bool IsCovered { get; set; } = true;

    /// <summary>Benefit year this fee row applies to.</summary>
    public int EffectiveYear { get; set; }
}
