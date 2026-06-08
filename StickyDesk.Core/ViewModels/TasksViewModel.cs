using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using StickyDesk.Core.Models;
using StickyDesk.Core.Services;

namespace StickyDesk.Core.ViewModels;

/// <summary>Drives the Tasks section: list, create, complete, delete.</summary>
public sealed partial class TasksViewModel : ObservableObject
{
    private readonly ITaskService _service;

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = new();

    public TasksViewModel(ITaskService service) => _service = service;

    public void Load()
    {
        Tasks.Clear();
        foreach (var t in _service.GetAll())
            Tasks.Add(new TaskItemViewModel(t));
    }

    public long Create(string title, DateTime? dueUtc, string priority)
    {
        var id = _service.Create(noteId: null, title, description: null, dueUtc, priority);
        Load();
        return id;
    }

    public void SetCompleted(long id, bool completed)
    {
        _service.SetCompleted(id, completed);
        Load();
    }

    public void Delete(long id)
    {
        _service.Delete(id);
        Load();
    }
}

/// <summary>Display projection of a <see cref="TaskItem"/> for the list.</summary>
public sealed class TaskItemViewModel
{
    public TaskItemViewModel(TaskItem t)
    {
        Id = t.Id;
        Title = string.IsNullOrWhiteSpace(t.Title) ? "Untitled task" : t.Title;
        IsCompleted = t.IsCompleted;

        Priority = t.Priority;
        PriorityText = char.ToUpper(t.Priority[0], CultureInfo.CurrentCulture) + t.Priority[1..];
        PriorityColor = t.Priority switch
        {
            TaskPriorities.High => "#DC2626",
            TaskPriorities.Low => "#16A34A",
            _ => "#D97706",
        };

        if (t.DueUtc is DateTime due)
        {
            var local = DateTime.SpecifyKind(due, DateTimeKind.Utc).ToLocalTime();
            DueText = local.ToString("ddd, MMM d · h:mm tt", CultureInfo.CurrentCulture);
        }
        else
        {
            DueText = "No due date";
        }
    }

    public long Id { get; }
    public string Title { get; }
    public bool IsCompleted { get; }
    public string Priority { get; }
    public string PriorityText { get; }
    public string PriorityColor { get; }
    public string DueText { get; }

    public bool NotCompleted => !IsCompleted;
    public double TitleOpacity => IsCompleted ? 0.5 : 1.0;
}
