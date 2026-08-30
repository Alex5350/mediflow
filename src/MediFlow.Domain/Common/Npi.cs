namespace MediFlow.Domain.Common;

/// <summary>
/// A National Provider Identifier (NPI) — the 10-digit identifier assigned to
/// healthcare providers. Validation implements the official NPI check-digit
/// algorithm: Luhn over the 9 digits prefixed with the constant 80840.
/// </summary>
public readonly record struct Npi
{
    public const int Length = 10;
    private const string NpiPrefix = "80840";

    public string Value { get; }

    private Npi(string value) => Value = value;

    public static bool IsValid(string? candidate) => TryParse(candidate, out _);

    public static bool TryParse(string? candidate, out Npi npi)
    {
        npi = default;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var digits = candidate.Trim();
        if (digits.Length != Length)
        {
            return false;
        }

        if (!digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        // Luhn checksum over "80840" + the first 9 digits; the 10th digit is the check digit.
        var payload = string.Concat(NpiPrefix, digits.AsSpan(0, 9));
        var sum = 0;
        var doubleNext = true; // rightmost payload digit is doubled per NPI spec
        for (var i = payload.Length - 1; i >= 0; i--)
        {
            var d = payload[i] - '0';
            if (doubleNext)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }
            sum += d;
            doubleNext = !doubleNext;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        if (checkDigit != digits[9] - '0')
        {
            return false;
        }

        npi = new Npi(digits);
        return true;
    }

    public static Npi Parse(string candidate) =>
        TryParse(candidate, out var npi)
            ? npi
            : throw new FormatException($"'{candidate}' is not a valid National Provider Identifier.");

    public override string ToString() => Value;
}
