using Tidy.Core.Deletion;
using Tidy.Core.Platform;

namespace Tidy.Core.VirtualDisks;

/// <summary>
/// Shrinks WSL and Docker disk images to the space their data takes. A disk can only be
/// compacted while nothing has it open, so this stops Docker Desktop and shuts WSL down
/// first; the user agrees to that before it starts. Nothing inside the disks changes.
/// </summary>
public sealed class VirtualDiskCompactor(VirtualDiskScanner scanner)
{
    public Func<string, string, TimeSpan, CommandResult> RunCommand { get; init; } = (file, args, timeout) => Wsl.Run(file, args, timeout);
    public Func<bool> IsDockerRunning { get; init; } = () => RunningApps.AnyRunning(["Docker Desktop"]);
    public Func<string, string?> Compact { get; init; } = VirtualDiskApi.Compact;
    public Func<string, bool> IsFree { get; init; } = VirtualDiskApi.IsFree;
    public TimeSpan ReleaseTimeout { get; init; } = TimeSpan.FromMinutes(1);

    public Task<CleanResult> CompactAsync(IReadOnlyList<string> paths, IProgress<string>? progress = null, CancellationToken cancellation = default) =>
        Task.Run(() => Run(paths, progress, cancellation), cancellation);

    internal CleanResult Run(IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellation)
    {
        var failures = new List<CleanFailure>();
        // Only disks found again now: a path that stopped being a WSL or Docker disk is refused.
        var known = scanner.Scan().ToDictionary(d => d.Path, PathUtil.Comparer);
        var disks = new List<VirtualDisk>();
        foreach (var raw in paths.Distinct(PathUtil.Comparer))
        {
            if (PathUtil.Normalize(raw) is { } path && known.TryGetValue(path, out var disk)) disks.Add(disk);
            else failures.Add(new CleanFailure(raw, "It is no longer a WSL or Docker disk, so Tidy left it alone."));
        }
        if (disks.Count == 0) return new CleanResult(0, [], failures);

        if (IsDockerRunning())
        {
            if (disks.Any(d => d.Owner == VirtualDiskOwner.Docker))
            {
                // Docker's data disk doesn't free blocks as files go; a trim marks them free so compacting can return them.
                progress?.Report("Trimming Docker's disk…");
                RunCommand("wsl.exe", "-d docker-desktop -u root -e fstrim /mnt/docker-desktop-disk", TimeSpan.FromMinutes(5));
            }
            progress?.Report("Stopping Docker Desktop…");
            if (RunCommand("docker", "desktop stop", TimeSpan.FromMinutes(3)).ExitCode != 0)
            {
                failures.AddRange(disks.Select(d => new CleanFailure(d.Path, "Tidy couldn't stop Docker Desktop. Quit it from its icon in the taskbar, then try again.")));
                return new CleanResult(0, [], failures);
            }
        }

        progress?.Report("Shutting down WSL…");
        RunCommand("wsl.exe", "--shutdown", TimeSpan.FromMinutes(2));

        var compacted = new List<string>();
        long freed = 0;
        for (var i = 0; i < disks.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var disk = disks[i];
            progress?.Report(disks.Count == 1 ? $"Compacting {disk.Name}…" : $"Compacting {disk.Name} ({i + 1} of {disks.Count})…");
            if (!WaitUntilFree(disk.Path, cancellation))
            {
                failures.Add(new CleanFailure(disk.Path, "WSL is still using it. Close any Linux windows and try again."));
                continue;
            }
            var before = new FileInfo(disk.Path).Length;
            if (Compact(disk.Path) is { } error)
            {
                failures.Add(new CleanFailure(disk.Path, error));
                continue;
            }
            compacted.Add(disk.Path);
            freed += Math.Max(0, before - new FileInfo(disk.Path).Length);
        }
        return new CleanResult(freed, compacted, failures);
    }

    /// <summary>WSL lets go of its disks a moment after it shuts down.</summary>
    private bool WaitUntilFree(string path, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + ReleaseTimeout;
        while (!IsFree(path))
        {
            if (DateTime.UtcNow > deadline) return false;
            cancellation.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
            cancellation.ThrowIfCancellationRequested();
        }
        return true;
    }
}
