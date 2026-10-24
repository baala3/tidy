using Tidy.Core.Catalog;
using Tidy.Core.Deletion;
using Tidy.Core.Platform;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;
using Tidy.Core.Scheduling;
using Tidy.Core.Settings;

namespace Tidy.App.Services;

/// <summary>
/// <c>Tidy.exe --background</c>, which Tidy's scheduled task starts once an hour. It never
/// opens a window: it cleans if a clean is due, warns if a drive is low, and exits.
/// </summary>
public static class BackgroundRun
{
    public static int Run()
    {
        var locations = UserLocations.Current();
        var settings = AppSettings.Load(locations.TidyData);
        if (!settings.NeedsBackgroundTask)
        {
            // Both were turned off but the task stayed, say because removing it failed; tidy up.
            ScheduledTask.Unregister();
            return 0;
        }
        // While Tidy is open the user is looking already, and a clean would pull rows out from
        // under them. The next hourly run catches up.
        if (Program.IsAppOpen()) return 0;

        var state = BackgroundState.Load(locations.TidyData);
        var now = DateTime.UtcNow;
        ScheduledCleanResult? cleaned = null;
        if (settings.AutoClean != CleanSchedule.Off)
        {
            var policy = new DeletionSafetyPolicy(locations);
            var clean = new ScheduledClean(new CacheScanner(policy, CleanupCatalog.Default), new FileDeleter(policy, new LargeFilePolicy(locations)));
            cleaned = clean.RunAsync(settings.AutoClean, state.LastCleanUtc, now).GetAwaiter().GetResult();
            if (cleaned is not null)
            {
                state.LastCleanUtc = now;
                var result = cleaned.Result;
                CleanHistory.Add(locations.TidyData, now, CleanSource.Scheduled, "Automatic clean", result.FreedBytes, cleaned.Cleaned, result.Failures.Count);
            }
        }
        var low = settings.LowSpaceAlerts ? LowSpace.DrivesToAlert(LowSpace.WatchedDrives(locations), state, now) : [];
        state.Save(locations.TidyData);

        var cleanedSomething = cleaned is { Cleaned.Count: > 0 };
        if (!cleanedSomething && low.Count == 0) return 0;
        Notifications.Register(Environment.ProcessPath!);
        if (cleanedSomething) Notifications.ShowCleaned(cleaned!);
        foreach (var disk in low) Notifications.ShowLowSpace(disk);
        return 0;
    }
}
