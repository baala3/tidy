using System.Text.Json;

namespace Tidy.Core.Settings;

public enum AppearanceMode
{
    System,
    Light,
    Dark,
}

/// <summary>User preferences, stored as JSON in Tidy's own AppData folder.</summary>
public sealed class AppSettings
{
    public static readonly int[] StaleOptionsDays = [30, 90, 180, 365, 730, 0];
    public const int DefaultStaleDays = 180;

    public AppearanceMode Appearance { get; set; } = AppearanceMode.System;

    /// <summary>"Consider stale after", in days. Zero shows every project.</summary>
    public int StaleDays { get; set; } = DefaultStaleDays;

    public static AppSettings Load(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "settings.json");
            if (!File.Exists(file)) return new AppSettings();
            var settings = JsonSerializer.Deserialize(File.ReadAllText(file), CoreJsonContext.Default.AppSettings) ?? new AppSettings();
            if (!StaleOptionsDays.Contains(settings.StaleDays)) settings.StaleDays = DefaultStaleDays;
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "settings.json");
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, CoreJsonContext.Default.AppSettings));
        File.Move(temp, file, overwrite: true);
    }
}
