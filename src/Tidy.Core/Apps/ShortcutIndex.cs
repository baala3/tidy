using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Tidy.Core.Platform;

namespace Tidy.Core.Apps;

/// <summary>
/// The Start menu, taskbar and desktop shortcuts, each resolved to its target program and
/// app ID. Launch history records many launches by shortcut or app ID ("Chrome",
/// "Anysphere.Cursor") instead of the program's path; this maps them back.
/// </summary>
public sealed class ShortcutIndex
{
    private readonly Dictionary<string, string> _targetByShortcut = new(PathUtil.Comparer);
    private readonly Dictionary<string, string> _targetByAppId = new(StringComparer.OrdinalIgnoreCase);

    public static ShortcutIndex Build()
    {
        var index = new ShortcutIndex();
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        ];
        foreach (var folder in folders.Where(Directory.Exists))
        {
            IEnumerable<string> links;
            try
            {
                links = Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var link in links) index.Add(link);
        }
        return index;
    }

    /// <summary>The program a launch-history entry refers to, or the entry itself when it is already a path.</summary>
    public string Resolve(string entry)
    {
        if (entry.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            if (_targetByShortcut.TryGetValue(entry, out var target)) return target;
            return Read(entry).Target ?? entry;
        }
        if (!entry.Contains('\\') && _targetByAppId.TryGetValue(entry, out var byId)) return byId;
        return entry;
    }

    private void Add(string link)
    {
        var (target, appId) = Read(link);
        if (target is null) return;
        _targetByShortcut[link] = target;
        if (appId is not null) _targetByAppId.TryAdd(appId, target);
    }

    /// <summary>Reads a shortcut without resolving it, so a missing target never triggers a search.</summary>
    internal static (string? Target, string? AppId) Read(string link)
    {
        if (!File.Exists(link)) return (null, null);
        object? shellLink = null;
        try
        {
            shellLink = new ShellLink();
            ((IPersistFile)shellLink).Load(link, 0);
            var path = new StringBuilder(1024);
            ((IShellLinkW)shellLink).GetPath(path, path.Capacity, IntPtr.Zero, 0);
            var target = path.Length > 0 && path.ToString().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path.ToString() : null;
            return (target, ReadAppId((IPropertyStore)shellLink));
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException or FileNotFoundException)
        {
            return (null, null);
        }
        finally
        {
            if (shellLink is not null) Marshal.ReleaseComObject(shellLink);
        }
    }

    private static string? ReadAppId(IPropertyStore store)
    {
        var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
        var value = new PropVariant();
        try
        {
            if (store.GetValue(ref key, ref value) != 0) return null;
            return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        // Only the first vtable slot is used.
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int length, IntPtr findData, uint flags);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, ref PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr Extra;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
