namespace MediFlow.Domain.Plans;

/// <summary>Plan product types offered to Medicare beneficiaries.</summary>
public enum PlanType
{
    /// <summary>Medicare Advantage (Part C) — replaces Original Medicare coverage.</summary>
    MedicareAdvantage = 1,

    /// <summary>Standalone Prescription Drug Plan (Part D).</summary>
    PrescriptionDrug = 2,
}

/// <summary>A plan product marketed for a given contract year.</summary>
public sealed class Plan
{
    public int Id { get; set; }

    /// <summary>Marketing code, e.g. MFP-2601 — unique within a contract year.</summary>
    public required string PlanCode { get; set; }

    public required string Name { get; set; }
    public required string Carrier { get; set; }
    public PlanType Type { get; set; }

    /// <summary>The benefit year this product's rates belong to.</summary>
    public int ContractYear { get; set; }

    public int MonthlyPremiumCents { get; set; }

    /// <summary>In-network medical deductible for the benefit year.</summary>
    public int DeductibleCents { get; set; }

    /// <summary>Member coinsurance share after deductible, in whole percent (e.g. 20).</summary>
    public byte CoinsurancePercent { get; set; }

    /// <summary>In-network out-of-pocket maximum for the benefit year.</summary>
    public int OopMaxCents { get; set; }

    /// <summary>CMS 5-star plans accept enrollments any month of the year.</summary>
    public bool IsFiveStar { get; set; }

    public bool IsActive { get; set; } = true;
}
