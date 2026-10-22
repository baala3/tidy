using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Tidy.App.ViewModels;
using Tidy.Core.Apps;
using Tidy.Core.Catalog;
using Tidy.Core.Deletion;
using Tidy.Core.Models;
using Tidy.Core.Platform;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;
using Tidy.Core.Settings;

namespace Tidy.App.Services;

/// <summary>
/// Everything the app knows: scan results, what is selected, disk usage and settings.
/// Scans run off the UI thread and stream rows in as they are measured. Every removal
/// goes through <see cref="FileDeleter"/>, which re-checks each path against the policy.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    private readonly DispatcherQueue _ui;
    private readonly FileDeleter _deleter;
    private CancellationTokenSource? _cacheScan;
    private CancellationTokenSource? _projectScan;
    private CancellationTokenSource? _largeFileScan;
    private CancellationTokenSource? _duplicateScan;
    private CancellationTokenSource? _installerScan;

    public UserLocations Locations { get; }
    public DeletionSafetyPolicy Policy { get; }
    public LargeFilePolicy LargeFilePolicy { get; }
    public AppSettings Settings { get; }

    public ObservableCollection<ItemViewModel> AppCacheItems { get; } = [];
    public ObservableCollection<ItemViewModel> DevToolItems { get; } = [];
    public BulkObservableCollection<LargeFileViewModel> LargeFiles { get; } = [];
    public BulkObservableCollection<AppViewModel> Apps { get; } = [];
    public BulkObservableCollection<DuplicateGroupViewModel> Duplicates { get; } = [];
    public BulkObservableCollection<InstallerViewModel> Installers { get; } = [];

    [ObservableProperty] public partial bool IsScanningCaches { get; private set; }
    [ObservableProperty] public partial bool IsScanningProjects { get; private set; }
    [ObservableProperty] public partial bool IsScanningLargeFiles { get; private set; }
    [ObservableProperty] public partial bool IsScanningEverything { get; private set; }
    [ObservableProperty] public partial bool IsScanningApps { get; private set; }
    [ObservableProperty] public partial DateTime? AppsScannedUtc { get; private set; }
    [ObservableProperty] public partial bool IsLaunchTrackingEnabled { get; private set; } = true;
    [ObservableProperty] public partial DateTime? CachesScannedUtc { get; private set; }
    [ObservableProperty] public partial DateTime? ProjectsScannedUtc { get; private set; }
    [ObservableProperty] public partial DateTime? LargeFilesScannedUtc { get; private set; }
    [ObservableProperty] public partial bool IsScanningDuplicates { get; private set; }
    [ObservableProperty] public partial DateTime? DuplicatesScannedUtc { get; private set; }
    [ObservableProperty] public partial DuplicateScanProgress DuplicatesProgress { get; private set; }
    [ObservableProperty] public partial bool IsScanningInstallers { get; private set; }
    [ObservableProperty] public partial DateTime? InstallersScannedUtc { get; private set; }
    [ObservableProperty] public partial DiskUsage Disk { get; private set; }
    [ObservableProperty] public partial long RecycleBinBytes { get; private set; }

    /// <summary>Raised whenever totals change, so the Overview and sidebar redraw.</summary>
    public event EventHandler? TotalsChanged;

    public AppState(DispatcherQueue ui)
    {
        _ui = ui;
        Locations = UserLocations.Current();
        Policy = new DeletionSafetyPolicy(Locations);
        LargeFilePolicy = new LargeFilePolicy(Locations);
        Settings = AppSettings.Load(Locations.TidyData);
        _deleter = new FileDeleter(Policy, LargeFilePolicy);
        Disk = DiskUsage.ForProfileDrive(Locations);
    }

    public bool IsScanningDevTools => IsScanningCaches || IsScanningProjects;

    public long AppCachesBytes => AppCacheItems.Sum(i => i.SizeBytes);
    public long AppCachesSafeBytes => AppCacheItems.Where(i => i.IsSafe).Sum(i => i.SizeBytes);
    public long DevToolsBytes => DevToolItems.Sum(i => i.SizeBytes);
    public long DevToolsSafeBytes => DevToolItems.Where(i => i.IsSafe).Sum(i => i.SizeBytes);
    public long LargeFilesBytes => LargeFiles.Sum(f => f.SizeBytes);
    public long AppsBytes => Apps.Sum(a => a.SizeBytes);
    public long DuplicatesBytes => Duplicates.Sum(g => g.WastedBytes);
    public long InstallersBytes => Installers.Sum(i => i.SizeBytes);

    public void SaveSettings() => Settings.Save(Locations.TidyData);

    // MARK: Scanning

    /// <summary>Every category, one after another so the PC stays responsive.</summary>
    public async Task ScanEverythingAsync()
    {
        if (IsScanningEverything) return;
        IsScanningEverything = true;
        try
        {
            await ScanCachesAsync();
            await ScanProjectsAsync();
            await ScanLargeFilesAsync();
            await ScanDuplicatesAsync();
            await ScanAppsAsync();
            await ScanInstallersAsync();
        }
        finally
        {
            IsScanningEverything = false;
        }
    }

    public async Task ScanCachesAsync()
    {
        var cancel = Restart(ref _cacheScan);
        IsScanningCaches = true;
        OnPropertyChanged(nameof(IsScanningDevTools));
        MarkUnseen(AppCacheItems, i => !i.Item.IsProjectArtifact);
        MarkUnseen(DevToolItems, i => !i.Item.IsProjectArtifact);
        try
        {
            var scanner = new CacheScanner(Policy, CleanupCatalog.Default);
            await StreamAsync(scanner.Scan, item => Merge(item.Category == ScanCategory.AppCaches ? AppCacheItems : DevToolItems, item), cancel.Token);
            if (cancel.IsCancellationRequested) return;
            RemoveUnseen(AppCacheItems);
            RemoveUnseen(DevToolItems, i => !i.Item.IsProjectArtifact);
            CachesScannedUtc = DateTime.UtcNow;
        }
        finally
        {
            if (!cancel.IsCancellationRequested) IsScanningCaches = false;
            OnPropertyChanged(nameof(IsScanningDevTools));
            RefreshDisk();
        }
    }

    public async Task ScanProjectsAsync()
    {
        var cancel = Restart(ref _projectScan);
        IsScanningProjects = true;
        OnPropertyChanged(nameof(IsScanningDevTools));
        MarkUnseen(DevToolItems, i => i.Item.IsProjectArtifact);
        try
        {
            var scanner = new ProjectScanner(Policy, CleanupCatalog.Default) { StaleDays = Settings.StaleDays };
            await StreamAsync(scanner.Scan, item => Merge(DevToolItems, item), cancel.Token);
            if (cancel.IsCancellationRequested) return;
            RemoveUnseen(DevToolItems, i => i.Item.IsProjectArtifact);
            ProjectsScannedUtc = DateTime.UtcNow;
        }
        finally
        {
            if (!cancel.IsCancellationRequested) IsScanningProjects = false;
            OnPropertyChanged(nameof(IsScanningDevTools));
            RefreshDisk();
        }
    }

    public async Task ScanDevToolsAsync()
    {
        await ScanCachesAsync();
        await ScanProjectsAsync();
    }

    public async Task ScanLargeFilesAsync()
    {
        var cancel = Restart(ref _largeFileScan);
        IsScanningLargeFiles = true;
        var selected = LargeFiles.Where(f => f.IsSelected).Select(f => f.Path).ToHashSet(PathUtil.Comparer);
        try
        {
            var scanner = new LargeFileScanner(LargeFilePolicy);
            var found = await Task.Run(() => scanner.Scan(cancel.Token).OrderByDescending(f => f.SizeBytes).ToList(), cancel.Token);
            LargeFiles.ReplaceAll(found.Select(file => new LargeFileViewModel(file) { IsSelected = selected.Contains(file.Path) }));
            LargeFilesScannedUtc = DateTime.UtcNow;
            NotifyTotals();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancel.IsCancellationRequested) IsScanningLargeFiles = false;
        }
    }

    /// <summary>Reads every look-alike file, so it reports how far it got while it runs.</summary>
    public async Task ScanDuplicatesAsync()
    {
        var cancel = Restart(ref _duplicateScan);
        IsScanningDuplicates = true;
        DuplicatesProgress = default;
        var selected = Duplicates.SelectMany(g => g.Copies).Where(c => c.IsSelected).Select(c => c.Path).ToHashSet(PathUtil.Comparer);
        try
        {
            var scanner = new DuplicateScanner(LargeFilePolicy);
            var progress = new Progress<DuplicateScanProgress>(p =>
            {
                if (!cancel.IsCancellationRequested) DuplicatesProgress = p;
            });
            var found = await Task.Run(() => scanner.Scan(progress, cancel.Token).OrderByDescending(g => g.WastedBytes).ToList(), cancel.Token);
            Duplicates.ReplaceAll(found.Select(group => new DuplicateGroupViewModel(group, selected)));
            DuplicatesScannedUtc = DateTime.UtcNow;
            NotifyTotals();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancel.IsCancellationRequested) IsScanningDuplicates = false;
        }
    }

    private CancellationTokenSource? _appScan;

    public async Task ScanAppsAsync()
    {
        var cancel = Restart(ref _appScan);
        IsScanningApps = true;
        var selected = Apps.Where(a => a.IsSelected).Select(a => a.App.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            var scanner = new AppScanner(Locations, AppCatalog.Default);
            var (found, tracking) = await Task.Run(() => (scanner.Scan(cancel.Token).ToList(), AppScanner.IsLaunchTrackingEnabled()), cancel.Token);
            IsLaunchTrackingEnabled = tracking;
            Apps.ReplaceAll(found.OrderByDescending(a => a.SizeBytes).Select(app => new AppViewModel(app) { IsSelected = selected.Contains(app.Id) }));
            AppsScannedUtc = DateTime.UtcNow;
            NotifyTotals();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancel.IsCancellationRequested) IsScanningApps = false;
        }
    }

    /// <summary>Installers for apps that are installed, so it uses the app list, scanning apps first if it has none yet.</summary>
    public async Task ScanInstallersAsync()
    {
        var cancel = Restart(ref _installerScan);
        IsScanningInstallers = true;
        var selected = Installers.Where(i => i.IsSelected).Select(i => i.Path).ToHashSet(PathUtil.Comparer);
        try
        {
            if (AppsScannedUtc is null) await ScanAppsAsync();
            if (cancel.IsCancellationRequested) return;
            var apps = Apps.Select(a => a.App).ToList();
            var scanner = new InstallerScanner(LargeFilePolicy);
            var found = await Task.Run(() => scanner.Scan(apps, cancel.Token).OrderByDescending(f => f.File.SizeBytes).ToList(), cancel.Token);
            Installers.ReplaceAll(found.Select(file => new InstallerViewModel(file) { IsSelected = selected.Contains(file.File.Path) }));
            InstallersScannedUtc = DateTime.UtcNow;
            NotifyTotals();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancel.IsCancellationRequested) IsScanningInstallers = false;
        }
    }

    public async Task<CleanResult> RecycleInstallersAsync(IReadOnlyList<InstallerViewModel> installers)
    {
        var result = await _deleter.MoveToRecycleBinAsync(installers.Select(i => i.Path).ToList(), DeletionKind.LargeFile, OwnerWindow);
        Forget(result.Removed);
        return result;
    }

    /// <summary>Removes apps one at a time: a desktop uninstaller must finish before the next one starts.</summary>
    public async Task<List<AppRemovalResult>> RemoveAppsAsync(IReadOnlyList<AppViewModel> apps, Action<int, AppViewModel> progress)
    {
        var remover = new AppRemover(AppCatalog.Default);
        var results = new List<AppRemovalResult>();
        for (var i = 0; i < apps.Count; i++)
        {
            progress(i, apps[i]);
            var result = await Task.Run(() => remover.RemoveAsync(apps[i].App));
            results.Add(result);
            if (result.Removed) Apps.Remove(apps[i]);
        }
        NotifyTotals();
        RefreshDisk();
        // An installer for an app that is gone now is how the user would get it back.
        if (results.Any(r => r.Removed)) _ = ScanInstallersAsync();
        return results;
    }

    private static CancellationTokenSource Restart(ref CancellationTokenSource? current)
    {
        current?.Cancel();
        current = new CancellationTokenSource();
        return current;
    }

    /// <summary>Runs a scanner on a worker thread and hands each row to the UI thread as it is found.</summary>
    private async Task StreamAsync(Func<CancellationToken, IEnumerable<ScanItem>> scan, Action<ScanItem> onItem, CancellationToken cancel)
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (var item in scan(cancel))
                {
                    _ui.TryEnqueue(() =>
                    {
                        if (cancel.IsCancellationRequested) return;
                        onItem(item);
                        NotifyTotals();
                    });
                }
            }, cancel);
            // Let the rows queued above land before the caller sweeps up unseen ones.
            var drained = new TaskCompletionSource();
            _ui.TryEnqueue(DispatcherQueuePriority.Low, () => drained.SetResult());
            await drained.Task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void MarkUnseen(IEnumerable<ItemViewModel> items, Func<ItemViewModel, bool> which)
    {
        foreach (var item in items.Where(which)) item.SeenInScan = false;
    }

    private void RemoveUnseen(ObservableCollection<ItemViewModel> items, Func<ItemViewModel, bool>? which = null)
    {
        foreach (var gone in items.Where(i => !i.SeenInScan && (which?.Invoke(i) ?? true)).ToList()) items.Remove(gone);
        NotifyTotals();
    }

    /// <summary>Updates a row in place so a rescan keeps the user's selection; new safe rows arrive selected.</summary>
    private static void Merge(ObservableCollection<ItemViewModel> items, ScanItem item)
    {
        var existing = items.FirstOrDefault(i => i.Id == item.Id);
        if (existing is not null)
        {
            existing.Item = item;
            existing.SeenInScan = true;
            return;
        }
        items.Add(new ItemViewModel(item) { IsSelected = item.Safety == SafetyLevel.Safe && item.BlockedReason is null });
    }

    // MARK: Cleaning

    public IntPtr OwnerWindow { get; set; }

    public async Task<CleanResult> CleanAsync(IReadOnlyList<ItemViewModel> items)
    {
        var paths = items.SelectMany(i => i.Item.Paths).ToList();
        var result = await _deleter.MoveToRecycleBinAsync(paths, DeletionKind.Cache, OwnerWindow);
        var removed = result.Removed.ToHashSet(PathUtil.Comparer);
        foreach (var item in items)
        {
            var remaining = item.Item.Paths.Where(p => !removed.Contains(p)).ToList();
            var list = item.Item.Category == ScanCategory.AppCaches ? AppCacheItems : DevToolItems;
            if (remaining.Count == 0)
            {
                list.Remove(item);
                continue;
            }
            if (remaining.Count == item.Item.Paths.Count) continue;
            var size = await Task.Run(() => remaining.Sum(p => FolderSizer.Measure(p).SizeBytes));
            item.Item = item.Item with { Paths = remaining, SizeBytes = size };
        }
        NotifyTotals();
        RefreshDisk();
        return result;
    }

    public async Task<CleanResult> RecycleLargeFilesAsync(IReadOnlyList<LargeFileViewModel> files)
    {
        var result = await _deleter.MoveToRecycleBinAsync(files.Select(f => f.Path).ToList(), DeletionKind.LargeFile, OwnerWindow);
        Forget(result.Removed);
        return result;
    }

    /// <summary>Removes extra copies. A group left with a single file is no longer a duplicate and drops off the list.</summary>
    public async Task<CleanResult> RecycleDuplicatesAsync(IReadOnlyList<DuplicateRemoval> removals)
    {
        var result = await _deleter.MoveDuplicatesToRecycleBinAsync(removals, OwnerWindow);
        Forget(result.Removed);
        return result;
    }

    /// <summary>
    /// Takes files that just moved to the Recycle Bin off every list that shows them: a big
    /// duplicate or installer is also a large file, and any of those pages can remove it.
    /// </summary>
    private void Forget(IReadOnlyList<string> paths)
    {
        var removed = paths.ToHashSet(PathUtil.Comparer);
        foreach (var file in LargeFiles.Where(f => removed.Contains(f.Path)).ToList()) LargeFiles.Remove(file);
        foreach (var installer in Installers.Where(i => removed.Contains(i.Path)).ToList()) Installers.Remove(installer);
        var groups = Duplicates.Where(g => g.Remove(removed)).ToList();
        if (groups.Count > 0)
        {
            // A group that lost a copy is the same object with fewer rows, which a list can't
            // see; one reset redraws them all.
            Duplicates.ReplaceAll(Duplicates.Where(g => g.Copies.Count > 1).ToList());
        }
        NotifyTotals();
        RefreshDisk();
    }

    public void RefreshDisk()
    {
        Disk = DiskUsage.ForProfileDrive(Locations);
        _ = Task.Run(RecycleBin.TotalSize).ContinueWith(t => _ui.TryEnqueue(() => RecycleBinBytes = t.Result), TaskScheduler.Default);
    }

    public void NotifyTotals() => TotalsChanged?.Invoke(this, EventArgs.Empty);
}
