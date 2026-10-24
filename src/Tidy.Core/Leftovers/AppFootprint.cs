using Microsoft.Win32;
using Tidy.Core.Apps;
using Tidy.Core.Platform;
using Windows.Management.Deployment;

namespace Tidy.Core.Leftovers;

/// <summary>What ties an app to its folders: its name, its publisher, and the folders it told Windows about.</summary>
public sealed record AppFootprint
{
    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    /// <summary>Folders the app registered: its install location, and the folders of its icon and its uninstaller.</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];
    /// <summary>Listed on Tidy's Apps page. Runtimes, drivers, components and Store apps are not.</summary>
    public bool IsListed { get; init; }

    /// <summary>
    /// Everything installed: every uninstall entry, listed or not, and every Store package
    /// for this account. Each one keeps its folders from being offered as a leftover.
    /// </summary>
    public static List<AppFootprint> ReadInstalled(UserLocations locations, AppCatalog catalog)
    {
        var footprints = new List<AppFootprint>();
        foreach (var (hive, view, _) in AppScanner.UninstallRoots)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(AppScanner.UninstallKey);
            if (root is null) continue;
            foreach (var keyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(keyName);
                if (key is not null) footprints.Add(FromUninstallEntry(key, locations, catalog));
            }
        }
        footprints.AddRange(StorePackages());
        return footprints;
    }

    private static AppFootprint FromUninstallEntry(RegistryKey key, UserLocations locations, AppCatalog catalog)
    {
        var folders = new List<string?>
        {
            PathUtil.Normalize(Environment.ExpandEnvironmentVariables((key.GetValue("InstallLocation") as string)?.Trim().Trim('"') ?? "")),
            Path.GetDirectoryName(AppScanner.IconFromDisplayIcon(key.GetValue("DisplayIcon") as string) ?? ""),
        };
        if (key.GetValue("UninstallString") is string uninstall && !string.IsNullOrWhiteSpace(uninstall))
        {
            var (file, _) = AppRemover.SplitCommand(uninstall);
            if (PathUtil.Normalize(file) is { } program && File.Exists(program)) folders.Add(Path.GetDirectoryName(program));
        }
        var listed = AppScanner.ListedName(key, catalog);
        return new AppFootprint
        {
            Name = (key.GetValue("DisplayName") as string)?.Trim() ?? "",
            Publisher = (key.GetValue("Publisher") as string)?.Trim() ?? "",
            Folders = UsefulFolders(folders, locations),
            IsListed = listed is not null,
        };
    }

    /// <summary>
    /// Folders that say something about one app. Program Files, Windows, a drive or a profile
    /// folder holds many apps, so an app that names one of those claims nothing by it.
    /// </summary>
    internal static List<string> UsefulFolders(IEnumerable<string?> folders, UserLocations locations)
    {
        string[] broad =
        [
            locations.Home, locations.LocalAppData, locations.RoamingAppData, locations.LocalLow,
            Path.GetDirectoryName(locations.LocalAppData)!, Path.Combine(locations.LocalAppData, "Programs"),
        ];
        return folders
            .Select(PathUtil.Normalize)
            .OfType<string>()
            .Where(f => f.Length > 3 && Directory.Exists(f) && !AppScanner.IsSharedFolder(f) && !broad.Any(b => PathUtil.IsSameOrUnder(b, f)))
            .Distinct(PathUtil.Comparer)
            .ToList();
    }

    private static IEnumerable<AppFootprint> StorePackages()
    {
        List<Windows.ApplicationModel.Package> packages;
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
            string name, publisher;
            try
            {
                name = package.DisplayName;
                publisher = package.PublisherDisplayName;
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or FileNotFoundException or IOException or UnauthorizedAccessException)
            {
                name = "";
                publisher = "";
            }
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) name = package.Id.Name;
            // "SpotifyAB.SpotifyMusic": the part after the publisher's prefix names the app too.
            var shortName = package.Id.Name[(package.Id.Name.LastIndexOf('.') + 1)..];
            yield return new AppFootprint { Name = name, Publisher = publisher ?? "" };
            if (!shortName.Equals(name, StringComparison.OrdinalIgnoreCase)) yield return new AppFootprint { Name = shortName };
        }
    }
}
