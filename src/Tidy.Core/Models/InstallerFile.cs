using Tidy.Core.Apps;

namespace Tidy.Core.Models;

public enum InstallerAge
{
    /// <summary>The installer is an older version than what is installed.</summary>
    Older,
    Same,
    /// <summary>The installer is newer: it may be an update the user still means to run.</summary>
    Newer,
    /// <summary>One of the two has no version Tidy can read.</summary>
    Unknown,
}

/// <summary>An installer in the personal folders for an app that is already installed.</summary>
public sealed record InstallerFile(LargeFile File, InstalledApp App, string? Version)
{
    public InstallerAge Age => Version is null || App.Version.Length == 0
        ? InstallerAge.Unknown
        : InstallerMatcher.CompareVersions(Version, App.Version) switch
        {
            < 0 => InstallerAge.Older,
            0 => InstallerAge.Same,
            > 0 => InstallerAge.Newer,
        };
}
