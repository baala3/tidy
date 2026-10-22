using Tidy.Core.Cloud;
using Tidy.Core.Platform;

namespace Tidy.Core.Safety;

/// <summary>
/// Which OneDrive files may be made online-only. Nothing is deleted: the file stays in
/// OneDrive and in File Explorer, and downloads again when opened. So the one thing that
/// must hold is that OneDrive really has it: the file is a synced placeholder, fully up to
/// date in the cloud, and the user hasn't asked to always keep it on this device.
/// </summary>
public sealed class OneDrivePolicy(UserLocations locations)
{
    /// <summary>
    /// OneDrive folders a signed-in OneDrive manages. Leftover folders are left out; they stay
    /// on the never-delete list through <see cref="UserLocations.OneDriveRoots"/> regardless.
    /// </summary>
    public IReadOnlyList<string> Roots => _roots ??= locations.OneDriveRoots.Where(CloudFiles.IsSyncRoot).ToList();

    private IReadOnlyList<string>? _roots;

    /// <summary>The OneDrive folder holding <paramref name="path"/>, or null.</summary>
    public string? RootOf(string path) => Roots.FirstOrDefault(r => PathUtil.IsStrictlyUnder(path, r));

    /// <summary>Null when the file may be made online-only now, or the reason it may not.</summary>
    public string? Refusal(string rawPath)
    {
        using var view = CloudFiles.ExposePlaceholders();
        return Check(rawPath);
    }

    private string? Check(string rawPath)
    {
        var path = PathUtil.Normalize(rawPath);
        if (path is null || RootOf(path) is not { } root) return "It isn't in a OneDrive folder.";
        if (FindFiles.Of(path) is not { IsDirectory: false } entry) return "It no longer exists.";
        if (!entry.IsReparsePoint || !CloudFiles.IsCloudTag(entry.ReparseTag)) return "OneDrive doesn't manage it, so its only copy is this one.";
        if ((entry.Attributes & CloudFiles.NotFullyLocal) != 0) return "It is already online-only.";
        if ((entry.Attributes & CloudFiles.Pinned) != 0) return "It is set to always stay on this device.";
        if ((entry.Attributes & CloudFiles.Unpinned) != 0) return "OneDrive is already freeing it up.";
        if (HasLinkBetween(path, root)) return "A link or junction is on its path, so Tidy left it alone.";
        if (DeletionSafetyPolicy.ProtectedFragments.Any(path[root.Length..].ToLowerInvariant().Contains)) return "Protected location. Not eligible.";
        return CloudFiles.Info(path) switch
        {
            null => "It is in use. Close the app using it and try again.",
            { PinState: CloudPinState.Pinned } => "It is set to always stay on this device.",
            { PinState: CloudPinState.Unpinned } => "OneDrive is already freeing it up.",
            { InSync: false } => "It hasn't finished syncing, so the only up-to-date copy is this one.",
            _ => null,
        };
    }

    /// <summary>Folders in OneDrive are placeholders too; any other kind of reparse point is a link.</summary>
    private static bool HasLinkBetween(string path, string root)
    {
        for (var folder = Path.GetDirectoryName(path); folder is not null && PathUtil.IsStrictlyUnder(folder, root); folder = Path.GetDirectoryName(folder))
        {
            if (FindFiles.Of(folder) is not { } entry) return true;
            if (entry.IsReparsePoint && !CloudFiles.IsCloudTag(entry.ReparseTag)) return true;
        }
        return false;
    }
}
