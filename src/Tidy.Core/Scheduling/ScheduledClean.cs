using Tidy.Core.Deletion;
using Tidy.Core.Models;
using Tidy.Core.Platform;
using Tidy.Core.Scanning;
using Tidy.Core.Settings;

namespace Tidy.Core.Scheduling;

/// <summary>A scheduled clean that ran: what <see cref="FileDeleter"/> did, and the rows it emptied.</summary>
public sealed record ScheduledCleanResult(CleanResult Result, IReadOnlyList<string> Cleaned);

/// <summary>
/// The automatic clean. It takes only App Caches and Dev Tools rows marked Safe to Clean,
/// never project folders or device backups, and every path still goes through
/// <see cref="FileDeleter"/>: re-checked against the policy, then into the Recycle Bin.
/// </summary>
public sealed class ScheduledClean(CacheScanner scanner, FileDeleter deleter)
{
    /// <summary>How long a due clean waits for open apps to close before it cleans without their caches.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    /// <summary>When the next clean is due; <see cref="DateTime.MinValue"/> when it has never run, null when off.</summary>
    public static DateTime? DueUtc(CleanSchedule schedule, DateTime? lastUtc) => schedule switch
    {
        CleanSchedule.Off => null,
        CleanSchedule.Weekly => lastUtc?.AddDays(7) ?? DateTime.MinValue,
        CleanSchedule.Monthly => lastUtc?.AddMonths(1) ?? DateTime.MinValue,
        _ => throw new ArgumentOutOfRangeException(nameof(schedule)),
    };

    public static bool IsEligible(ScanItem item) =>
        item.Safety == SafetyLevel.Safe && !item.IsProjectArtifact && !item.IsDeviceBackup;

    /// <summary>
    /// Cleans if a clean is due. Returns null when it isn't, or when it is waiting for an open
    /// app; the caller then leaves the last clean time alone so the next hourly run tries again.
    /// </summary>
    public async Task<ScheduledCleanResult?> RunAsync(CleanSchedule schedule, DateTime? lastUtc, DateTime nowUtc, CancellationToken cancellation = default)
    {
        if (DueUtc(schedule, lastUtc) is not { } due || nowUtc < due) return null;

        var items = scanner.Scan(cancellation).Where(IsEligible).ToList();
        var ready = items.Where(i => i.BlockedReason is null).ToList();
        // A browser that's open blocks its cache. Give it a day to close, then clean the rest without it.
        if (ready.Count < items.Count && nowUtc - due < Grace) return null;

        var result = await deleter.MoveToRecycleBinAsync(ready.SelectMany(i => i.Paths).ToList(), DeletionKind.Cache, IntPtr.Zero);
        var removed = result.Removed.ToHashSet(PathUtil.Comparer);
        var cleaned = ready.Where(i => i.Paths.Any(removed.Contains)).Select(i => i.Title).ToList();
        return new ScheduledCleanResult(result, cleaned);
    }
}
