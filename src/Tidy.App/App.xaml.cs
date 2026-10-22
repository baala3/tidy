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

    public static ViewModels.DuplicatesViewModel Duplicates => _duplicates ??= new(State);

    public static ViewModels.InstallersViewModel Installers => _installers ??= new(State);

    public static ViewModels.OneDriveViewModel OneDrive => _oneDrive ??= new(State);

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
        _window.Activate();
        // Like the Mac app: open and it scans everything, one category at a time.
        _ = State.ScanEverythingAsync();
    }
}
