using System.Collections.ObjectModel;
using CacheNote.Core.Data;

namespace CacheNote.Core.ViewModels;

/// <summary>Drives the Favorites section: notes that are starred or pinned.</summary>
public sealed class FavoritesViewModel
{
    private readonly INoteRepository _notes;

    public ObservableCollection<NoteListItemViewModel> Items { get; } = new();

    public FavoritesViewModel(INoteRepository notes) => _notes = notes;

    public void Load()
    {
        Items.Clear();
        foreach (var n in _notes.GetFavoritesAndPinned())
            Items.Add(new NoteListItemViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Snippet = Snippet(n.ContentPlain),
                Pinned = n.Pinned,
                Favorite = n.Favorite,
            });
    }

    private static string Snippet(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
            return "";
        var s = plain.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length > 80 ? s[..80] + "…" : s;
    }
}
