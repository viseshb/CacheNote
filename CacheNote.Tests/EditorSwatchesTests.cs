using CacheNote.Core.Ui;

namespace CacheNote.Tests;

/// <summary>The editor font-colour swatches must never offer black in this dark-theme app.</summary>
public sealed class EditorSwatchesTests
{
    [Fact]
    public void Dark_KeepsWhiteAndDefault()
    {
        var dark = EditorSwatches.Visible(dark: true);
        Assert.Contains(dark, s => s.Hex == EditorSwatches.White);
        Assert.Contains(dark, s => s.Hex == EditorSwatches.Auto);
    }

    [Fact]
    public void Swatches_Never_Offer_Black()
    {
        foreach (var dark in new[] { true, false })
            Assert.DoesNotContain(EditorSwatches.Visible(dark), s => s.Hex.Equals("#000000", StringComparison.OrdinalIgnoreCase) || s.Hex.Equals("#18181B", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Both_Themes_Keep_The_Accent_Colors_And_Default()
    {
        foreach (var dark in new[] { true, false })
        {
            var v = EditorSwatches.Visible(dark);
            Assert.Contains(v, s => s.Hex == EditorSwatches.Auto);    // "Default"
            Assert.Contains(v, s => s.Hex == "#2563EB");              // accent blue
            Assert.Contains(v, s => s.Hex == "#EF4444");              // red
        }
    }
}
