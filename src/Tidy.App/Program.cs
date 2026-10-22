using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Tidy.Core.WindowsCleanup;

namespace Tidy.App;

/// <summary>
/// Tidy's entry point. Started with <c>--elevated</c>, it is the administrator helper for the
/// Windows page and never opens a window; otherwise it starts the app as XAML would.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == ElevatedHelper.Argument) return ElevatedHelper.Run(args[1..], new DiskCleanupHandlers());

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(start =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }
}
