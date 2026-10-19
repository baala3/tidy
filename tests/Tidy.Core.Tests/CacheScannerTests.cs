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

    private const string InfoPlist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <plist version="1.0">
        <dict>
            <key>Build Version</key>
            <string>22D68</string>
            <key>Device Name</key>
            <string>Sam's iPhone</string>
            <key>Last Backup Date</key>
            <date>2025-03-03T10:15:00Z</date>
        </dict>
        </plist>
        """;

    [Fact]
    public void EachDeviceBackupIsItsOwnCheckFirstRow()
    {
        _profile.File(@"home\AppData\Roaming\Apple Computer\MobileSync\Backup\00008110-001A\ab\abcdef", 900_000);
        File.WriteAllText(_profile.P(@"home\AppData\Roaming\Apple Computer\MobileSync\Backup\00008110-001A\Info.plist"), InfoPlist);
        _profile.File(@"home\Apple\MobileSync\Backup\00008030-002B\cd\cdef01", 700_000);

        var backups = Scan().Where(i => i.IsDeviceBackup).OrderBy(i => i.Title, StringComparer.Ordinal).ToList();

        Assert.Equal(["Sam's iPhone Backup", "iPhone or iPad Backup"], backups.Select(b => b.Title));
        Assert.All(backups, b => Assert.Equal(SafetyLevel.CheckFirst, b.Safety));
        Assert.Equal(new DateTime(2025, 3, 3, 10, 15, 0, DateTimeKind.Utc), backups[0].LastModifiedUtc);
        Assert.Contains("Sam's iPhone", backups[0].Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceBackupsWaitForAppleDevicesToClose()
    {
        _profile.File(@"home\Apple\MobileSync\Backup\00008030-002B\cd\cdef01", 700_000);
        var backup = Assert.Single(Scan(running: true), i => i.IsDeviceBackup);
        Assert.Equal("Close Apple Devices or iTunes to clean this", backup.BlockedReason);
    }

    [Fact]
    public void TelegramMediaCacheIsFoundButItsSignInIsNot()
    {
        _profile.File(@"home\AppData\Roaming\Telegram Desktop\tdata\user_data\media_cache\0\blob", 900_000);
        _profile.File(@"home\AppData\Roaming\Telegram Desktop\tdata\key_datas", 900_000);
        var telegram = Assert.Single(Scan(), i => i.Id == "telegram-media-cache");
        Assert.EndsWith(@"user_data\media_cache", Assert.Single(telegram.Paths), StringComparison.Ordinal);
    }
}
