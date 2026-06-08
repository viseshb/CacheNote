using CommunityToolkit.Mvvm.ComponentModel;

namespace StickyDesk.Core.ViewModels;

/// <summary>One row in the inline checklist. Raises change notifications so the
/// owning <see cref="EditorViewModel"/> can persist edits.</summary>
public sealed partial class ChecklistItemViewModel : ObservableObject
{
    public long Id { get; init; }

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _isDone;
}
