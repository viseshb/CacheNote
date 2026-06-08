using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace StickyDesk_App.Controls;

/// <summary>
/// Centers its (single) child at width = min(available, <see cref="MaxContentWidth"/>) and full
/// height. Deterministic — unlike Stretch+MaxWidth or star-grid tricks it works even when the
/// child is a ScrollViewer or has no intrinsic width. Wide screens → centered column; narrow →
/// full width. Used to keep page content centered (see also the CenteredColumn style).
/// </summary>
public sealed partial class CenterMaxPanel : Panel
{
    public static readonly DependencyProperty MaxContentWidthProperty =
        DependencyProperty.Register(nameof(MaxContentWidth), typeof(double), typeof(CenterMaxPanel),
            new PropertyMetadata(820.0, (d, _) => ((CenterMaxPanel)d).InvalidateMeasure()));

    public double MaxContentWidth
    {
        get => (double)GetValue(MaxContentWidthProperty);
        set => SetValue(MaxContentWidthProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? MaxContentWidth : Math.Min(available.Width, MaxContentWidth);
        double maxH = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(w, available.Height));
            maxH = Math.Max(maxH, child.DesiredSize.Height);
        }
        return new Size(
            double.IsInfinity(available.Width) ? w : available.Width,
            double.IsInfinity(available.Height) ? maxH : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var w = Math.Min(final.Width, MaxContentWidth);
        var x = Math.Max(0, (final.Width - w) / 2);
        foreach (var child in Children)
            child.Arrange(new Rect(x, 0, w, final.Height));
        return final;
    }
}
