using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CacheNote_App.Converters;

/// <summary>"#RRGGBB" (or "#AARRGGBB") → SolidColorBrush, for tag/priority color dots + chips.</summary>
public sealed partial class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hex = (value as string)?.TrimStart('#');
        if (string.IsNullOrEmpty(hex))
            return new SolidColorBrush(Color.FromArgb(0xFF, 0x71, 0x71, 0x7A));

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

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
