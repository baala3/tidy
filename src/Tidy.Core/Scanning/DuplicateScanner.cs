using Tidy.Core.Models;
using Tidy.Core.Platform;
using Tidy.Core.Safety;

namespace Tidy.Core.Scanning;

/// <summary>
/// Files with identical contents in Documents, Desktop, Downloads, Videos, Music and
/// Pictures. Candidates are narrowed cheaply first (same size, then the same first and
/// last 64 KB), so only real look-alikes are read in full. Hard links are one file under
/// two names, not a duplicate: removing one frees nothing, so they are left out.
/// </summary>
public sealed class DuplicateScanner(LargeFilePolicy policy)
{
    /// <summary>Copies of smaller files add up to little and would make the list long.</summary>
    public const long DefaultMinimumBytes = 1024 * 1024;

    public long MinimumBytes { get; init; } = DefaultMinimumBytes;

    /// <summary>Groups come out largest files first, so the biggest wins show up early.</summary>
    public IEnumerable<DuplicateGroup> Scan(IProgress<DuplicateScanProgress>? progress = null, CancellationToken cancellation = default)
    {
        var files = new LargeFileScanner(policy) { MinimumBytes = MinimumBytes }.Scan(cancellation).ToList();
        var buckets = files.GroupBy(f => f.SizeBytes).Where(g => g.Count() > 1).OrderByDescending(g => g.Key).ToList();
        var toCompare = buckets.Sum(b => b.Count());
        var compared = 0;
        progress?.Report(new DuplicateScanProgress(0, toCompare));

        foreach (var bucket in buckets)
        {
            cancellation.ThrowIfCancellationRequested();
            var copies = DistinctFiles(bucket);
            foreach (var group in GroupsOfSameContent(copies, bucket.Key, cancellation)) yield return group;
            compared += bucket.Count();
            progress?.Report(new DuplicateScanProgress(compared, toCompare));
        }
    }

    /// <summary>One entry per file on disk: a second name for the same file (a hard link) is dropped.</summary>
    private static List<DuplicateCopy> DistinctFiles(IEnumerable<LargeFile> files)
    {
        var seen = new HashSet<FileIdentity>();
        var copies = new List<DuplicateCopy>();
        foreach (var file in files)
        {
            if (FileIdentity.Of(file.Path) is not { } identity || !seen.Add(identity)) continue;
            try
            {
                copies.Add(new DuplicateCopy(file, File.GetCreationTimeUtc(file.Path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return copies;
    }

    private static IEnumerable<DuplicateGroup> GroupsOfSameContent(List<DuplicateCopy> copies, long size, CancellationToken cancellation)
    {
        if (copies.Count < 2) yield break;
        var bySample = copies
            .Select(c => (Copy: c, Hash: FileContent.SampleHash(c.Path, cancellation)))
            .Where(x => x.Hash is not null)
            .GroupBy(x => x.Hash!, x => x.Copy)
            .Where(g => g.Count() > 1);
        foreach (var sample in bySample)
        {
            // A file that fits inside the sample was read whole already.
            var byContent = size <= FileContent.SampleBytes * 2
                ? [new Grouping(sample.Key, sample.ToList())]
                : sample
                    .Select(c => (Copy: c, Hash: FileContent.FullHash(c.Path, cancellation)))
                    .Where(x => x.Hash is not null)
                    .GroupBy(x => x.Hash!, x => x.Copy)
                    .Select(g => new Grouping(g.Key, g.ToList()));
            foreach (var group in byContent.Where(g => g.Copies.Count > 1))
            {
                yield return new DuplicateGroup(group.Hash, size, group.Copies.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList());
            }
        }
    }

    private sealed record Grouping(string Hash, List<DuplicateCopy> Copies);
}
