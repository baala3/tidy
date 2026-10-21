using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Tidy.Core.Platform;

public readonly record struct FoundEntry(string Path, string Name, FileAttributes Attributes, uint ReparseTag, long Length, DateTime LastWriteUtc)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;
}

/// <summary>
/// Lists a folder with <c>FindFirstFileExW</c>, which also returns each entry's reparse tag.
/// The tag tells a cloud placeholder (a OneDrive file or folder) from a link or junction,
/// which <see cref="System.IO.Enumeration.FileSystemEnumerable{T}"/> can't.
/// </summary>
public static class FindFiles
{
    public static IEnumerable<FoundEntry> In(string folder)
    {
        var entries = new List<FoundEntry>();
        using (var handle = FindFirstFileExW(Path.Combine(folder, "*"), FindExInfoBasic, out var data, FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH))
        {
            if (handle.IsInvalid) return entries;
            do
            {
                if (data.cFileName is "." or "..") continue;
                entries.Add(ToEntry(Path.Combine(folder, data.cFileName), data));
            }
            while (FindNextFileW(handle, out data));
        }
        return entries;
    }

    /// <summary>The entry for one path, or null when it isn't there.</summary>
    public static FoundEntry? Of(string path)
    {
        using var handle = FindFirstFileExW(path, FindExInfoBasic, out var data, FindExSearchNameMatch, IntPtr.Zero, 0);
        return handle.IsInvalid ? null : ToEntry(path, data);
    }

    private static FoundEntry ToEntry(string path, WIN32_FIND_DATAW data)
    {
        var attributes = (FileAttributes)data.dwFileAttributes;
        var tag = (attributes & FileAttributes.ReparsePoint) != 0 ? data.dwReserved0 : 0;
        var written = DateTime.FromFileTimeUtc(((long)data.ftLastWriteTimeHigh << 32) | data.ftLastWriteTimeLow);
        return new FoundEntry(path, data.cFileName, attributes, tag, ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow, written);
    }

    private const int FindExInfoBasic = 1;
    private const int FindExSearchNameMatch = 0;
    private const int FIND_FIRST_EX_LARGE_FETCH = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public uint ftCreationTimeLow, ftCreationTimeHigh;
        public uint ftLastAccessTimeLow, ftLastAccessTimeHigh;
        public uint ftLastWriteTimeLow, ftLastWriteTimeHigh;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
    }

    private sealed class SafeFindHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => FindClose(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFindHandle FindFirstFileExW(string fileName, int infoLevel, out WIN32_FIND_DATAW data, int searchOp, IntPtr filter, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextFileW(SafeFindHandle handle, out WIN32_FIND_DATAW data);

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(IntPtr handle);
}
