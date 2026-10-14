using System.Text.Json;
using System.Text.Json.Serialization;
using Tidy.Core.Models;

namespace Tidy.Core.Apps;

public sealed record PreinstalledEntry
{
    /// <summary>Package name prefix, matched case-insensitively.</summary>
    [JsonPropertyName("match")] public required string Match { get; init; }
    /// <summary>Friendly name, or null to keep the package's own display name.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("tier")] public required string Tier { get; init; }
    [JsonPropertyName("explanation")] public required string Explanation { get; init; }

    public SafetyLevel Safety => Tier switch
    {
        "safe" => SafetyLevel.Safe,
        "medium" => SafetyLevel.CheckFirst,
        _ => throw new InvalidDataException($"Unknown tier '{Tier}' on {Match}"),
    };
}

/// <summary>
/// What Tidy knows about apps: Store apps that come with Windows or the PC and are safe
/// or worth checking before removing, and apps that must never be offered because other
/// apps or Windows itself depend on them.
/// </summary>
public sealed class AppCatalog
{
    [JsonPropertyName("preinstalled")] public required List<PreinstalledEntry> Preinstalled { get; init; }
    [JsonPropertyName("neverStore")] public required List<string> NeverStore { get; init; }
    [JsonPropertyName("neverStoreSuffixes")] public required List<string> NeverStoreSuffixes { get; init; }
    [JsonPropertyName("neverDesktop")] public required List<string> NeverDesktop { get; init; }

    private static readonly Lazy<AppCatalog> Embedded = new(() =>
    {
        using var stream = typeof(AppCatalog).Assembly.GetManifestResourceStream("Tidy.Core.apps.json")
            ?? throw new InvalidOperationException("apps.json is not embedded");
        var catalog = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.AppCatalog)
            ?? throw new InvalidDataException("apps.json is empty");
        foreach (var entry in catalog.Preinstalled) _ = entry.Safety;
        return catalog;
    });

    public static AppCatalog Default => Embedded.Value;

    public PreinstalledEntry? PreinstalledFor(string packageName) =>
        Preinstalled.FirstOrDefault(e => packageName.StartsWith(e.Match, StringComparison.OrdinalIgnoreCase));

    /// <summary>Store packages Windows or other apps depend on: the Store, codecs, Xbox sign-in, drivers' companions.</summary>
    public bool IsProtectedPackage(string packageName) =>
        NeverStore.Any(p => packageName.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
        NeverStoreSuffixes.Any(s => packageName.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>Desktop entries other apps depend on: runtimes, redistributables, drivers.</summary>
    public bool IsProtectedDesktopApp(string displayName) =>
        NeverDesktop.Any(n => displayName.Contains(n, StringComparison.OrdinalIgnoreCase));
}
