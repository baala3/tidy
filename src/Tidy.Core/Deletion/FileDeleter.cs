using Tidy.Core.Platform;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;

namespace Tidy.Core.Deletion;

public sealed record CleanFailure(string Path, string Reason);

public sealed record CleanResult(long FreedBytes, IReadOnlyList<string> Removed, IReadOnlyList<CleanFailure> Failures);

/// <summary>Removes <paramref name="Path"/>, an extra copy of <paramref name="KeepPath"/>, which stays.</summary>
public sealed record DuplicateRemoval(string Path, string KeepPath);

public enum DeletionKind
{
    /// <summary>Cache folders and project artifacts, gated by <see cref="DeletionSafetyPolicy"/>.</summary>
    Cache,
    /// <summary>Personal files from Large Files, gated by <see cref="LargeFilePolicy"/>.</summary>
    LargeFile,
}

/// <summary>
/// The only code in Tidy that removes anything. Every path is re-checked against the
/// safety policy at the moment of removal, not just at scan time, and then moved to the
/// Recycle Bin. Nothing is ever deleted for good.
/// </summary>
public sealed class FileDeleter(DeletionSafetyPolicy cachePolicy, LargeFilePolicy largeFilePolicy, LeftoverPolicy leftoverPolicy)
{
    /// <summary>For tests: replaces the shell call. Returns null on success, or a reason.</summary>
    internal Func<string, string?>? RecycleOverride { get; init; }

    public Task<CleanResult> MoveToRecycleBinAsync(
        IReadOnlyList<string> paths, DeletionKind kind, IntPtr ownerWindow, IProgress<string>? progress = null) =>
        OnStaThread(() => Run(paths, kind, ownerWindow, progress));

    /// <summary>
    /// Extra copies from Duplicates. Each one is compared byte for byte with the copy that
    /// stays, right before it moves, so a file that changed since the scan is never lost.
    /// </summary>
    public Task<CleanResult> MoveDuplicatesToRecycleBinAsync(
        IReadOnlyList<DuplicateRemoval> removals, IntPtr ownerWindow, IProgress<string>? progress = null) =>
        OnStaThread(() => RunDuplicates(removals, ownerWindow, progress));

    /// <summary>
    /// Folders apps left behind. What is installed and running is read again first, and each
    /// folder has to still be a leftover nothing installed could own.
    /// </summary>
    public Task<CleanResult> MoveLeftoversToRecycleBinAsync(
        IReadOnlyList<string> paths, IntPtr ownerWindow, IProgress<string>? progress = null) =>
        OnStaThread(() => RunLeftovers(paths, ownerWindow, progress));

    private static Task<CleanResult> OnStaThread(Func<CleanResult> run)
    {
        var completion = new TaskCompletionSource<CleanResult>();
        // IFileOperation is apartment-threaded, so it gets a thread of its own.
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(run());
            }
            catch (Exception e)
            {
                completion.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = "Tidy recycle",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal CleanResult Run(IReadOnlyList<string> paths, DeletionKind kind, IntPtr ownerWindow, IProgress<string>? progress) =>
        Recycle(paths.Distinct(PathUtil.Comparer).Select(p => (p, (Func<string, string?>)(path => Refusal(path, kind)))), ownerWindow, progress);

    internal CleanResult RunLeftovers(IReadOnlyList<string> paths, IntPtr ownerWindow, IProgress<string>? progress)
    {
        var refusal = leftoverPolicy.Refusals(paths);
        return Recycle(paths.Distinct(PathUtil.Comparer).Select(p => (p, refusal)), ownerWindow, progress);
    }

    internal CleanResult RunDuplicates(IReadOnlyList<DuplicateRemoval> removals, IntPtr ownerWindow, IProgress<string>? progress) =>
        Recycle(removals.DistinctBy(r => r.Path, PathUtil.Comparer).Select(r => (r.Path, (Func<string, string?>)(path => DuplicateRefusal(path, r.KeepPath)))), ownerWindow, progress);

    private CleanResult Recycle(IEnumerable<(string Path, Func<string, string?> Refusal)> items, IntPtr ownerWindow, IProgress<string>? progress)
    {
        var removed = new List<string>();
        var failures = new List<CleanFailure>();
        long freed = 0;

        foreach (var (raw, check) in items)
        {
            var path = PathUtil.Normalize(raw) ?? raw;
            progress?.Report(path);

            var refusal = check(path);
            if (refusal is not null)
            {
                failures.Add(new CleanFailure(path, refusal));
                continue;
            }

            var size = FolderSizer.Measure(path).SizeBytes;
            var capacity = RecycleBin.CapacityFor(path);
            if (!capacity.Enabled)
            {
                failures.Add(new CleanFailure(path, $"The Recycle Bin is turned off for drive {Path.GetPathRoot(path)}, so it could not be put back. Tidy left it alone."));
                continue;
            }
            if (size > capacity.MaxBytes)
            {
                failures.Add(new CleanFailure(path, "It is larger than the Recycle Bin can hold, so Windows would delete it for good. Tidy left it alone."));
                continue;
            }

            var error = RecycleOverride is { } fake ? fake(path) : RecycleBin.Recycle(path, ownerWindow);
            if (error is null)
            {
                removed.Add(path);
                freed += size;
            }
            else
            {
                failures.Add(new CleanFailure(path, error));
            }
        }
        return new CleanResult(freed, removed, failures);
    }

    private string? Refusal(string path, DeletionKind kind)
    {
        switch (kind)
        {
            case DeletionKind.Cache:
                return cachePolicy.Evaluate(path) switch
                {
                    SafetyDecision.Allow => null,
                    SafetyDecision.BlockedNeverDelete => "Protected location. Not eligible for cleaning.",
                    SafetyDecision.BlockedNotAllowlisted => "Skipped for safety.",
                    SafetyDecision.BlockedLink => "A link or junction is on its path, so Tidy left it alone.",
                    SafetyDecision.BlockedInvalid => "It no longer exists.",
                    _ => throw new ArgumentOutOfRangeException(nameof(path)),
                };
            case DeletionKind.LargeFile:
                return largeFilePolicy.IsEligibleForDeletion(path) ? null : "Skipped for safety.";
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private string? DuplicateRefusal(string path, string keepPath)
    {
        if (!largeFilePolicy.IsEligibleForDeletion(path)) return "Skipped for safety.";
        var keep = PathUtil.Normalize(keepPath);
        if (keep is null || PathUtil.AreSame(path, keep)) return "Tidy couldn't tell which copy stays, so it left this one alone.";
        if (!File.Exists(keep) || (File.GetAttributes(keep) & (FileAttributes.ReparsePoint | FolderSizer.CloudOnly)) != 0)
        {
            return "The copy Tidy was keeping is no longer on this PC, so it left this one alone.";
        }
        if (FileIdentity.Of(path) is not { } identity || FileIdentity.Of(keep) is not { } kept) return "It is in use. Close the app using it and try again.";
        if (identity == kept) return "Both names point at the same file, so removing one would free nothing.";
        return FileContent.SameContent(path, keep) ? null : "It no longer matches the copy Tidy is keeping, so Tidy left it alone.";
    }
}
