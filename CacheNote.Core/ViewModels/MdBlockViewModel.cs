using CommunityToolkit.Mvvm.ComponentModel;

namespace CacheNote.Core.ViewModels;

/// <summary>A Markdown block in the editor: raw source (edited as monospace) + a preview toggle.</summary>
public sealed partial class MdBlockViewModel : ObservableObject
{
    public long Id { get; init; }

    [ObservableProperty]
    private string _content = "";

    /// <summary>When true, show the rendered Markdown; otherwise the editable source.</summary>
    [ObservableProperty]
    private bool _preview;
}
