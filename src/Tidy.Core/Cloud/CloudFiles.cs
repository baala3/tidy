using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Tidy.Core.Cloud;

public readonly record struct PlaceholderInfo(CloudPinState PinState, bool InSync);

public enum CloudPinState
{
    Unspecified = 0,
    /// <summary>"Always keep on this device".</summary>
    Pinned = 1,
    /// <summary>"Free up space": the sync app may drop the local copy.</summary>
    Unpinned = 2,
    Excluded = 3,
    Inherit = 4,
}

/// <summary>
/// The Windows Cloud Files API that OneDrive builds on. A synced file is a placeholder: the
/// same file in File Explorer whether its contents are on this PC or only in the cloud.
/// </summary>
public static class CloudFiles
{
    /// <summary>Contents are in the cloud only; opening the file downloads it.</summary>
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    public const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    /// <summary>"Always keep on this device".</summary>
    public const FileAttributes Pinned = (FileAttributes)0x00080000;
    public const FileAttributes Unpinned = (FileAttributes)0x00100000;

    /// <summary>Any of these means some or all of the file's contents are not on this PC.</summary>
    public const FileAttributes NotFullyLocal = RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline;

    /// <summary>
    /// Windows disguises placeholders as plain files to every process but the sync app, so
    /// that old programs aren't confused by them. Tidy has to see them for what they are to
    /// tell a synced file from one only this PC has. Applies to the calling thread until disposed.
    /// </summary>
    public static PlaceholderView ExposePlaceholders() => new(RtlSetThreadPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS));

    public readonly struct PlaceholderView(sbyte previous) : IDisposable
    {
        public void Dispose() => _ = RtlSetThreadPlaceholderCompatibilityMode(previous);
    }

    /// <summary>The calling thread's placeholder mode: 1 disguises them, 2 exposes them.</summary>
    internal static sbyte ThreadPlaceholderMode() => RtlQueryThreadPlaceholderCompatibilityMode();

    /// <summary>
    /// Whether a sync app has registered the folder as its sync root. OneDrive keeps its folder
    /// registered while signed in, running or not; a folder left behind after signing out isn't.
    /// </summary>
    public static bool IsSyncRoot(string folder)
    {
        var buffer = new byte[1024];
        return Directory.Exists(folder) && CfGetSyncRootInfoByPath(folder, CF_SYNC_ROOT_INFO_BASIC, buffer, buffer.Length, out _) >= 0;
    }

    /// <summary>IO_REPARSE_TAG_CLOUD and its fifteen numbered variants.</summary>
    public static bool IsCloudTag(uint reparseTag) => (reparseTag & 0xFFFF0FFF) == 0x9000001A;

    /// <summary>Pin and sync state of a placeholder, or null for a file that isn't one or can't be opened.</summary>
    public static PlaceholderInfo? Info(string path)
    {
        using var handle = Open(path, FILE_READ_ATTRIBUTES);
        if (handle.IsInvalid) return null;
        // Basic info is two ints and two file ids, then the provider's identity blob of up to 4 KB.
        var buffer = new byte[4096 + 64];
        if (CfGetPlaceholderInfo(handle, CF_PLACEHOLDER_INFO_BASIC, buffer, buffer.Length, out _) < 0) return null;
        return new PlaceholderInfo((CloudPinState)BitConverter.ToInt32(buffer, 0), BitConverter.ToInt32(buffer, 4) == CF_IN_SYNC_STATE_IN_SYNC);
    }

    /// <summary>Bytes the file takes on disk now: zero once only the cloud has its contents.</summary>
    public static long OnDiskBytes(string path)
    {
        var low = GetCompressedFileSizeW(path, out var high);
        return low == uint.MaxValue && Marshal.GetLastWin32Error() != 0 ? 0 : ((long)high << 32) | low;
    }

    /// <summary>
    /// What File Explorer's "Free up space" does (and <c>attrib -P +U</c>): marks the file as
    /// fine to keep online only. The sync app then drops the local copy in the background.
    /// Tidy doesn't drop it itself: from any process but the sync app's, that is a request the
    /// sync app has to answer, and a busy or stuck one would leave Tidy waiting.
    /// Returns null on success, or the reason it failed.
    /// </summary>
    public static string? MarkOnlineOnly(string path)
    {
        using var handle = Open(path, FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES);
        if (handle.IsInvalid) return Marshal.GetLastWin32Error() == ERROR_SHARING_VIOLATION ? "It is in use. Close the app using it and try again." : "Windows denied access to it.";
        var hr = CfSetPinState(handle, CloudPinState.Unpinned, 0, IntPtr.Zero);
        return hr >= 0 ? null : $"Windows could not mark it online-only (0x{hr:X8}).";
    }

    private static SafeFileHandle Open(string path, uint access) =>
        CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    private const sbyte PHCM_EXPOSE_PLACEHOLDERS = 2;
    private const int CF_PLACEHOLDER_INFO_BASIC = 0;
    private const int CF_SYNC_ROOT_INFO_BASIC = 0;
    private const int CF_IN_SYNC_STATE_IN_SYNC = 1;
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_WRITE_ATTRIBUTES = 0x0100;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint FILE_SHARE_DELETE = 4;
    private const uint OPEN_EXISTING = 3;
    private const int ERROR_SHARING_VIOLATION = 32;

    [DllImport("ntdll.dll")]
    private static extern sbyte RtlSetThreadPlaceholderCompatibilityMode(sbyte mode);

    [DllImport("ntdll.dll")]
    private static extern sbyte RtlQueryThreadPlaceholderCompatibilityMode();

    [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
    private static extern int CfGetSyncRootInfoByPath(string path, int infoClass, byte[] buffer, int length, out int returned);

    [DllImport("cldapi.dll")]
    private static extern int CfGetPlaceholderInfo(SafeFileHandle handle, int infoClass, byte[] buffer, int length, out int returned);

    [DllImport("cldapi.dll")]
    private static extern int CfSetPinState(SafeFileHandle handle, CloudPinState state, int flags, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string path, out uint high);
}
