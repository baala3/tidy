using System.Text.Json;
using System.Text.Json.Serialization;
using Tidy.Core.Models;
using Tidy.Core.Platform;

namespace Tidy.Core.Catalog;

public sealed record CloseFirst(
    [property: JsonPropertyName("app")] string App,
    [property: JsonPropertyName("processes")] string[] Processes);

public sealed record CatalogEntry
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("tier")] public required string Tier { get; init; }
    [JsonPropertyName("scope")] public required string Scope { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("explanation")] public required string Explanation { get; init; }
    [JsonPropertyName("paths")] public required string[] Paths { get; init; }
    /// <summary>Offer the folder's children, never the folder itself (Temp, CrashDumps).</summary>
    [JsonPropertyName("contentsOnly")] public bool ContentsOnly { get; init; }
    /// <summary>Skip children touched more recently than this, so files an app is still using stay put.</summary>
    [JsonPropertyName("minAgeHours")] public int? MinAgeHours { get; init; }
    [JsonPropertyName("closeFirst")] public CloseFirst? CloseFirst { get; init; }

    public SafetyLevel Safety => Tier switch
    {
        "safe" => SafetyLevel.Safe,
        "medium" => SafetyLevel.CheckFirst,
        _ => throw new InvalidDataException($"Unknown tier '{Tier}' on {Key}"),
    };

    public ScanCategory Category => Scope switch
    {
        "app_caches" => ScanCategory.AppCaches,
        "dev_tools" => ScanCategory.DevTools,
        _ => throw new InvalidDataException($"Unknown scope '{Scope}' on {Key}"),
    };
}

public sealed record ChromiumAppsSection
{
    [JsonPropertyName("explanation")] public required string Explanation { get; init; }
    [JsonPropertyName("folders")] public required string[] Folders { get; init; }
    [JsonPropertyName("names")] public required Dictionary<string, string> Names { get; init; }
    [JsonPropertyName("devTools")] public required string[] DevTools { get; init; }
}

public sealed record ArtifactText(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("explanation")] string Explanation);

/// <summary>
/// What Tidy knows about: every cache location it may offer, with a name, a tier
/// and a plain-language explanation. A location missing from here is never shown.
/// Entries only propose paths; <see cref="Safety.DeletionSafetyPolicy"/> decides.
/// </summary>
public sealed class CleanupCatalog
{
    [JsonPropertyName("entries")] public required List<CatalogEntry> Entries { get; init; }
    [JsonPropertyName("chromiumApps")] public required ChromiumAppsSection ChromiumApps { get; init; }
    [JsonPropertyName("artifacts")] public required Dictionary<string, ArtifactText> Artifacts { get; init; }

    private static readonly Lazy<CleanupCatalog> Embedded = new(LoadEmbedded);

    public static CleanupCatalog Default => Embedded.Value;

    private static CleanupCatalog LoadEmbedded()
    {
        using var stream = typeof(CleanupCatalog).Assembly.GetManifestResourceStream("Tidy.Core.catalog.json")
            ?? throw new InvalidOperationException("catalog.json is not embedded");
        var catalog = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.CleanupCatalog)
            ?? throw new InvalidDataException("catalog.json is empty");
        foreach (var entry in catalog.Entries)
        {
            _ = entry.Safety;
            _ = entry.Category;
        }
        return catalog;
    }

    public ArtifactText ArtifactTextFor(string kind) =>
        Artifacts.TryGetValue(kind, out var text) ? text : throw new KeyNotFoundException($"No catalog text for artifact '{kind}'");

    /// <summary>Replaces the <c>{Home}</c>-style tokens in a catalog pattern.</summary>
    public static string ResolveTokens(string pattern, UserLocations locations) => pattern
        .Replace("{Home}", locations.Home, StringComparison.Ordinal)
        .Replace("{Local}", locations.LocalAppData, StringComparison.Ordinal)
        .Replace("{Roaming}", locations.RoamingAppData, StringComparison.Ordinal)
        .Replace("{LocalLow}", locations.LocalLow, StringComparison.Ordinal)
        .Replace("{Temp}", locations.Temp, StringComparison.Ordinal);

    /// <summary>
    /// The existing folders a pattern names. A <c>*</c> segment matches any one
    /// subfolder. Links are never followed, so a junction cannot pull a folder
    /// from somewhere else into a row.
    /// </summary>
    public static IEnumerable<string> Expand(string pattern, UserLocations locations)
    {
        var resolved = ResolveTokens(pattern, locations);
        var segments = resolved.Split('\\');
        IEnumerable<string> current = [segments[0] + "\\"];
        foreach (var segment in segments.Skip(1))
        {
            current = segment == "*"
                ? current.SelectMany(SubdirectoriesNoLinks)
                : current.Select(dir => Path.Combine(dir, segment)).Where(p => Directory.Exists(p) && !PathUtil.IsReparsePoint(p));
        }
        return current.Select(p => PathUtil.Normalize(p)!).Where(p => p is not null);
    }

    private static IEnumerable<string> SubdirectoriesNoLinks(string dir)
    {
        try
        {
            return new DirectoryInfo(dir)
                .EnumerateDirectories()
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .Select(d => d.FullName)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
