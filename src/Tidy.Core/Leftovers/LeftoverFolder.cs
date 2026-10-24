namespace Tidy.Core.Leftovers;

/// <summary>Why Tidy thinks a folder belonged to an app that is gone. Strongest first.</summary>
public enum LeftoverEvidence
{
    /// <summary>A folder the app registered with Windows: its install, icon or uninstaller folder.</summary>
    RegisteredFolder,
    /// <summary>Named after the app inside a folder named after its publisher, like <c>%APPDATA%\Mozilla\Firefox</c>.</summary>
    PublisherAndName,
    /// <summary>Named like the app's install folder, like <c>%APPDATA%\discord</c> next to <c>%LOCALAPPDATA%\Discord</c>.</summary>
    InstallFolderName,
    /// <summary>Named after the whole app, not just a word of it.</summary>
    Name,
    /// <summary>A program folder with its uninstaller still in it, which Windows no longer lists.</summary>
    OrphanedInstall,
    /// <summary>A folder Tidy's list of common apps says belongs to that app.</summary>
    KnownAppFolder,
}

/// <summary>A folder an app that is no longer installed left behind. Always Check First.</summary>
public sealed record LeftoverFolder
{
    public required string Path { get; init; }
    /// <summary>The app it most likely came from.</summary>
    public required string AppName { get; init; }
    public required LeftoverEvidence Evidence { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime LastModifiedUtc { get; init; }
    /// <summary>
    /// In ProgramData or Program Files, which only an administrator can change. Shown so the
    /// user can remove it themselves; Tidy never does.
    /// </summary>
    public bool NeedsAdministrator { get; init; }
    /// <summary>When Tidy last saw the app installed, if it ever did.</summary>
    public DateTime? AppLastSeenUtc { get; init; }

    /// <summary>Whether it came from the app called <paramref name="appName"/>, version and architecture aside.</summary>
    public bool IsFrom(string appName) => NameKeys.Bare(AppName) == NameKeys.Bare(appName);
}
