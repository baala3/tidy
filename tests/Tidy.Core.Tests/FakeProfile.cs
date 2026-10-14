using System.Diagnostics;
using Tidy.Core.Platform;

namespace Tidy.Core.Tests;

/// <summary>
/// A throwaway user profile under the temp folder. Tests never point Tidy at the real
/// profile: the deleter and scanners only ever see these folders.
/// </summary>
public sealed class FakeProfile : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "tidy-tests", Guid.NewGuid().ToString("N"));
    public UserLocations Locations { get; }

    public FakeProfile()
    {
        var root = PathUtil.Normalize(PathUtil.LongPath(Directory.CreateDirectory(Root).FullName))!;
        var home = Path.Combine(root, "home");
        var local = Path.Combine(home, "AppData", "Local");
        Locations = new UserLocations
        {
            Home = home,
            LocalAppData = local,
            RoamingAppData = Path.Combine(home, "AppData", "Roaming"),
            LocalLow = Path.Combine(home, "AppData", "LocalLow"),
            Temp = Path.Combine(local, "Temp"),
            Documents = Path.Combine(home, "Documents"),
            Desktop = Path.Combine(home, "Desktop"),
            Downloads = Path.Combine(home, "Downloads"),
            Pictures = Path.Combine(home, "Pictures"),
            Music = Path.Combine(home, "Music"),
            Videos = Path.Combine(home, "Videos"),
            DeveloperRoots = [Path.Combine(root, "dev")],
            TidyData = Path.Combine(local, "Tidy"),
        };
        foreach (var dir in new[] { Locations.Temp, Locations.RoamingAppData, Locations.LocalLow, Locations.Documents, Locations.Downloads, Path.Combine(root, "dev") })
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Creates a file (and its folders) with <paramref name="bytes"/> bytes, optionally backdated.</summary>
    public string File(string relativeToRoot, int bytes = 1024, DateTime? writtenUtc = null)
    {
        var path = Path.Combine(Root, relativeToRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        if (writtenUtc is { } t) System.IO.File.SetLastWriteTimeUtc(path, t);
        return path;
    }

    public string Dir(string relativeToRoot) => Directory.CreateDirectory(Path.Combine(Root, relativeToRoot)).FullName;

    public string P(string relativeToRoot) => Path.Combine(Root, relativeToRoot);

    /// <summary>Backdates every file and folder under <paramref name="path"/>.</summary>
    public static void Age(string path, DateTime utc)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            if (Directory.Exists(entry)) Directory.SetLastWriteTimeUtc(entry, utc);
            else System.IO.File.SetLastWriteTimeUtc(entry, utc);
        }
        Directory.SetLastWriteTimeUtc(path, utc);
    }

    public static void Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("mklink failed");
    }

    public void Dispose()
    {
        // Junctions first, so deleting the tree never follows one.
        foreach (var dir in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories).ToList())
        {
            if (Directory.Exists(dir) && PathUtil.IsReparsePoint(dir)) Directory.Delete(dir);
        }
        Directory.Delete(Root, recursive: true);
    }
}
