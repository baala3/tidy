using System.Runtime.InteropServices;
using Tidy.Core.Platform;
using Tidy.Core.VirtualDisks;

namespace Tidy.Core.Tests;

public sealed class VirtualDiskTests : IDisposable
{
    private readonly FakeProfile _profile = new();
    private readonly List<string> _commands = [];
    private readonly List<string> _compacted = [];

    public void Dispose() => _profile.Dispose();

    /// <summary>A real, empty dynamic VHDX, made the way WSL makes its disks.</summary>
    private string Disk(string relative)
    {
        var path = _profile.P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var type = new VIRTUAL_STORAGE_TYPE { DeviceId = 3, VendorId = new Guid("EC984AEC-A0F9-47e9-901F-71415A66345B") };
        var parameters = new CREATE_VIRTUAL_DISK_PARAMETERS { Version = 1, MaximumSize = 1L << 30, SectorSizeInBytes = 512 };
        Assert.Equal(0, CreateVirtualDisk(ref type, path, 0x003f0000, IntPtr.Zero, 0, 0, ref parameters, IntPtr.Zero, out var handle));
        CloseHandle(handle);
        return path;
    }

    private VirtualDiskScanner Scanner(params WslDistro[] distros) => new(_profile.Locations) { Distros = () => distros };

    private VirtualDiskCompactor Compactor(VirtualDiskScanner scanner, bool dockerRunning, int dockerStopExit = 0) => new(scanner)
    {
        IsDockerRunning = () => dockerRunning,
        RunCommand = (file, args, _) =>
        {
            _commands.Add($"{file} {args}");
            return new CommandResult(file == "docker" ? dockerStopExit : 0, "");
        },
        Compact = path =>
        {
            _commands.Add("compact " + Path.GetFileName(path));
            _compacted.Add(path);
            return null;
        },
        IsFree = _ => true,
    };

    [Fact]
    public void ReadsHowMuchOfANewDiskIsInUse()
    {
        var path = Disk(@"wsl\Ubuntu\ext4.vhdx");

        var usage = Vhdx.Read(path);

        Assert.NotNull(usage);
        Assert.Equal(new FileInfo(path).Length, usage.Value.FileBytes);
        Assert.InRange(usage.Value.InUseBytes, 1, usage.Value.FileBytes);
    }

    [Fact]
    public void AnythingElseIsNotADisk()
    {
        var fake = _profile.File(@"wsl\fake.vhdx", 300_000);
        Assert.Null(Vhdx.Read(fake));
        Assert.False(Vhdx.HasSignature(fake));
    }

    [Fact]
    public void CompactsADiskNothingIsUsing() => Assert.Null(VirtualDiskApi.Compact(Disk(@"wsl\Ubuntu\ext4.vhdx")));

    [Fact]
    public void FindsWslDistrosAndDockersDisk()
    {
        var ubuntu = Disk(@"home\AppData\Local\wsl\{1234}\ext4.vhdx");
        var docker = Disk(@"home\AppData\Local\Docker\wsl\disk\docker_data.vhdx");
        _profile.File(@"home\AppData\Local\wsl\{5678}\ext4.vhdx", 1000);

        var disks = Scanner(
            new WslDistro("Ubuntu", Path.GetDirectoryName(ubuntu)!, "ext4.vhdx"),
            new WslDistro("Broken", _profile.P(@"home\AppData\Local\wsl\{5678}"), "ext4.vhdx"),
            new WslDistro("Gone", _profile.P(@"home\AppData\Local\wsl\{9999}"), "ext4.vhdx")).Scan();

        Assert.Equal(["Ubuntu", "Docker Desktop data"], disks.Select(d => d.Name));
        Assert.Equal(VirtualDiskOwner.Wsl, disks[0].Owner);
        Assert.Equal(VirtualDiskOwner.Docker, disks[1].Owner);
        Assert.Equal(PathUtil.Normalize(docker), disks[1].Path);
        // A fresh disk has nothing to give back, but Docker's only shows its free space after a trim.
        Assert.False(disks[0].IsWorthCompacting);
        Assert.True(disks[1].IsWorthCompacting);
        Assert.Equal(0, disks[1].KnownReclaimableBytes);
    }

    [Fact]
    public void ADiskBehindALinkIsNotOffered()
    {
        var real = Disk(@"elsewhere\ext4.vhdx");
        _profile.Dir(@"home\AppData\Local\wsl");
        FakeProfile.Junction(_profile.P(@"home\AppData\Local\wsl\{1234}"), Path.GetDirectoryName(real)!);

        Assert.Empty(Scanner(new WslDistro("Ubuntu", _profile.P(@"home\AppData\Local\wsl\{1234}"), "ext4.vhdx")).Scan());
    }

    [Fact]
    public void TrimsAndStopsDockerThenShutsWslDownBeforeCompacting()
    {
        var ubuntu = Disk(@"home\AppData\Local\wsl\{1234}\ext4.vhdx");
        var docker = Disk(@"home\AppData\Local\Docker\wsl\disk\docker_data.vhdx");
        var scanner = Scanner(new WslDistro("Ubuntu", Path.GetDirectoryName(ubuntu)!, "ext4.vhdx"));

        var result = Compactor(scanner, dockerRunning: true).Run([ubuntu, docker], null, TestContext.Current.CancellationToken);

        Assert.Equal(
        [
            "wsl.exe -d docker-desktop -u root -e fstrim /mnt/docker-desktop-disk",
            "docker desktop stop",
            "wsl.exe --shutdown",
            "compact ext4.vhdx",
            "compact docker_data.vhdx",
        ], _commands);
        Assert.Equal(2, result.Removed.Count);
    }

    [Fact]
    public void WithoutDockerOnlyWslStops()
    {
        var ubuntu = Disk(@"home\AppData\Local\wsl\{1234}\ext4.vhdx");

        Compactor(Scanner(new WslDistro("Ubuntu", Path.GetDirectoryName(ubuntu)!, "ext4.vhdx")), dockerRunning: false)
            .Run([ubuntu], null, TestContext.Current.CancellationToken);

        Assert.Equal(["wsl.exe --shutdown", "compact ext4.vhdx"], _commands);
    }

    [Fact]
    public void LeavesEverythingRunningIfDockerWontStop()
    {
        var docker = Disk(@"home\AppData\Local\Docker\wsl\disk\docker_data.vhdx");

        var result = Compactor(Scanner(), dockerRunning: true, dockerStopExit: 1).Run([docker], null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("wsl.exe --shutdown", _commands);
        Assert.Empty(_compacted);
        Assert.Contains("couldn't stop Docker Desktop", Assert.Single(result.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyCompactsDisksItFindsAgain()
    {
        var other = Disk(@"home\Documents\vm.vhdx");

        var result = Compactor(Scanner(), dockerRunning: false).Run([other], null, TestContext.Current.CancellationToken);

        Assert.Empty(_commands);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void GivesUpOnADiskWslWontRelease()
    {
        var ubuntu = Disk(@"home\AppData\Local\wsl\{1234}\ext4.vhdx");
        var compactor = new VirtualDiskCompactor(Scanner(new WslDistro("Ubuntu", Path.GetDirectoryName(ubuntu)!, "ext4.vhdx")))
        {
            IsDockerRunning = () => false,
            RunCommand = (_, _, _) => new CommandResult(0, ""),
            Compact = path =>
            {
                _compacted.Add(path);
                return null;
            },
            IsFree = _ => false,
            ReleaseTimeout = TimeSpan.FromMilliseconds(600),
        };

        var result = compactor.Run([ubuntu], null, TestContext.Current.CancellationToken);

        Assert.Empty(_compacted);
        Assert.Contains("still using it", Assert.Single(result.Failures).Reason, StringComparison.Ordinal);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VIRTUAL_STORAGE_TYPE
    {
        public uint DeviceId;
        public Guid VendorId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CREATE_VIRTUAL_DISK_PARAMETERS
    {
        public int Version;
        public Guid UniqueId;
        public long MaximumSize;
        public uint BlockSizeInBytes;
        public uint SectorSizeInBytes;
        public IntPtr ParentPath;
        public IntPtr SourcePath;
    }

    [DllImport("virtdisk.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateVirtualDisk(ref VIRTUAL_STORAGE_TYPE type, string path, int access, IntPtr security, int flags, int providerFlags,
        ref CREATE_VIRTUAL_DISK_PARAMETERS parameters, IntPtr overlapped, out IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
