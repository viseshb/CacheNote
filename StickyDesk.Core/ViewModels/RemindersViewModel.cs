using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using StickyDesk.Core.Models;
using StickyDesk.Core.Services;

namespace StickyDesk.Core.ViewModels;

/// <summary>Drives the Reminders section: list, create, complete, delete.</summary>
public sealed partial class RemindersViewModel : ObservableObject
{
    private readonly IReminderService _service;

    public ObservableCollection<ReminderItemViewModel> Reminders { get; } = new();

    public RemindersViewModel(IReminderService service) => _service = service;

    public void Load()
    {
        Reminders.Clear();
        foreach (var r in _service.GetAll())
            Reminders.Add(new ReminderItemViewModel(r));
    }

    public long Create(string? message, DateTime remindUtc, string repeat)
    {
        var id = _service.Create(noteId: null, message, remindUtc, repeat);
        Load();
        return id;
    }

    public void Complete(long id)
    {
        _service.Complete(id);
        Load();
    }

    public void Delete(long id)
    {
        _service.Delete(id);
        Load();
    }
}

/// <summary>Display projection of a <see cref="Reminder"/> for the list.</summary>
public sealed class ReminderItemViewModel
{
    public ReminderItemViewModel(Reminder r)
    {
        Id = r.Id;
        Title = string.IsNullOrWhiteSpace(r.Message) ? "Reminder" : r.Message!;
        IsDone = r.IsDismissed;

        var local = DateTime.SpecifyKind(r.EffectiveFireUtc, DateTimeKind.Utc).ToLocalTime();
        WhenText = local.ToString("ddd, MMM d · h:mm tt", CultureInfo.CurrentCulture);
        RepeatText = r.Repeat == RepeatKinds.Once
            ? ""
            : char.ToUpper(r.Repeat[0], CultureInfo.CurrentCulture) + r.Repeat[1..];
    }

    public long Id { get; }
    public string Title { get; }
    public string WhenText { get; }
    public string RepeatText { get; }
    public bool IsDone { get; }

    // UI-agnostic helpers (the View maps bools → Visibility via a converter).
    public bool NotDone => !IsDone;
    public bool HasRepeat => RepeatText.Length > 0;
    public double TitleOpacity => IsDone ? 0.5 : 1.0;
}
