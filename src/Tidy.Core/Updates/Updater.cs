using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Tidy.Core.Updates;

/// <summary>
/// A newer Tidy on GitHub Releases, with the installer and portable zip for this PC's
/// architecture. <see cref="Zip"/> is null when the release has no zip.
/// </summary>
public sealed record AppRelease(Version Version, Uri Page, Uri Installer, Uri Checksums, Uri? Zip)
{
    public string InstallerName => Path.GetFileName(Installer.AbsolutePath);
}

/// <summary>
/// Asks GitHub for Tidy's latest release and installs it. The request carries nothing about
/// the PC beyond what any HTTPS request does. Only installers from Tidy's own releases are
/// downloaded, and each one must match the release's SHA256SUMS.txt before it runs.
/// </summary>
public sealed class Updater(HttpClient http, Version current)
{
    public const string Repository = "baala3/tidy";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F3B1D2E-8C4A-4E7B-9A1F-5D2C7E9B3A10}_is1";
    private const string TempPrefix = "tidy-update-";
    private static readonly Uri LatestRelease = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private static readonly string DownloadPrefix = $"/{Repository}/releases/download/";
    private static readonly char[] Whitespace = [' ', '\t'];

    /// <summary>The version that is running, as three parts.</summary>
    public Version Current { get; } = Normalize(current);

    public static string Architecture => RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

    /// <summary>The newest release, or null when it is no newer than the running version.</summary>
    public async Task<AppRelease?> CheckAsync(CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        AddUserAgent(request);
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        return Parse(json, Architecture) is { } release && release.Version > Current ? release : null;
    }

    /// <summary>
    /// Downloads the installer into a new temp folder and checks it against the release's
    /// SHA-256. Returns its path; a file that doesn't match is deleted and never returned.
    /// </summary>
    public async Task<string> DownloadAsync(AppRelease release, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var sums = await GetStringAsync(release.Checksums, cancellation).ConfigureAwait(false);
        var expected = ExpectedHash(sums, release.InstallerName)
            ?? throw new InvalidDataException($"The release doesn't list a checksum for {release.InstallerName}.");

        var folder = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, release.InstallerName);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.Installer);
            AddUserAgent(request);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var target = File.Create(file))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total.Value);
                }
            }
            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The download doesn't match the release's published SHA-256.");
            }
            return file;
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    /// <summary>
    /// Runs the installer silently. It closes Tidy if it is still open, and opens the new
    /// version when it is done.
    /// </summary>
    public static void Install(string installer) =>
        Process.Start(new ProcessStartInfo(installer)
        {
            ArgumentList = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/RELAUNCH=1" },
            UseShellExecute = false,
        })?.Dispose();

    /// <summary>
    /// True when this copy of Tidy is the one the installer put in place, so the installer can
    /// update it. A portable copy from the zip is updated by downloading the new zip instead.
    /// </summary>
    public static bool IsInstalledCopy(string appFolder)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue("InstallLocation") is string location && location.Length > 0
            && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(appFolder)), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes installers left by earlier updates. They can't be removed while they run.</summary>
    public static void CleanUpDownloads()
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(Path.GetTempPath(), TempPrefix + "*")) TryDelete(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Reads GitHub's release JSON. Null for a draft or prerelease, a tag that isn't a version,
    /// or a release without this architecture's installer and checksums from Tidy's own repo.
    /// </summary>
    internal static AppRelease? Parse(string json, string architecture)
    {
        GitHubRelease? release;
        try
        {
            release = JsonSerializer.Deserialize(json, CoreJsonContext.Default.GitHubRelease);
        }
        catch (JsonException)
        {
            return null;
        }
        if (release is null || release.Draft || release.Prerelease || ParseVersion(release.TagName) is not { } version) return null;
        if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps) return null;
        var installer = Asset(release, $"Tidy-Setup-{architecture}.exe");
        var checksums = Asset(release, "SHA256SUMS.txt");
        return installer is null || checksums is null ? null
            : new AppRelease(version, page, installer, checksums, Asset(release, $"Tidy-win-{architecture}.zip"));
    }

    /// <summary>"v1.2.3" or "1.2.3" as a three-part version; null for anything else.</summary>
    internal static Version? ParseVersion(string? tag)
    {
        var text = tag?.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) && version.Revision <= 0 && text!.Count(c => c == '.') == 2 ? Normalize(version) : null;
    }

    /// <summary>The hash for <paramref name="file"/> in a <c>sha256sum</c>-style list.</summary>
    internal static string? ExpectedHash(string sums, string file)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split(Whitespace, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Length == 64 && parts[0].All(char.IsAsciiHexDigit)
                && string.Equals(parts[1].Trim().TrimStart('*'), file, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0];
            }
        }
        return null;
    }

    private static Uri? Asset(GitHubRelease release, string name)
    {
        var url = release.Assets?.FirstOrDefault(a => a.Name == name)?.BrowserDownloadUrl;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com"
            && uri.AbsolutePath.StartsWith(DownloadPrefix, StringComparison.Ordinal) && uri.AbsolutePath.EndsWith("/" + name, StringComparison.Ordinal)
            ? uri : null;
    }

    private async Task<string> GetStringAsync(Uri uri, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddUserAgent(request);
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
    }

    // GitHub turns away requests without a User-Agent.
    private void AddUserAgent(HttpRequestMessage request) =>
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Tidy", Current.ToString(3)));

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
}
