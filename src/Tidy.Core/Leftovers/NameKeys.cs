using System.Text;
using Tidy.Core.Apps;

namespace Tidy.Core.Leftovers;

/// <summary>
/// App and folder names reduced to something comparable: lower case, letters and digits only.
/// "Zoom Workplace (64-bit)" and the folder "ZoomWorkplace" share the key <c>zoomworkplace</c>.
/// A name has two keys: all of it, and the bare name without versions, architectures and
/// anything in brackets, so "7-Zip 23.01 (x64)" also matches a folder called "7-Zip".
/// </summary>
internal static class NameKeys
{
    /// <summary>Shorter keys ("ea", "go") name too many things to say anything.</summary>
    private const int MinLength = 3;

    private static readonly HashSet<string> ArchitectureWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "x64", "x86", "x86_64", "amd64", "arm64", "64-bit", "32-bit", "64bit", "32bit",
    };

    /// <summary>
    /// Folder names that are Windows' own, a publisher's shared folder, or say nothing about
    /// which app they belong to. Never offered as a leftover and never used to match one.
    /// </summary>
    public static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "programs", "common", "commonfiles", "bin", "app", "apps", "application", "applicationdata", "data", "userdata",
        "temp", "tmp", "cache", "caches", "log", "logs", "update", "updater", "updates", "settings", "config", "user", "users",
        "shared", "runtime", "runtimes", "plugins", "downloads", "default", "local", "roaming", "locallow", "programdata",
        "programfiles", "programfilesx86", "system", "system32", "drivers", "package", "packages", "install", "installer",
        "setup", "uninstall", "support", "tools", "resources", "lib", "backup", "documents", "desktop", "templates",
        "microsoft", "windows", "google", "mozilla", "apple", "applecomputer", "adobe", "intel", "nvidia", "nvidiacorporation", "amd",
        "comms", "connecteddevicesplatform", "d3dscache", "crashdumps", "placeholdertilelogofolder", "publishers",
        "virtualstore", "history", "temporaryinternetfiles", "packagecache", "softwaredistribution", "startmenu", "ssh",
        "usoprivate", "usoshared", "tidy", "toastnotificationmanagercompat", "speech", "electron",
    };

    /// <summary>"Zoom Workplace (64-bit)" becomes <c>zoomworkplace</c>.</summary>
    public static string Full(string name) => Alphanumeric(name);

    /// <summary>"7-Zip 23.01 (x64)" becomes <c>7zip</c>: no brackets, versions or architectures.</summary>
    public static string Bare(string name) =>
        Alphanumeric(string.Join(' ', Words(WithoutBrackets(name)).Where(w => !IsVersion(w) && !ArchitectureWords.Contains(w))));

    /// <summary>The keys a name is known by, too-short ones left out.</summary>
    public static IReadOnlySet<string> Of(string name)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { Full(name), Bare(name) })
        {
            if (key.Length >= MinLength) keys.Add(key);
        }
        return keys;
    }

    /// <summary>A folder's keys, or none when its name is too generic to belong to one app.</summary>
    public static IReadOnlySet<string> OfFolder(string folder)
    {
        var keys = Of(Path.GetFileName(folder));
        return keys.Any(Generic.Contains) ? new HashSet<string>() : keys;
    }

    public static bool IsGenericFolder(string folder) =>
        Of(Path.GetFileName(folder)) is var keys && (keys.Count == 0 || keys.Any(Generic.Contains));

    /// <summary>A publisher without "Inc.", "Corporation" and the like: "Discord Inc." becomes <c>discord</c>.</summary>
    public static IReadOnlySet<string> OfPublisher(string publisher)
    {
        var keys = new HashSet<string>(Of(publisher), StringComparer.Ordinal);
        var company = Alphanumeric(string.Join(' ', Words(WithoutBrackets(publisher)).Where(w => !InstallerMatcher.CompanyNoise.Contains(Alphanumeric(w)))));
        if (company.Length >= MinLength) keys.Add(company);
        return keys;
    }

    /// <summary>"Mozilla Firefox" by Mozilla is <c>firefox</c> inside a folder named after Mozilla.</summary>
    public static IReadOnlySet<string> OfNameWithoutPublisher(string name, string publisher)
    {
        var publisherWords = Words(publisher).Select(Alphanumeric).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);
        var rest = Words(WithoutBrackets(name)).SkipWhile(w => publisherWords.Contains(Alphanumeric(w))).ToList();
        return rest.Count == 0 ? new HashSet<string>() : Of(string.Join(' ', rest));
    }

    /// <summary>"Docker Desktop" starts with <c>docker</c>: its company folder is often named that.</summary>
    public static string? FirstWord(string name)
    {
        var first = Words(name).Select(Alphanumeric).FirstOrDefault(w => w.Length > 0);
        return first is { Length: >= MinLength } ? first : null;
    }

    private static string[] Words(string text) =>
        text.Split((char[])[' ', '\t', '_', ','], StringSplitOptions.RemoveEmptyEntries);

    private static bool IsVersion(string word)
    {
        var trimmed = word.TrimStart('v', 'V');
        return trimmed.Length > 0 && trimmed.All(c => char.IsAsciiDigit(c) || c == '.');
    }

    private static string WithoutBrackets(string text)
    {
        var result = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var c in text)
        {
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth = Math.Max(0, depth - 1);
            else if (depth == 0) result.Append(c);
        }
        return result.ToString();
    }

    private static string Alphanumeric(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}
