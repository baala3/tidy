namespace Tidy.Core.Models;

/// <summary>
/// One removable folder belonging to one kind of project. A rule only ever
/// proposes a path: <see cref="Safety.DeletionSafetyPolicy"/> still has the final
/// say, so a rule can narrow what Tidy deletes but never widen it.
/// </summary>
public sealed record ProjectArtifactRule
{
    /// <summary>Key into the catalog's artifact explanations.</summary>
    public required string Kind { get; init; }
    public required string ProjectType { get; init; }
    /// <summary>Folder to remove, relative to the project root. May have several segments (<c>vendor\bundle</c>).</summary>
    public required string Folder { get; init; }
    /// <summary>Any one of these, relative to the project root, must exist for the rule to fire.</summary>
    public string[] RootMarkers { get; init; } = [];
    /// <summary>Extensions that identify the project when no fixed filename does (<c>csproj</c>, <c>uproject</c>).</summary>
    public string[] RootMarkerExtensions { get; init; } = [];
    /// <summary>
    /// When any of these exist (relative to the artifact's parent), a reinstall
    /// restores the exact same versions. When the list is non-empty and none exist,
    /// the row drops to Check First.
    /// </summary>
    public string[] Lockfiles { get; init; } = [];
    /// <summary>Evidence a rebuild is possible at all, relative to the artifact's parent. Empty means none needed.</summary>
    public string[] RebuildMarkers { get; init; } = [];
    /// <summary>Files that, found inside the artifact folder, mean it holds real state and must not be offered.</summary>
    public string[] RefuseWhenPresent { get; init; } = [];
    public required string Restore { get; init; }
    public SafetyLevel Level { get; init; } = SafetyLevel.Safe;

    public string FolderName => Folder.Split('\\')[^1];

    public bool MatchesRoot(string root)
    {
        foreach (var marker in RootMarkers)
        {
            if (Path.Exists(Path.Combine(root, marker))) return true;
        }
        if (RootMarkerExtensions.Length == 0) return false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root))
            {
                var ext = Path.GetExtension(file).TrimStart('.');
                if (RootMarkerExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    public bool RefusesArtifact(string artifactPath) =>
        RefuseWhenPresent.Any(name => Path.Exists(Path.Combine(artifactPath, name)));

    public bool CanRebuild(string artifactPath) =>
        RebuildMarkers.Length == 0 || RebuildMarkers.Any(m => Path.Exists(ResolveBeside(artifactPath, m)));

    public bool HasLockfile(string artifactPath) =>
        Lockfiles.Length == 0 || Lockfiles.Any(m => Path.Exists(ResolveBeside(artifactPath, m)));

    private static string ResolveBeside(string artifactPath, string relative) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(artifactPath)!, relative));
}
