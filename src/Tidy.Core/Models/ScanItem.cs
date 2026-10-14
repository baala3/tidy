namespace Tidy.Core.Models;

public enum ScanCategory
{
    AppCaches,
    DevTools,
}

/// <summary>
/// One row in App Caches or Dev Tools. A row may cover several folders (every
/// Chrome profile's cache, say), which are removed together.
/// </summary>
public sealed record ScanItem
{
    /// <summary>Stable across scans, so a selection survives a rescan.</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Explanation { get; init; }
    public required SafetyLevel Safety { get; init; }
    public required ScanCategory Category { get; init; }
    /// <summary>Picks the row's icon: browser, editor, packages, build, temp, ...</summary>
    public required string Kind { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public required long SizeBytes { get; init; }
    public DateTime? LastModifiedUtc { get; init; }

    /// <summary>For project artifacts: the project folder's name and path.</summary>
    public string? ProjectName { get; init; }
    public string? ProjectPath { get; init; }
    /// <summary>For project artifacts: how it comes back, as a command or plain steps.</summary>
    public string? RestoreHint { get; init; }

    /// <summary>
    /// Set when the row is shown but cannot be cleaned right now, for example a
    /// browser cache while the browser is open. Such rows are never selectable.
    /// </summary>
    public string? BlockedReason { get; init; }

    public bool IsProjectArtifact => ProjectPath is not null;
}
