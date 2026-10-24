using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Tidy.Core.Settings;
using Tidy.Core.Updates;

namespace Tidy.App.Services;

public enum UpdateStatus
{
    NotChecked,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Failed,
}

/// <summary>
/// Whether a newer Tidy is out, and installing it. Checks when the app opens and once a day
/// while it stays open, unless automatic checks are off in Settings.
/// </summary>
public sealed partial class UpdateState : ObservableObject
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly AppSettings _settings;
    private readonly Updater _updater;
    private readonly DispatcherQueueTimer _timer;

    [ObservableProperty] public partial UpdateStatus Status { get; private set; }
    [ObservableProperty] public partial AppRelease? Release { get; private set; }
    [ObservableProperty] public partial double Progress { get; private set; }
    [ObservableProperty] public partial string Problem { get; private set; } = "";
    [ObservableProperty] public partial DateTime? CheckedUtc { get; private set; }

    public UpdateState(DispatcherQueue ui, AppSettings settings)
    {
        _settings = settings;
        _updater = new Updater(Http, Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        IsInstalledCopy = Updater.IsInstalledCopy(AppContext.BaseDirectory);
        _timer = ui.CreateTimer();
        _timer.Interval = CheckInterval;
        _timer.Tick += (_, _) => _ = CheckAsync();
    }

    public Version Current => _updater.Current;

    /// <summary>Installed with the installer, so an update can install itself; a portable copy downloads the zip.</summary>
    public bool IsInstalledCopy { get; }

    public bool IsBusy => Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Installing;

    /// <summary>Called once the window is up.</summary>
    public void Start()
    {
        Updater.CleanUpDownloads();
        ApplySchedule();
    }

    /// <summary>Starts or stops the daily check to match the setting, checking right away when it is turned on.</summary>
    public void ApplySchedule()
    {
        if (_settings.CheckForUpdates)
        {
            _timer.Start();
            _ = CheckAsync();
        }
        else
        {
            _timer.Stop();
        }
    }

    public async Task CheckAsync()
    {
        if (IsBusy) return;
        Problem = "";
        Status = UpdateStatus.Checking;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Release = await _updater.CheckAsync(timeout.Token);
            Status = Release is null ? UpdateStatus.UpToDate : UpdateStatus.Available;
            CheckedUtc = DateTime.UtcNow;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            Fail("Couldn't reach GitHub. Check your connection and try again.");
        }
    }

    /// <summary>Downloads the installer, checks it and runs it. Tidy closes, and the installer reopens it.</summary>
    public async Task InstallAsync()
    {
        if (Release is not { } release || IsBusy) return;
        Problem = "";
        Progress = 0;
        Status = UpdateStatus.Downloading;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var installer = await _updater.DownloadAsync(release, new Progress<double>(p => Progress = p), timeout.Token);
            Status = UpdateStatus.Installing;
            Updater.Install(installer);
            Application.Current.Exit();
        }
        catch (InvalidDataException e)
        {
            Fail(e.Message + " Nothing was installed.");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            Fail("The download didn't finish. Nothing was installed.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Fail("Windows didn't start the installer. Nothing was installed.");
        }
    }

    partial void OnStatusChanged(UpdateStatus value) => OnPropertyChanged(nameof(IsBusy));

    private void Fail(string problem)
    {
        Problem = problem;
        // A release that is already known stays on offer, with the problem under it.
        Status = Release is null ? UpdateStatus.Failed : UpdateStatus.Available;
    }
}
