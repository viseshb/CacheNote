using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CacheNote_App.Converters;

/// <summary>true → a translucent accent fill (selected row), false → transparent.
/// Theme-agnostic translucent tint reads well on both light and dark surfaces.</summary>
public sealed partial class BoolToSelectionBrushConverter : IValueConverter
{
    private static readonly Brush Selected = new SolidColorBrush(Color.FromArgb(0x3D, 0x25, 0x63, 0xEB));
    private static readonly Brush None = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? Selected : None;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
