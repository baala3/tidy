using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tidy.App.Services;

namespace Tidy.App.Views;

public sealed partial class AboutPage : Page
{
    private readonly UpdateState _updates = App.Updates;

    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"Version {_updates.Current.ToString(3)} for Windows";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _updates.PropertyChanged += OnUpdatesChanged;
        Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => _updates.PropertyChanged -= OnUpdatesChanged;

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        var release = _updates.Release;
        var version = release?.Version.ToString(3);
        var status = _updates.Status;
        (UpdateTitle.Text, UpdateDetail.Text) = status switch
        {
            UpdateStatus.NotChecked => ("Updates", App.State.Settings.CheckForUpdates ? "Not checked yet" : "Automatic checks are off in Settings"),
            UpdateStatus.Checking => ("Checking for updates…", "Asking GitHub for the latest release"),
            UpdateStatus.UpToDate => ("Tidy is up to date", $"Checked {Format.RelativeTime(_updates.CheckedUtc!.Value, DateTime.UtcNow)}"),
            UpdateStatus.Available => ($"Tidy {version} is available",
                _updates.Problem.Length > 0 ? _updates.Problem
                : _updates.IsInstalledCopy ? "Tidy closes while it installs, then opens again. Your settings stay."
                : "This copy of Tidy came from the zip. Download the new zip and replace this folder with it."),
            UpdateStatus.Downloading => ($"Downloading Tidy {version}…", $"{_updates.Progress:P0}"),
            UpdateStatus.Installing => ($"Installing Tidy {version}…", "Tidy closes now and opens again when it's done."),
            UpdateStatus.Failed => ("Couldn't check for updates", _updates.Problem),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        var offering = release is not null;
        UpdateRing.IsActive = _updates.IsBusy;
        UpdateIcon.Visibility = _updates.IsBusy ? Visibility.Collapsed : Visibility.Visible;
        UpdateIcon.Glyph = offering ? "" : "";
        DownloadBar.Visibility = status == UpdateStatus.Downloading ? Visibility.Visible : Visibility.Collapsed;
        DownloadBar.Value = _updates.Progress;
        NotesButton.Visibility = offering ? Visibility.Visible : Visibility.Collapsed;
        UpdateAction.Content = !offering ? (status == UpdateStatus.UpToDate ? "Check Again" : "Check Now")
            : _updates.IsInstalledCopy ? "Install Update" : "Download";
        UpdateAction.Style = (Style)Application.Current.Resources[offering ? "PrimaryButtonStyle" : "SecondaryButtonStyle"];
        UpdateAction.IsEnabled = !_updates.IsBusy;
    }

    private void OnNotesClick(object sender, RoutedEventArgs e)
    {
        if (_updates.Release is { } release) Shell.OpenUrl(release.Page.ToString());
    }

    private async void OnUpdateActionClick(object sender, RoutedEventArgs e)
    {
        if (_updates.Release is not { } release) await _updates.CheckAsync();
        else if (_updates.IsInstalledCopy) await _updates.InstallAsync();
        else Shell.OpenUrl((release.Zip ?? release.Page).ToString());
    }
}
