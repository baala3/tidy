using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Deletion;

namespace Tidy.App.ViewModels;

/// <summary>
/// Duplicates: personal files, so nothing is pre-selected. "Select extra copies" picks every
/// copy but one in each group, and one copy of every file always stays.
/// </summary>
public sealed partial class DuplicatesViewModel : ObservableObject
{
    private readonly AppState _state;

    public static readonly IReadOnlyList<string> SortOptions = ["Most space", "Largest files", "Name"];

    public BulkObservableCollection<DuplicateGroupViewModel> Visible { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SortIndex { get; set; }
    [ObservableProperty] public partial bool IsCleaning { get; private set; }

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; private set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; private set; }
    public ObservableCollection<CleanFailure> ResultFailures { get; } = [];

    public DuplicatesViewModel(AppState state)
    {
        _state = state;
        _state.Duplicates.CollectionChanged += OnGroupsChanged;
        foreach (var group in _state.Duplicates) group.Changed += OnGroupChanged;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsScanningDuplicates) or nameof(AppState.DuplicatesScannedUtc) or nameof(AppState.DuplicatesProgress)) RaiseDerived();
        };
        Rebuild();
    }

    public bool IsScanning => _state.IsScanningDuplicates;
    public bool IsBusy => IsScanning || IsCleaning;
    public bool HasGroups => _state.Duplicates.Count > 0;
    public bool IsFirstScan => !HasGroups && IsScanning;
    public bool IsEmpty => !HasGroups && !IsScanning;
    public bool NothingMatches => HasGroups && Visible.Count == 0;

    public int SelectedCount => _state.Duplicates.Sum(g => g.SelectedCount);
    public long SelectedBytes => _state.Duplicates.Sum(g => g.SelectedBytes);
    public bool CanRecycle => SelectedCount > 0 && !IsBusy;
    public string RecycleButtonText => SelectedCount == 0
        ? "Move to Recycle Bin"
        : $"Move to Recycle Bin ({Format.Bytes(SelectedBytes)})";

    public string Summary
    {
        get
        {
            if (IsScanning)
            {
                var p = _state.DuplicatesProgress;
                return p.FilesToCompare == 0 ? "Looking for files of the same size…" : $"Comparing {p.FilesCompared:N0} of {Format.Count(p.FilesToCompare, "file", "files")}…";
            }
            if (!HasGroups) return _state.DuplicatesScannedUtc is null ? "Not scanned yet" : "No duplicates found";
            var shown = Visible.Count == _state.Duplicates.Count
                ? Format.Count(Visible.Count, "file has copies", "files have copies")
                : $"{Visible.Count:N0} of {Format.Count(_state.Duplicates.Count, "file has copies", "files have copies")}";
            return $"{shown} · {Format.Bytes(Visible.Sum(g => g.WastedBytes))} in extra copies";
        }
    }

    public string FirstScanText
    {
        get
        {
            var p = _state.DuplicatesProgress;
            return p.FilesToCompare == 0
                ? "Looking for files of the same size in Documents, Desktop, Downloads, Videos, Music and Pictures…"
                : $"Comparing the contents of {p.FilesCompared:N0} of {Format.Count(p.FilesToCompare, "file", "files")}…";
        }
    }

    /// <summary>Checked when every group has all its extra copies picked, mixed when some are.</summary>
    public bool? SelectExtrasState
    {
        get
        {
            if (!HasGroups) return false;
            var full = _state.Duplicates.Count(g => g.AllExtrasSelected);
            if (full == _state.Duplicates.Count) return true;
            return SelectedCount == 0 ? false : null;
        }
    }

    public void ToggleExtras()
    {
        var select = SelectExtrasState != true;
        foreach (var group in _state.Duplicates)
        {
            if (select) group.SelectExtras();
            else group.ClearSelection();
        }
    }

    public Task ScanAsync() => _state.ScanDuplicatesAsync();

    public IReadOnlyList<DuplicateRemoval> SelectedRemovals() => _state.Duplicates.SelectMany(g => g.Removals()).ToList();

    public async Task RecycleAsync(IReadOnlyList<DuplicateRemoval> removals)
    {
        IsCleaning = true;
        RaiseDerived();
        try
        {
            var result = await _state.RecycleDuplicatesAsync(removals);
            ResultFailures.Clear();
            foreach (var failure in result.Failures) ResultFailures.Add(failure);
            ResultHasFailures = result.Failures.Count > 0;
            ResultTitle = result.Removed.Count == 0 ? "Nothing was moved" : $"Freed {Format.Bytes(result.FreedBytes)}";
            ResultMessage = result.Removed.Count == 0
                ? "Tidy left every copy where it was. The reasons are below."
                : $"Moved {Format.Count(result.Removed.Count, "copy", "copies")} to the Recycle Bin. One copy of each file is still where it was.";
            IsResultOpen = true;
            Rebuild();
        }
        finally
        {
            IsCleaning = false;
            RaiseDerived();
        }
    }

    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnSortIndexChanged(int value) => Rebuild();

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (DuplicateGroupViewModel g in e.NewItems ?? Array.Empty<DuplicateGroupViewModel>()) g.Changed += OnGroupChanged;
        foreach (DuplicateGroupViewModel g in e.OldItems ?? Array.Empty<DuplicateGroupViewModel>()) g.Changed -= OnGroupChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var g in _state.Duplicates) g.Changed += OnGroupChanged;
        }
        Rebuild();
    }

    private void OnGroupChanged(object? sender, EventArgs e) => RaiseDerived();

    private void Rebuild()
    {
        var query = SearchText.Trim();
        IEnumerable<DuplicateGroupViewModel> shown = _state.Duplicates;
        if (query.Length > 0)
        {
            shown = shown.Where(g => g.Copies.Any(c =>
                c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                c.Copy.File.Folder.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        }
        shown = SortIndex switch
        {
            1 => shown.OrderByDescending(g => g.SizeBytes),
            2 => shown.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => shown.OrderByDescending(g => g.WastedBytes),
        };
        Visible.ReplaceAll(shown);
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(IsScanning), nameof(IsBusy), nameof(HasGroups), nameof(IsFirstScan), nameof(IsEmpty), nameof(NothingMatches),
            nameof(SelectedCount), nameof(SelectedBytes), nameof(CanRecycle), nameof(RecycleButtonText), nameof(Summary),
            nameof(FirstScanText), nameof(SelectExtrasState),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
