using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StickyDesk_App.Models;

namespace StickyDesk_App;

/// <summary>
/// The landing hub. Shows feature cards; clicking one navigates to that section
/// only (onboarding-style), instead of putting everything on one page.
/// </summary>
public sealed partial class HomePage : Page
{
    public IReadOnlyList<HomeCard> Cards { get; } =
    [
        new() { Title = "Notes",     Subtitle = "Rich notes & checklists",  GlyphCode = 0xE70F, AccentHex = "#2563EB", Target = "notes" },
        new() { Title = "Tasks",     Subtitle = "To-dos with priority",     GlyphCode = 0xE8FD, AccentHex = "#16A34A", Target = "tasks" },
        new() { Title = "Reminders", Subtitle = "Time-based nudges",        GlyphCode = 0xE823, AccentHex = "#D97706", Target = "reminders" },
        new() { Title = "Calendar",  Subtitle = "Month & week view",        GlyphCode = 0xE787, AccentHex = "#7C3AED", Target = "calendar" },
        new() { Title = "Favorites", Subtitle = "Starred & pinned",         GlyphCode = 0xE735, AccentHex = "#DC2626", Target = "favorites" },
        new() { Title = "Settings",  Subtitle = "Theme, startup, keys",     GlyphCode = 0xE713, AccentHex = "#71717A", Target = "settings" },
    ];

    public HomePage() => InitializeComponent();

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        var target = (sender as Button)?.Tag as string;
        switch (target)
        {
            case "notes":
                Frame.Navigate(typeof(MainPage));
                break;
            case "tasks":
                Frame.Navigate(typeof(TasksPage));
                break;
            case "reminders":
                Frame.Navigate(typeof(RemindersPage));
                break;
            case "calendar":
                Frame.Navigate(typeof(CalendarPage));
                break;
            case "favorites":
                Frame.Navigate(typeof(FavoritesPage));
                break;
            case "settings":
                Frame.Navigate(typeof(SettingsPage));
                break;
        }
    }
}
