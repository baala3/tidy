using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Deletion;

namespace Tidy.App.ViewModels;

/// <summary>
/// Installers for apps that are already installed. They are the user's own downloads, so
/// nothing is pre-selected, and the apps themselves are never touched.
/// </summary>
public sealed partial class InstallersViewModel : ObservableObject
{
    private readonly AppState _state;

    public static readonly IReadOnlyList<string> SortOptions = ["Largest", "Oldest", "Name"];

    public BulkObservableCollection<InstallerViewModel> Visible { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SortIndex { get; set; }
    [ObservableProperty] public partial bool IsCleaning { get; private set; }

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; private set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; private set; }
    public ObservableCollection<CleanFailure> ResultFailures { get; } = [];

    public InstallersViewModel(AppState state)
    {
        _state = state;
        _state.Installers.CollectionChanged += OnInstallersChanged;
        foreach (var installer in _state.Installers) installer.PropertyChanged += OnInstallerChanged;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsScanningInstallers) or nameof(AppState.InstallersScannedUtc)) RaiseDerived();
        };
        Rebuild();
    }

    public bool IsScanning => _state.IsScanningInstallers;
    public bool IsBusy => IsScanning || IsCleaning;
    public bool HasInstallers => _state.Installers.Count > 0;
    public bool IsFirstScan => !HasInstallers && IsScanning;
    public bool IsEmpty => !HasInstallers && !IsScanning;
    public bool NothingMatches => HasInstallers && Visible.Count == 0;

    private IEnumerable<InstallerViewModel> Selected => _state.Installers.Where(i => i.IsSelected);
    public int SelectedCount => Selected.Count();
    public bool CanRecycle => SelectedCount > 0 && !IsBusy;
    public string RecycleButtonText => SelectedCount == 0
        ? "Move to Recycle Bin"
        : $"Move to Recycle Bin ({Format.Bytes(Selected.Sum(i => i.SizeBytes))})";

    public string Summary
    {
        get
        {
            if (!HasInstallers) return IsScanning ? "Scanning…" : _state.InstallersScannedUtc is null ? "Not scanned yet" : "No installers for apps you have";
            var shown = Visible.Count == _state.Installers.Count
                ? Format.Count(Visible.Count, "installer", "installers")
                : $"{Visible.Count:N0} of {Format.Count(_state.Installers.Count, "installer", "installers")}";
            return $"{shown} for apps you already have · {Format.Bytes(Visible.Sum(i => i.SizeBytes))}";
        }
    }

    /// <summary>Checked when every installer is selected, mixed when some are.</summary>
    public bool? SelectAllState
    {
        get
        {
            if (!HasInstallers) return false;
            var selected = SelectedCount;
            return selected == 0 ? false : selected == _state.Installers.Count ? true : null;
        }
    }

    public void ToggleAll()
    {
        var select = SelectAllState != true;
        foreach (var installer in _state.Installers) installer.IsSelected = select;
    }

    public Task ScanAsync() => _state.ScanInstallersAsync();

    public IReadOnlyList<InstallerViewModel> SelectedInstallers() => Selected.ToList();

    public async Task RecycleAsync(IReadOnlyList<InstallerViewModel> installers)
    {
        IsCleaning = true;
        RaiseDerived();
        try
        {
            var result = await _state.RecycleInstallersAsync(installers);
            ResultFailures.Clear();
            foreach (var failure in result.Failures) ResultFailures.Add(failure);
            ResultHasFailures = result.Failures.Count > 0;
            ResultTitle = result.Removed.Count == 0 ? "Nothing was moved" : $"Freed {Format.Bytes(result.FreedBytes)}";
            ResultMessage = result.Removed.Count == 0
                ? "Tidy left every installer where it was. The reasons are below."
                : $"Moved {Format.Count(result.Removed.Count, "installer", "installers")} to the Recycle Bin. The apps are still installed.";
            IsResultOpen = true;
        }
        finally
        {
            IsCleaning = false;
            RaiseDerived();
        }
    }

    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnSortIndexChanged(int value) => Rebuild();

    private void OnInstallersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (InstallerViewModel i in e.NewItems ?? Array.Empty<InstallerViewModel>()) i.PropertyChanged += OnInstallerChanged;
        foreach (InstallerViewModel i in e.OldItems ?? Array.Empty<InstallerViewModel>()) i.PropertyChanged -= OnInstallerChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var i in _state.Installers) i.PropertyChanged += OnInstallerChanged;
        }
        Rebuild();
    }

    private void OnInstallerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallerViewModel.IsSelected)) RaiseDerived();
    }

    private void Rebuild()
    {
        var query = SearchText.Trim();
        IEnumerable<InstallerViewModel> shown = _state.Installers;
        if (query.Length > 0)
        {
            shown = shown.Where(i =>
                i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                i.Installer.App.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                i.Installer.File.Folder.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }
        shown = SortIndex switch
        {
            1 => shown.OrderBy(i => i.Installer.File.LastModifiedUtc),
            2 => shown.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => shown.OrderByDescending(i => i.SizeBytes),
        };
        Visible.ReplaceAll(shown);
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(IsScanning), nameof(IsBusy), nameof(HasInstallers), nameof(IsFirstScan), nameof(IsEmpty), nameof(NothingMatches),
            nameof(SelectedCount), nameof(CanRecycle), nameof(RecycleButtonText), nameof(Summary), nameof(SelectAllState),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
