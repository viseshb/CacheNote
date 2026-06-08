namespace CacheNote_App.Controls;

/// <summary>
/// One source of truth for the app's responsive breakpoint. Below <see cref="CompactMax"/>
/// (element width in DIPs) pages switch to their single-column / collapsed layout
/// (master-detail notes, toolbars folded into a dropdown, rows stacked).
/// </summary>
public static class Responsive
{
    /// <summary>Width (DIP) below which a page is "compact".</summary>
    public const double CompactMax = 640;

    public static bool IsCompact(double width) => width < CompactMax;
}
