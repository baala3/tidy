using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace Tidy.Core.Apps;

/// <summary>
/// What an installer says it installs, read from the file without running it: the version
/// resource of an .exe, the Property table of an .msi, or the manifest inside an .msix or
/// .appx package.
/// </summary>
public sealed record InstallerIdentity
{
    public string? ProductName { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    /// <summary>An .msi's ProductCode, which names its entry in Settings → Apps exactly.</summary>
    public string? ProductCode { get; init; }
    /// <summary>An .msix or .appx package's identity name, like <c>Microsoft.WindowsTerminal</c>.</summary>
    public string? PackageName { get; init; }

    /// <summary>
    /// Whether the file installs something. Packages always do; an .exe has to say so, because
    /// apps also put plain programs in these folders, like the launcher Firefox leaves on the
    /// Desktop in place of a shortcut.
    /// </summary>
    public bool IsInstaller { get; init; }

    public static InstallerIdentity? Read(string path)
    {
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".exe" => FromExe(path),
                ".msi" => FromMsi(path),
                ".msix" or ".appx" => FromPackage(path, "AppxManifest.xml"),
                ".msixbundle" or ".appxbundle" => FromPackage(path, "AppxMetadata/AppxBundleManifest.xml"),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static InstallerIdentity FromExe(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new InstallerIdentity
        {
            ProductName = NonEmpty(info.ProductName) ?? NonEmpty(info.FileDescription),
            Publisher = NonEmpty(info.CompanyName),
            Version = NonEmpty(info.ProductVersion) ?? NonEmpty(info.FileVersion),
            IsInstaller = NamesAnInstaller(Path.GetFileNameWithoutExtension(path), info.OriginalFilename, info.InternalName, info.FileDescription, info.ProductName)
                || HasInstallerMarker(path),
        };
    }

    private static readonly string[] InstallerWords = ["setup", "install", "bootstrapper"];

    /// <summary>"SteamSetup", "Firefox Installer", "npp.8.6.Installer.x64", "Inno Setup bootstrapper".</summary>
    internal static bool NamesAnInstaller(params string?[] names) =>
        names.Any(n => n is not null && InstallerWords.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The installer frameworks that build most Windows setup programs leave their name in the file.</summary>
    private static readonly byte[][] InstallerMarkers =
    [
        "NullsoftInst"u8.ToArray(),
        "Inno Setup Setup Data"u8.ToArray(),
        "InstallShield"u8.ToArray(),
        ".wixburn"u8.ToArray(),
    ];

    /// <summary>The markers sit in the first few megabytes: in the PE headers or right after the setup stub.</summary>
    private const int MarkerScanBytes = 4 * 1024 * 1024;

    private static bool HasInstallerMarker(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[(int)Math.Min(stream.Length, MarkerScanBytes)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var data = buffer.AsSpan(0, read);
        foreach (var marker in InstallerMarkers)
        {
            if (data.IndexOf(marker) >= 0) return true;
        }
        return false;
    }

    private static InstallerIdentity? FromPackage(string path, string manifestPath)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry(manifestPath);
        if (entry is null) return null;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root;
        var identity = root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity");
        if (identity is null) return null;
        var displayName = root!.Elements().FirstOrDefault(e => e.Name.LocalName == "Properties")?
            .Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value;
        return new InstallerIdentity
        {
            ProductName = displayName is { } d && !d.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? d : null,
            Version = (string?)identity.Attribute("Version"),
            PackageName = (string?)identity.Attribute("Name"),
            IsInstaller = true,
        };
    }

    private static InstallerIdentity? FromMsi(string path)
    {
        if (MsiOpenDatabaseW(path, IntPtr.Zero, out var database) != 0) return null;
        try
        {
            if (MsiDatabaseOpenViewW(database, "SELECT `Property`, `Value` FROM `Property`", out var view) != 0) return null;
            try
            {
                if (MsiViewExecute(view, IntPtr.Zero) != 0) return null;
                var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                while (MsiViewFetch(view, out var record) == 0)
                {
                    try
                    {
                        if (RecordString(record, 1) is { } name && RecordString(record, 2) is { } value) properties[name] = value;
                    }
                    finally
                    {
                        Close(record);
                    }
                }
                return new InstallerIdentity
                {
                    ProductName = properties.GetValueOrDefault("ProductName"),
                    Publisher = properties.GetValueOrDefault("Manufacturer"),
                    Version = properties.GetValueOrDefault("ProductVersion"),
                    ProductCode = properties.GetValueOrDefault("ProductCode"),
                    IsInstaller = true,
                };
            }
            finally
            {
                Close(view);
            }
        }
        finally
        {
            Close(database);
        }
    }

    private static string? RecordString(IntPtr record, uint field)
    {
        uint length = 0;
        if (MsiRecordGetStringW(record, field, null, ref length) != ERROR_MORE_DATA && length == 0) return "";
        var buffer = new StringBuilder((int)length + 1);
        length++;
        return MsiRecordGetStringW(record, field, buffer, ref length) == 0 ? buffer.ToString() : null;
    }

    /// <summary>Closing a handle can't fail in a way worth acting on.</summary>
    private static void Close(IntPtr handle) => _ = MsiCloseHandle(handle);

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const uint ERROR_MORE_DATA = 234;

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out IntPtr database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetStringW(IntPtr record, uint field, StringBuilder? value, ref uint length);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(IntPtr handle);
}
