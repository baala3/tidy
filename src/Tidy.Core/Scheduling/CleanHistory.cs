using System.Text.Json;

namespace Tidy.Core.Scheduling;

public enum CleanSource
{
    /// <summary>The user chose what to clean and pressed the button.</summary>
    Manual,
    /// <summary>The scheduled task cleaned by itself.</summary>
    Scheduled,
}

/// <summary>One clean, as the History page shows it.</summary>
public sealed class HistoryEntry
{
    public required DateTime WhenUtc { get; init; }
    public required CleanSource Source { get; init; }
    /// <summary>The page it happened on: "App Caches", "Large Files", "Windows"...</summary>
    public required string What { get; init; }
    public required long FreedBytes { get; init; }
    /// <summary>What was removed, by name. Long lists keep the first <see cref="CleanHistory.MaxItems"/>.</summary>
    public required IReadOnlyList<string> Items { get; init; }
    /// <summary>How many items there were in all, which can be more than <see cref="Items"/> holds.</summary>
    public required int ItemCount { get; init; }
    /// <summary>How many items Tidy left alone.</summary>
    public int Failed { get; init; }
}

/// <summary>
/// Every clean, manual or scheduled, newest first, in history.json beside the settings.
/// The app and the background run both add to it, so each change holds a lock across the read and the write.
/// </summary>
public static class CleanHistory
{
    public const int MaxEntries = 500;
    public const int MaxItems = 100;

    public static List<HistoryEntry> Load(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "history.json");
            if (!File.Exists(file)) return [];
            return JsonSerializer.Deserialize(File.ReadAllText(file), CoreJsonContext.Default.ListHistoryEntry) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Records a clean that removed something. One that removed nothing is not history.</summary>
    public static void Add(string folder, DateTime whenUtc, CleanSource source, string what, long freedBytes, IReadOnlyList<string> items, int failed)
    {
        if (items.Count == 0) return;
        var entry = new HistoryEntry
        {
            WhenUtc = whenUtc,
            Source = source,
            What = what,
            FreedBytes = freedBytes,
            Items = items.Take(MaxItems).ToList(),
            ItemCount = items.Count,
            Failed = failed,
        };
        using var mutex = new Mutex(false, @"Local\Tidy.History");
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The other process died holding it; the file is still whole, since writes are atomic.
        }
        try
        {
            var entries = Load(folder);
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "history.json");
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, CoreJsonContext.Default.ListHistoryEntry));
            File.Move(temp, file, overwrite: true);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
