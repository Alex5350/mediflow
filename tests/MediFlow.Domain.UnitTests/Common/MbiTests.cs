namespace MediFlow.Domain.UnitTests.Common;

using MediFlow.Domain.Common;
using Xunit;

public class MbiTests
{
    // The CMS example MBI from the beneficiary card fact sheet.
    [Theory]
    [InlineData("1EG4TE5MK73")]
    [InlineData("1EG4-TE5-MK73")]   // dashes accepted on input
    [InlineData(" 1eg4te5mk73 ")]   // case/space tolerant
    public void Accepts_valid_mbis(string candidate)
    {
        Assert.True(Mbi.IsValid(candidate));
    }

    [Theory]
    [InlineData("1EG4TE5MK7")]       // too short
    [InlineData("1EG4TE5MK733")]     // too long
    [InlineData("1EG4TE5MK7S")]      // ambiguous letter (S) banned anywhere
    [InlineData("0EG4TE5MK73")]      // position 1 may not be 0
    [InlineData("1SB4TE5MK73")]      // ambiguous letters (S, B) banned
    [InlineData("1EG4TE5MK7!")]      // punctuation
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_invalid_mbis(string candidate)
    {
        Assert.False(Mbi.IsValid(candidate));
    }

    [Fact]
    public void ToDisplay_groups_1_4_4_2()
    {
        var mbi = Mbi.Parse("1EG4TE5MK73");
        Assert.Equal("1EG4-TE5-MK73", mbi.ToDisplay());
        Assert.Equal("1EG4TE5MK73", mbi.Value);
        Assert.Equal("1EG4TE5MK73", mbi.ToString());
    }

    [Fact]
    public void Parse_throws_for_garbage()
    {
        Assert.Throws<FormatException>(() => Mbi.Parse("not-an-mbi"));
    }

    [Fact]
    public void Missing_value_fails_try_parse()
    {
        Assert.False(Mbi.TryParse(null, out _));
        Assert.False(Mbi.TryParse(string.Empty, out _));
    }
}
