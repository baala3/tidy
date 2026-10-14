using Tidy.Core.Catalog;
using Tidy.Core.Models;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;

namespace Tidy.Core.Tests;

public sealed class CacheScannerTests : IDisposable
{
    private readonly FakeProfile _profile = new();

    public void Dispose() => _profile.Dispose();

    private List<ScanItem> Scan(bool running = false) =>
        new CacheScanner(new DeletionSafetyPolicy(_profile.Locations), CleanupCatalog.Default)
        {
            IsAnyRunning = _ => running,
        }.Scan(TestContext.Current.CancellationToken).ToList();

    [Fact]
    public void FindsCatalogCachesAcrossProfiles()
    {
        _profile.File(@"home\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data\data_1", 600_000);
        _profile.File(@"home\AppData\Local\Google\Chrome\User Data\Profile 2\Cache\Cache_Data\data_1", 300_000);
        var chrome = Assert.Single(Scan(), i => i.Id == "chrome-cache");
        Assert.Equal(2, chrome.Paths.Count);
        Assert.Equal(900_000, chrome.SizeBytes);
        Assert.Null(chrome.BlockedReason);
    }

    [Fact]
    public void BrowserCacheIsBlockedWhileTheBrowserRuns()
    {
        _profile.File(@"home\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data\data_1", 600_000);
        var chrome = Assert.Single(Scan(running: true), i => i.Id == "chrome-cache");
        Assert.Equal("Close Google Chrome to clean this", chrome.BlockedReason);
    }

    [Fact]
    public void TempOffersOnlyOldChildren()
    {
        _profile.File(@"home\AppData\Local\Temp\old.tmp", 600_000, DateTime.UtcNow.AddDays(-3));
        _profile.File(@"home\AppData\Local\Temp\fresh.tmp", 600_000);
        var temp = Assert.Single(Scan(), i => i.Id == "windows-temp");
        Assert.EndsWith("old.tmp", Assert.Single(temp.Paths), StringComparison.Ordinal);
    }

    [Fact]
    public void ElectronAppCacheGetsAFriendlyName()
    {
        _profile.File(@"home\AppData\Roaming\discord\Cache\Cache_Data\index", 300_000);
        _profile.File(@"home\AppData\Roaming\discord\Code Cache\js\index", 300_000);
        var discord = Assert.Single(Scan(), i => i.Id == "chromium:discord");
        Assert.Equal("Discord Cache", discord.Title);
        Assert.Equal(2, discord.Paths.Count);
        Assert.Equal(ScanCategory.AppCaches, discord.Category);
    }

    [Fact]
    public void TinyCachesAreLeftOut()
    {
        _profile.File(@"home\AppData\Local\npm-cache\_logs\debug.log", 2_000);
        Assert.DoesNotContain(Scan(), i => i.Id == "npm-cache");
    }

    [Fact]
    public void AppCoveredByTheCatalogNeverGetsASecondGenericRow()
    {
        _profile.File(@"home\AppData\Roaming\Cursor\Cache\Cache_Data\index", 600_000);
        _profile.File(@"home\AppData\Roaming\Cursor\SomeNewCache\index", 600_000);
        _profile.File(@"home\AppData\Roaming\Cursor\GPUCache\index", 600_000);
        _profile.File(@"home\AppData\Roaming\Cursor\GPUCache\data_0", 1);
        var items = Scan();
        Assert.Single(items, i => i.Title.StartsWith("Cursor", StringComparison.Ordinal));
    }

    [Fact]
    public void VsCodeIsClaimedByItsCatalogEntryNotTheGenericRule()
    {
        _profile.File(@"home\AppData\Roaming\Code\Cache\Cache_Data\index", 600_000);
        var items = Scan();
        Assert.Contains(items, i => i.Id == "vscode-cache" && i.Category == ScanCategory.DevTools);
        Assert.DoesNotContain(items, i => i.Id == "chromium:code");
    }
}
