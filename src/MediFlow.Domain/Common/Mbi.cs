namespace MediFlow.Domain.Common;

/// <summary>
/// A Medicare Beneficiary Identifier (MBI) — the 11-character identifier printed on
/// Medicare cards (e.g. 1EG4-TE5-MK73). Validation applies the published CMS character
/// classes in simplified form: exactly 11 alphanumeric characters, no ambiguous letters
/// (B, I, L, O, S, Z) or the digits 0/1 in positions that must be unambiguous, with
/// dashes optional on input and never stored.
/// </summary>
public readonly record struct Mbi
{
    public const int Length = 11;

    // Letters the MBI format never uses because they are easily misread.
    private static readonly HashSet<char> AmbiguousLetters = ['B', 'I', 'L', 'O', 'S', 'Z'];

    public string Value { get; }

    private Mbi(string value) => Value = value;

    public static bool IsValid(string? candidate) => TryParse(candidate, out _);

    public static bool TryParse(string? candidate, out Mbi mbi)
    {
        mbi = default;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var compact = candidate.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (compact.Length != Length)
        {
            return false;
        }

        foreach (var c in compact)
        {
            if (char.IsAsciiDigit(c))
            {
                continue;
            }

            if (char.IsAsciiLetter(c) && !AmbiguousLetters.Contains(c))
            {
                continue;
            }

            return false;
        }

        // Position 1 is never 0 and the first character class in the CMS format
        // is "digit or consonant", so 0 at position 1 is invalid.
        if (compact[0] == '0')
        {
            return false;
        }

        mbi = new Mbi(compact);
        return true;
    }

    /// <summary>Throws <see cref="FormatException"/> when the candidate is not a valid MBI.</summary>
    public static Mbi Parse(string candidate) =>
        TryParse(candidate, out var mbi)
            ? mbi
            : throw new FormatException($"'{candidate}' is not a valid Medicare Beneficiary Identifier.");

    /// <summary>Display form with CMS grouping (1EG4-TE5-MK73).</summary>
    public string ToDisplay() => $"{Value[..4]}-{Value[4..7]}-{Value[7..]}";

    public override string ToString() => Value;
}
