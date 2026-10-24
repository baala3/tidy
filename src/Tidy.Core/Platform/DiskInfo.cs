namespace Tidy.Core.Platform;

public readonly record struct DiskUsage(string Drive, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    /// <summary>The drive holding the user's profile, which is where everything Tidy scans lives.</summary>
    public static DiskUsage ForProfileDrive(UserLocations locations) => For(Path.GetPathRoot(locations.Home)!);

    /// <summary>The drive at <paramref name="root"/>, such as <c>C:\</c>.</summary>
    public static DiskUsage For(string root)
    {
        var drive = new DriveInfo(root);
        return new DiskUsage(drive.Name.TrimEnd('\\'), drive.TotalSize, drive.TotalFreeSpace);
    }
}
