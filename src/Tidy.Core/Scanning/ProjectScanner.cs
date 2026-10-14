using Tidy.Core.Catalog;
using Tidy.Core.Models;
using Tidy.Core.Platform;
using Tidy.Core.Safety;

namespace Tidy.Core.Scanning;

/// <summary>
/// Finds developer projects and their rebuildable folders: <c>node_modules</c>, Rust
/// <c>target</c>, Python environments, .NET <c>bin</c> and <c>obj</c>, and the rest of
/// <see cref="ProjectArtifactCatalog"/>. Only projects nobody has touched for the stale
/// threshold, and that nothing is running inside, are listed.
/// </summary>
public sealed class ProjectScanner(DeletionSafetyPolicy policy, CleanupCatalog catalog)
{
    public const int MaxDepth = 5;
    /// <summary>A folder with more entries than this is a data dump, not a source tree; skip it.</summary>
    public const int MaxEntriesPerFolder = 2000;

    /// <summary>Zero lists every project regardless of age.</summary>
    public int StaleDays { get; init; } = 180;
    public Func<IReadOnlySet<string>> WorkingDirectories { get; init; } = ProcessInspector.WorkingDirectories;

    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppData", "Application Data", "Local Settings", "OneDrive", "Windows", "Program Files", "Program Files (x86)",
        "ProgramData", "$Recycle.Bin", "System Volume Information", "Music", "Pictures", "Videos", "Saved Games",
        "Favorites", "Links", "Contacts", "Searches", "scoop", "go", "miniconda3", "anaconda3", "miniforge3", "mambaforge",
    };

    public IEnumerable<string> SearchRoots()
    {
        var l = policy.Locations;
        foreach (var name in UserLocations.DeveloperFolderNames.Concat(["Developer", "Development", "Projects", "Code", "Work", "source\\repos"]))
        {
            yield return Path.Combine(l.Home, name);
        }
        yield return l.Documents;
        yield return l.Desktop;
        yield return l.Downloads;
        foreach (var root in l.DeveloperRoots) yield return root;
        // Last: its one-level walk would otherwise mark the folders above as visited
        // before they get their own full-depth walk.
        yield return l.Home;
    }

    public IEnumerable<ScanItem> Scan(CancellationToken cancellation = default)
    {
        var roots = DiscoverProjectRoots(cancellation);
        var cwd = WorkingDirectories();
        var cutoff = StaleDays > 0 ? DateTime.UtcNow.AddDays(-StaleDays) : (DateTime?)null;

        foreach (var root in roots)
        {
            cancellation.ThrowIfCancellationRequested();
            var artifacts = ArtifactsIn(root).ToList();
            if (artifacts.Count == 0) continue;

            var gitDir = ProjectActivity.GitDirectory(root, policy.Locations.Home);
            if (ProjectActivity.IsInUse(root, cwd, gitDir)) continue;
            if (cutoff is { } c && ProjectActivity.HasActivitySince(root, c, gitDir, cancellation)) continue;

            foreach (var (rule, path) in artifacts)
            {
                cancellation.ThrowIfCancellationRequested();
                if (policy.Evaluate(path) != SafetyDecision.Allow) continue;
                var stats = FolderSizer.Measure(path, cancellation);
                if (stats.SizeBytes == 0) continue;
                var text = catalog.ArtifactTextFor(rule.Kind);
                var lockfileMissing = !rule.HasLockfile(path);
                yield return new ScanItem
                {
                    Id = "project:" + path.ToLowerInvariant(),
                    Title = text.Name,
                    Explanation = lockfileMissing
                        ? text.Explanation + " This project has no lockfile, so a reinstall may pick up newer versions."
                        : text.Explanation,
                    Safety = lockfileMissing ? SafetyLevel.CheckFirst : rule.Level,
                    Category = ScanCategory.DevTools,
                    Kind = "project",
                    Paths = [path],
                    SizeBytes = stats.SizeBytes,
                    LastModifiedUtc = stats.NewestWriteUtc,
                    ProjectName = Path.GetFileName(root),
                    ProjectPath = root,
                    RestoreHint = rule.Restore,
                };
            }
        }
    }

    /// <summary>The artifact folders present in one project root, each matched to the rule that proposes it.</summary>
    private IEnumerable<(ProjectArtifactRule Rule, string Path)> ArtifactsIn(string root)
    {
        var seen = new HashSet<string>(PathUtil.Comparer);
        foreach (var rule in ProjectArtifactCatalog.Rules)
        {
            var path = Path.Combine(root, rule.Folder);
            if (seen.Contains(path) || !Directory.Exists(path)) continue;
            if (!rule.MatchesRoot(root) || rule.RefusesArtifact(path) || !rule.CanRebuild(path)) continue;
            seen.Add(path);
            yield return (rule, path);
        }
    }

    public List<string> DiscoverProjectRoots(CancellationToken cancellation = default)
    {
        var found = new List<string>();
        var visited = new HashSet<string>(PathUtil.Comparer);

        void Walk(string directory, int depth, int maxDepth)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!visited.Add(directory)) return;
            if (ProjectArtifactCatalog.Rules.Any(r => r.MatchesRoot(directory)) && policy.CanHoldProjects(directory))
            {
                found.Add(directory);
            }
            if (depth >= maxDepth) return;

            List<DirectoryInfo> children;
            try
            {
                children = new DirectoryInfo(directory).EnumerateDirectories().Take(MaxEntriesPerFolder + 1).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;
            }
            if (children.Count > MaxEntriesPerFolder) return;

            foreach (var child in children)
            {
                var name = child.Name;
                if (name.StartsWith('.') || name.StartsWith('$')) continue;
                if ((child.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                if (SkipNames.Contains(name) || ProjectArtifactCatalog.ArtifactFolderNames.Contains(name)) continue;
                Walk(child.FullName, depth + 1, maxDepth);
            }
        }

        foreach (var root in SearchRoots().Select(PathUtil.Normalize).OfType<string>().Distinct(PathUtil.Comparer))
        {
            if (!Directory.Exists(root) || PathUtil.IsReparsePoint(root)) continue;
            // The profile folder itself only one level down: Documents, Desktop and the
            // developer folders are walked as roots of their own.
            Walk(root, 0, PathUtil.AreSame(root, policy.Locations.Home) ? 1 : MaxDepth);
        }
        return found;
    }
}
