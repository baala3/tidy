using System.Globalization;
using Tidy.Core.Catalog;
using Tidy.Core.Models;
using Tidy.Core.Platform;
using Tidy.Core.Safety;

namespace Tidy.Core.Scanning;

/// <summary>
/// Finds the caches catalog.json describes, plus the disk caches of Chromium and
/// Electron apps in AppData. Everything it proposes goes through the safety policy,
/// and anything the catalog does not name is left out of the results.
/// </summary>
public sealed class CacheScanner(DeletionSafetyPolicy policy, CleanupCatalog catalog)
{
    /// <summary>Rows smaller than this are noise, not space anyone will notice.</summary>
    public const long MinimumItemBytes = 512 * 1024;

    public Func<IEnumerable<string>, bool> IsAnyRunning { get; init; } = RunningApps.AnyRunning;

    /// <summary>Items are reported one at a time as they are measured, so the list fills in live.</summary>
    public IEnumerable<ScanItem> Scan(CancellationToken cancellation = default)
    {
        var claimed = new HashSet<string>(PathUtil.Comparer);
        foreach (var entry in catalog.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var item = ScanEntry(entry, claimed, cancellation);
            if (item is not null) yield return item;
        }
        foreach (var item in ScanChromiumApps(claimed, cancellation))
        {
            yield return item;
        }
        foreach (var item in ScanDeviceBackups(cancellation))
        {
            yield return item;
        }
    }

    /// <summary>One Check First row per iPhone or iPad backup, named after the device.</summary>
    private IEnumerable<ScanItem> ScanDeviceBackups(CancellationToken cancellation)
    {
        var section = catalog.DeviceBackups;
        var blocked = IsAnyRunning(section.CloseFirst.Processes) ? $"Close {section.CloseFirst.App} to clean this" : null;
        foreach (var root in section.Paths.Select(p => CleanupCatalog.ResolveTokens(p, policy.Locations)))
        {
            if (!Directory.Exists(root) || PathUtil.IsReparsePoint(root)) continue;
            foreach (var path in SafeSubdirectories(root))
            {
                cancellation.ThrowIfCancellationRequested();
                if (policy.Evaluate(path) != SafetyDecision.Allow) continue;
                var stats = FolderSizer.Measure(path, cancellation);
                if (stats.SizeBytes < MinimumItemBytes) continue;
                var backup = DeviceBackup.Read(path);
                var date = backup.LastBackupUtc ?? stats.NewestWriteUtc;
                yield return new ScanItem
                {
                    Id = ScanItem.DeviceBackupIdPrefix + Path.GetFileName(path).ToLowerInvariant(),
                    Title = backup.DeviceName == DeviceBackup.UnknownDevice ? "iPhone or iPad Backup" : $"{backup.DeviceName} Backup",
                    Explanation = section.Explanation
                        .Replace("{device}", backup.DeviceName, StringComparison.Ordinal)
                        .Replace("{date}", date.ToLocalTime().ToString("MMMM d, yyyy", CultureInfo.CurrentCulture), StringComparison.Ordinal),
                    Safety = SafetyLevel.CheckFirst,
                    Category = ScanCategory.AppCaches,
                    Kind = "devices",
                    Paths = [path],
                    SizeBytes = stats.SizeBytes,
                    LastModifiedUtc = date,
                    BlockedReason = blocked,
                };
            }
        }
    }

    private ScanItem? ScanEntry(CatalogEntry entry, HashSet<string> claimed, CancellationToken cancellation)
    {
        var folders = entry.Paths
            .SelectMany(p => CleanupCatalog.Expand(p, policy.Locations))
            .Where(claimed.Add)
            .ToList();
        if (folders.Count == 0) return null;

        var candidates = entry.ContentsOnly ? folders.SelectMany(Children) : folders;
        var cutoff = entry.MinAgeHours is { } hours ? DateTime.UtcNow.AddHours(-hours) : (DateTime?)null;

        var paths = new List<string>();
        long size = 0;
        var newest = DateTime.MinValue;
        foreach (var path in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            if (policy.Evaluate(path) != SafetyDecision.Allow) continue;
            var stats = FolderSizer.Measure(path, cancellation);
            if (cutoff is { } c && stats.NewestWriteUtc > c) continue;
            if (stats.SizeBytes == 0 && entry.ContentsOnly) continue;
            paths.Add(path);
            size += stats.SizeBytes;
            if (stats.NewestWriteUtc > newest) newest = stats.NewestWriteUtc;
        }
        if (paths.Count == 0 || size < MinimumItemBytes) return null;

        return new ScanItem
        {
            Id = entry.Key,
            Title = entry.Name,
            Explanation = entry.Explanation,
            Safety = entry.Safety,
            Category = entry.Category,
            Kind = entry.Kind,
            Paths = paths,
            SizeBytes = size,
            LastModifiedUtc = newest == DateTime.MinValue ? null : newest,
            BlockedReason = entry.CloseFirst is { } close && IsAnyRunning(close.Processes)
                ? $"Close {close.App} to clean this"
                : null,
        };
    }

    private IEnumerable<ScanItem> ScanChromiumApps(HashSet<string> claimed, CancellationToken cancellation)
    {
        var section = catalog.ChromiumApps;
        var byApp = new Dictionary<string, List<string>>(PathUtil.Comparer);
        // An app a catalog entry already covers never gets a second, generic row.
        var coveredApps = claimed.Select(Path.GetDirectoryName).OfType<string>().ToHashSet(PathUtil.Comparer);
        foreach (var root in new[] { policy.Locations.RoamingAppData, policy.Locations.LocalAppData })
        {
            foreach (var appFolder in SafeSubdirectories(root))
            {
                if (coveredApps.Contains(appFolder)) continue;
                foreach (var cacheName in section.Folders)
                {
                    var path = Path.Combine(appFolder, cacheName);
                    if (!Directory.Exists(path) || claimed.Contains(path)) continue;
                    if (policy.Evaluate(path) != SafetyDecision.Allow) continue;
                    var app = Path.GetFileName(appFolder);
                    if (!byApp.TryGetValue(app, out var list)) byApp[app] = list = [];
                    list.Add(path);
                }
            }
        }

        foreach (var (app, paths) in byApp.OrderBy(kv => kv.Key, PathUtil.Comparer))
        {
            cancellation.ThrowIfCancellationRequested();
            long size = 0;
            var newest = DateTime.MinValue;
            foreach (var path in paths)
            {
                claimed.Add(path);
                var stats = FolderSizer.Measure(path, cancellation);
                size += stats.SizeBytes;
                if (stats.NewestWriteUtc > newest) newest = stats.NewestWriteUtc;
            }
            if (size < MinimumItemBytes) continue;
            var name = section.Names.TryGetValue(app, out var friendly) ? friendly : TitleCase(app);
            yield return new ScanItem
            {
                Id = "chromium:" + app.ToLowerInvariant(),
                Title = $"{name} Cache",
                Explanation = section.Explanation.Replace("{app}", name, StringComparison.Ordinal),
                Safety = SafetyLevel.Safe,
                Category = section.DevTools.Contains(app, StringComparer.OrdinalIgnoreCase) ? ScanCategory.DevTools : ScanCategory.AppCaches,
                Kind = "app",
                Paths = paths,
                SizeBytes = size,
                LastModifiedUtc = newest == DateTime.MinValue ? null : newest,
            };
        }
    }

    private static string TitleCase(string name) =>
        name.Length > 0 && char.IsLower(name[0]) ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name) : name;

    private static List<string> Children(string folder)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(folder).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<string> SafeSubdirectories(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateDirectories()
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .Select(d => d.FullName)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
