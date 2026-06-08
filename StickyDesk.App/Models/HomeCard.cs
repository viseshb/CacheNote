using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace StickyDesk_App.Models;

/// <summary>A single tile on the home hub.</summary>
public sealed class HomeCard
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }

    /// <summary>Segoe Fluent glyph code point (e.g. 0xE70F).</summary>
    public required int GlyphCode { get; init; }

    /// <summary>Glyph as a string for FontIcon binding.</summary>
    public string Glyph => ((char)GlyphCode).ToString();

    /// <summary>Accent color hex (#RRGGBB).</summary>
    public required string AccentHex { get; init; }

    /// <summary>Navigation key handled by the home page (e.g. "notes").</summary>
    public required string Target { get; init; }

    public SolidColorBrush AccentBrush => new(ColorFromHex(AccentHex));

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(
            255,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }
}
