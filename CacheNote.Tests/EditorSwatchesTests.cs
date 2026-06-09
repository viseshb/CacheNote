using CacheNote.Core.Ui;

namespace CacheNote.Tests;

/// <summary>The editor font-colour swatches must never offer the near-invisible colour for a theme:
/// no black in dark mode, no white in light mode (the user's requirement).</summary>
public sealed class EditorSwatchesTests
{
    [Fact]
    public void Dark_HasNoBlack_ButKeepsWhite()
    {
        var dark = EditorSwatches.Visible(dark: true);
        Assert.DoesNotContain(dark, s => s.Hex == EditorSwatches.Black);
        Assert.Contains(dark, s => s.Hex == EditorSwatches.White);   // white is fine on dark
    }

    [Fact]
    public void Light_HasNoWhite_ButKeepsBlack()
    {
        var light = EditorSwatches.Visible(dark: false);
        Assert.DoesNotContain(light, s => s.Hex == EditorSwatches.White);
        Assert.Contains(light, s => s.Hex == EditorSwatches.Black);   // black is fine on light
    }

    [Fact]
    public void Both_Themes_Keep_The_Accent_Colors_And_Default()
    {
        foreach (var dark in new[] { true, false })
        {
            var v = EditorSwatches.Visible(dark);
            Assert.Contains(v, s => s.Hex == EditorSwatches.Auto);    // "Default"
            Assert.Contains(v, s => s.Hex == "#2563EB");              // accent blue
            Assert.Contains(v, s => s.Hex == "#DC2626");              // red
        }
    }
}
