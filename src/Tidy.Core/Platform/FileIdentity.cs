using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Tidy.Core.Platform;

/// <summary>
/// Which file on disk a path names: the volume and the file's index on it. Two paths with
/// the same identity are hard links to one file, so removing one frees nothing.
/// </summary>
public readonly record struct FileIdentity(uint Volume, ulong Index)
{
    /// <summary>Null when the file can't be opened, for example because another app holds it exclusively.</summary>
    public static FileIdentity? Of(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Of(handle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static FileIdentity? Of(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var info)
            ? new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow)
            : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out BY_HANDLE_FILE_INFORMATION info);
}
