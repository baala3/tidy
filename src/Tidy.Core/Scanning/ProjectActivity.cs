using System.IO.Enumeration;
using Tidy.Core.Models;
using Tidy.Core.Platform;

namespace Tidy.Core.Scanning;

/// <summary>
/// Whether a project's rebuildable folders are offered at all. Both checks look at the
/// project, never at the artifact alone: a <c>node_modules</c> folder only changes date
/// when packages are added or removed, so on its own it says when <c>npm install</c>
/// last ran, not whether anyone still works on the project.
/// </summary>
public static class ProjectActivity
{
    /// <summary>Most entries the activity walk reads in one project before giving up.</summary>
    public const int WalkEntryLimit = 20_000;

    /// <summary>Files git rewrites on commit, checkout, fetch and index refresh.</summary>
    public static readonly string[] GitActivityFiles = ["index", "HEAD", @"logs\HEAD", "FETCH_HEAD", "ORIG_HEAD"];

    public static readonly string[] GitLockFiles = ["index.lock", "HEAD.lock"];

    /// <summary>
    /// In use right now: a process works inside the project, or git holds a lock on
    /// its repository. Hidden whatever the age setting says.
    /// </summary>
    public static bool IsInUse(string projectRoot, IReadOnlySet<string> workingDirectories, string? gitDir)
    {
        if (workingDirectories.Any(cwd => PathUtil.IsSameOrUnder(cwd, projectRoot))) return true;
        return gitDir is not null && GitLockFiles.Any(f => File.Exists(Path.Combine(gitDir, f)));
    }

    /// <summary>Whether anything in the project changed after <paramref name="cutoffUtc"/>. Cheap signals first.</summary>
    public static bool HasActivitySince(string projectRoot, DateTime cutoffUtc, string? gitDir, CancellationToken cancellation = default)
    {
        if (gitDir is not null && GitActivityFiles.Any(f => WriteTime(Path.Combine(gitDir, f)) > cutoffUtc)) return true;

        var seen = 0;
        var walk = new FileSystemEnumerable<bool>(
            projectRoot,
            (ref FileSystemEntry e) => e.LastWriteTimeUtc.UtcDateTime > cutoffUtc,
            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 })
        {
            ShouldRecursePredicate = (ref FileSystemEntry e) =>
                (e.Attributes & FileAttributes.ReparsePoint) == 0 &&
                !e.FileName.Equals(".git", StringComparison.OrdinalIgnoreCase) &&
                !ProjectArtifactCatalog.ArtifactFolderNames.Contains(e.FileName.ToString()),
        };
        try
        {
            foreach (var recent in walk)
            {
                cancellation.ThrowIfCancellationRequested();
                if (recent) return true;
                if (++seen >= WalkEntryLimit) return false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    /// <summary>
    /// The git directory of the repository holding the project, which may sit above it
    /// in a monorepo. Stops at the profile folder: a dotfiles repo there would make every
    /// project look busy. Worktrees (<c>.git</c> is a file) resolve to their own git dir.
    /// </summary>
    public static string? GitDirectory(string projectRoot, string stopAt)
    {
        var current = projectRoot;
        while (current is not null && PathUtil.IsStrictlyUnder(current, Path.GetPathRoot(current)!))
        {
            if (PathUtil.AreSame(current, stopAt)) return null;
            var dotGit = Path.Combine(current, ".git");
            if (Directory.Exists(dotGit)) return dotGit;
            if (File.Exists(dotGit))
            {
                try
                {
                    var line = File.ReadLines(dotGit).FirstOrDefault() ?? "";
                    if (line.StartsWith("gitdir:", StringComparison.Ordinal))
                    {
                        var target = line["gitdir:".Length..].Trim().Replace('/', '\\');
                        return Path.GetFullPath(Path.Combine(current, target));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
                return null;
            }
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    private static DateTime WriteTime(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
}
