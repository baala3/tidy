using System.IO.Enumeration;
using System.Text.RegularExpressions;
using Tidy.Core.Apps;
using Tidy.Core.Catalog;
using Tidy.Core.Platform;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;

namespace Tidy.Core.Leftovers;

/// <summary>Folders outside the profile that only an administrator can change.</summary>
public sealed record SharedRoots(string ProgramData, IReadOnlyList<string> ProgramFiles)
{
    public static SharedRoots Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(p => p.Length > 0)
            .Distinct(PathUtil.Comparer)
            .ToList());
}

/// <summary>What is on the PC right now, gathered once per scan.</summary>
public sealed record LeftoverInputs
{
    /// <summary>Every uninstall entry and Store package. Anything they could own is never offered.</summary>
    public required IReadOnlyList<AppFootprint> Installed { get; init; }
    /// <summary>Apps Tidy saw installed before that are gone now.</summary>
    public required IReadOnlyList<RememberedApp> Gone { get; init; }
    public required IReadOnlyList<KnownLeftover> KnownFolders { get; init; }
    public required IReadOnlyList<string> RunningExecutables { get; init; }
    public required DateTime NowUtc { get; init; }
}

/// <summary>
/// Finds folders that belong to apps that are no longer installed. A folder is offered only
/// on strong evidence of which app it came from: a folder the app registered, its publisher
/// and name together, its install folder's name, or its whole name. Sharing a word is not
/// enough. Being claimed is easy on purpose: a folder that any installed app, running program
/// or program folder could be using, by path or by a looser reading of its name, is left alone.
/// Over-claiming only means a leftover isn't offered; under-claiming would offer live data.
/// </summary>
public sealed partial class LeftoverFinder(DeletionSafetyPolicy policy, SharedRoots shared)
{
    /// <summary>
    /// Folders found without Tidy having seen their app installed must be untouched this long,
    /// so a portable app or one installed some other way, still in use, is never offered.
    /// </summary>
    public const int QuietDays = 30;

    private sealed record Candidate(string Path, string AppName, LeftoverEvidence Evidence, bool MustBeQuiet, DateTime? LastSeenUtc);

    private UserLocations Locations => policy.Locations;
    private string Programs => System.IO.Path.Combine(Locations.LocalAppData, "Programs");

    /// <summary>Per-user roots, innermost first so a folder's root is the closest one.</summary>
    private IEnumerable<string> UserRoots => [Programs, Locations.LocalAppData, Locations.RoamingAppData, Locations.LocalLow];

    private IEnumerable<string> SharedRootFolders => [shared.ProgramData, .. shared.ProgramFiles];

    /// <param name="only">When set, only these folders are considered: the removal-time re-check.</param>
    public IEnumerable<LeftoverFolder> Find(LeftoverInputs inputs, IReadOnlySet<string>? only = null, CancellationToken cancellation = default)
    {
        var children = new Dictionary<string, List<string>>(PathUtil.Comparer);
        var candidates = Candidates(inputs, children)
            .DistinctBy(c => c.Path, PathUtil.Comparer)
            .OrderBy(c => c.Path.Length)
            .ToList();
        // Program folders are apps someone may be using, unless there is evidence the app is gone.
        var orphaned = candidates
            .Where(c => c.Evidence is LeftoverEvidence.RegisteredFolder or LeftoverEvidence.OrphanedInstall)
            .Select(c => c.Path)
            .ToHashSet(PathUtil.Comparer);
        var programs = new[] { Programs }.Concat(shared.ProgramFiles)
            .SelectMany(root => Children(root, children))
            .Where(p => !orphaned.Contains(p))
            .ToList();
        var claims = new Claims(inputs, programs);

        var accepted = new List<string>();
        foreach (var candidate in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            if (only is not null && !only.Contains(candidate.Path)) continue;
            // A folder inside one already offered goes with it.
            if (accepted.Any(a => PathUtil.IsStrictlyUnder(candidate.Path, a))) continue;
            if (Evaluate(candidate, claims, inputs.NowUtc, cancellation) is not { } folder) continue;
            accepted.Add(candidate.Path);
            yield return folder;
        }
    }

    private IEnumerable<Candidate> Candidates(LeftoverInputs inputs, Dictionary<string, List<string>> children)
    {
        foreach (var gone in inputs.Gone)
        {
            var footprint = gone.Footprint;
            foreach (var folder in footprint.Folders.Select(PathUtil.Normalize).OfType<string>().Where(Directory.Exists))
            {
                yield return new Candidate(folder, gone.Name, LeftoverEvidence.RegisteredFolder, false, gone.LastSeenUtc);
            }
            foreach (var (folder, evidence) in ByName(footprint, children))
            {
                yield return new Candidate(folder, gone.Name, evidence, false, gone.LastSeenUtc);
            }
        }

        foreach (var orphan in OrphanedInstalls(children, inputs.NowUtc))
        {
            var name = System.IO.Path.GetFileName(orphan);
            yield return new Candidate(orphan, name, LeftoverEvidence.OrphanedInstall, true, null);
            foreach (var (folder, evidence) in ByName(new AppFootprint { Name = name, Folders = [orphan] }, children))
            {
                yield return new Candidate(folder, name, evidence, true, null);
            }
        }

        foreach (var known in inputs.KnownFolders)
        {
            // "Microsoft Visual Studio Code (User)" is Visual Studio Code.
            var key = NameKeys.Bare(known.App);
            if (inputs.Installed.Any(app => NameKeys.Full(app.Name).Contains(key, StringComparison.Ordinal))) continue;
            foreach (var pattern in known.Folders)
            {
                var folder = PathUtil.Normalize(CleanupCatalog.ResolveTokens(pattern, Locations));
                if (folder is not null && Directory.Exists(folder)) yield return new Candidate(folder, known.App, LeftoverEvidence.KnownAppFolder, true, null);
            }
        }
    }

    /// <summary>Folders in the roots that the app's name, publisher or install folder name points at.</summary>
    private IEnumerable<(string Folder, LeftoverEvidence Evidence)> ByName(AppFootprint app, Dictionary<string, List<string>> children)
    {
        var nameKeys = NameKeys.Of(app.Name);
        var folderKeys = app.Folders.SelectMany(NameKeys.OfFolder).ToHashSet(StringComparer.Ordinal);
        var publisherKeys = NameKeys.OfPublisher(app.Publisher);
        var inside = nameKeys.Concat(NameKeys.OfNameWithoutPublisher(app.Name, app.Publisher)).Concat(folderKeys).ToHashSet(StringComparer.Ordinal);
        if (inside.Count == 0) yield break;

        foreach (var root in UserRoots.Concat(SharedRootFolders))
        {
            foreach (var child in Children(root, children))
            {
                var keys = NameKeys.OfFolder(child);
                if (keys.Overlaps(nameKeys)) yield return (child, LeftoverEvidence.Name);
                else if (keys.Overlaps(folderKeys)) yield return (child, LeftoverEvidence.InstallFolderName);

                // A publisher's folder is often a generic-looking name like "Mozilla", so it is matched on its plain keys.
                if (publisherKeys.Count == 0 || !NameKeys.Of(System.IO.Path.GetFileName(child)).Overlaps(publisherKeys)) continue;
                foreach (var grandchild in Children(child, children))
                {
                    if (NameKeys.OfFolder(grandchild).Overlaps(inside)) yield return (grandchild, LeftoverEvidence.PublisherAndName);
                }
            }
        }
    }

    /// <summary>
    /// Program folders that still hold their uninstaller, though Windows lists nothing installed
    /// there: the uninstall entry went and the folder stayed. Squirrel apps (Discord, Slack) leave
    /// a <c>.dead</c> file behind when they uninstall.
    /// </summary>
    private IEnumerable<string> OrphanedInstalls(Dictionary<string, List<string>> children, DateTime nowUtc)
    {
        foreach (var root in new[] { Programs, Locations.LocalAppData }.Concat(shared.ProgramFiles))
        {
            foreach (var folder in Children(root, children))
            {
                // Only a quiet one: an install still being used, or set up some other way, writes to its folder.
                if (HasUninstaller(folder) && FolderSizer.Measure(folder).NewestWriteUtc <= nowUtc.AddDays(-QuietDays)) yield return folder;
            }
        }
    }

    internal static bool HasUninstaller(string folder)
    {
        try
        {
            var files = Directory.EnumerateFiles(folder).Select(System.IO.Path.GetFileName).OfType<string>().ToList();
            if (files.Any(f => f.Equals(".dead", StringComparison.OrdinalIgnoreCase) || UninstallerName().IsMatch(f))) return true;
            return files.Any(f => f.Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) &&
                Directory.EnumerateDirectories(folder, "app-*").Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private LeftoverFolder? Evaluate(Candidate candidate, Claims claims, DateTime nowUtc, CancellationToken cancellation)
    {
        var path = candidate.Path;
        var userRoot = UserRoots.FirstOrDefault(r => PathUtil.IsStrictlyUnder(path, r));
        var sharedRoot = userRoot is null ? SharedRootFolders.FirstOrDefault(r => r.Length > 0 && PathUtil.IsStrictlyUnder(path, r)) : null;
        var root = userRoot ?? sharedRoot;
        // Anywhere else (another drive, the profile's own folders) is out of scope.
        if (root is null) return null;
        if (NameKeys.IsGenericFolder(path)) return null;
        if (PathUtil.IsReparsePoint(path) || PathUtil.HasReparsePointBetween(path, root)) return null;
        if (userRoot is not null && policy.IsNeverDelete(path)) return null;
        if (DeletionSafetyPolicy.IsProtectedName(path[root.Length..])) return null;
        if (claims.Claim(path)) return null;

        var (stats, isProtected) = Inspect(path, cancellation);
        if (isProtected) return null;
        if (candidate.MustBeQuiet && stats.NewestWriteUtc > nowUtc.AddDays(-QuietDays)) return null;

        return new LeftoverFolder
        {
            Path = path,
            AppName = candidate.AppName,
            Evidence = candidate.Evidence,
            SizeBytes = stats.SizeBytes,
            LastModifiedUtc = stats.NewestWriteUtc,
            NeedsAdministrator = sharedRoot is not null,
            AppLastSeenUtc = candidate.LastSeenUtc,
        };
    }

    /// <summary>
    /// Sizes the folder and looks at every name in it. Anything that looks like a credential
    /// store, password manager or wallet keeps the whole folder off the list.
    /// </summary>
    private static (FolderStats Stats, bool Protected) Inspect(string path, CancellationToken cancellation)
    {
        long size = 0;
        var newest = DateTime.MinValue;
        var entries = 0;
        try
        {
            newest = Directory.GetLastWriteTimeUtc(path);
            var enumerable = new FileSystemEnumerable<(long Size, DateTime Write, bool Protected)>(
                path,
                (ref FileSystemEntry e) => (
                    (e.Attributes & FolderSizer.CloudOnly) != 0 || e.IsDirectory ? 0 : e.Length,
                    e.LastWriteTimeUtc.UtcDateTime,
                    DeletionSafetyPolicy.IsProtectedName(e.FileName.ToString())),
                FolderSizer.Options)
            {
                ShouldRecursePredicate = (ref FileSystemEntry e) => (e.Attributes & FileAttributes.ReparsePoint) == 0,
            };
            foreach (var (entrySize, write, isProtected) in enumerable)
            {
                cancellation.ThrowIfCancellationRequested();
                if (isProtected) return (default, true);
                size += entrySize;
                if (write > newest) newest = write;
                entries++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return (new FolderStats(size, newest, entries), false);
    }

    private static List<string> Children(string folder, Dictionary<string, List<string>> cache)
    {
        if (cache.TryGetValue(folder, out var known)) return known;
        var found = new List<string>();
        try
        {
            if (folder.Length > 0 && Directory.Exists(folder))
            {
                foreach (var child in Directory.EnumerateDirectories(folder))
                {
                    if (!PathUtil.IsReparsePoint(child) && PathUtil.Normalize(child) is { } normalized) found.Add(normalized);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        cache[folder] = found;
        return found;
    }

    /// <summary>Inno Setup's unins000.exe, NSIS's uninst.exe or "Uninstall App.exe", and plain uninstall.exe.</summary>
    [GeneratedRegex(@"^(unins\d{3}|uninst|uninstall|uninstall .+)\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex UninstallerName();

    /// <summary>Everything that keeps a folder from being offered, read the loose way.</summary>
    private sealed class Claims
    {
        private readonly List<string> _folders;
        private readonly List<string> _executables;
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private readonly List<string> _names;

        public Claims(LeftoverInputs inputs, IReadOnlyList<string> programFolders)
        {
            _folders = [.. inputs.Installed.SelectMany(a => a.Folders), .. programFolders];
            _executables = inputs.RunningExecutables.Select(PathUtil.Normalize).OfType<string>().ToList();
            _names = inputs.Installed.Select(a => NameKeys.Full(a.Name)).Where(n => n.Length > 0).ToList();
            foreach (var app in inputs.Installed)
            {
                _keys.UnionWith(NameKeys.Of(app.Name));
                _keys.UnionWith(NameKeys.OfNameWithoutPublisher(app.Name, app.Publisher));
                _keys.UnionWith(NameKeys.OfPublisher(app.Publisher));
                if (NameKeys.FirstWord(app.Name) is { } first) _keys.Add(first);
                foreach (var folder in app.Folders) _keys.UnionWith(NameKeys.Of(System.IO.Path.GetFileName(folder)));
            }
            foreach (var folder in programFolders) _keys.UnionWith(NameKeys.Of(System.IO.Path.GetFileName(folder)));
            foreach (var exe in _executables) _keys.UnionWith(NameKeys.Of(System.IO.Path.GetFileNameWithoutExtension(exe)));
        }

        public bool Claim(string path)
        {
            if (_folders.Any(f => PathUtil.IsSameOrUnder(path, f) || PathUtil.IsSameOrUnder(f, path))) return true;
            if (_executables.Any(e => PathUtil.IsStrictlyUnder(e, path))) return true;
            var keys = NameKeys.Of(System.IO.Path.GetFileName(path));
            // %APPDATA%\Code belongs to "Microsoft Visual Studio Code"; shorter keys are in too many names.
            return keys.Overlaps(_keys) || keys.Any(k => k.Length >= 4 && _names.Any(n => n.Contains(k, StringComparison.Ordinal)));
        }
    }
}
