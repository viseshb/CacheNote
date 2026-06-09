using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CacheNote.Core.Models;
using CacheNote.Core.Services;
using CacheNote.Core.ViewModels;
using Windows.System;

namespace CacheNote_App;

/// <summary>
/// Calendar section: Month / Week grids + Day / Agenda lists, merging tasks, reminders, and
/// calendar events. Create / edit / delete events (location, meeting link with Join, all-day,
/// recurrence, colour, and an alert routed through the reminder/toast engine).
/// </summary>
public sealed partial class CalendarPage : Page
{
    public CalendarViewModel Vm { get; }
    private readonly EventService _events = App.GetService<EventService>();

    private static readonly string[] Kinds = [EventKinds.Event, EventKinds.Meeting, EventKinds.Appointment, EventKinds.Birthday];
    private static readonly string[] Recurrences = [EventRecurrence.None, EventRecurrence.Daily, EventRecurrence.Weekly, EventRecurrence.Monthly, EventRecurrence.Yearly];
    private static readonly (string Label, string Hex)[] Colors =
    [
        ("Blue", "#2563EB"), ("Purple", "#7C3AED"), ("Green", "#16A34A"),
        ("Amber", "#D97706"), ("Red", "#DC2626"), ("Teal", "#0EA5E9"),
    ];
    private static readonly (string Label, int? Minutes)[] Alerts =
    [
        ("No alert", null), ("At start time", 0), ("5 minutes before", 5),
        ("10 minutes before", 10), ("30 minutes before", 30), ("1 hour before", 60), ("1 day before", 1440),
    ];

    public CalendarPage()
    {
        Vm = App.GetService<CalendarViewModel>();
        InitializeComponent();

        Loaded += (_, _) => { Vm.Load(); UpdateEmpty(); };
        Vm.SelectedDayItems.CollectionChanged += (_, _) => UpdateEmpty();
        Vm.ListItems.CollectionChanged += (_, _) => UpdateEmpty();
    }

    private void UpdateEmpty()
    {
        AgendaEmpty.Visibility = Vm.SelectedDayItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListEmpty.Visibility = Vm.ListItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CalendarDayViewModel day)
            Vm.SelectDay(day);
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => Vm.Prev();

    private void Next_Click(object sender, RoutedEventArgs e) => Vm.Next();

    private void Today_Click(object sender, RoutedEventArgs e) => Vm.GoToToday();

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        Vm.SetMode(ModeCombo.SelectedIndex switch
        {
            1 => CalendarViewMode.Week,
            2 => CalendarViewMode.Day,
            3 => CalendarViewMode.Agenda,
            _ => CalendarViewMode.Month,
        });
    }

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string url && !string.IsNullOrWhiteSpace(url))
        {
            if (!url.Contains("://"))
                url = "https://" + url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                await Launcher.LaunchUriAsync(uri);
        }
    }

    private async void AddEvent_Click(object sender, RoutedEventArgs e) => await ShowEventDialog(null);

    private async void EditEvent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is long id && _events.GetById(id) is { } ev)
            await ShowEventDialog(ev);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async System.Threading.Tasks.Task ShowEventDialog(CalendarEvent? existing)
    {
        var editing = existing is not null;
        var localStart = editing
            ? DateTime.SpecifyKind(existing!.StartUtc, DateTimeKind.Utc).ToLocalTime()
            : DefaultStart();
        var localEnd = editing && existing!.EndUtc.HasValue
            ? DateTime.SpecifyKind(existing.EndUtc.Value, DateTimeKind.Utc).ToLocalTime()
            : localStart.AddHours(1);

        var title = new TextBox { PlaceholderText = "Title", Text = existing?.Title ?? "" };
        title.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty, "EventTitle");

        var datePick = new CalendarDatePicker { Date = new DateTimeOffset(localStart.Date), HorizontalAlignment = HorizontalAlignment.Stretch };
        datePick.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty, "EventDate");

        var allDay = new ToggleSwitch { Header = "All day", IsOn = existing?.AllDay ?? false };
        allDay.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty, "EventAllDay");

        var startTime = new TimePicker { Header = "Start", Time = localStart.TimeOfDay, ClockIdentifier = "12HourClock", MinWidth = 130 };
        var endTime = new TimePicker { Header = "End", Time = localEnd.TimeOfDay, ClockIdentifier = "12HourClock", MinWidth = 130 };
        void SyncTimeEnabled() { startTime.IsEnabled = endTime.IsEnabled = !allDay.IsOn; }
        SyncTimeEnabled();
        allDay.Toggled += (_, _) => SyncTimeEnabled();

        var kind = new ComboBox { Header = "Type", MinWidth = 150 };
        foreach (var k in Kinds)
            kind.Items.Add(new ComboBoxItem { Content = Capitalize(k) });
        kind.SelectedIndex = Math.Max(0, Array.IndexOf(Kinds, existing?.Kind ?? EventKinds.Event));

        var recurrence = new ComboBox { Header = "Repeat", MinWidth = 150 };
        foreach (var r in Recurrences)
            recurrence.Items.Add(new ComboBoxItem { Content = r == EventRecurrence.None ? "Does not repeat" : Capitalize(r) });
        recurrence.SelectedIndex = Math.Max(0, Array.IndexOf(Recurrences, existing?.Recurrence ?? EventRecurrence.None));

        var color = new ComboBox { Header = "Colour", MinWidth = 150 };
        foreach (var (label, _) in Colors)
            color.Items.Add(new ComboBoxItem { Content = label });
        color.SelectedIndex = Math.Max(0, Array.FindIndex(Colors, c => c.Hex == (existing?.ColorHex ?? "#2563EB")));

        var location = new TextBox { Header = "Location", PlaceholderText = "Optional", Text = existing?.Location ?? "" };
        var meetingUrl = new TextBox { Header = "Meeting link (Google Meet / Zoom / Teams)", PlaceholderText = "https://…", Text = existing?.MeetingUrl ?? "" };
        meetingUrl.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty, "EventMeetingUrl");

        var alert = new ComboBox { Header = "Alert", MinWidth = 180 };
        foreach (var (label, _) in Alerts)
            alert.Items.Add(new ComboBoxItem { Content = label });
        alert.SelectedIndex = Math.Max(0, Array.FindIndex(Alerts, a => a.Minutes == existing?.AlertMinutes));

        var times = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        times.Children.Add(startTime);
        times.Children.Add(endTime);

        var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        typeRow.Children.Add(kind);
        typeRow.Children.Add(recurrence);
        typeRow.Children.Add(color);

        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(title);
        panel.Children.Add(datePick);
        panel.Children.Add(allDay);
        panel.Children.Add(times);
        panel.Children.Add(typeRow);
        panel.Children.Add(location);
        panel.Children.Add(meetingUrl);
        panel.Children.Add(alert);

        var dialog = new ContentDialog
        {
            Title = editing ? "Edit event" : "New event",
            PrimaryButtonText = "Save",
            SecondaryButtonText = editing ? "Delete" : null,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new ScrollViewer { Content = panel, MaxHeight = 520, HorizontalScrollMode = ScrollMode.Disabled },
        };
        dialog.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty, "EventDialog");

        var result = await DialogHost.ShowAsync(dialog);

        if (result == ContentDialogResult.Primary)
        {
            var date = datePick.Date?.Date ?? localStart.Date;
            var isAllDay = allDay.IsOn;
            var startLocal = isAllDay ? date : date + startTime.Time;
            DateTime? endLocal = isAllDay ? null : date + endTime.Time;
            if (endLocal is { } el && el <= startLocal)
                endLocal = startLocal.AddHours(1);

            var ev = existing ?? new CalendarEvent();
            ev.Title = string.IsNullOrWhiteSpace(title.Text) ? "Untitled event" : title.Text.Trim();
            ev.StartUtc = startLocal.ToUniversalTime();
            ev.EndUtc = endLocal?.ToUniversalTime();
            ev.AllDay = isAllDay;
            ev.Kind = Kinds[Math.Max(0, kind.SelectedIndex)];
            ev.Recurrence = Recurrences[Math.Max(0, recurrence.SelectedIndex)];
            ev.ColorHex = Colors[Math.Max(0, color.SelectedIndex)].Hex;
            ev.Location = string.IsNullOrWhiteSpace(location.Text) ? null : location.Text.Trim();
            ev.MeetingUrl = string.IsNullOrWhiteSpace(meetingUrl.Text) ? null : meetingUrl.Text.Trim();
            ev.AlertMinutes = Alerts[Math.Max(0, alert.SelectedIndex)].Minutes;

            _events.Save(ev);
            Vm.Reload();
            UpdateEmpty();
        }
        else if (result == ContentDialogResult.Secondary && editing)
        {
            _events.Delete(existing!.Id);
            Vm.Reload();
            UpdateEmpty();
        }
    }

    private DateTime DefaultStart()
    {
        var day = Vm.SelectedDate == default ? DateTime.Today : Vm.SelectedDate;
        var now = DateTime.Now;
        // Default to the next round hour on the selected day.
        return day.Date.AddHours(now.Hour + 1);
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
