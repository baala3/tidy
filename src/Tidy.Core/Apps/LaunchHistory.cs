using System.Runtime.InteropServices;
using Microsoft.Win32;
using Tidy.Core.Platform;

namespace Tidy.Core.Apps;

/// <summary>
/// When the user last opened each app, from the launch log Explorer keeps for Start,
/// the taskbar and File Explorer (UserAssist). It needs no admin rights. It does not see
/// apps started any other way, like from a terminal or at sign-in, and it is empty when
/// "Let Windows track app launches" is off in Settings → Privacy.
/// </summary>
public sealed class LaunchHistory
{
    private static readonly string[] CountKeys =
    [
        // Executables and Store app IDs.
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count",
        // Shortcuts.
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}\Count",
    ];

    /// <summary>Raw entries: program paths, shortcut paths and app IDs.</summary>
    private readonly Dictionary<string, DateTime> _lastRun;
    /// <summary>The same launches, with shortcuts and app IDs resolved to the program they start.</summary>
    private readonly Dictionary<string, DateTime> _lastRunByProgram = new(PathUtil.Comparer);

    public bool IsTrackingEnabled { get; }

    private LaunchHistory(Dictionary<string, DateTime> lastRun, bool tracking, ShortcutIndex? shortcuts)
    {
        _lastRun = lastRun;
        IsTrackingEnabled = tracking;
        foreach (var (entry, when) in lastRun)
        {
            var program = shortcuts?.Resolve(entry) ?? entry;
            if (!_lastRunByProgram.TryGetValue(program, out var existing) || when > existing) _lastRunByProgram[program] = when;
        }
    }

    public static LaunchHistory Empty { get; } = new(new Dictionary<string, DateTime>(PathUtil.Comparer), false, null);

    public static LaunchHistory Read(ShortcutIndex? shortcuts = null)
    {
        var lastRun = new Dictionary<string, DateTime>(PathUtil.Comparer);
        foreach (var keyPath in CountKeys)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            if (key is null) continue;
            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not byte[] { Length: >= 68 } data) continue;
                var fileTime = BitConverter.ToInt64(data, 60);
                if (fileTime <= 0) continue;
                DateTime when;
                try
                {
                    when = DateTime.FromFileTimeUtc(fileTime);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }
                var name = ResolveKnownFolder(Rot13(valueName));
                if (!lastRun.TryGetValue(name, out var existing) || when > existing) lastRun[name] = when;
            }
        }
        using var advanced = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        var tracking = advanced?.GetValue("Start_TrackProgs") is not int flag || flag != 0;
        return new LaunchHistory(lastRun, tracking && lastRun.Count > 0, shortcuts);
    }

    /// <summary>Last launch of a Store app, matched on its package family name.</summary>
    public DateTime? ForPackageFamily(string familyName)
    {
        DateTime? newest = null;
        var prefix = familyName + "!";
        foreach (var (name, when) in _lastRun)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (newest is null || when > newest)) newest = when;
        }
        return newest;
    }

    /// <summary>Last launch of anything in <paramref name="folder"/>, or of <paramref name="exe"/>.</summary>
    public DateTime? ForDesktopApp(string? exe, string? folder)
    {
        DateTime? newest = null;
        foreach (var (name, when) in _lastRunByProgram)
        {
            var matches = (exe is not null && PathUtil.AreSame(name, exe)) ||
                (folder is not null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && PathUtil.IsStrictlyUnder(name, folder));
            if (matches && (newest is null || when > newest)) newest = when;
        }
        return newest;
    }

    internal static string Rot13(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= 'a' and <= 'z') chars[i] = (char)('a' + (c - 'a' + 13) % 26);
            else if (c is >= 'A' and <= 'Z') chars[i] = (char)('A' + (c - 'A' + 13) % 26);
        }
        return new string(chars);
    }

    /// <summary>Entries start with a known-folder GUID, like <c>{6D809377-...}\App\app.exe</c> for Program Files.</summary>
    private static string ResolveKnownFolder(string name)
    {
        if (name.Length < 39 || name[0] != '{' || name[37] != '}' || !Guid.TryParse(name[1..37], out var id)) return name;
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var raw) != 0) return name;
        try
        {
            return Marshal.PtrToStringUni(raw) + name[38..];
        }
        finally
        {
            Marshal.FreeCoTaskMem(raw);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);
}
