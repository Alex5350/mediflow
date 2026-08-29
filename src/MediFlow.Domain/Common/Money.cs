namespace MediFlow.Domain.Common;

/// <summary>
/// Money helpers. All monetary amounts in MediFlow are stored as US-cents integers
/// (<c>*Cents</c> fields) to keep arithmetic exact; decimals appear only at the edges.
/// </summary>
public static class Money
{
    /// <summary>Rounds a fractional dollar amount to cents away from zero — the
    /// deterministic convention used for coinsurance splits (see ADR 0004).</summary>
    public static int FromDollars(decimal dollars) =>
        (int)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);

    /// <summary>Percent of an amount in cents, rounded away from zero at half-cent boundaries.</summary>
    public static int PercentOf(int cents, byte percent) =>
        (int)Math.Round(cents * percent / 100m, MidpointRounding.AwayFromZero);

    /// <summary>"$1,234.56" — negative amounts render as -$1,234.56.</summary>
    public static string Format(int cents) =>
        (cents < 0 ? "-" : "") + "$" + (Math.Abs(cents) / 100m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
}
