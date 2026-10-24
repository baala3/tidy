using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Tidy.App.Services;
using Tidy.Core.Scheduling;
using Tidy.Core.WindowsCleanup;

namespace Tidy.App;

/// <summary>
/// Tidy's entry point. Started with <c>--elevated</c>, it is the administrator helper for the
/// Windows page; with <c>--background</c>, the hourly run from Tidy's scheduled task; with
/// <c>--uninstall</c>, the uninstaller's cleanup. None of those opens a window. Otherwise it
/// starts the app as XAML would, including when Windows starts it to deliver a notification click.
/// </summary>
public static class Program
{
    public const string UninstallArgument = "--uninstall";

    /// <summary>Held while Tidy's window is open, so the background run can tell.</summary>
    private const string OpenMutexName = @"Local\Tidy.App";

    private static Mutex? _open;

    public static bool IsAppOpen()
    {
        if (!Mutex.TryOpenExisting(OpenMutexName, out var open)) return false;
        open.Dispose();
        return true;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : null;
        if (mode == ElevatedHelper.Argument) return ElevatedHelper.Run(args[1..], new DiskCleanupHandlers());

        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (mode == ScheduledTask.Argument) return BackgroundRun.Run();
        if (mode == UninstallArgument)
        {
            ScheduledTask.Unregister();
            Notifications.Unregister();
            return 0;
        }

        _open = new Mutex(false, OpenMutexName);
        Application.Start(start =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }
}
