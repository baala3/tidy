using Microsoft.UI.Xaml.Controls;
using Tidy.Core.Settings;

namespace Tidy.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly bool _ready;

    public SettingsPage()
    {
        InitializeComponent();
        var settings = App.State.Settings;
        ThemeBox.SelectedIndex = (int)settings.Appearance;
        StaleBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.StaleOptionsDays, settings.StaleDays));
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
}
