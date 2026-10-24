using System.Xml.Linq;
using Tidy.Core.Catalog;
using Tidy.Core.Deletion;
using Tidy.Core.Platform;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;
using Tidy.Core.Scheduling;
using Tidy.Core.Settings;

namespace Tidy.Core.Tests;

public sealed class SchedulingTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    private const long GB = 1024L * 1024 * 1024;

    private readonly FakeProfile _profile = new();
    private readonly List<string> _recycled = [];

    public void Dispose() => _profile.Dispose();

    private ScheduledClean Clean(bool chromeOpen = false)
    {
        var policy = new DeletionSafetyPolicy(_profile.Locations);
        var scanner = new CacheScanner(policy, CleanupCatalog.Default)
        {
            IsAnyRunning = names => chromeOpen && names.Contains("chrome", StringComparer.OrdinalIgnoreCase),
        };
        var deleter = new FileDeleter(policy, new LargeFilePolicy(_profile.Locations))
        {
            RecycleOverride = path =>
            {
                _recycled.Add(path);
                return null;
            },
        };
        return new ScheduledClean(scanner, deleter);
    }

    private string Npm() => _profile.File(@"home\AppData\Local\npm-cache\_cacache\blob", 600_000);

    private string Chrome() => _profile.File(@"home\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data\data_1", 600_000);

    [Fact]
    public void WeeklyAndMonthlyCountFromTheLastClean()
    {
        var last = new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc);
        Assert.Null(ScheduledClean.DueUtc(CleanSchedule.Off, last));
        Assert.Equal(last.AddDays(7), ScheduledClean.DueUtc(CleanSchedule.Weekly, last));
        Assert.Equal(new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc), ScheduledClean.DueUtc(CleanSchedule.Monthly, last));
        Assert.Equal(DateTime.MinValue, ScheduledClean.DueUtc(CleanSchedule.Weekly, null));
    }

    [Fact]
    public async Task DoesNothingUntilDue()
    {
        Npm();
        var result = await Clean().RunAsync(CleanSchedule.Weekly, Now.AddDays(-6), Now, TestContext.Current.CancellationToken);
        Assert.Null(result);
        Assert.Empty(_recycled);
    }

    [Fact]
    public async Task CleansOnlySafeRows()
    {
        Npm();
        // NuGet's package folder is Check First: a scheduled run never takes it.
        _profile.File(@"home\.nuget\packages\newtonsoft.json\13.0.1\lib.dll", 600_000);

        var result = await Clean().RunAsync(CleanSchedule.Weekly, null, Now, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.EndsWith("npm-cache", Assert.Single(_recycled), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["npm Package Cache"], result.Cleaned);
        Assert.Equal(600_000, result.Result.FreedBytes);
    }

    [Fact]
    public async Task WaitsADayForAnOpenBrowser()
    {
        Npm();
        Chrome();
        var last = Now.AddDays(-7).AddHours(-2);

        var waiting = await Clean(chromeOpen: true).RunAsync(CleanSchedule.Weekly, last, Now, TestContext.Current.CancellationToken);
        Assert.Null(waiting);
        Assert.Empty(_recycled);

        var later = Now.AddDays(1);
        var result = await Clean(chromeOpen: true).RunAsync(CleanSchedule.Weekly, last, later, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.EndsWith("npm-cache", Assert.Single(_recycled), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CleansTheBrowserCacheOnceTheBrowserIsClosed()
    {
        Chrome();
        var result = await Clean().RunAsync(CleanSchedule.Monthly, Now.AddMonths(-2), Now, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(_recycled);
    }

    [Fact]
    public void LowMeansUnderTenPercentCappedAtTwentyGigabytes()
    {
        Assert.True(LowSpace.IsLow(new DiskUsage("C:", 100 * GB, 9 * GB)));
        Assert.False(LowSpace.IsLow(new DiskUsage("C:", 100 * GB, 11 * GB)));
        Assert.False(LowSpace.IsLow(new DiskUsage("C:", 2000 * GB, 25 * GB)));
        Assert.True(LowSpace.IsLow(new DiskUsage("C:", 2000 * GB, 19 * GB)));
        Assert.False(LowSpace.IsLow(new DiskUsage("C:", 0, 0)));
    }

    [Fact]
    public void AlertsEachDriveAtMostOnceADay()
    {
        var state = new BackgroundState();
        var drives = new[] { new DiskUsage("C:", 100 * GB, 5 * GB), new DiskUsage("D:", 100 * GB, 50 * GB) };

        Assert.Equal("C:", Assert.Single(LowSpace.DrivesToAlert(drives, state, Now)).Drive);
        Assert.Empty(LowSpace.DrivesToAlert(drives, state, Now.AddHours(23)));
        Assert.Single(LowSpace.DrivesToAlert(drives, state, Now.AddHours(25)));
    }

    [Fact]
    public void BackgroundStateSurvivesARoundTrip()
    {
        var folder = _profile.Locations.TidyData;
        var state = new BackgroundState { LastCleanUtc = Now };
        state.LowSpaceAlertUtc["C:"] = Now;
        state.Save(folder);

        var loaded = BackgroundState.Load(folder);
        Assert.Equal(Now, loaded.LastCleanUtc);
        Assert.Equal(Now, loaded.LowSpaceAlertUtc["C:"]);
    }

    [Fact]
    public void HistoryKeepsTheNewestFirstAndSkipsEmptyCleans()
    {
        var folder = _profile.Locations.TidyData;
        CleanHistory.Add(folder, Now.AddDays(-1), CleanSource.Manual, "Large Files", 5000, ["movie.mkv"], 0);
        CleanHistory.Add(folder, Now, CleanSource.Scheduled, "Automatic clean", 600_000, ["npm Cache"], 1);
        CleanHistory.Add(folder, Now, CleanSource.Manual, "App Caches", 0, [], 2);

        var history = CleanHistory.Load(folder);
        Assert.Equal(2, history.Count);
        Assert.Equal(CleanSource.Scheduled, history[0].Source);
        Assert.Equal(["npm Cache"], history[0].Items);
        Assert.Equal(1, history[0].Failed);
        Assert.Equal("Large Files", history[1].What);
    }

    [Fact]
    public void HistoryTrimsLongListsButKeepsTheCount()
    {
        var folder = _profile.Locations.TidyData;
        var files = Enumerable.Range(0, 250).Select(i => $"file{i}.bin").ToList();
        CleanHistory.Add(folder, Now, CleanSource.Manual, "Duplicates", 1, files, 0);

        var entry = Assert.Single(CleanHistory.Load(folder));
        Assert.Equal(CleanHistory.MaxItems, entry.Items.Count);
        Assert.Equal(250, entry.ItemCount);
    }

    [Fact]
    public void ABrokenHistoryFileReadsAsEmpty()
    {
        Directory.CreateDirectory(_profile.Locations.TidyData);
        File.WriteAllText(Path.Combine(_profile.Locations.TidyData, "history.json"), "{ not json");
        Assert.Empty(CleanHistory.Load(_profile.Locations.TidyData));
    }

    [Fact]
    public void TaskRunsTidyHourlyAsTheUserWithoutAdminRights()
    {
        var xml = XDocument.Parse(ScheduledTask.Xml(@"C:\Users\A & B\Tidy\Tidy.exe", "S-1-5-21-1", new DateTime(2026, 10, 24)));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal("\"C:\\Users\\A & B\\Tidy\\Tidy.exe\"", xml.Descendants(ns + "Command").Single().Value);
        Assert.Equal(ScheduledTask.Argument, xml.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("PT1H", xml.Descendants(ns + "Interval").Single().Value);
        Assert.Equal("2026-10-24T00:00:00", xml.Descendants(ns + "StartBoundary").Single().Value);
        Assert.Equal("S-1-5-21-1", xml.Descendants(ns + "UserId").Single().Value);
        Assert.Equal("InteractiveToken", xml.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal("LeastPrivilege", xml.Descendants(ns + "RunLevel").Single().Value);
    }

    [Fact]
    public void SettingsOnlyNeedTheTaskWhenSomethingIsOn()
    {
        Assert.False(new AppSettings().NeedsBackgroundTask);
        Assert.True(new AppSettings { AutoClean = CleanSchedule.Monthly }.NeedsBackgroundTask);
        Assert.True(new AppSettings { LowSpaceAlerts = true }.NeedsBackgroundTask);
    }
}
