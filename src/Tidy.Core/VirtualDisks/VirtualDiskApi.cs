using System.Runtime.InteropServices;

namespace Tidy.Core.VirtualDisks;

/// <summary>
/// Compacts a VHDX with the Virtual Disk API, the call behind <c>Optimize-VHD</c> and
/// diskpart's <c>compact vdisk</c>. On a disk nothing has attached it needs no admin rights,
/// keeps every byte of data, and gives back the blocks Linux has freed.
/// </summary>
public static class VirtualDiskApi
{
    /// <summary>Null on success, or the reason it failed.</summary>
    public static string? Compact(string path)
    {
        var type = new VIRTUAL_STORAGE_TYPE { DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_VHDX, VendorId = VendorMicrosoft };
        var open = new OPEN_VIRTUAL_DISK_PARAMETERS { Version = 1 };
        var error = OpenVirtualDisk(ref type, path, VIRTUAL_DISK_ACCESS_METAOPS, 0, ref open, out var handle);
        if (error != 0) return Describe(error);
        try
        {
            var compact = new COMPACT_VIRTUAL_DISK_PARAMETERS { Version = 1 };
            error = CompactVirtualDisk(handle, 0, ref compact, IntPtr.Zero);
            return error == 0 ? null : Describe(error);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Whether nothing has the disk open: WSL keeps it open while its VM runs.</summary>
    public static bool IsFree(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string Describe(int error) => error switch
    {
        32 => "WSL is still using it. Close any Linux windows and try again.",
        5 => "Windows denied access to it.",
        2 or 3 => "It no longer exists.",
        _ => $"Windows could not compact it (error {error}).",
    };

    private const uint VIRTUAL_STORAGE_TYPE_DEVICE_VHDX = 3;
    private const int VIRTUAL_DISK_ACCESS_METAOPS = 0x00200000;
    private static readonly Guid VendorMicrosoft = new("EC984AEC-A0F9-47e9-901F-71415A66345B");

    [StructLayout(LayoutKind.Sequential)]
    private struct VIRTUAL_STORAGE_TYPE
    {
        public uint DeviceId;
        public Guid VendorId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OPEN_VIRTUAL_DISK_PARAMETERS
    {
        public int Version;
        public uint RWDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMPACT_VIRTUAL_DISK_PARAMETERS
    {
        public int Version;
        public uint Reserved;
    }

    [DllImport("virtdisk.dll", CharSet = CharSet.Unicode)]
    private static extern int OpenVirtualDisk(ref VIRTUAL_STORAGE_TYPE type, string path, int access, int flags, ref OPEN_VIRTUAL_DISK_PARAMETERS parameters, out IntPtr handle);

    [DllImport("virtdisk.dll")]
    private static extern int CompactVirtualDisk(IntPtr handle, int flags, ref COMPACT_VIRTUAL_DISK_PARAMETERS parameters, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
