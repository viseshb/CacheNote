using StickyDesk.Core.Updates;

namespace StickyDesk.Tests;

public sealed class SemVerTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("1.2.0", "1.10.0", false)]   // numeric, not lexical
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0-beta", "1.0.0", false)] // pre-release tag ignored → equal
    [InlineData("1.1.0-rc1", "1.0.5", true)]
    public void IsNewer_ComparesNumerically(string latest, string current, bool expected)
        => Assert.Equal(expected, SemVer.IsNewer(latest, current));

    [Fact]
    public void TryParse_HandlesMissingParts()
    {
        Assert.True(SemVer.TryParse("v3", out var v));
        Assert.Equal((3, 0, 0), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void IsNewer_BadInput_IsFalse(string? latest)
        => Assert.False(SemVer.IsNewer(latest, "1.0.0"));
}
