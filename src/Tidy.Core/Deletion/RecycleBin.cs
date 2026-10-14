using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Tidy.Core.Deletion;

/// <summary>
/// Moves items to the Recycle Bin through the shell's <c>IFileOperation</c>, the same
/// path Explorer's Delete key takes, so everything Tidy removes can be restored.
/// </summary>
public static class RecycleBin
{
    public readonly record struct Capacity(bool Enabled, long MaxBytes);

    /// <summary>
    /// How the Recycle Bin is set up for the drive holding <paramref name="path"/>. Windows
    /// deletes for good, without asking, when the bin is off for a drive or an item is
    /// larger than the bin. Tidy checks first and refuses instead.
    /// </summary>
    public static Capacity CapacityFor(string path)
    {
        var root = Path.GetPathRoot(path)!;
        var drive = new DriveInfo(root);
        if (drive.DriveType != DriveType.Fixed) return new Capacity(false, 0);

        // Default when the user never changed anything: on, at 5% of the drive.
        var fallback = new Capacity(true, drive.TotalSize / 20);
        var volume = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPointW(root, volume, volume.Capacity)) return fallback;
        // "\\?\Volume{guid}\" -> "{guid}"
        var guid = volume.ToString();
        var open = guid.IndexOf('{', StringComparison.Ordinal);
        var close = guid.IndexOf('}', StringComparison.Ordinal);
        if (open < 0 || close < open) return fallback;
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\{guid[open..(close + 1)]}");
        if (key is null) return fallback;
        var nuke = key.GetValue("NukeOnDelete") is int n && n != 0;
        var maxMb = key.GetValue("MaxCapacity") is int mb ? mb : (int)(fallback.MaxBytes / (1024 * 1024));
        return new Capacity(!nuke, (long)maxMb * 1024 * 1024);
    }

    /// <summary>Total size of everything in the Recycle Bin across all drives.</summary>
    public static long TotalSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? info.i64Size : 0;
    }

    /// <summary>
    /// Recycles one file or folder. Returns null on success, or the reason it failed.
    /// Must run on an STA thread. The shell moves a folder as one rename, so a folder
    /// with a file held open fails as a whole instead of being half removed.
    /// </summary>
    public static string? Recycle(string path, IntPtr ownerWindow)
    {
        var operation = (IFileOperation)new FileOperation();
        try
        {
            operation.SetOperationFlags(
                FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOFX_EARLYFAILURE |
                FOFX_RECYCLEONDELETE | FOF_ALLOWUNDO |
                // Belt and braces next to CapacityFor: if Windows would still destroy the
                // item instead of recycling it, it asks the user rather than doing it quietly.
                FOF_WANTNUKEWARNING);
            if (ownerWindow != IntPtr.Zero) operation.SetOwnerWindow(ownerWindow);

            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
            if (hr != 0) return DescribeError(hr);
            try
            {
                operation.DeleteItem(item, IntPtr.Zero);
                operation.PerformOperations();
                if (operation.GetAnyOperationsAborted()) return "Cancelled";
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (COMException e)
        {
            return DescribeError(e.HResult);
        }
        finally
        {
            Marshal.ReleaseComObject(operation);
        }
        return Path.Exists(path) ? "It is in use. Close the app using it and try again." : null;
    }

    private static string DescribeError(int hr) => hr switch
    {
        unchecked((int)0x80070020) or unchecked((int)0x80270009) or unchecked((int)0x8027000C) =>
            "It is in use. Close the app using it and try again.",
        unchecked((int)0x80070005) or unchecked((int)0x80030005) => "Windows denied access to it.",
        unchecked((int)0x80070002) or unchecked((int)0x80070003) => "It no longer exists.",
        unchecked((int)0x80270000) or unchecked((int)0x800704C7) => "Cancelled",
        // Every other copy-engine refusal (0x8027xxxx) is a lock or protection on something inside.
        _ when (hr & 0xFFFF0000) == 0x80270000 => "Something inside it is in use or protected. Close the app using it and try again.",
        _ => $"Windows could not move it to the Recycle Bin (0x{hr:X8}).",
    };

    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOERRORUI = 0x0400;
    private const uint FOF_WANTNUKEWARNING = 0x4000;
    private const uint FOFX_EARLYFAILURE = 0x00100000;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;

    [ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperation;

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        uint Advise(IntPtr sink);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IntPtr sink);
        void MoveItems(IntPtr items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IntPtr sink);
        void CopyItems(IntPtr items, IShellItem destination);
        void DeleteItem(IShellItem item, IntPtr sink);
        void DeleteItems(IntPtr items);
        uint NewItem(IShellItem destination, FileAttributes attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IntPtr sink);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)]
        bool GetAnyOperationsAborted();
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder volumeName, int length);
}
