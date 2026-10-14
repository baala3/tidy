using Tidy.Core.Models;
using Tidy.Core.Platform;

namespace Tidy.Core.Safety;

/// <summary>
/// Bounds for Large Files. Deliberately apart from <see cref="DeletionSafetyPolicy"/>:
/// these are personal files, not caches, so they are never part of a one-click clean
/// and each one is picked by hand.
/// </summary>
public sealed class LargeFilePolicy(UserLocations locations)
{
    /// <summary>Managed libraries and project folders the scan never descends into.</summary>
    public static readonly IReadOnlySet<string> ExcludedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Dependency and build folders belong in Dev Tools, removed a whole folder at a time.
        "node_modules", "bower_components", "vendor", "Pods", "target", "build", "dist", "out", "bin", "obj",
        "venv", "env", "site-packages", "__pycache__", "DerivedData", "Library", "Intermediate", "DerivedDataCache",
        // Apps keep their own databases in the personal folders. Pulling one file out breaks them.
        "Outlook Files", "OneNote Notebooks", "Lightroom", "Adobe", "Zoom", "Steam", "My Games", "Saved Games",
        "WindowsPowerShell", "PowerShell", "Visual Studio 2019", "Visual Studio 2022", "Visual Studio 2026",
        "Camera Roll", "Screenshots",
    };

    public IEnumerable<(string Root, string Label)> ScanRoots() =>
    [
        (locations.Documents, "Documents"),
        (locations.Desktop, "Desktop"),
        (locations.Downloads, "Downloads"),
        (locations.Videos, "Videos"),
        (locations.Music, "Music"),
        (locations.Pictures, "Pictures"),
    ];

    public static bool IsExcludedDirectoryName(string name) =>
        name.StartsWith('.') || name.StartsWith('$') || ExcludedFolderNames.Contains(name) ||
        ProjectArtifactCatalog.ArtifactFolderNames.Contains(name);

    /// <summary>
    /// A file may be removed only if it is a plain, local file under a scan root,
    /// outside every excluded folder, with no link anywhere on its path.
    /// </summary>
    public bool IsEligibleForDeletion(string rawPath)
    {
        var path = PathUtil.Normalize(rawPath);
        if (path is null || !File.Exists(path)) return false;
        var root = ScanRoots().Select(r => r.Root).FirstOrDefault(r => PathUtil.IsStrictlyUnder(path, r));
        if (root is null) return false;
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.System | FileAttributes.ReparsePoint)) != 0) return false;
        if ((attributes & Scanning.FolderSizer.CloudOnly) != 0) return false;
        var between = path[(root.Length + 1)..].Split('\\')[..^1];
        if (between.Any(IsExcludedDirectoryName)) return false;
        return !PathUtil.HasReparsePointBetween(path, root);
    }
}
