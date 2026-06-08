using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace StickyDesk_App.Controls;

/// <summary>
/// A lightweight horizontal wrap panel (WinUI has no built-in one). Lays children
/// left-to-right and wraps to the next row when the available width runs out — the
/// basis for the app's fluid/responsive toolbars and card grids.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WrapPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        var max = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        var childConstraint = new Size(max, double.PositiveInfinity);

        double lineWidth = 0, lineHeight = 0, totalWidth = 0, totalHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            var d = child.DesiredSize;

            if (lineWidth > 0 && lineWidth + HorizontalSpacing + d.Width > max)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + VerticalSpacing;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += (lineWidth > 0 ? HorizontalSpacing : 0) + d.Width;
            lineHeight = Math.Max(lineHeight, d.Height);
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        return new Size(
            double.IsInfinity(max) ? totalWidth : Math.Min(totalWidth, max),
            totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children;
        var n = children.Count;
        var y = 0.0;
        var i = 0;

        while (i < n)
        {
            // Measure one line: how many children fit, and the tallest one.
            var start = i;
            double lineWidth = 0, lineHeight = 0;
            while (i < n)
            {
                var d = children[i].DesiredSize;
                var next = (lineWidth > 0 ? HorizontalSpacing : 0) + d.Width;
                if (lineWidth > 0 && lineWidth + next > finalSize.Width)
                    break;
                lineWidth += next;
                lineHeight = Math.Max(lineHeight, d.Height);
                i++;
            }

            // Arrange this line, vertically centered so combos/buttons line up.
            var x = 0.0;
            for (var j = start; j < i; j++)
            {
                var d = children[j].DesiredSize;
                var cy = y + (lineHeight - d.Height) / 2;
                children[j].Arrange(new Rect(x, cy, d.Width, d.Height));
                x += d.Width + HorizontalSpacing;
            }

            y += lineHeight + VerticalSpacing;
        }

        return finalSize;
    }
}
