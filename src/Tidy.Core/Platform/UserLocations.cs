using System.Runtime.InteropServices;

namespace Tidy.Core.Platform;

/// <summary>
/// Every user folder Tidy reasons about, resolved once. Tests build one rooted in
/// a throwaway folder so nothing ever touches the real profile.
/// </summary>
public sealed record UserLocations
{
    public required string Home { get; init; }
    public required string LocalAppData { get; init; }
    public required string RoamingAppData { get; init; }
    public required string LocalLow { get; init; }
    public required string Temp { get; init; }
    public required string Documents { get; init; }
    public required string Desktop { get; init; }
    public required string Downloads { get; init; }
    public required string Pictures { get; init; }
    public required string Music { get; init; }
    public required string Videos { get; init; }
    /// <summary>OneDrive roots (personal and work). The folders themselves are never removable.</summary>
    public IReadOnlyList<string> OneDriveRoots { get; init; } = [];
    /// <summary>
    /// Top-level developer folders outside the profile, like <c>C:\dev</c> or <c>D:\src</c>.
    /// Windows developers keep projects there to stay clear of path-length limits.
    /// </summary>
    public IReadOnlyList<string> DeveloperRoots { get; init; } = [];
    /// <summary>Where Tidy keeps its own settings.</summary>
    public required string TidyData { get; init; }

    private static readonly string[] OneDriveVariables = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    public static readonly string[] DeveloperFolderNames =
        ["dev", "src", "source", "repos", "projects", "code", "git", "work", "workspace", "github"];

    public IEnumerable<string> PersonalFolders =>
        [Documents, Desktop, Downloads, Pictures, Music, Videos];

    public static UserLocations Current()
    {
        var home = Norm(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var local = Norm(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var oneDrive = OneDriveVariables
            .Select(Environment.GetEnvironmentVariable)
            .Select(PathUtil.Normalize)
            .OfType<string>()
            .Distinct(PathUtil.Comparer)
            .ToList();

        return new UserLocations
        {
            Home = home,
            LocalAppData = local,
            RoamingAppData = Norm(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            LocalLow = KnownFolder(KnownFolderIds.LocalAppDataLow) ?? Path.Combine(Path.GetDirectoryName(local)!, "LocalLow"),
            Temp = Norm(PathUtil.LongPath(Path.GetTempPath())),
            Documents = KnownFolder(KnownFolderIds.Documents) ?? Path.Combine(home, "Documents"),
            Desktop = KnownFolder(KnownFolderIds.Desktop) ?? Path.Combine(home, "Desktop"),
            Downloads = KnownFolder(KnownFolderIds.Downloads) ?? Path.Combine(home, "Downloads"),
            Pictures = KnownFolder(KnownFolderIds.Pictures) ?? Path.Combine(home, "Pictures"),
            Music = KnownFolder(KnownFolderIds.Music) ?? Path.Combine(home, "Music"),
            Videos = KnownFolder(KnownFolderIds.Videos) ?? Path.Combine(home, "Videos"),
            OneDriveRoots = oneDrive,
            DeveloperRoots = FindDeveloperRoots(),
            TidyData = Path.Combine(local, "Tidy"),
        };
    }

    private static List<string> FindDeveloperRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            foreach (var name in DeveloperFolderNames)
            {
                var candidate = Path.Combine(drive.RootDirectory.FullName, name);
                if (Directory.Exists(candidate) && !PathUtil.IsReparsePoint(candidate)) roots.Add(candidate);
            }
        }
        return roots;
    }

    private static string Norm(string path) =>
        PathUtil.Normalize(path) ?? throw new InvalidOperationException($"Unusable profile path: {path}");

    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var raw) != 0) return null;
        try
        {
            return PathUtil.Normalize(Marshal.PtrToStringUni(raw));
        }
        finally
        {
            Marshal.FreeCoTaskMem(raw);
        }
    }

    private static class KnownFolderIds
    {
        public static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
        public static readonly Guid Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
        public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");
        public static readonly Guid Pictures = new("33E28130-4E1E-4676-835A-98395C3BC3BB");
        public static readonly Guid Music = new("4BD8D571-6D19-48D3-BE97-422220080E43");
        public static readonly Guid Videos = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");
        public static readonly Guid LocalAppDataLow = new("A520A1A4-1780-4FF6-BD18-167343C5AF16");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
