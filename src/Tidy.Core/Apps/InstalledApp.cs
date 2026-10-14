using Tidy.Core.Models;

namespace Tidy.Core.Apps;

public enum AppKind
{
    /// <summary>A Microsoft Store (MSIX/AppX) package, removed through the package manager.</summary>
    Store,
    /// <summary>A desktop program with an entry in Settings → Apps, removed by its own uninstaller.</summary>
    Desktop,
}

public sealed record InstalledApp
{
    /// <summary>Store: the package full name. Desktop: the registry hive and key, like <c>HKLM64\{GUID}</c>.</summary>
    public required string Id { get; init; }
    public required AppKind Kind { get; init; }
    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime? InstalledUtc { get; init; }
    /// <summary>Last launch Windows recorded, or null when it has none.</summary>
    public DateTime? LastUsedUtc { get; init; }
    public bool IsRunning { get; init; }
    public string? InstallLocation { get; init; }
    /// <summary>A file Windows can draw the app's icon from: an .exe, .ico or a Store logo.</summary>
    public string? IconPath { get; init; }

    /// <summary>Set for apps that came with Windows or the PC, from apps.json.</summary>
    public PreinstalledEntry? Preinstalled { get; init; }

    /// <summary>Desktop: the command Settings → Apps would run.</summary>
    public string? UninstallCommand { get; init; }
    /// <summary>Store: the package family name, for matching launches and the data folder.</summary>
    public string? PackageFamilyName { get; init; }

    public bool IsPreinstalled => Preinstalled is not null;
    public SafetyLevel? Safety => Preinstalled?.Safety;

    /// <summary>
    /// Not opened from Start, the taskbar or File Explorer for <paramref name="days"/> days.
    /// An app with no recorded launch counts only once it has been installed that long.
    /// </summary>
    public bool IsUnusedFor(int days, DateTime nowUtc)
    {
        if (IsRunning) return false;
        var cutoff = nowUtc.AddDays(-days);
        if (LastUsedUtc is { } used) return used < cutoff;
        return InstalledUtc is { } installed && installed < cutoff;
    }
}
