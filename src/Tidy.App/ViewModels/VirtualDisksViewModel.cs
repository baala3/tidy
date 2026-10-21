using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Deletion;

namespace Tidy.App.ViewModels;

/// <summary>
/// WSL & Docker: disk images that only ever grow. Compacting keeps everything inside, but it
/// has to stop WSL and Docker Desktop, so nothing is selected for you.
/// </summary>
public sealed partial class VirtualDisksViewModel : ObservableObject
{
    private readonly AppState _state;

    [ObservableProperty] public partial bool IsCompacting { get; private set; }
    [ObservableProperty] public partial string Progress { get; private set; } = "";

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; private set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; private set; }
    public ObservableCollection<CleanFailure> ResultFailures { get; } = [];

    public VirtualDisksViewModel(AppState state)
    {
        _state = state;
        _state.VirtualDisks.CollectionChanged += OnDisksChanged;
        foreach (var disk in _state.VirtualDisks) disk.PropertyChanged += OnDiskChanged;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsScanningVirtualDisks) or nameof(AppState.VirtualDisksScannedUtc)) RaiseDerived();
        };
    }

    public BulkObservableCollection<VirtualDiskViewModel> Disks => _state.VirtualDisks;

    public bool IsScanning => _state.IsScanningVirtualDisks;
    public bool IsBusy => IsScanning || IsCompacting;
    public bool HasDisks => Disks.Count > 0;

    private IEnumerable<VirtualDiskViewModel> Selected => Disks.Where(d => d.IsSelected && d.CanSelect);
    public int SelectedCount => Selected.Count();
    public bool CanCompact => SelectedCount > 0 && !IsBusy;
    public string CompactButtonText => IsCompacting ? Progress
        : SelectedCount == 0 ? "Compact Selected"
        : Selected.Sum(d => d.Disk.KnownReclaimableBytes) is > 0 and var known ? $"Compact Selected ({Format.Bytes(known)})" : "Compact Selected";

    public string Summary => !HasDisks
        ? IsScanning ? "Scanning…" : _state.VirtualDisksScannedUtc is null ? "Not scanned yet" : "No WSL or Docker disks"
        : $"{Format.Count(Disks.Count, "disk", "disks")} · {Format.Bytes(_state.VirtualDisksBytes)} on disk · {Format.Bytes(_state.VirtualDisksReclaimableBytes)} can be given back";

    public IReadOnlyList<VirtualDiskViewModel> SelectedDisks() => Selected.ToList();

    public Task ScanAsync() => _state.ScanVirtualDisksAsync();

    public async Task CompactAsync(IReadOnlyList<VirtualDiskViewModel> disks)
    {
        IsCompacting = true;
        Progress = "Starting…";
        RaiseDerived();
        try
        {
            var progress = new Progress<string>(text =>
            {
                Progress = text;
                OnPropertyChanged(nameof(CompactButtonText));
            });
            var result = await _state.CompactVirtualDisksAsync(disks, progress);
            ResultFailures.Clear();
            foreach (var failure in result.Failures) ResultFailures.Add(failure);
            ResultHasFailures = result.Failures.Count > 0;
            ResultTitle = result.Removed.Count == 0 ? "Nothing was compacted" : $"Gave back {Format.Bytes(result.FreedBytes)}";
            ResultMessage = result.Removed.Count == 0
                ? "The reasons are below."
                : $"Compacted {Format.Count(result.Removed.Count, "disk", "disks")}. Everything inside is as it was; WSL and Docker start again the next time you open them.";
            IsResultOpen = true;
        }
        finally
        {
            IsCompacting = false;
            Progress = "";
            RaiseDerived();
        }
    }

    private void OnDisksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (VirtualDiskViewModel d in e.NewItems ?? Array.Empty<VirtualDiskViewModel>()) d.PropertyChanged += OnDiskChanged;
        foreach (VirtualDiskViewModel d in e.OldItems ?? Array.Empty<VirtualDiskViewModel>()) d.PropertyChanged -= OnDiskChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var d in Disks) d.PropertyChanged += OnDiskChanged;
        }
        RaiseDerived();
    }

    private void OnDiskChanged(object? sender, PropertyChangedEventArgs e) => RaiseDerived();

    private void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(IsScanning), nameof(IsBusy), nameof(HasDisks), nameof(SelectedCount), nameof(CanCompact), nameof(CompactButtonText), nameof(Summary),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
