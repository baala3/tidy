using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using Tidy.Core.Platform;
using Tidy.Core.Scanning;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Tidy.Core.Apps;

/// <summary>
/// The apps the user can remove: Store apps installed for this account, and desktop
/// programs listed in Settings → Apps. Runtimes, drivers, codecs and the Store's own
/// plumbing are left out, because other apps break without them.
/// </summary>
public sealed class AppScanner(UserLocations locations, AppCatalog catalog)
{
    public IEnumerable<InstalledApp> Scan(CancellationToken cancellation = default)
    {
        var history = LaunchHistory.Read(ShortcutIndex.Build());
        var running = RunningExecutables();
        foreach (var app in ScanStore(history, cancellation)) yield return app;
        foreach (var app in WithoutNestedSizes(ScanDesktop(history, running, cancellation).ToList())) yield return app;
    }

    /// <summary>
    /// A game installed inside Steam's folder is also counted in Steam's own size. Each byte
    /// is counted once: a program's size leaves out the programs installed inside it.
    /// </summary>
    internal static IEnumerable<InstalledApp> WithoutNestedSizes(List<InstalledApp> apps)
    {
        foreach (var app in apps)
        {
            if (app.InstallLocation is not { } folder)
            {
                yield return app;
                continue;
            }
            var nested = apps.Where(other => other.InstallLocation is { } inner && PathUtil.IsStrictlyUnder(inner, folder)).Sum(other => other.SizeBytes);
            yield return nested > 0 && nested <= app.SizeBytes ? app with { SizeBytes = app.SizeBytes - nested } : app;
        }
    }

    public static bool IsLaunchTrackingEnabled() => LaunchHistory.Read().IsTrackingEnabled;

    // MARK: Store

    private IEnumerable<InstalledApp> ScanStore(LaunchHistory history, CancellationToken cancellation)
    {
        IEnumerable<Package> packages;
        try
        {
            packages = new PackageManager().FindPackagesForUser(string.Empty).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            yield break;
        }

        foreach (var package in packages)
        {
            cancellation.ThrowIfCancellationRequested();
            var app = ReadPackage(package, history);
            if (app is not null) yield return app;
        }
    }

    /// <summary>Null for anything that is not a removable, user-facing app.</summary>
    internal InstalledApp? ReadPackage(Package package, LaunchHistory history)
    {
        try
        {
            if (!IsRemovableStoreApp(package)) return null;
            var entries = package.GetAppListEntriesAsync().AsTask().GetAwaiter().GetResult();
            if (entries.Count == 0) return null;

            var name = package.Id.Name;
            var preinstalled = catalog.PreinstalledFor(name);
            var displayName = preinstalled?.Name
                ?? NonEmpty(package.DisplayName)
                ?? NonEmpty(entries[0].DisplayInfo.DisplayName)
                ?? name;
            var location = package.InstalledPath;
            var dataFolder = Path.Combine(locations.LocalAppData, "Packages", package.Id.FamilyName);
            var size = FolderSizer.Measure(location).SizeBytes + (Directory.Exists(dataFolder) ? FolderSizer.Measure(dataFolder).SizeBytes : 0);
            var version = package.Id.Version;
            return new InstalledApp
            {
                Id = package.Id.FullName,
                Kind = AppKind.Store,
                Name = displayName,
                Publisher = NonEmpty(package.PublisherDisplayName) ?? "",
                Version = $"{version.Major}.{version.Minor}.{version.Build}",
                SizeBytes = size,
                InstalledUtc = package.InstalledDate.UtcDateTime,
                LastUsedUtc = history.ForPackageFamily(package.Id.FamilyName),
                InstallLocation = location,
                IconPath = LogoPath(package),
                Preinstalled = preinstalled,
                PackageFamilyName = package.Id.FamilyName,
            };
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or FileNotFoundException or IOException)
        {
            return null;
        }
    }

    /// <summary>A package the user may remove: not a framework or resource, not part of Windows, not on the protected list.</summary>
    internal bool IsRemovableStoreApp(Package package) =>
        !package.IsFramework &&
        !package.IsResourcePackage &&
        package.SignatureKind != PackageSignatureKind.System &&
        !catalog.IsProtectedPackage(package.Id.Name);

    private static string? LogoPath(Package package)
    {
        try
        {
            var logo = package.Logo?.LocalPath;
            if (logo is null) return null;
            if (File.Exists(logo)) return logo;
            // Logos ship per scale (Logo.scale-200.png); the manifest names the unqualified file.
            var folder = Path.GetDirectoryName(logo)!;
            var stem = Path.GetFileNameWithoutExtension(logo);
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, stem + ".*").OrderByDescending(f => f.Contains("scale-200", StringComparison.OrdinalIgnoreCase)).FirstOrDefault()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // MARK: Desktop

    private static readonly (RegistryHive Hive, RegistryView View, string Label)[] UninstallRoots =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32"),
        (RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU"),
    ];

    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private IEnumerable<InstalledApp> ScanDesktop(LaunchHistory history, IReadOnlyList<string> running, CancellationToken cancellation)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view, label) in UninstallRoots)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(UninstallKey);
            if (root is null) continue;
            foreach (var keyName in root.GetSubKeyNames())
            {
                cancellation.ThrowIfCancellationRequested();
                using var key = root.OpenSubKey(keyName);
                if (key is null) continue;
                var app = ReadDesktopEntry(key, $"{label}\\{keyName}", history, running);
                if (app is not null && seen.Add(app.Name + "|" + app.Version)) yield return app;
            }
        }
    }

    internal InstalledApp? ReadDesktopEntry(RegistryKey key, string id, LaunchHistory history, IReadOnlyList<string> running)
    {
        var name = (key.GetValue("DisplayName") as string)?.Trim();
        var uninstall = key.GetValue("UninstallString") as string;
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(uninstall)) return null;
        if (key.GetValue("SystemComponent") is int system && system == 1) return null;
        if (key.GetValue("ParentKeyName") is string) return null;
        if (key.GetValue("ReleaseType") is string release && release.Contains("Update", StringComparison.OrdinalIgnoreCase)) return null;
        if (catalog.IsProtectedDesktopApp(name)) return null;

        var location = PathUtil.Normalize((key.GetValue("InstallLocation") as string)?.Trim().Trim('"'));
        // An install location that is a whole Program Files folder or a drive says nothing about the app.
        if (location is not null && (location.Length <= 3 || IsSharedFolder(location))) location = null;
        if (location is not null && !Directory.Exists(location)) location = null;

        var icon = IconFromDisplayIcon(key.GetValue("DisplayIcon") as string);
        var exe = icon is not null && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? icon : null;
        var folder = location ?? (exe is not null && !IsSharedFolder(Path.GetDirectoryName(exe)!) ? Path.GetDirectoryName(exe) : null);

        // Tidy never offers to uninstall itself.
        if (folder is not null && Environment.ProcessPath is { } self && PathUtil.IsStrictlyUnder(self, folder)) return null;

        var size = key.GetValue("EstimatedSize") is int kb && kb > 0
            ? kb * 1024L
            : folder is not null ? FolderSizer.Measure(folder).SizeBytes : 0;

        return new InstalledApp
        {
            Id = id,
            Kind = AppKind.Desktop,
            Name = name,
            Publisher = (key.GetValue("Publisher") as string)?.Trim() ?? "",
            Version = (key.GetValue("DisplayVersion") as string)?.Trim() ?? "",
            SizeBytes = size,
            InstalledUtc = ParseInstallDate(key.GetValue("InstallDate") as string),
            LastUsedUtc = history.ForDesktopApp(exe, folder),
            IsRunning = folder is not null && running.Any(p => PathUtil.IsStrictlyUnder(p, folder)),
            InstallLocation = folder,
            IconPath = icon,
            UninstallCommand = uninstall.Trim(),
        };
    }

    private static bool IsSharedFolder(string folder)
    {
        var shared = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };
        return shared.Any(s => s.Length > 0 && PathUtil.AreSame(folder.TrimEnd('\\'), s));
    }

    /// <summary><c>"C:\App\app.exe",0</c> becomes <c>C:\App\app.exe</c>.</summary>
    internal static string? IconFromDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim();
        var comma = path.LastIndexOf(',');
        if (comma > 2 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma];
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return PathUtil.Normalize(path) is { } full && File.Exists(full) ? full : null;
    }

    private static DateTime? ParseInstallDate(string? value) =>
        DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
            ? date.ToUniversalTime()
            : null;

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? null : value;

    private static List<string> RunningExecutables()
    {
        var paths = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } file) paths.Add(file);
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                }
            }
        }
        return paths;
    }
}
