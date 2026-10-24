using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tidy.Core.Scheduling;
using Tidy.Core.Settings;

namespace Tidy.App.Views;

public sealed partial class SettingsPage : Page
{
    /// <summary>False while the page sets its own controls, so that doesn't count as the user changing them.</summary>
    private bool _ready;

    public SettingsPage()
    {
        InitializeComponent();
        var settings = App.State.Settings;
        ThemeBox.SelectedIndex = (int)settings.Appearance;
        StaleBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.StaleOptionsDays, settings.StaleDays));
        AutoCleanBox.SelectedIndex = (int)settings.AutoClean;
        LowSpaceSwitch.IsOn = settings.LowSpaceAlerts;
        ShowLastClean();
        _ready = true;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        App.State.Settings.Appearance = (AppearanceMode)ThemeBox.SelectedIndex;
        App.State.SaveSettings();
        App.Window.ApplyAppearance(App.State.Settings.Appearance);
    }

    private async void OnStaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        App.State.Settings.StaleDays = AppSettings.StaleOptionsDays[StaleBox.SelectedIndex];
        App.State.SaveSettings();
        await App.State.ScanProjectsAsync();
    }

    private async void OnAutoCleanChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var settings = App.State.Settings;
        var before = settings.AutoClean;
        settings.AutoClean = (CleanSchedule)AutoCleanBox.SelectedIndex;
        await ApplyBackgroundTaskAsync(() =>
        {
            settings.AutoClean = before;
            AutoCleanBox.SelectedIndex = (int)before;
        });
    }

    private async void OnLowSpaceToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var settings = App.State.Settings;
        settings.LowSpaceAlerts = LowSpaceSwitch.IsOn;
        await ApplyBackgroundTaskAsync(() =>
        {
            settings.LowSpaceAlerts = !LowSpaceSwitch.IsOn;
            LowSpaceSwitch.IsOn = settings.LowSpaceAlerts;
        });
    }

    /// <summary>Saves the change and creates or removes the task to match. If that fails, the change is undone.</summary>
    private async Task ApplyBackgroundTaskAsync(Action undo)
    {
        AutoCleanBox.IsEnabled = LowSpaceSwitch.IsEnabled = false;
        var problem = await App.State.ApplyBackgroundTaskAsync();
        if (problem is not null)
        {
            _ready = false;
            undo();
            _ready = true;
        }
        App.State.SaveSettings();
        ScheduleProblem.Message = problem ?? "";
        ScheduleProblem.IsOpen = problem is not null;
        AutoCleanBox.IsEnabled = LowSpaceSwitch.IsEnabled = true;
        ShowLastClean();
    }

    private void ShowLastClean()
    {
        var settings = App.State.Settings;
        var last = BackgroundState.Load(App.State.Locations.TidyData).LastCleanUtc;
        LastCleanText.Visibility = settings.AutoClean == CleanSchedule.Off && last is null ? Visibility.Collapsed : Visibility.Visible;
        var parts = new List<string>
        {
            last is { } when ? $"Last automatic clean: {Day(when)}." : "No automatic clean yet. The first one runs within the hour.",
        };
        if (last is not null && ScheduledClean.DueUtc(settings.AutoClean, last) is { } due) parts.Add($"Next: {Day(due)}.");
        LastCleanText.Text = string.Join(" ", parts);
    }

    private static string Day(DateTime utc) => utc.ToLocalTime().ToString("MMMM d", CultureInfo.CurrentCulture);
}
