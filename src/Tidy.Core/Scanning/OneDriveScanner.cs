using Tidy.Core.Cloud;
using Tidy.Core.Models;
using Tidy.Core.Platform;
using Tidy.Core.Safety;

namespace Tidy.Core.Scanning;

/// <summary>
/// Files in OneDrive that also take space on this PC and could be online-only instead:
/// synced, up to date in the cloud, and not set to always stay on this device.
/// </summary>
public sealed class OneDriveScanner(OneDrivePolicy policy)
{
    public const long DefaultMinimumBytes = 1024 * 1024;

    public long MinimumBytes { get; init; } = DefaultMinimumBytes;

    public IEnumerable<LargeFile> Scan(CancellationToken cancellation = default)
    {
        foreach (var root in policy.Roots)
        {
            if (!Directory.Exists(root)) continue;
            var label = Path.GetFileName(root);
            foreach (var file in Walk(root, root, label, cancellation)) yield return file;
        }
    }

    private IEnumerable<LargeFile> Walk(string folder, string root, string label, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        List<FoundEntry> entries;
        try
        {
            using var view = CloudFiles.ExposePlaceholders();
            entries = FindFiles.In(folder).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var entry in entries)
        {
            var cloud = entry.IsReparsePoint && CloudFiles.IsCloudTag(entry.ReparseTag);
            if (entry.IsDirectory)
            {
                // Links lead somewhere else; Personal Vault and project build folders aren't for this.
                // Camera Roll stays in: phone photos are what fills OneDrive on a PC.
                if (entry.IsReparsePoint && !cloud) continue;
                if (entry.Name.StartsWith('.') || entry.Name.StartsWith('$') || ProjectArtifactCatalog.ArtifactFolderNames.Contains(entry.Name)) continue;
                if (DeletionSafetyPolicy.ProtectedFragments.Any(entry.Name.ToLowerInvariant().Contains)) continue;
                foreach (var file in Walk(entry.Path, root, label, cancellation)) yield return file;
                continue;
            }
            if (!cloud || entry.Length < MinimumBytes) continue;
            // Unpinned files are already on their way to online-only.
            if ((entry.Attributes & (CloudFiles.NotFullyLocal | CloudFiles.Pinned | CloudFiles.Unpinned | FileAttributes.System)) != 0) continue;
            if (CloudFiles.Info(entry.Path) is not { InSync: true } info || info.PinState is CloudPinState.Pinned or CloudPinState.Unpinned) continue;
            yield return new LargeFile(entry.Path, entry.Length, entry.LastWriteUtc, LargeFile.KindFor(entry.Path), label);
        }
    }
}
