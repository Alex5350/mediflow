namespace MediFlow.Domain.Members;

using Common;

/// <summary>A Medicare beneficiary enrolled with the plan sponsor.</summary>
public sealed class Member
{
    public int Id { get; set; }

    /// <summary>Medicare Beneficiary Identifier, stored without dashes (see <see cref="Mbi"/>).</summary>
    public required string Mbi { get; set; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public DateOnly DateOfBirth { get; set; }

    /// <summary>Two-letter USPS state code.</summary>
    public required string StateCode { get; set; }

    /// <summary>Part A entitlement date, if entitled.</summary>
    public DateOnly? PartAEffective { get; set; }

    /// <summary>Part B entitlement date, if entitled. MA/PDP enrollment requires Part B.</summary>
    public DateOnly? PartBEffective { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string DisplayName => $"{LastName}, {FirstName}";
}
