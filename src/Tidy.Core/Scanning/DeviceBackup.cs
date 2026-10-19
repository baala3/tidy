using System.Globalization;
using System.Xml.Linq;

namespace Tidy.Core.Scanning;

/// <summary>
/// What an iPhone or iPad backup folder says about itself, from the Info.plist iTunes and
/// Apple Devices write next to the backup.
/// </summary>
public sealed record DeviceBackup(string DeviceName, DateTime? LastBackupUtc)
{
    public const string UnknownDevice = "an iPhone or iPad";

    public static DeviceBackup Read(string folder)
    {
        var values = ReadPlist(Path.Combine(folder, "Info.plist"));
        var name = values.GetValueOrDefault("Device Name") ?? values.GetValueOrDefault("Display Name");
        var date = values.GetValueOrDefault("Last Backup Date") is { } text &&
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : (DateTime?)null;
        return new DeviceBackup(string.IsNullOrWhiteSpace(name) ? UnknownDevice : name.Trim(), date);
    }

    /// <summary>The top-level string and date values of an XML property list. A binary or broken one reads as empty.</summary>
    private static Dictionary<string, string> ReadPlist(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path)) return values;
            var dict = XDocument.Load(path).Root?.Element("dict");
            if (dict is null) return values;
            string? key = null;
            foreach (var element in dict.Elements())
            {
                if (element.Name == "key") key = element.Value;
                else if (key is not null && element.Name.LocalName is "string" or "date") values[key] = element.Value;
                if (element.Name != "key") key = null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
        }
        return values;
    }
}
