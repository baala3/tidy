using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Apps;

namespace Tidy.App.ViewModels;

public enum AppFilter
{
    All,
    Preinstalled,
    Unused,
}

public sealed record AppProblem(string Name, string Reason);

/// <summary>
/// Apps: preinstalled extras and apps that go unused. Removing an app cannot be undone from
/// the Recycle Bin, so nothing here is pre-selected and every removal is confirmed first.
/// </summary>
public sealed partial class AppsViewModel : ObservableObject
{
    private readonly AppState _state;

    public static readonly IReadOnlyList<string> SortOptions = ["Largest", "Least recently used", "Name"];

    public BulkObservableCollection<AppViewModel> Visible { get; } = [];

    [ObservableProperty] public partial AppFilter Filter { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SortIndex { get; set; }
    [ObservableProperty] public partial bool IsRemoving { get; private set; }
    [ObservableProperty] public partial string Progress { get; private set; } = "";

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; private set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; private set; }
    public ObservableCollection<AppProblem> ResultFailures { get; } = [];

    public AppsViewModel(AppState state)
    {
        _state = state;
        _state.Apps.CollectionChanged += OnAppsChanged;
        foreach (var app in _state.Apps) app.PropertyChanged += OnAppChanged;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsScanningApps) or nameof(AppState.AppsScannedUtc) or nameof(AppState.IsLaunchTrackingEnabled)) RaiseDerived();
        };
        Rebuild();
    }

    public bool IsScanning => _state.IsScanningApps;
    public bool IsBusy => IsScanning || IsRemoving;
    public bool HasApps => _state.Apps.Count > 0;
    public bool IsFirstScan => !HasApps && IsScanning;
    public bool NothingMatches => HasApps && Visible.Count == 0;
    public bool ShowTrackingOff => HasApps && !_state.IsLaunchTrackingEnabled;

    public int AllCount => _state.Apps.Count;
    public int PreinstalledCount => _state.Apps.Count(a => a.IsPreinstalled);
    public int UnusedCount => _state.IsLaunchTrackingEnabled ? _state.Apps.Count(a => a.IsUnused) : 0;

    private IEnumerable<AppViewModel> Selected => _state.Apps.Where(a => a.IsSelected);
    public int SelectedCount => Selected.Count();
    public bool CanRemove => SelectedCount > 0 && !IsBusy;
    public string RemoveButtonText => IsRemoving ? Progress
        : SelectedCount == 0 ? "Uninstall Selected"
        : $"Uninstall {Format.Count(SelectedCount, "App", "Apps")}";

    public string Summary
    {
        get
        {
            if (!HasApps) return IsScanning ? "Scanning…" : _state.AppsScannedUtc is null ? "Not scanned yet" : "No apps found";
            var parts = new List<string> { Format.Count(AllCount, "app", "apps") };
            if (PreinstalledCount > 0) parts.Add(Format.Count(PreinstalledCount, "preinstalled extra", "preinstalled extras"));
            if (UnusedCount > 0) parts.Add($"{UnusedCount:N0} not opened in {AppViewModel.UnusedDays} days");
            return string.Join(" · ", parts);
        }
    }

    private IEnumerable<AppViewModel> SafeExtras => _state.Apps.Where(a => a.IsSafe);
    public bool HasSafeExtras => SafeExtras.Any();

    /// <summary>Checked when every Safe preinstalled extra is selected.</summary>
    public bool? SelectSafeExtrasState
    {
        get
        {
            var safe = SafeExtras.ToList();
            if (safe.Count == 0) return false;
            var selected = safe.Count(a => a.IsSelected);
            return selected == 0 ? false : selected == safe.Count ? true : null;
        }
    }

    public void ToggleSafeExtras()
    {
        var select = SelectSafeExtrasState != true;
        foreach (var app in SafeExtras) app.IsSelected = select;
    }

    public Task ScanAsync() => _state.ScanAppsAsync();

    public IReadOnlyList<AppViewModel> SelectedApps() => Selected.ToList();

    public async Task RemoveAsync(IReadOnlyList<AppViewModel> apps)
    {
        IsRemoving = true;
        RaiseDerived();
        try
        {
            var results = await _state.RemoveAppsAsync(apps, (index, app) =>
            {
                Progress = $"Removing {index + 1} of {apps.Count}…";
                RaiseDerived();
            });
            var removed = results.Where(r => r.Removed).ToList();
            ResultFailures.Clear();
            foreach (var failed in results.Where(r => !r.Removed)) ResultFailures.Add(new AppProblem(failed.App.Name, failed.Problem ?? ""));
            ResultHasFailures = ResultFailures.Count > 0;
            ResultTitle = removed.Count == 0 ? "Nothing was removed" : $"Removed {Format.Count(removed.Count, "app", "apps")}";
            ResultMessage = removed.Count == 0
                ? "The reasons are below."
                : $"Freed about {Format.Bytes(removed.Sum(r => r.App.SizeBytes))}." + (ResultHasFailures ? " Some apps were left installed." : "");
            IsResultOpen = true;
        }
        finally
        {
            IsRemoving = false;
            Progress = "";
            RaiseDerived();
        }
    }

    partial void OnFilterChanged(AppFilter value) => Rebuild();
    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnSortIndexChanged(int value) => Rebuild();

    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (AppViewModel a in e.NewItems ?? Array.Empty<AppViewModel>()) a.PropertyChanged += OnAppChanged;
        foreach (AppViewModel a in e.OldItems ?? Array.Empty<AppViewModel>()) a.PropertyChanged -= OnAppChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var a in _state.Apps) a.PropertyChanged += OnAppChanged;
        }
        Rebuild();
    }

    private void OnAppChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppViewModel.IsSelected)) RaiseDerived();
    }

    private void Rebuild()
    {
        var query = SearchText.Trim();
        IEnumerable<AppViewModel> shown = Filter switch
        {
            AppFilter.All => _state.Apps,
            AppFilter.Preinstalled => _state.Apps.Where(a => a.IsPreinstalled),
            AppFilter.Unused => _state.Apps.Where(a => a.IsUnused),
            _ => throw new ArgumentOutOfRangeException(nameof(Filter)),
        };
        if (query.Length > 0)
        {
            shown = shown.Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                a.App.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }
        shown = SortIndex switch
        {
            1 => shown.OrderBy(a => a.SortableLastUsed),
            2 => shown.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => shown.OrderByDescending(a => a.SizeBytes),
        };
        Visible.ReplaceAll(shown);
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(IsScanning), nameof(IsBusy), nameof(HasApps), nameof(IsFirstScan), nameof(NothingMatches), nameof(ShowTrackingOff),
            nameof(AllCount), nameof(PreinstalledCount), nameof(UnusedCount), nameof(SelectedCount), nameof(CanRemove),
            nameof(RemoveButtonText), nameof(Summary), nameof(HasSafeExtras), nameof(SelectSafeExtrasState),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
