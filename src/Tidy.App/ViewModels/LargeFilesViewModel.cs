using Tidy.App.Services;

namespace Tidy.App.ViewModels;

/// <summary>
/// Large Files: personal files, so nothing is ever pre-selected and nothing here takes
/// part in a one-click clean. Each file is chosen by hand.
/// </summary>
public sealed partial class LargeFilesViewModel : FileListViewModel
{
    private readonly AppState _state;

    public static readonly IReadOnlyList<SizeOption> Sizes =
    [
        new("Over 5 MB", 5L << 20), new("Over 10 MB", 10L << 20), new("Over 50 MB", 50L << 20),
        new("Over 100 MB", 100L << 20), new("Over 500 MB", 500L << 20), new("Over 1 GB", 1L << 30),
    ];

    public LargeFilesViewModel(AppState state) : base(state.LargeFiles, Sizes)
    {
        _state = state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsScanningLargeFiles) or nameof(AppState.LargeFilesScannedUtc)) RaiseDerived();
        };
        Rebuild();
    }

    public override bool IsScanning => _state.IsScanningLargeFiles;
    public bool IsCleaning => IsWorking;
    public bool CanRecycle => CanAct;
    public string RecycleButtonText => SelectedCount == 0
        ? "Move to Recycle Bin"
        : $"Move to Recycle Bin ({Format.Bytes(SelectedBytes)})";

    public string Summary => Files.Count == 0
        ? IsScanning ? "Scanning…" : _state.LargeFilesScannedUtc is null ? "Not scanned yet" : "No files over 5 MB"
        : ShownText;

    public Task ScanAsync() => _state.ScanLargeFilesAsync();

    public Task RecycleAsync(IReadOnlyList<LargeFileViewModel> files) =>
        RunAsync(() => _state.RecycleLargeFilesAsync(files), result =>
        {
            ResultTitle = result.Removed.Count == 0 ? "Nothing was moved" : $"Freed {Format.Bytes(result.FreedBytes)}";
            ResultMessage = result.Removed.Count == 0
                ? "Tidy left every file where it was. The reasons are below."
                : $"Moved {Format.Count(result.Removed.Count, "file", "files")} to the Recycle Bin. You can restore them from there.";
        });

    protected override void RaiseDerived()
    {
        base.RaiseDerived();
        OnPropertyChanged(nameof(IsCleaning));
        OnPropertyChanged(nameof(CanRecycle));
        OnPropertyChanged(nameof(RecycleButtonText));
        OnPropertyChanged(nameof(Summary));
    }
}
