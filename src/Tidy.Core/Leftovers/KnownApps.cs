using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tidy.Core.Leftovers;

public sealed record RememberedApp
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("publisher")] public string Publisher { get; init; } = "";
    [JsonPropertyName("folders")] public List<string> Folders { get; init; } = [];
    [JsonPropertyName("lastSeenUtc")] public required DateTime LastSeenUtc { get; init; }

    public AppFootprint Footprint => new() { Name = Name, Publisher = Publisher, Folders = Folders, IsListed = true };
}

/// <summary>
/// The apps Tidy has seen installed, kept in its own folder. Windows forgets an app the
/// moment its uninstaller runs, and with it the folders it registered; this is how Tidy
/// still knows which folders were the app's once it is gone.
/// </summary>
public sealed class KnownApps
{
    /// <summary>An app gone this long has long since been cleaned up after, or was never coming back.</summary>
    public const int KeepDays = 730;

    private const string FileName = "known-apps.json";

    [JsonPropertyName("apps")] public List<RememberedApp> Apps { get; init; } = [];

    public static KnownApps Load(string folder)
    {
        try
        {
            var file = Path.Combine(folder, FileName);
            if (!File.Exists(file)) return new KnownApps();
            return JsonSerializer.Deserialize(File.ReadAllText(file), CoreJsonContext.Default.KnownApps) ?? new KnownApps();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new KnownApps();
        }
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, FileName);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, CoreJsonContext.Default.KnownApps));
        File.Move(temp, file, overwrite: true);
    }

    /// <summary>Remembers the listed apps installed now, and forgets apps gone longer than <see cref="KeepDays"/>.</summary>
    public void Record(IEnumerable<AppFootprint> installed, DateTime nowUtc)
    {
        var byIdentity = Apps.ToDictionary(a => Identity(a.Name, a.Publisher), StringComparer.Ordinal);
        foreach (var app in installed.Where(a => a.IsListed && a.Name.Length > 0))
        {
            byIdentity[Identity(app.Name, app.Publisher)] = new RememberedApp
            {
                Name = app.Name,
                Publisher = app.Publisher,
                Folders = [.. app.Folders],
                LastSeenUtc = nowUtc,
            };
        }
        var cutoff = nowUtc.AddDays(-KeepDays);
        Apps.Clear();
        Apps.AddRange(byIdentity.Values.Where(a => a.LastSeenUtc >= cutoff).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Apps seen before that nothing installed now goes by.</summary>
    public List<RememberedApp> Gone(IEnumerable<AppFootprint> installed)
    {
        var present = installed.Select(a => Identity(a.Name, a.Publisher)).ToHashSet(StringComparer.Ordinal);
        return Apps.Where(a => !present.Contains(Identity(a.Name, a.Publisher))).ToList();
    }

    /// <summary>An update keeps the app: "Python 3.12.1" and "Python 3.12.2" by one publisher are one app.</summary>
    private static string Identity(string name, string publisher) => NameKeys.Bare(name) + "|" + NameKeys.Full(publisher);
}
