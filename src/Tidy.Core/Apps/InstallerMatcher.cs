using System.Text.RegularExpressions;
using Tidy.Core.Platform;

namespace Tidy.Core.Apps;

/// <summary>
/// Decides whether an installer file is for an app that is already installed. A match has
/// to be exact: an .msi's ProductCode, an .msix's package name, or the same product name
/// once words like "Setup", "x64" and version numbers are set aside. When it is unsure,
/// it says no, so an installer for something else is never offered.
/// </summary>
public static partial class InstallerMatcher
{
    /// <summary>Words that say how something is packaged, not what it is.</summary>
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "setup", "installer", "install", "installation", "bootstrapper", "stub", "web", "online", "offline", "full",
        "x64", "x86", "amd64", "arm64", "aarch64", "win", "win32", "win64", "windows", "bit", "user", "system",
        "latest", "release", "stable",
        "en", "us", "gb", "ja", "jp", "de", "fr", "zh", "cn", "tw", "ko", "kr", "ru", "nl", "pl", "pt", "br",
    };

    /// <summary>Words every company name has, so they say nothing about which company it is.</summary>
    private static readonly HashSet<string> CompanyNoise = new(StringComparer.Ordinal)
    {
        "inc", "llc", "ltd", "limited", "corp", "corporation", "co", "company", "gmbh", "ag", "sa", "bv", "oy", "ab",
        "foundation", "software", "technologies", "technology", "the", "and",
    };

    /// <summary>The installed app this installer is for, or null.</summary>
    public static InstalledApp? Match(string installerPath, InstallerIdentity? identity, IReadOnlyList<InstalledApp> apps)
    {
        // A program sitting inside an app's own folder is part of that app, not its installer.
        if (apps.Any(a => a.InstallLocation is { } folder && PathUtil.IsStrictlyUnder(installerPath, folder))) return null;

        if (identity?.ProductCode is { } code && apps.FirstOrDefault(a => a.Kind == AppKind.Desktop && KeyName(a.Id).Equals(code, StringComparison.OrdinalIgnoreCase)) is { } byCode)
        {
            return byCode;
        }
        if (identity?.PackageName is { } package)
        {
            return apps.FirstOrDefault(a => a.Kind == AppKind.Store && a.PackageFamilyName is { } family &&
                family.Split('_')[0].Equals(package, StringComparison.OrdinalIgnoreCase));
        }

        if (identity?.ProductName is { } product && apps.FirstOrDefault(a => SameProduct(product, identity.Publisher, a)) is { } byName)
        {
            return byName;
        }
        // Many installers carry no useful version resource; their file name often says it all.
        var stem = Path.GetFileNameWithoutExtension(installerPath);
        return apps.FirstOrDefault(a => SameProduct(SplitCamelCase(stem), identity?.Publisher, a));
    }

    /// <summary>Whether a product name (and its publisher, when known) names the same thing as an installed app.</summary>
    public static bool SameProduct(string product, string? publisher, InstalledApp app)
    {
        var appPublisher = Words(app.Publisher, CompanyNoise);
        var filePublisher = Words(publisher, CompanyNoise);
        // Two companies with nothing in common make two different products, whatever they're called.
        if (appPublisher.Count > 0 && filePublisher.Count > 0 && !appPublisher.Overlaps(filePublisher)) return false;

        var publisherWords = new HashSet<string>(appPublisher, StringComparer.Ordinal);
        publisherWords.UnionWith(filePublisher);
        var appName = Core(app.Name, publisherWords);
        var fileName = Core(product, publisherWords);
        return appName.Count > 0 && appName.SetEquals(fileName);
    }

    /// <summary>The words that name the product: noise, numbers and the publisher's name taken out, unless that leaves nothing.</summary>
    private static HashSet<string> Core(string name, HashSet<string> publisherWords)
    {
        var words = Words(name, Noise);
        var withoutPublisher = new HashSet<string>(words.Except(publisherWords), StringComparer.Ordinal);
        // "Discord" by Discord Inc. is named after its publisher; keep the name then.
        return withoutPublisher.Count > 0 ? withoutPublisher : words;
    }

    private static HashSet<string> Words(string? text, HashSet<string> skip) =>
        string.IsNullOrWhiteSpace(text)
            ? new HashSet<string>(StringComparer.Ordinal)
            : NonWord().Split(text.ToLowerInvariant())
                .Where(w => w.Length > 0 && !skip.Contains(w) && !VersionLike().IsMatch(w))
                .ToHashSet(StringComparer.Ordinal);

    /// <summary>"DiscordSetup" becomes "Discord Setup", "VSCodeUserSetup" becomes "VS Code User Setup".</summary>
    public static string SplitCamelCase(string text) => CamelBoundary().Replace(text, " ");

    private static string KeyName(string appId) => appId[(appId.IndexOf('\\', StringComparison.Ordinal) + 1)..];

    /// <summary>Which of two version strings is newer, comparing their numbers part by part.</summary>
    public static int CompareVersions(string a, string b)
    {
        var left = Numbers(a);
        var right = Numbers(b);
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var x = i < left.Count ? left[i] : 0;
            var y = i < right.Count ? right[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static List<long> Numbers(string version) =>
        Digits().Matches(version).Select(m => long.TryParse(m.Value, out var n) ? n : 0).Take(4).ToList();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"^v?\d+$")]
    private static partial Regex VersionLike();

    [GeneratedRegex(@"(?<=\p{Ll})(?=\p{Lu})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})|(?<=\p{L})(?=\p{N})|(?<=\p{N})(?=\p{L})")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
