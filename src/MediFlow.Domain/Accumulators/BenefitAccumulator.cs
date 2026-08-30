namespace MediFlow.Domain.Accumulators;

/// <summary>
/// A member's year-to-date benefit accumulators for one benefit year. The
/// adjudication engine advances these as it processes lines and reports the new
/// values back with its decision; the worker commits them transactionally.
/// </summary>
public sealed class BenefitAccumulator
{
    public int Id { get; set; }

    public int MemberId { get; set; }

    /// <summary>Accumulators are tracked per member per benefit year (not per plan).</summary>
    public int BenefitYear { get; set; }

    /// <summary>Deductible dollars met year-to-date, in cents.</summary>
    public int DeductibleMetCents { get; set; }

    /// <summary>Out-of-pocket dollars met year-to-date, in cents.</summary>
    public int OopMetCents { get; set; }
}
