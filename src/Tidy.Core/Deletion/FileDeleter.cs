using Tidy.Core.Platform;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;

namespace Tidy.Core.Deletion;

public sealed record CleanFailure(string Path, string Reason);

public sealed record CleanResult(long FreedBytes, IReadOnlyList<string> Removed, IReadOnlyList<CleanFailure> Failures);

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
public sealed class FileDeleter(DeletionSafetyPolicy cachePolicy, LargeFilePolicy largeFilePolicy)
{
    /// <summary>For tests: replaces the shell call. Returns null on success, or a reason.</summary>
    internal Func<string, string?>? RecycleOverride { get; init; }

    public Task<CleanResult> MoveToRecycleBinAsync(
        IReadOnlyList<string> paths, DeletionKind kind, IntPtr ownerWindow, IProgress<string>? progress = null)
    {
        var completion = new TaskCompletionSource<CleanResult>();
        // IFileOperation is apartment-threaded, so it gets a thread of its own.
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Run(paths, kind, ownerWindow, progress));
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

    internal CleanResult Run(IReadOnlyList<string> paths, DeletionKind kind, IntPtr ownerWindow, IProgress<string>? progress)
    {
        var removed = new List<string>();
        var failures = new List<CleanFailure>();
        long freed = 0;

        foreach (var raw in paths.Distinct(PathUtil.Comparer))
        {
            var path = PathUtil.Normalize(raw) ?? raw;
            progress?.Report(path);

            var refusal = Refusal(path, kind);
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
}
