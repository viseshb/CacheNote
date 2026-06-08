using CommunityToolkit.Mvvm.ComponentModel;

namespace StickyDesk.Core.ViewModels;

/// <summary>A row in the notes list (left pane of the Notes section).</summary>
public sealed partial class NoteListItemViewModel : ObservableObject
{
    public long Id { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title = "";

    [ObservableProperty]
    private string _snippet = "";

    [ObservableProperty]
    private bool _pinned;

    [ObservableProperty]
    private bool _favorite;

    /// <summary>Drives the custom rounded selection highlight in the list.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title;
}
