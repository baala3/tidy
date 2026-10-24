using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Deletion;

namespace Tidy.App.ViewModels;

public enum LeftoverFilter
{
    All,
    JustRemoved,
}

/// <summary>
/// Folders apps left behind. Every one is Check First and nothing is pre-selected: Tidy can
/// be sure an app is gone, but not that nobody wants what it kept.
/// </summary>
public sealed partial class LeftoversViewModel : ObservableObject
{
    private readonly AppState _state;

    public static readonly IReadOnlyList<string> SortOptions = ["Largest", "Oldest", "Name"];

    public BulkObservableCollection<LeftoverViewModel> Visible { get; } = [];

    [ObservableProperty] public partial LeftoverFilter Filter { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SortIndex { get; set; }
    [ObservableProperty] public partial bool IsCleaning { get; private set; }

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; private set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; private set; }
    public ObservableCollection<CleanFailure> ResultFailures { get; } = [];

    public LeftoversViewModel(AppState state)
    {
        _state = state;
        _state.Leftovers.CollectionChanged += OnLeftoversChanged;
        foreach (var leftover in _state.Leftovers) leftover.PropertyChanged += OnLeftoverChanged;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsScanningLeftovers) or nameof(AppState.LeftoversScannedUtc)) RaiseDerived();
        };
        Rebuild();
    }

    public bool IsScanning => _state.IsScanningLeftovers;
    public bool IsBusy => IsScanning || IsCleaning;
    public bool HasLeftovers => _state.Leftovers.Count > 0;
    public bool IsFirstScan => !HasLeftovers && IsScanning;
    public bool IsEmpty => !HasLeftovers && !IsScanning;
    public bool NothingMatches => HasLeftovers && Visible.Count == 0;

    public int AllCount => _state.Leftovers.Count;
    public int JustRemovedCount => _state.Leftovers.Count(l => l.IsFromJustRemoved);
    public bool HasJustRemoved => JustRemovedCount > 0;

    private IEnumerable<LeftoverViewModel> Selectable => Visible.Where(l => l.CanSelect);
    private IEnumerable<LeftoverViewModel> Selected => _state.Leftovers.Where(l => l.IsSelected);
    public int SelectedCount => Selected.Count();
    public bool CanRecycle => SelectedCount > 0 && !IsBusy;
    public string RecycleButtonText => SelectedCount == 0
        ? "Move to Recycle Bin"
        : $"Move to Recycle Bin ({Format.Bytes(Selected.Sum(l => l.SizeBytes))})";

    public string Summary
    {
        get
        {
            if (!HasLeftovers) return IsScanning ? "Scanning…" : _state.LeftoversScannedUtc is null ? "Not scanned yet" : "Nothing left behind";
            var shown = Visible.Count == _state.Leftovers.Count
                ? Format.Count(Visible.Count, "folder", "folders")
                : $"{Visible.Count:N0} of {Format.Count(_state.Leftovers.Count, "folder", "folders")}";
            return $"{shown} left by apps that are gone · {Format.Bytes(Visible.Sum(l => l.SizeBytes))}";
        }
    }

    /// <summary>Checked when every folder shown that can be removed is selected, mixed when some are.</summary>
    public bool? SelectAllState
    {
        get
        {
            var selectable = Selectable.ToList();
            if (selectable.Count == 0) return false;
            var selected = selectable.Count(l => l.IsSelected);
            return selected == 0 ? false : selected == selectable.Count ? true : null;
        }
    }

    public bool CanSelectAll => Selectable.Any();

    public void ToggleAll()
    {
        var select = SelectAllState != true;
        foreach (var leftover in Selectable) leftover.IsSelected = select;
    }

    public Task ScanAsync() => _state.ScanLeftoversAsync();

    public IReadOnlyList<LeftoverViewModel> SelectedLeftovers() => Selected.ToList();

    public async Task RecycleAsync(IReadOnlyList<LeftoverViewModel> leftovers)
    {
        IsCleaning = true;
        RaiseDerived();
        try
        {
            var result = await _state.RecycleLeftoversAsync(leftovers);
            ResultFailures.Clear();
            foreach (var failure in result.Failures) ResultFailures.Add(failure);
            ResultHasFailures = result.Failures.Count > 0;
            ResultTitle = result.Removed.Count == 0 ? "Nothing was moved" : $"Freed {Format.Bytes(result.FreedBytes)}";
            ResultMessage = result.Removed.Count == 0
                ? "Tidy left every folder where it was. The reasons are below."
                : $"Moved {Format.Count(result.Removed.Count, "folder", "folders")} to the Recycle Bin.";
            IsResultOpen = true;
        }
        finally
        {
            IsCleaning = false;
            RaiseDerived();
        }
    }

    partial void OnFilterChanged(LeftoverFilter value) => Rebuild();
    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnSortIndexChanged(int value) => Rebuild();

    private void OnLeftoversChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (LeftoverViewModel l in e.NewItems ?? Array.Empty<LeftoverViewModel>()) l.PropertyChanged += OnLeftoverChanged;
        foreach (LeftoverViewModel l in e.OldItems ?? Array.Empty<LeftoverViewModel>()) l.PropertyChanged -= OnLeftoverChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var l in _state.Leftovers) l.PropertyChanged += OnLeftoverChanged;
        }
        if (Filter == LeftoverFilter.JustRemoved && !HasJustRemoved) Filter = LeftoverFilter.All;
        Rebuild();
    }

    private void OnLeftoverChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeftoverViewModel.IsSelected)) RaiseDerived();
    }

    private void Rebuild()
    {
        var query = SearchText.Trim();
        IEnumerable<LeftoverViewModel> shown = Filter switch
        {
            LeftoverFilter.All => _state.Leftovers,
            LeftoverFilter.JustRemoved => _state.Leftovers.Where(l => l.IsFromJustRemoved),
            _ => throw new ArgumentOutOfRangeException(nameof(Filter)),
        };
        if (query.Length > 0)
        {
            shown = shown.Where(l =>
                l.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                l.Folder.AppName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }
        shown = SortIndex switch
        {
            1 => shown.OrderBy(l => l.Folder.LastModifiedUtc),
            2 => shown.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => shown.OrderByDescending(l => l.SizeBytes),
        };
        Visible.ReplaceAll(shown);
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(IsScanning), nameof(IsBusy), nameof(HasLeftovers), nameof(IsFirstScan), nameof(IsEmpty), nameof(NothingMatches),
            nameof(AllCount), nameof(JustRemovedCount), nameof(HasJustRemoved), nameof(SelectedCount), nameof(CanRecycle),
            nameof(RecycleButtonText), nameof(Summary), nameof(SelectAllState), nameof(CanSelectAll),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
