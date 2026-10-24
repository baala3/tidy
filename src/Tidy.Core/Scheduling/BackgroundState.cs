using System.Text.Json;

namespace Tidy.Core.Scheduling;

/// <summary>
/// What the hourly background run remembers between runs, kept apart from settings.json
/// so the app and the background run never write the same file.
/// </summary>
public sealed class BackgroundState
{
    /// <summary>When the schedule last cleaned. Null until the first scheduled clean.</summary>
    public DateTime? LastCleanUtc { get; set; }

    /// <summary>When each drive ("C:") last got a low space alert, so it gets at most one a day.</summary>
    public Dictionary<string, DateTime> LowSpaceAlertUtc { get; set; } = [];

    public static BackgroundState Load(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "background.json");
            if (!File.Exists(file)) return new BackgroundState();
            return JsonSerializer.Deserialize(File.ReadAllText(file), CoreJsonContext.Default.BackgroundState) ?? new BackgroundState();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new BackgroundState();
        }
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "background.json");
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, CoreJsonContext.Default.BackgroundState));
        File.Move(temp, file, overwrite: true);
    }
}
