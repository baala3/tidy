using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tidy.Core.WindowsCleanup;

/// <summary>Measuring and running Disk Cleanup handlers, so tests can stand in for Windows.</summary>
public interface ICleanupHandlers
{
    /// <summary>Bytes the handler would free, or null when it isn't on this PC or can't say.</summary>
    long? Measure(string key, string volume);

    /// <summary>Runs the handler. Returns null on success, or the reason it failed.</summary>
    string? Purge(string key, string volume, Action<long> freedSoFar, out long freed);
}

/// <summary>
/// Windows' own Disk Cleanup handlers, the code behind Disk Cleanup and Storage Sense. Each is a
/// COM object registered under <c>Explorer\VolumeCaches</c>, and decides for itself what to
/// remove. Most need administrator rights to see or touch anything.
/// </summary>
public sealed class DiskCleanupHandlers : ICleanupHandlers
{
    public const string VolumeCachesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    public long? Measure(string key, string volume) => WithHandler(key, volume, (cache, callback) =>
        cache.GetSpaceUsed(out var used, callback) >= 0 ? used : (long?)null);

    public string? Purge(string key, string volume, Action<long> freedSoFar, out long freed)
    {
        long total = 0;
        string? error = "Windows doesn't have this cleanup on this PC.";
        WithHandler<object?>(key, volume, (cache, callback) =>
        {
            if (cache.GetSpaceUsed(out var used, callback) < 0 || used == 0)
            {
                error = null;
                return null;
            }
            var progress = new Callback(bytes =>
            {
                total = bytes;
                freedSoFar(bytes);
            });
            var hr = cache.Purge(used, progress);
            error = hr >= 0 ? null : $"Windows couldn't finish it (0x{hr:X8}).";
            if (hr >= 0 && total == 0) total = used;
            return null;
        });
        freed = total;
        return error;
    }

    /// <summary>Creates the handler, initializes it for the volume, runs <paramref name="use"/> and lets it go.</summary>
    private static T? WithHandler<T>(string key, string volume, Func<IEmptyVolumeCache, Callback, T?> use)
    {
        using var registry = Registry.LocalMachine.OpenSubKey($@"{VolumeCachesKey}\{key}");
        if (registry?.GetValue("") is not string clsid || !Guid.TryParse(clsid, out var id)) return default;
        if (Type.GetTypeFromCLSID(id) is not { } type) return default;
        object? handler = null;
        try
        {
            handler = Activator.CreateInstance(type);
            if (handler is not IEmptyVolumeCache cache) return default;
            var hkey = registry.Handle.DangerousGetHandle();
            uint flags = 0;
            int hr;
            IntPtr name, description, button = IntPtr.Zero;
            if (handler is IEmptyVolumeCache2 cache2)
            {
                hr = cache2.InitializeEx(hkey, volume, key, out name, out description, out button, ref flags);
            }
            else
            {
                hr = cache.Initialize(hkey, volume, out name, out description, ref flags);
            }
            foreach (var text in new[] { name, description, button }) Marshal.FreeCoTaskMem(text);
            // S_FALSE: nothing to clean here, or not without administrator rights.
            if (hr != 0) return default;
            try
            {
                return use(cache, new Callback(_ => { }));
            }
            finally
            {
                _ = cache.Deactivate(out _);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return default;
        }
        finally
        {
            if (handler is not null && Marshal.IsComObject(handler)) Marshal.ReleaseComObject(handler);
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class Callback(Action<long> freed) : IEmptyVolumeCacheCallBack
    {
        public int ScanProgress(long spaceUsed, uint flags, IntPtr status) => 0;

        public int PurgeProgress(long spaceFreed, long spaceToFree, uint flags, IntPtr status)
        {
            freed(spaceFreed);
            return 0;
        }
    }

    [ComImport, Guid("8FCE5227-04DA-11d1-A004-00805F8ABE06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEmptyVolumeCache
    {
        [PreserveSig] int Initialize(IntPtr regKey, [MarshalAs(UnmanagedType.LPWStr)] string volume, out IntPtr displayName, out IntPtr description, ref uint flags);
        [PreserveSig] int GetSpaceUsed(out long spaceUsed, IEmptyVolumeCacheCallBack callback);
        [PreserveSig] int Purge(long spaceToFree, IEmptyVolumeCacheCallBack callback);
        [PreserveSig] int ShowProperties(IntPtr window);
        [PreserveSig] int Deactivate(out uint flags);
    }

    [ComImport, Guid("02B7E3BA-4DB3-11d2-B2D9-00C04F8EEC8C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEmptyVolumeCache2
    {
        [PreserveSig] int Initialize(IntPtr regKey, [MarshalAs(UnmanagedType.LPWStr)] string volume, out IntPtr displayName, out IntPtr description, ref uint flags);
        [PreserveSig] int GetSpaceUsed(out long spaceUsed, IEmptyVolumeCacheCallBack callback);
        [PreserveSig] int Purge(long spaceToFree, IEmptyVolumeCacheCallBack callback);
        [PreserveSig] int ShowProperties(IntPtr window);
        [PreserveSig] int Deactivate(out uint flags);
        [PreserveSig] int InitializeEx(IntPtr regKey, [MarshalAs(UnmanagedType.LPWStr)] string volume, [MarshalAs(UnmanagedType.LPWStr)] string keyName,
            out IntPtr displayName, out IntPtr description, out IntPtr buttonText, ref uint flags);
    }

    [ComImport, Guid("6E793361-73C6-11D0-8469-00AA00442901"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEmptyVolumeCacheCallBack
    {
        [PreserveSig] int ScanProgress(long spaceUsed, uint flags, IntPtr status);
        [PreserveSig] int PurgeProgress(long spaceFreed, long spaceToFree, uint flags, IntPtr status);
    }
}
