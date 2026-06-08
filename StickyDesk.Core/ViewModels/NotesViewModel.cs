using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StickyDesk.Core.Data;
using StickyDesk.Core.Models;
using StickyDesk.Core.Services;

namespace StickyDesk.Core.ViewModels;

/// <summary>
/// Drives the Notes section: the list of notes (left) and the active note's editor
/// state (right). The RTF body round-trips through the view's RichEditBox via
/// <see cref="ContentRequested"/> / <see cref="GetContentRtf"/> / <see cref="SaveContent"/>.
/// </summary>
public sealed partial class NotesViewModel : ObservableObject
{
    private readonly INoteRepository _notes;
    private readonly IChecklistRepository _checklist;
    private readonly ITagService _tags;
    private readonly ISearchService _search;
    private readonly IMdBlockRepository _md;

    public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();
    public ObservableCollection<ChecklistItemViewModel> Items { get; } = new();

    /// <summary>Markdown blocks ({} tool) appended to the current note.</summary>
    public ObservableCollection<MdBlockViewModel> MdBlocks { get; } = new();

    /// <summary>Tags attached to the currently-open note (chips under the title).</summary>
    public ObservableCollection<TagViewModel> CurrentNoteTags { get; } = new();

    /// <summary>All tags in the app (the list-pane filter dropdown).</summary>
    public ObservableCollection<TagViewModel> AllTags { get; } = new();

    [ObservableProperty]
    private NoteListItemViewModel? _selected;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _savedStatus = "";

    public long CurrentNoteId { get; private set; }

    /// <summary>Raised when the view should load a note's RTF into the editor.</summary>
    public event Action<long>? ContentRequested;

    public NotesViewModel(INoteRepository notes, IChecklistRepository checklist, ITagService tags, ISearchService search, IMdBlockRepository md)
    {
        _notes = notes;
        _checklist = checklist;
        _tags = tags;
        _search = search;
        _md = md;
    }

    public void LoadList()
    {
        Notes.Clear();
        foreach (var n in _notes.GetAllActive())
            Notes.Add(ToItem(n));

        if (Notes.Count == 0)
            Notes.Add(ToItem(InsertBlank()));

        Select(Notes[0]);
        LoadAllTags();
    }

    /// <summary>
    /// Rebuild the list from a search query (FTS) or a tag filter; null/empty both → show all.
    /// Unlike <see cref="LoadList"/> this does NOT create a blank note when results are empty.
    /// </summary>
    public void ApplyFilter(string? query, long? tagId)
    {
        Notes.Clear();

        IReadOnlyList<Note> result;
        if (!string.IsNullOrWhiteSpace(query))
            result = _search.SearchNotes(query);
        else if (tagId is long t)
            result = _tags.GetNotesForTag(t);
        else
            result = _notes.GetAllActive();

        foreach (var n in result)
            Notes.Add(ToItem(n));

        if (Notes.Count > 0)
            Select(Notes[0]);
        else
            Items.Clear();
    }

    public void LoadAllTags()
    {
        AllTags.Clear();
        foreach (var t in _tags.GetAll())
            AllTags.Add(new TagViewModel(t));
    }

    private void LoadTagsForCurrent()
    {
        CurrentNoteTags.Clear();
        if (CurrentNoteId == 0)
            return;
        foreach (var t in _tags.GetForNote(CurrentNoteId))
            CurrentNoteTags.Add(new TagViewModel(t));
    }

    public void AddTagToCurrent(string name)
    {
        if (CurrentNoteId == 0 || string.IsNullOrWhiteSpace(name))
            return;
        var id = _tags.GetOrCreate(name.Trim());
        _tags.AddToNote(CurrentNoteId, id);
        LoadTagsForCurrent();
        LoadAllTags();
    }

    public void RemoveTagFromCurrent(long tagId)
    {
        if (CurrentNoteId == 0)
            return;
        _tags.RemoveFromNote(CurrentNoteId, tagId);
        LoadTagsForCurrent();
    }

    /// <summary>Load a note into the editor and mark it selected.</summary>
    public void Select(NoteListItemViewModel? item)
    {
        if (item is null)
            return;

        foreach (var n in Notes)
            n.IsSelected = ReferenceEquals(n, item);

        CurrentNoteId = item.Id;
        var note = _notes.GetById(item.Id);
        Title = note?.Title ?? "";

        Items.Clear();
        foreach (var ci in _checklist.GetByNote(item.Id))
            Items.Add(Wrap(ci));

        MdBlocks.Clear();
        foreach (var mb in _md.GetByNote(item.Id))
            MdBlocks.Add(WrapMd(mb));

        LoadTagsForCurrent();
        ContentRequested?.Invoke(item.Id);
        Selected = item;
    }

    public byte[]? GetContentRtf(long id) => _notes.GetById(id)?.ContentRtf;

    public void SaveContent(byte[] rtf, string plain)
    {
        if (CurrentNoteId == 0)
            return;

        _notes.UpdateContent(CurrentNoteId, Title, rtf, plain);

        // Update the row for the note actually being saved (CurrentNoteId) — NOT
        // Selected, which the two-way binding may have already moved to another note.
        var item = Notes.FirstOrDefault(n => n.Id == CurrentNoteId);
        if (item is not null)
        {
            item.Title = Title;
            item.Snippet = Snippet(plain);
        }
        SavedStatus = $"Saved {DateTime.Now:HH:mm:ss}";
    }

    public long NewNote()
    {
        var item = ToItem(InsertBlank());
        Notes.Insert(0, item);
        return item.Id;
    }

    public long Duplicate()
    {
        var src = CurrentNoteId == 0 ? null : _notes.GetById(CurrentNoteId);
        if (src is null)
            return 0;

        var copy = new Note
        {
            Title = string.IsNullOrWhiteSpace(src.Title) ? "Untitled copy" : src.Title + " copy",
            ContentRtf = src.ContentRtf,
            ContentPlain = src.ContentPlain,
        };
        var id = _notes.Insert(copy);

        var order = 0;
        foreach (var ci in _checklist.GetByNote(CurrentNoteId))
        {
            var newId = _checklist.Add(id, ci.Text, order++);
            if (ci.IsDone)
                _checklist.SetDone(newId, true);
        }

        Notes.Insert(0, ToItem(_notes.GetById(id)!));
        return id;
    }

    public void ArchiveCurrent()
    {
        if (CurrentNoteId == 0)
            return;
        _notes.SetArchived(CurrentNoteId, true);
        DropCurrentAndReselect();
    }

    public void DeleteCurrent()
    {
        if (CurrentNoteId == 0)
            return;
        _notes.SoftDelete(CurrentNoteId);
        DropCurrentAndReselect();
    }

    [RelayCommand]
    private void TogglePin()
    {
        if (Selected is null)
            return;
        var pinned = !Selected.Pinned;
        _notes.SetPinned(Selected.Id, pinned);
        ReloadPreservingSelection(Selected.Id);
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Selected is null)
            return;
        var fav = !Selected.Favorite;
        _notes.SetFavorite(Selected.Id, fav);
        Selected.Favorite = fav;
    }

    [RelayCommand]
    private void AddChecklistItem()
    {
        if (CurrentNoteId == 0)
            return;
        var id = _checklist.Add(CurrentNoteId, "", Items.Count);
        Items.Add(Wrap(new ChecklistItem { Id = id, NoteId = CurrentNoteId }));
    }

    public void RemoveItem(ChecklistItemViewModel vm)
    {
        _checklist.Delete(vm.Id);
        Items.Remove(vm);
    }

    /// <summary>Insert a Markdown block ({} tool) into the current note (appended).</summary>
    [RelayCommand]
    private void AddMarkdownBlock()
    {
        if (CurrentNoteId == 0)
            return;
        var id = _md.Add(CurrentNoteId, "", MdBlocks.Count);
        var vm = WrapMd(new MdBlock { Id = id, NoteId = CurrentNoteId });
        vm.Preview = false;   // open new blocks in edit mode
        MdBlocks.Add(vm);
    }

    public void RemoveMdBlock(MdBlockViewModel vm)
    {
        _md.Delete(vm.Id);
        MdBlocks.Remove(vm);
    }

    private MdBlockViewModel WrapMd(MdBlock block)
    {
        var vm = new MdBlockViewModel { Id = block.Id, Content = block.Content, Preview = !string.IsNullOrEmpty(block.Content) };
        vm.PropertyChanged += OnMdChanged;
        return vm;
    }

    private void OnMdChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MdBlockViewModel vm && e.PropertyName == nameof(MdBlockViewModel.Content))
            _md.UpdateContent(vm.Id, vm.Content);
    }

    // ----- helpers -----
    private Note InsertBlank()
    {
        var id = _notes.Insert(new Note { Title = "" });
        return _notes.GetById(id)!;
    }

    private void DropCurrentAndReselect()
    {
        var cur = Notes.FirstOrDefault(n => n.Id == CurrentNoteId);
        if (cur is not null)
            Notes.Remove(cur);

        if (Notes.Count == 0)
            Notes.Add(ToItem(InsertBlank()));

        Select(Notes[0]);
    }

    private void ReloadPreservingSelection(long selId)
    {
        Notes.Clear();
        foreach (var n in _notes.GetAllActive())
            Notes.Add(ToItem(n));
        Select(Notes.FirstOrDefault(n => n.Id == selId) ?? Notes.FirstOrDefault());
    }

    private NoteListItemViewModel ToItem(Note n) => new()
    {
        Id = n.Id,
        Title = n.Title,
        Snippet = Snippet(n.ContentPlain),
        Pinned = n.Pinned,
        Favorite = n.Favorite,
    };

    private static string Snippet(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
            return "";
        var s = plain.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length > 80 ? s[..80] + "…" : s;
    }

    private ChecklistItemViewModel Wrap(ChecklistItem item)
    {
        var vm = new ChecklistItemViewModel { Id = item.Id, Text = item.Text, IsDone = item.IsDone };
        vm.PropertyChanged += OnItemChanged;
        return vm;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChecklistItemViewModel vm)
            return;
        if (e.PropertyName == nameof(ChecklistItemViewModel.IsDone))
            _checklist.SetDone(vm.Id, vm.IsDone);
        else if (e.PropertyName == nameof(ChecklistItemViewModel.Text))
            _checklist.UpdateText(vm.Id, vm.Text);
    }
}
