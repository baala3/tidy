using System.IO.Enumeration;
using Tidy.Core.Models;
using Tidy.Core.Platform;
using Tidy.Core.Safety;

namespace Tidy.Core.Scanning;

/// <summary>
/// Big personal files in Documents, Desktop, Downloads, Videos, Music and Pictures.
/// Cloud-only OneDrive files are skipped: they take no space here, and reading one
/// would download it.
/// </summary>
public sealed class LargeFileScanner(LargeFilePolicy policy)
{
    public long MinimumBytes { get; init; } = 5L * 1024 * 1024;

    /// <summary>Only files of these kinds, or every file when null.</summary>
    public IReadOnlySet<LargeFileKind>? Kinds { get; init; }

    public IEnumerable<LargeFile> Scan(CancellationToken cancellation = default)
    {
        var seen = new HashSet<string>(PathUtil.Comparer);
        foreach (var (root, label) in policy.ScanRoots())
        {
            if (!Directory.Exists(root)) continue;
            var minimum = MinimumBytes;
            var kinds = Kinds;
            var files = new FileSystemEnumerable<LargeFile?>(
                root,
                (ref FileSystemEntry e) =>
                {
                    if (e.IsDirectory || e.Length < minimum) return null;
                    if ((e.Attributes & (FileAttributes.System | FileAttributes.ReparsePoint | FolderSizer.CloudOnly)) != 0) return null;
                    var path = e.ToFullPath();
                    var kind = LargeFile.KindFor(path);
                    return kinds is null || kinds.Contains(kind) ? new LargeFile(path, e.Length, e.LastWriteTimeUtc.UtcDateTime, kind, label) : null;
                },
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 })
            {
                ShouldRecursePredicate = (ref FileSystemEntry e) =>
                    (e.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0 &&
                    !LargeFilePolicy.IsExcludedDirectoryName(e.FileName.ToString()),
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && e.Length >= minimum,
            };
            foreach (var file in files)
            {
                cancellation.ThrowIfCancellationRequested();
                if (file is not null && seen.Add(file.Path)) yield return file;
            }
        }
    }
}
