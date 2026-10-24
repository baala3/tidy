using System.Net;
using System.Security.Cryptography;
using System.Text;
using Tidy.Core.Updates;

namespace Tidy.Core.Tests;

public sealed class UpdaterTests
{
    private const string Download = "https://github.com/baala3/tidy/releases/download/v0.3.0/";

    private static string ReleaseJson(string tag = "v0.3.0", bool draft = false, bool prerelease = false, string downloads = Download) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/baala3/tidy/releases/tag/{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "Tidy-Setup-x64.exe", "browser_download_url": "{{downloads}}Tidy-Setup-x64.exe" },
            { "name": "Tidy-Setup-arm64.exe", "browser_download_url": "{{downloads}}Tidy-Setup-arm64.exe" },
            { "name": "Tidy-win-x64.zip", "browser_download_url": "{{downloads}}Tidy-win-x64.zip" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "{{downloads}}SHA256SUMS.txt" }
          ]
        }
        """;

    [Fact]
    public void ReadsTheInstallerForThisArchitecture()
    {
        var release = Updater.Parse(ReleaseJson(), "arm64");
        Assert.NotNull(release);
        Assert.Equal(new Version(0, 3, 0), release.Version);
        Assert.Equal(Download + "Tidy-Setup-arm64.exe", release.Installer.ToString());
        Assert.Equal(Download + "SHA256SUMS.txt", release.Checksums.ToString());
        Assert.Equal("https://github.com/baala3/tidy/releases/tag/v0.3.0", release.Page.ToString());
        Assert.Null(release.Zip);
        Assert.Equal(Download + "Tidy-win-x64.zip", Updater.Parse(ReleaseJson(), "x64")!.Zip!.ToString());
    }

    [Theory]
    [InlineData("https://example.com/baala3/tidy/releases/download/v0.3.0/")]
    [InlineData("http://github.com/baala3/tidy/releases/download/v0.3.0/")]
    [InlineData("https://github.com/someone-else/tidy/releases/download/v0.3.0/")]
    [InlineData("https://github.com/baala3/tidy/raw/main/")]
    public void RefusesInstallersFromAnywhereElse(string downloads) => Assert.Null(Updater.Parse(ReleaseJson(downloads: downloads), "x64"));

    [Fact]
    public void SkipsDraftsAndPrereleases()
    {
        Assert.Null(Updater.Parse(ReleaseJson(draft: true), "x64"));
        Assert.Null(Updater.Parse(ReleaseJson(prerelease: true), "x64"));
    }

    [Fact]
    public void SkipsAReleaseWithoutTheInstaller() =>
        Assert.Null(Updater.Parse(ReleaseJson().Replace("Tidy-Setup-x64.exe", "Tidy-Setup-x86.exe", StringComparison.Ordinal), "x64"));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "message": "API rate limit exceeded" }""")]
    public void IgnoresAnythingElse(string json) => Assert.Null(Updater.Parse(json, "x64"));

    [Theory]
    [InlineData("v0.3.0", "0.3.0")]
    [InlineData("1.12.4", "1.12.4")]
    [InlineData(" V2.0.1 ", "2.0.1")]
    [InlineData("v1.0", null)]
    [InlineData("v1.0.0.0", null)]
    [InlineData("v1.0.0-beta", null)]
    [InlineData("nightly", null)]
    [InlineData(null, null)]
    public void ReadsVersionTags(string? tag, string? expected) => Assert.Equal(expected, Updater.ParseVersion(tag)?.ToString());

    [Fact]
    public void FindsTheHashForAFile()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = $"{a}  Tidy-Setup-arm64.exe\r\n{b} *Tidy-Setup-x64.exe\r\nnot-a-hash  Tidy-win-x64.zip\n";
        Assert.Equal(a, Updater.ExpectedHash(sums, "Tidy-Setup-arm64.exe"));
        Assert.Equal(b, Updater.ExpectedHash(sums, "tidy-setup-x64.exe"));
        Assert.Null(Updater.ExpectedHash(sums, "Tidy-win-x64.zip"));
        Assert.Null(Updater.ExpectedHash(sums, "Tidy-Setup.exe"));
    }

    [Fact]
    public async Task OffersOnlyANewerVersion()
    {
        var json = ReleaseJson();
        Assert.NotNull(await Serving(new Version(0, 2, 0), json).CheckAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(await Serving(new Version(0, 2, 9, 0), json).CheckAsync(TestContext.Current.CancellationToken));
        Assert.Null(await Serving(new Version(0, 3, 0, 0), json).CheckAsync(TestContext.Current.CancellationToken));
        Assert.Null(await Serving(new Version(0, 4, 0), json).CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChecksAsTidyWithAUserAgent()
    {
        HttpRequestMessage? sent = null;
        await new Updater(new HttpClient(new FakeServer(r => { sent = r; return Ok(ReleaseJson()); })), new Version(0, 2, 0)).CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal("https://api.github.com/repos/baala3/tidy/releases/latest", sent!.RequestUri!.ToString());
        Assert.Equal("Tidy/0.2.0", sent.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task DownloadsAnInstallerThatMatchesItsHash()
    {
        var installer = Encoding.UTF8.GetBytes("pretend installer");
        var release = Release();
        var progress = new List<double>();
        var file = await Server(installer, Hash(installer)).DownloadAsync(release, new SyncProgress(progress.Add), TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal("Tidy-Setup-x64.exe", Path.GetFileName(file));
            Assert.Equal(installer, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
            Assert.Equal(1.0, progress[^1]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [Fact]
    public async Task DeletesAnInstallerThatDoesNotMatch()
    {
        var before = TempDownloads();
        var updater = Server(Encoding.UTF8.GetBytes("tampered"), Hash(Encoding.UTF8.GetBytes("original")));
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.DownloadAsync(Release(), cancellation: TestContext.Current.CancellationToken));
        Assert.Equal(before, TempDownloads());
    }

    [Fact]
    public async Task RefusesWhenTheChecksumsLeaveTheInstallerOut()
    {
        var updater = new Updater(new HttpClient(new FakeServer(_ => Ok(new string('a', 64) + "  Tidy-Setup-arm64.exe\n"))), new Version(0, 2, 0));
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.DownloadAsync(Release(), cancellation: TestContext.Current.CancellationToken));
    }

    private static AppRelease Release() => Updater.Parse(ReleaseJson(), "x64")!;

    private static Updater Serving(Version current, string json) => new(new HttpClient(new FakeServer(_ => Ok(json))), current);

    private static Updater Server(byte[] installer, string hash) => new(new HttpClient(new FakeServer(request =>
        request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? Ok($"{hash}  Tidy-Setup-x64.exe\n{new string('0', 64)}  Tidy-Setup-arm64.exe\n")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) })), new Version(0, 2, 0));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string[] TempDownloads() => Directory.GetDirectories(Path.GetTempPath(), "tidy-update-*").Order().ToArray();

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    // Progress<T> posts to the thread pool; this reports in order, before DownloadAsync returns.
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
