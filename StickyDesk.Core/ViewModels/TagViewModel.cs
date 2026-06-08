using StickyDesk.Core.Models;

namespace StickyDesk.Core.ViewModels;

/// <summary>Display projection of a <see cref="Tag"/> (chips + filter list).</summary>
public sealed class TagViewModel
{
    public TagViewModel(Tag t)
    {
        Id = t.Id;
        Name = t.Name;
        ColorHex = t.ColorHex;
    }

    public long Id { get; }
    public string Name { get; }
    public string ColorHex { get; }
}
