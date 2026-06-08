using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CacheNote_App.Converters;

/// <summary>true → Collapsed, false → Visible (inverse of BoolToVisibilityConverter).</summary>
public sealed partial class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Collapsed;
}
