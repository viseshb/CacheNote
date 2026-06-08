using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StickyDesk.Core.Data;
using StickyDesk.Core.Models;
using StickyDesk.Core.Services;

namespace StickyDesk.Core.ViewModels;

public enum CalendarViewMode { Month, Week, Day, Agenda }

/// <summary>
/// Drives the Calendar section: Month (6×7) / Week (1×7) grids, plus Day and Agenda list views.
/// Day cells and the agenda merge tasks, reminders, and calendar events (events recurrence-expanded
/// across the visible window). All due/fire/start times are UTC and projected to local dates here.
/// </summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly ITaskRepository _tasks;
    private readonly IReminderRepository _reminders;
    private readonly EventService _events;

    public ObservableCollection<CalendarDayViewModel> Days { get; } = new();
    public ObservableCollection<CalendarEntryViewModel> SelectedDayItems { get; } = new();
    public ObservableCollection<CalendarEntryViewModel> ListItems { get; } = new();
    public string[] WeekdayHeaders { get; } = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    [ObservableProperty]
    private string _periodLabel = "";

    [ObservableProperty]
    private string _selectedDayLabel = "";

    [ObservableProperty]
    private CalendarViewMode _mode = CalendarViewMode.Month;

    /// <summary>True for Month/Week (grid) views; false for Day/Agenda (list) views.</summary>
    [ObservableProperty]
    private bool _showGrid = true;

    /// <summary>Inverse of <see cref="ShowGrid"/> — true for the Day/Agenda list views.</summary>
    [ObservableProperty]
    private bool _showList;

    private DateTime _anchor;    // any date inside the shown period (local)
    private DateTime _selected;  // the selected day (local date)
    private Dictionary<DateTime, List<CalendarEntryViewModel>> _byDay = new();

    public CalendarViewModel(ITaskRepository tasks, IReminderRepository reminders, EventService events)
    {
        _tasks = tasks;
        _reminders = reminders;
        _events = events;
    }

    public DateTime SelectedDate => _selected;
    public DateTime AnchorDate => _anchor;

    public void Load()
    {
        _anchor = DateTime.Today;
        _selected = DateTime.Today;
        Rebuild();
    }

    /// <summary>Re-read data and rebuild (call after adding/editing/deleting an event).</summary>
    public void Reload() => Rebuild();

    public void SetMode(CalendarViewMode mode)
    {
        Mode = mode;
        Rebuild();
    }

    public void Prev()
    {
        _anchor = Mode switch
        {
            CalendarViewMode.Month => _anchor.AddMonths(-1),
            CalendarViewMode.Week => _anchor.AddDays(-7),
            CalendarViewMode.Day => _anchor.AddDays(-1),
            _ => _anchor.AddDays(-30),
        };
        if (Mode == CalendarViewMode.Day)
            _selected = _anchor;
        Rebuild();
    }

    public void Next()
    {
        _anchor = Mode switch
        {
            CalendarViewMode.Month => _anchor.AddMonths(1),
            CalendarViewMode.Week => _anchor.AddDays(7),
            CalendarViewMode.Day => _anchor.AddDays(1),
            _ => _anchor.AddDays(30),
        };
        if (Mode == CalendarViewMode.Day)
            _selected = _anchor;
        Rebuild();
    }

    public void GoToToday()
    {
        _anchor = DateTime.Today;
        _selected = DateTime.Today;
        Rebuild();
    }

    public void SelectDay(CalendarDayViewModel? day)
    {
        if (day is null)
            return;
        _selected = day.Date;
        foreach (var d in Days)
            d.IsSelected = d.Date == _selected;
        ShowSelectedItems();
    }

    private void Rebuild()
    {
        ShowGrid = Mode is CalendarViewMode.Month or CalendarViewMode.Week;
        ShowList = !ShowGrid;
        if (ShowGrid)
            BuildGrid();
        else
            BuildList();
    }

    private void BuildGrid()
    {
        var (start, count) = Mode == CalendarViewMode.Month ? MonthGrid() : WeekGrid();
        var windowEnd = start.AddDays(count - 1);
        BuildIndex(start, windowEnd);

        Days.Clear();
        var refMonth = _anchor.Month;
        for (var i = 0; i < count; i++)
        {
            var date = start.AddDays(i);
            var n = _byDay.TryGetValue(date, out var items) ? items.Count : 0;
            Days.Add(new CalendarDayViewModel
            {
                Date = date,
                DayNumber = date.Day.ToString(CultureInfo.CurrentCulture),
                InCurrentMonth = Mode == CalendarViewMode.Week || date.Month == refMonth,
                IsToday = date == DateTime.Today,
                ItemCount = n,
                IsSelected = date == _selected,
            });
        }

        PeriodLabel = Mode == CalendarViewMode.Month
            ? _anchor.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
            : $"Week of {WeekStart(_anchor):MMM d}";
        ShowSelectedItems();
    }

    private void BuildList()
    {
        DateTime windowStart, windowEnd;
        if (Mode == CalendarViewMode.Day)
        {
            windowStart = windowEnd = _anchor.Date;
            PeriodLabel = _anchor.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
        }
        else // Agenda
        {
            windowStart = _anchor.Date;
            windowEnd = _anchor.Date.AddDays(30);
            PeriodLabel = $"{windowStart:MMM d} – {windowEnd:MMM d}";
        }

        BuildIndex(windowStart, windowEnd);

        ListItems.Clear();
        for (var date = windowStart; date <= windowEnd; date = date.AddDays(1))
            if (_byDay.TryGetValue(date, out var items))
                foreach (var e in items.OrderBy(x => x.SortKey))
                    ListItems.Add(e);

        SelectedDayLabel = PeriodLabel;
    }

    private (DateTime start, int count) MonthGrid()
    {
        var first = new DateTime(_anchor.Year, _anchor.Month, 1);
        var offset = (int)first.DayOfWeek;   // Sunday = 0
        return (first.AddDays(-offset), 42);
    }

    private (DateTime start, int count) WeekGrid() => (WeekStart(_anchor), 7);

    private static DateTime WeekStart(DateTime d) => d.Date.AddDays(-(int)d.DayOfWeek);

    private void ShowSelectedItems()
    {
        SelectedDayItems.Clear();
        SelectedDayLabel = _selected.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
        if (_byDay.TryGetValue(_selected, out var items))
            foreach (var e in items.OrderBy(x => x.SortKey))
                SelectedDayItems.Add(e);
    }

    private void BuildIndex(DateTime windowStart, DateTime windowEnd)
    {
        _byDay = new Dictionary<DateTime, List<CalendarEntryViewModel>>();

        foreach (var t in _tasks.GetAll())
        {
            if (t.DueUtc is not DateTime due)
                continue;
            var local = DateTime.SpecifyKind(due, DateTimeKind.Utc).ToLocalTime();
            if (local.Date < windowStart.Date || local.Date > windowEnd.Date)
                continue;
            var color = t.Priority switch
            {
                TaskPriorities.High => "#DC2626",
                TaskPriorities.Low => "#16A34A",
                _ => "#D97706",
            };
            Add(local, new CalendarEntryViewModel(
                string.IsNullOrWhiteSpace(t.Title) ? "Task" : t.Title, local, "Task", color, t.IsCompleted));
        }

        foreach (var r in _reminders.GetAll())
        {
            if (r.IsDismissed)
                continue;
            var local = DateTime.SpecifyKind(r.EffectiveFireUtc, DateTimeKind.Utc).ToLocalTime();
            if (local.Date < windowStart.Date || local.Date > windowEnd.Date)
                continue;
            Add(local, new CalendarEntryViewModel(
                string.IsNullOrWhiteSpace(r.Message) ? "Reminder" : r.Message!, local, "Reminder", "#7C3AED", false));
        }

        foreach (var ev in _events.GetAll())
        {
            var localStart = DateTime.SpecifyKind(ev.StartUtc, DateTimeKind.Utc).ToLocalTime();
            foreach (var occ in Recurrence.Occurrences(localStart, ev.Recurrence, windowStart, windowEnd))
            {
                var kindLabel = ev.Kind switch
                {
                    EventKinds.Meeting => "Meeting",
                    EventKinds.Appointment => "Appt",
                    EventKinds.Birthday => "Birthday",
                    _ => "Event",
                };
                Add(occ, new CalendarEntryViewModel(
                    string.IsNullOrWhiteSpace(ev.Title) ? "Event" : ev.Title, occ, kindLabel,
                    ev.ColorHex, dimmed: false, eventId: ev.Id, meetingUrl: ev.MeetingUrl, allDay: ev.AllDay));
            }
        }
    }

    private void Add(DateTime localDt, CalendarEntryViewModel entry)
    {
        var key = localDt.Date;
        if (!_byDay.TryGetValue(key, out var list))
        {
            list = new List<CalendarEntryViewModel>();
            _byDay[key] = list;
        }
        list.Add(entry);
    }
}

/// <summary>One day cell in the calendar grid.</summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    public DateTime Date { get; init; }
    public string DayNumber { get; init; } = "";
    public bool InCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public int ItemCount { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    public double DayOpacity => InCurrentMonth ? 1.0 : 0.30;
    public bool HasItems => ItemCount > 0;
    public string CountText => ItemCount > 0 ? ItemCount.ToString(CultureInfo.CurrentCulture) : "";
}

/// <summary>A task, reminder, or event shown in a day cell / agenda.</summary>
public sealed class CalendarEntryViewModel
{
    public CalendarEntryViewModel(string title, DateTime localTime, string kind, string colorHex, bool dimmed,
        long eventId = 0, string? meetingUrl = null, bool allDay = false)
    {
        Title = title;
        Kind = kind;
        ColorHex = colorHex;
        EventId = eventId;
        MeetingUrl = meetingUrl;
        AllDay = allDay;
        TimeText = allDay ? "All day" : localTime.ToString("h:mm tt", CultureInfo.CurrentCulture);
        DateText = localTime.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
        SortKey = allDay ? TimeSpan.Zero : localTime.TimeOfDay;
        Opacity = dimmed ? 0.5 : 1.0;
    }

    public string Title { get; }
    public string Kind { get; }
    public string ColorHex { get; }
    public string TimeText { get; }
    public string DateText { get; }
    public TimeSpan SortKey { get; }
    public double Opacity { get; }

    public long EventId { get; }
    public string? MeetingUrl { get; }
    public bool AllDay { get; }
    public bool HasMeeting => !string.IsNullOrWhiteSpace(MeetingUrl);
    public bool IsEvent => EventId > 0;
}
