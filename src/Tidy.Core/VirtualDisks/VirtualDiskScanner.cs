using Tidy.Core.Platform;

namespace Tidy.Core.VirtualDisks;

/// <summary>
/// The disk images of WSL distros and Docker Desktop. Only disks WSL lists for this user, and
/// Docker's own disks in its own folder, are found; no other virtual disk is ever offered.
/// </summary>
public sealed class VirtualDiskScanner(UserLocations locations)
{
    public Func<IReadOnlyList<WslDistro>> Distros { get; init; } = Wsl.Distros;

    /// <summary>Where Docker Desktop keeps its data disk, newest layout first.</summary>
    private string[] DockerDisks =>
    [
        Path.Combine(locations.LocalAppData, "Docker", "wsl", "disk", "docker_data.vhdx"),
        Path.Combine(locations.LocalAppData, "Docker", "wsl", "data", "ext4.vhdx"),
    ];

    private string DockerFolder => Path.Combine(locations.LocalAppData, "Docker");

    public IReadOnlyList<VirtualDisk> Scan()
    {
        var disks = new List<VirtualDisk>();
        var seen = new HashSet<string>(PathUtil.Comparer);
        foreach (var distro in Distros())
        {
            var path = Path.Combine(distro.BasePath, distro.VhdFileName);
            var docker = distro.Name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase) || PathUtil.IsStrictlyUnder(path, DockerFolder);
            Add(disks, seen, path, FriendlyName(distro.Name), docker ? VirtualDiskOwner.Docker : VirtualDiskOwner.Wsl, distro.Name == "docker-desktop-data");
        }
        foreach (var path in DockerDisks) Add(disks, seen, path, "Docker Desktop data", VirtualDiskOwner.Docker, trimFirst: true);
        return disks;
    }

    private static void Add(List<VirtualDisk> disks, HashSet<string> seen, string rawPath, string name, VirtualDiskOwner owner, bool trimFirst = false)
    {
        if (PathUtil.Normalize(rawPath) is not { } path || !seen.Add(path)) return;
        if (!File.Exists(path) || !path.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase)) return;
        // A link could point the compaction at some other disk.
        if (PathUtil.HasReparsePointBetween(path, Path.GetPathRoot(path)!)) return;
        if (Vhdx.Read(path) is not { } usage) return;
        disks.Add(new VirtualDisk(name, path, owner, usage) { FreesSpaceOnlyAfterTrim = trimFirst });
    }

    private static string FriendlyName(string distro) => distro switch
    {
        "docker-desktop" => "Docker Desktop",
        "docker-desktop-data" => "Docker Desktop data",
        _ => distro,
    };
}
