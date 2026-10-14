namespace Tidy.Core.Platform;

public readonly record struct DiskUsage(string Drive, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    /// <summary>The drive holding the user's profile, which is where everything Tidy scans lives.</summary>
    public static DiskUsage ForProfileDrive(UserLocations locations)
    {
        var drive = new DriveInfo(Path.GetPathRoot(locations.Home)!);
        return new DiskUsage(drive.Name.TrimEnd('\\'), drive.TotalSize, drive.TotalFreeSpace);
    }
}
