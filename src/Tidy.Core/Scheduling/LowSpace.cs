using Tidy.Core.Platform;

namespace Tidy.Core.Scheduling;

/// <summary>When a drive counts as running low, and which drives the background run watches.</summary>
public static class LowSpace
{
    /// <summary>On big drives 10% is a lot of room, so the threshold stops growing here.</summary>
    public const long MaxThresholdBytes = 20L * 1024 * 1024 * 1024;

    public static readonly TimeSpan AlertInterval = TimeSpan.FromDays(1);

    /// <summary>Less than 10% free, and never more than 20 GB.</summary>
    public static long ThresholdBytes(long totalBytes) => Math.Min(totalBytes / 10, MaxThresholdBytes);

    public static bool IsLow(DiskUsage disk) => disk.TotalBytes > 0 && disk.FreeBytes < ThresholdBytes(disk.TotalBytes);

    /// <summary>
    /// The drives that are low and haven't had an alert in the last day. Records the alert in
    /// <paramref name="state"/>, which the caller saves.
    /// </summary>
    public static List<DiskUsage> DrivesToAlert(IEnumerable<DiskUsage> drives, BackgroundState state, DateTime nowUtc)
    {
        var alert = new List<DiskUsage>();
        foreach (var disk in drives.Where(IsLow))
        {
            if (state.LowSpaceAlertUtc.TryGetValue(disk.Drive, out var last) && nowUtc - last < AlertInterval) continue;
            state.LowSpaceAlertUtc[disk.Drive] = nowUtc;
            alert.Add(disk);
        }
        return alert;
    }

    /// <summary>The Windows drive and the profile drive: where Tidy's cleaning makes a difference.</summary>
    public static List<DiskUsage> WatchedDrives(UserLocations locations)
    {
        var roots = new[] { Path.GetPathRoot(Environment.SystemDirectory), Path.GetPathRoot(locations.Home) }
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var drives = new List<DiskUsage>();
        foreach (var root in roots)
        {
            try
            {
                drives.Add(DiskUsage.For(root));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
        return drives;
    }
}
