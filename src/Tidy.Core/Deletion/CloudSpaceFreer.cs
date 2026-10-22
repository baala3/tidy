using Tidy.Core.Cloud;
using Tidy.Core.Platform;
using Tidy.Core.Safety;

namespace Tidy.Core.Deletion;

/// <summary>
/// Makes OneDrive files online-only, the way File Explorer's "Free up space" does. Nothing is
/// deleted, so this is not a removal and doesn't go through <see cref="FileDeleter"/>; each
/// file is still re-checked against <see cref="OneDrivePolicy"/> right before it changes.
/// The space comes back once OneDrive drops the local copies, so the result counts what the
/// marked files take on disk now: the space about to be freed.
/// </summary>
public sealed class CloudSpaceFreer(OneDrivePolicy policy)
{
    public Task<CleanResult> FreeUpSpaceAsync(IReadOnlyList<string> paths, IProgress<string>? progress = null, CancellationToken cancellation = default) =>
        Task.Run(() => Run(paths, progress, cancellation), cancellation);

    internal CleanResult Run(IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellation)
    {
        var freed = new List<string>();
        var failures = new List<CleanFailure>();
        long bytes = 0;
        foreach (var raw in paths.Distinct(PathUtil.Comparer))
        {
            cancellation.ThrowIfCancellationRequested();
            var path = PathUtil.Normalize(raw) ?? raw;
            progress?.Report(path);
            if (policy.Refusal(path) is { } refusal)
            {
                failures.Add(new CleanFailure(path, refusal));
                continue;
            }
            var onDisk = CloudFiles.OnDiskBytes(path);
            if (CloudFiles.MarkOnlineOnly(path) is { } error)
            {
                failures.Add(new CleanFailure(path, error));
                continue;
            }
            freed.Add(path);
            bytes += onDisk;
        }
        return new CleanResult(bytes, freed, failures);
    }
}
