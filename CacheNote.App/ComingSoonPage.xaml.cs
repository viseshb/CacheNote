using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CacheNote_App;

/// <summary>
/// Parameterized placeholder for sections not yet built. Navigation parameter is
/// "Title|Subtitle|Milestone" (e.g. "Reminders|Time-based notifications|M3").
/// </summary>
public sealed partial class ComingSoonPage : Page
{
    public ComingSoonPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var parts = (e.Parameter as string ?? "Coming soon||").Split('|');
        TitleText.Text = parts.Length > 0 ? parts[0] : "Coming soon";
        SubtitleText.Text = parts.Length > 1 ? parts[1] : "";
        MilestoneText.Text = parts.Length > 2 ? $"Coming in {parts[2]}" : "Coming soon";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
