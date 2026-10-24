using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Tidy.App.Services;

namespace Tidy.App;

public partial class App : Application
{
    private MainWindow? _window;

    public static AppState State { get; private set; } = null!;
    public static MainWindow Window => ((App)Current)._window!;

    // One per tab and kept for the app's lifetime, so filters and scroll survive navigation.
    private static ViewModels.CleanListViewModel? _caches;
    private static ViewModels.CleanListViewModel? _dev;
    private static ViewModels.LargeFilesViewModel? _large;

    public static ViewModels.CleanListViewModel CleanList(Core.Models.ScanCategory category) => category switch
    {
        Core.Models.ScanCategory.AppCaches => _caches ??= new(State, category),
        Core.Models.ScanCategory.DevTools => _dev ??= new(State, category),
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static ViewModels.LargeFilesViewModel LargeFiles => _large ??= new(State);

    private static ViewModels.DuplicatesViewModel? _duplicates;
    private static ViewModels.InstallersViewModel? _installers;
    private static ViewModels.OneDriveViewModel? _oneDrive;
    private static ViewModels.VirtualDisksViewModel? _virtualDisks;
    private static ViewModels.WindowsViewModel? _windows;

    public static ViewModels.DuplicatesViewModel Duplicates => _duplicates ??= new(State);

    public static ViewModels.InstallersViewModel Installers => _installers ??= new(State);

    public static ViewModels.OneDriveViewModel OneDrive => _oneDrive ??= new(State);

    public static ViewModels.VirtualDisksViewModel VirtualDisks => _virtualDisks ??= new(State);

    public static ViewModels.WindowsViewModel Windows => _windows ??= new(State);

    private static ViewModels.AppsViewModel? _apps;

    public static ViewModels.AppsViewModel Apps => _apps ??= new(State);

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        State = new AppState(DispatcherQueue.GetForCurrentThread());
        _window = new MainWindow();
        // Clicks on Tidy's notifications come here, including the one Windows started Tidy for.
        ToastActivator.Clicked += OnNotificationClicked;
        ToastActivator.Start();
        _window.Closed += (_, _) => ToastActivator.Stop();
        _window.Activate();
        // Like the Mac app: open and it scans everything, one category at a time.
        _ = State.ScanEverythingAsync();
        // Points the task and notifications at this copy of Tidy, in case the app moved.
        if (State.Settings.NeedsBackgroundTask)
        {
            _ = State.ApplyBackgroundTaskAsync();
            _ = Task.Run(() => Notifications.Register(Environment.ProcessPath!));
        }
    }

    private void OnNotificationClicked(string arguments)
    {
        var page = Notifications.PageOf(arguments);
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (page is not null) Window.NavigateTo(page);
            Window.BringToFront();
        });
    }
}
