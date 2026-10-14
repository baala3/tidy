namespace Tidy.Core.Models;

/// <summary>
/// The two tiers a scan result can carry. There is deliberately no "Do Not Delete"
/// tier: dangerous locations are never on the allowlist, so they are never scanned,
/// sized or shown.
/// </summary>
public enum SafetyLevel
{
    /// <summary>"Safe to Clean": holds nothing but data its owner rebuilds by itself.</summary>
    Safe,
    /// <summary>"Check First": rebuildable, but losing it costs time, a download, or a sign-in.</summary>
    CheckFirst,
}

public static class SafetyLevelText
{
    public static string DisplayName(this SafetyLevel level) => level switch
    {
        SafetyLevel.Safe => "Safe to Clean",
        SafetyLevel.CheckFirst => "Check First",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}
