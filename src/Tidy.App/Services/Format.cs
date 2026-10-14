using System.Globalization;

namespace Tidy.App.Services;

public static class Format
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Sizes in binary units with Windows' labels, so they match File Explorer and
    /// Settings: "4.07 GB", "43.2 MB", "512 KB".
    /// </summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024) return bytes == 1 ? "1 byte" : $"{bytes} bytes";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var pattern = value < 10 ? "0.##" : value < 100 ? "0.#" : "0";
        if (unit == 1) pattern = "0";
        return value.ToString(pattern, CultureInfo.CurrentCulture) + " " + Units[unit];
    }

    public static string Percent(double fraction) =>
        fraction is > 0 and < 0.001 ? "<0.1%" : (fraction * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";

    public static string Count(int count, string one, string many) =>
        count.ToString("N0", CultureInfo.CurrentCulture) + " " + (count == 1 ? one : many);

    public static string RelativeTime(DateTime utc, DateTime nowUtc)
    {
        var span = nowUtc - utc;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return Count((int)span.TotalMinutes, "minute", "minutes") + " ago";
        if (span < TimeSpan.FromDays(1)) return Count((int)span.TotalHours, "hour", "hours") + " ago";
        return utc.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);
    }

    /// <summary>"3 days ago", "5 months ago", "2 years ago": how long since something changed.</summary>
    public static string Age(DateTime utc, DateTime nowUtc)
    {
        var days = (nowUtc - utc).TotalDays;
        if (days < 1) return "today";
        if (days < 2) return "yesterday";
        if (days < 31) return Count((int)days, "day", "days") + " ago";
        if (days < 365) return Count((int)Math.Round(days / 30.44), "month", "months") + " ago";
        return Count((int)Math.Round(days / 365.25), "year", "years") + " ago";
    }
}
