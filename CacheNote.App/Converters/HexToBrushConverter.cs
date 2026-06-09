using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CacheNote_App.Converters;

/// <summary>"#RRGGBB" (or "#AARRGGBB") → SolidColorBrush, for tag/priority color dots + chips.</summary>
public sealed partial class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Defensive: the hex comes from DB text (events.color_hex etc.) — a malformed value
        // would throw inside x:Bind evaluation and crash the page render. Fall back to gray.
        var fallback = new SolidColorBrush(Color.FromArgb(0xFF, 0x71, 0x71, 0x7A));
        var hex = (value as string)?.TrimStart('#');
        if (string.IsNullOrEmpty(hex) || (hex.Length != 6 && hex.Length != 8))
            return fallback;

        try
        {
            byte a = 0xFF, r, g, b;
            if (hex.Length == 8)
            {
                a = System.Convert.ToByte(hex.Substring(0, 2), 16);
                r = System.Convert.ToByte(hex.Substring(2, 2), 16);
                g = System.Convert.ToByte(hex.Substring(4, 2), 16);
                b = System.Convert.ToByte(hex.Substring(6, 2), 16);
            }
            else
            {
                r = System.Convert.ToByte(hex.Substring(0, 2), 16);
                g = System.Convert.ToByte(hex.Substring(2, 2), 16);
                b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            }
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }
        catch
        {
            return fallback;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
