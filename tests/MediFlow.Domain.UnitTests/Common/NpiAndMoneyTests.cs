namespace MediFlow.Domain.UnitTests.Common;

using MediFlow.Domain.Common;
using Xunit;

public class NpiTests
{
    // 1234567893 is the canonical valid test NPI (check digit verifies).
    [Theory]
    [InlineData("1234567893")]
    public void Accepts_valid_npis(string candidate)
    {
        Assert.True(Npi.IsValid(candidate));
    }

    [Theory]
    [InlineData("1234567890")]   // check digit wrong
    [InlineData("123456789")]    // too short
    [InlineData("12345678934")]  // too long
    [InlineData("123456789a")]   // non-digit
    [InlineData("")]
    public void Rejects_invalid_npis(string candidate)
    {
        Assert.False(Npi.IsValid(candidate));
    }

    [Fact]
    public void Check_digit_uses_the_80840_prefix()
    {
        // Construct a valid NPI for any 9 digits by computing the check digit
        // with the documented algorithm, then assert round-trip validity.
        foreach (var digits in new[] { "100000000", "999999999", "523456789" })
        {
            var payload = "80840" + digits;
            var sum = 0;
            var doubleNext = true;
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
            Assert.True(Npi.IsValid(digits + checkDigit), $"{digits}{checkDigit} should be valid");
        }
    }

    [Fact]
    public void Null_or_whitespace_fails()
    {
        Assert.False(Npi.IsValid(null));
        Assert.False(Npi.TryParse("  ", out _));
    }
}

public class MoneyTests
{
    [Theory]
    [InlineData(0, "$0.00")]
    [InlineData(123456, "$1,234.56")]
    [InlineData(5, "$0.05")]
    [InlineData(-250, "-$2.50")]
    public void Format_renders_dollars(int cents, string expected)
    {
        Assert.Equal(expected, Money.Format(cents));
    }

    [Fact]
    public void PercentOf_rounds_away_from_zero_at_half_cent()
    {
        // 10% of 95c = 9.5c → 10c (away from zero), never banker's 10 vs 9 ambiguity
        Assert.Equal(10, Money.PercentOf(95, 10));
        Assert.Equal(9, Money.PercentOf(85, 10));
        Assert.Equal(0, Money.PercentOf(4, 10));
    }
}
