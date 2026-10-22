using Tidy.App.Services;

namespace Tidy.App.ViewModels;

/// <summary>
/// OneDrive: files that are safely in the cloud but also take space here. Freeing up space
/// keeps them in OneDrive and in File Explorer; they download again when opened.
/// </summary>
public sealed partial class OneDriveViewModel : FileListViewModel
{
    private readonly AppState _state;

    public static readonly IReadOnlyList<SizeOption> Sizes =
    [
        new("Over 1 MB", 1L << 20), new("Over 5 MB", 5L << 20), new("Over 10 MB", 10L << 20), new("Over 50 MB", 50L << 20),
        new("Over 100 MB", 100L << 20), new("Over 500 MB", 500L << 20), new("Over 1 GB", 1L << 30),
    ];

    public OneDriveViewModel(AppState state) : base(state.OneDriveFiles, Sizes)
    {
        _state = state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsScanningOneDrive) or nameof(AppState.OneDriveScannedUtc) or nameof(AppState.IsOneDriveRunning)) RaiseDerived();
        };
        Rebuild();
    }

    public override bool IsScanning => _state.IsScanningOneDrive;

    /// <summary>Without OneDrive running, an online-only file couldn't download again when opened.</summary>
    public bool ShowNotRunning => _state.OneDriveScannedUtc is not null && !_state.IsOneDriveRunning;

    public override bool CanAct => base.CanAct && _state.IsOneDriveRunning;

    public string FreeButtonText => SelectedCount == 0 ? "Free Up Space" : $"Free Up Space ({Format.Bytes(SelectedBytes)})";

    public string Summary => Files.Count == 0
        ? IsScanning ? "Scanning…" : _state.OneDriveScannedUtc is null ? "Not scanned yet" : "Nothing to free up"
        : $"{ShownText} on this PC, also in OneDrive";

    /// <summary>Checked when every file shown is selected, mixed when some are.</summary>
    public bool? SelectShownState
    {
        get
        {
            if (Visible.Count == 0) return false;
            var selected = Visible.Count(f => f.IsSelected);
            return selected == 0 ? false : selected == Visible.Count ? true : null;
        }
    }

    public void ToggleShown()
    {
        var select = SelectShownState != true;
        foreach (var file in Visible) file.IsSelected = select;
    }

    public Task ScanAsync() => _state.ScanOneDriveAsync();

    public Task FreeUpAsync(IReadOnlyList<LargeFileViewModel> files) =>
        RunAsync(() => _state.FreeUpOneDriveAsync(files), result =>
        {
            ResultTitle = result.Removed.Count == 0 ? "Nothing changed" : $"Freeing up {Format.Bytes(result.FreedBytes)}";
            ResultMessage = result.Removed.Count == 0
                ? "Every file still has its copy on this PC. The reasons are below."
                : $"{Format.Count(result.Removed.Count, "file is", "files are")} now online-only. OneDrive removes the copies from this PC in the background, usually within a minute. They stay in OneDrive and download again when you open them.";
        });

    protected override void RaiseDerived()
    {
        base.RaiseDerived();
        foreach (var name in new[] { nameof(ShowNotRunning), nameof(FreeButtonText), nameof(Summary), nameof(SelectShownState) })
        {
            OnPropertyChanged(name);
        }
    }
}
