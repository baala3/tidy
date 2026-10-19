using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Deletion;
using Tidy.Core.Models;
using Tidy.Core.Platform;

namespace Tidy.App.ViewModels;

/// <summary>One copy of a duplicated file.</summary>
public sealed partial class DuplicateCopyViewModel(DuplicateCopy copy, DuplicateGroupViewModel group) : ObservableObject
{
    public DuplicateCopy Copy { get; } = copy;
    public DuplicateGroupViewModel Group { get; } = group;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>False for the last copy left unselected: one copy of every file always stays.</summary>
    public bool CanSelect => IsSelected || Group.SelectedCount < Group.Count - 1;

    public bool IsSuggestedKeep => PathUtil.AreSame(Copy.Path, Group.SuggestedKeepPath);
    public string Path => Copy.Path;
    public string Name => Copy.File.Name;
    public long SizeBytes => Copy.File.SizeBytes;
    public string Glyph => Glyphs.ForFile(Copy.File.Kind);
    public string Subtitle => $"{Copy.File.SourceLabel} · {DisplayFolder()}";
    public string ModifiedText => Copy.File.LastWriteTimeText();
    public string CheckTooltip => CanSelect ? "" : "One copy always stays";

    partial void OnIsSelectedChanged(bool value) => Group.SelectionChanged();

    internal void RaiseCanSelect()
    {
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(CheckTooltip));
    }

    private string DisplayFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Copy.File.Folder;
        return folder.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + folder[home.Length..] : folder;
    }
}

/// <summary>
/// Files with the same contents, listed as one group. It is a list of its copies so a
/// grouped ListView can show it directly, with the header bound to the group itself.
/// </summary>
public sealed class DuplicateGroupViewModel : List<DuplicateCopyViewModel>
{
    public DuplicateGroupViewModel(DuplicateGroup group, IReadOnlySet<string> selected)
    {
        Group = group;
        SuggestedKeepPath = group.SuggestedKeep.Path;
        AddRange(group.Copies.Select(c => new DuplicateCopyViewModel(c, this)));
        // A rescan keeps what was picked, as long as one copy is still left unpicked.
        foreach (var copy in this.Where(c => selected.Contains(c.Path)).Take(Count - 1)) copy.IsSelected = true;
    }

    public DuplicateGroup Group { get; private set; }
    public string SuggestedKeepPath { get; }

    public IReadOnlyList<DuplicateCopyViewModel> Copies => this;
    public long SizeBytes => Group.SizeBytes;
    public long WastedBytes => Group.WastedBytes;
    public string Title => this.FirstOrDefault(c => c.IsSuggestedKeep)?.Name ?? this[0].Name;
    public string Subtitle => $"{Format.Count(Count, "copy", "copies")} · {Format.Bytes(SizeBytes)} each · {Format.Bytes(WastedBytes)} extra";
    public int SelectedCount => this.Count(c => c.IsSelected);
    public long SelectedBytes => SelectedCount * SizeBytes;

    /// <summary>Raised whenever a copy is picked or unpicked, so the page can update its totals.</summary>
    public event EventHandler? Changed;

    /// <summary>Picks every copy but the one to keep: the suggested one, unless the user picked that.</summary>
    public void SelectExtras()
    {
        var keep = KeepFor(this.Where(c => !c.IsSuggestedKeep || !c.IsSelected));
        foreach (var copy in this) copy.IsSelected = copy != keep;
    }

    public void ClearSelection()
    {
        foreach (var copy in this) copy.IsSelected = false;
    }

    public bool AllExtrasSelected => SelectedCount == Count - 1;

    /// <summary>What to remove, each paired with the copy that stays.</summary>
    public IEnumerable<DuplicateRemoval> Removals()
    {
        var keep = KeepFor(this.Where(c => !c.IsSelected));
        return keep is null ? [] : this.Where(c => c.IsSelected).Select(c => new DuplicateRemoval(c.Path, keep.Path));
    }

    /// <summary>Drops copies that are gone from disk. True when it lost any.</summary>
    public bool Remove(IReadOnlySet<string> removed)
    {
        if (RemoveAll(c => removed.Contains(c.Path)) == 0) return false;
        Group = Group with { Copies = this.Select(c => c.Copy).ToList() };
        SelectionChanged();
        return true;
    }

    internal void SelectionChanged()
    {
        foreach (var copy in this) copy.RaiseCanSelect();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private DuplicateCopyViewModel? KeepFor(IEnumerable<DuplicateCopyViewModel> candidates)
    {
        var list = candidates.ToList();
        return list.FirstOrDefault(c => c.IsSuggestedKeep) ?? list.FirstOrDefault();
    }
}
