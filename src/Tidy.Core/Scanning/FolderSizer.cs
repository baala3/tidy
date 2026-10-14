using System.IO.Enumeration;

namespace Tidy.Core.Scanning;

public readonly record struct FolderStats(long SizeBytes, DateTime NewestWriteUtc, int Entries);

/// <summary>
/// Sizes a folder tree without following links. A junction inside <c>node_modules</c>
/// (pnpm makes many) points at shared storage that removing the folder never touches,
/// so counting it would promise space that never comes back.
/// </summary>
public static class FolderSizer
{
    /// <summary>Cloud placeholders take no local space until opened; never count or open them.</summary>
    public const FileAttributes CloudOnly = (FileAttributes)0x00400000 | (FileAttributes)0x00040000 | FileAttributes.Offline;

    public static FolderStats Measure(string path, CancellationToken cancellation = default)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return new FolderStats(info.Length, info.LastWriteTimeUtc, 1);
        }
        long size = 0;
        var newest = DateTime.MinValue;
        var entries = 0;
        try
        {
            newest = Directory.GetLastWriteTimeUtc(path);
            var enumerable = new FileSystemEnumerable<(long Size, DateTime Write)>(
                path,
                (ref FileSystemEntry e) => ((e.Attributes & CloudOnly) != 0 ? 0 : e.Length, e.LastWriteTimeUtc.UtcDateTime),
                Options)
            {
                ShouldRecursePredicate = (ref FileSystemEntry e) => (e.Attributes & FileAttributes.ReparsePoint) == 0,
            };
            foreach (var (entrySize, write) in enumerable)
            {
                cancellation.ThrowIfCancellationRequested();
                size += entrySize;
                if (write > newest) newest = write;
                entries++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return new FolderStats(size, newest, entries);
    }

    internal static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };
}
