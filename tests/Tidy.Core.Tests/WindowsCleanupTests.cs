using System.Text.RegularExpressions;
using Microsoft.Win32;
using Tidy.Core.Catalog;
using Tidy.Core.WindowsCleanup;

namespace Tidy.Core.Tests;

public sealed partial class WindowsCleanupTests : IDisposable
{
    private readonly Func<string, Task<ElevatedSession.Launched>> _launch = ElevatedSession.Launch;

    public void Dispose() => ElevatedSession.Launch = _launch;

    private sealed class FakeHandlers : ICleanupHandlers
    {
        public List<string> Purged { get; } = [];

        public long? Measure(string key, string volume) => key.Length * 1000L;

        public string? Purge(string key, string volume, Action<long> freedSoFar, out long freed)
        {
            Purged.Add(key);
            freedSoFar(500);
            freed = key.Length * 1000L;
            return null;
        }
    }

    /// <summary>Runs the helper on a thread of this process instead of elevating, optionally tampering with its arguments.</summary>
    private static void RunHelperInProcess(ICleanupHandlers handlers, Func<List<string>, List<string>>? tamper = null) =>
        ElevatedSession.Launch = arguments =>
        {
            var args = Arguments().Matches(arguments).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Value).ToList();
            Assert.Equal(ElevatedHelper.Argument, args[0]);
            args = (tamper ?? (a => a))(args.Skip(1).ToList());
            var exited = Task.Run(() => ElevatedHelper.Run(args, handlers));
            return Task.FromResult(new ElevatedSession.Launched(null, exited));
        };

    [GeneratedRegex("\"([^\"]*)\"|\\S+")]
    private static partial Regex Arguments();

    [Fact]
    public async Task ChecksSizesThroughTheHelper()
    {
        RunHelperInProcess(new FakeHandlers());
        var messages = new List<ElevatedMessage>();

        var outcome = await ElevatedSession.RunAsync(ElevatedHelper.Measure, ["Update Cleanup", "Delivery Optimization Files"], messages.Add, TestContext.Current.CancellationToken);

        Assert.True(outcome.Started);
        Assert.Equal(
        [
            new ElevatedMessage(ElevatedMessageType.Working, "Update Cleanup"),
            new ElevatedMessage(ElevatedMessageType.Size, "Update Cleanup", 14_000),
            new ElevatedMessage(ElevatedMessageType.Working, "Delivery Optimization Files"),
            new ElevatedMessage(ElevatedMessageType.Size, "Delivery Optimization Files", 27_000),
        ], messages);
    }

    [Fact]
    public async Task CleansThroughTheHelper()
    {
        var handlers = new FakeHandlers();
        RunHelperInProcess(handlers);
        var messages = new List<ElevatedMessage>();

        await ElevatedSession.RunAsync(ElevatedHelper.Clean, ["Setup Log Files"], messages.Add, TestContext.Current.CancellationToken);

        Assert.Equal(["Setup Log Files"], handlers.Purged);
        Assert.Contains(new ElevatedMessage(ElevatedMessageType.Progress, "Setup Log Files", 500), messages);
        Assert.Equal(new ElevatedMessage(ElevatedMessageType.Done, "Setup Log Files", 15_000), messages[^1]);
    }

    /// <summary>The helper runs whatever it's told with administrator rights, so it only takes catalog keys.</summary>
    [Fact]
    public async Task TheHelperRefusesAnythingNotInTheCatalog()
    {
        var handlers = new FakeHandlers();
        RunHelperInProcess(handlers);

        var outcome = await ElevatedSession.RunAsync(ElevatedHelper.Clean, ["DownloadsFolder"], _ => { }, TestContext.Current.CancellationToken);

        Assert.False(outcome.Started);
        Assert.Empty(handlers.Purged);
    }

    /// <summary>A helper pointed at a pipe some other process owns must not report to it.</summary>
    [Fact]
    public async Task TheHelperOnlyTalksToTheAppThatStartedIt()
    {
        var handlers = new FakeHandlers();
        RunHelperInProcess(handlers, args =>
        {
            args[1] = (Environment.ProcessId + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return args;
        });
        var messages = new List<ElevatedMessage>();

        await ElevatedSession.RunAsync(ElevatedHelper.Clean, ["Setup Log Files"], messages.Add, TestContext.Current.CancellationToken);

        Assert.Empty(messages);
        Assert.Empty(handlers.Purged);
    }

    [Fact]
    public void EveryEntryNamesADiskCleanupHandlerAndATier()
    {
        var keys = CleanupCatalog.Default.WindowsCleanup.Select(e => e.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.DoesNotContain("DownloadsFolder", keys);
        Assert.All(CleanupCatalog.Default.WindowsCleanup, e => Assert.True(Enum.IsDefined(e.Safety)));
    }

    /// <summary>Delivery Optimization reports its size to anyone, so the real handler can be checked without elevating.</summary>
    [Fact]
    public void MeasuresARealWindowsHandler()
    {
        const string key = "Delivery Optimization Files";
        using var registered = Registry.LocalMachine.OpenSubKey($@"{DiskCleanupHandlers.VolumeCachesKey}\{key}");
        Assert.SkipWhen(registered is null, "This Windows has no Delivery Optimization handler.");

        Assert.NotNull(new DiskCleanupHandlers().Measure(key, Path.GetPathRoot(Environment.SystemDirectory)!));
    }
}
