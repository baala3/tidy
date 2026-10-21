using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Tidy.App.Services;
using Tidy.App.Views;
using Tidy.Core.Models;
using Tidy.Core.Settings;
using Windows.System;

namespace Tidy.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, (Type Page, object? Parameter)> Pages = new()
    {
        ["overview"] = (typeof(OverviewPage), null),
        ["caches"] = (typeof(CleanPage), ScanCategory.AppCaches),
        ["dev"] = (typeof(CleanPage), ScanCategory.DevTools),
        ["large"] = (typeof(LargeFilesPage), null),
        ["duplicates"] = (typeof(DuplicatesPage), null),
        ["installers"] = (typeof(InstallersPage), null),
        ["onedrive"] = (typeof(OneDrivePage), null),
        ["wsl"] = (typeof(VirtualDisksPage), null),
        ["windows"] = (typeof(WindowsPage), null),
        ["apps"] = (typeof(AppsPage), null),
        ["settings"] = (typeof(SettingsPage), null),
        ["about"] = (typeof(AboutPage), null),
    };

    private string _currentTag = "overview";

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Tidy.ico"));
        // AppWindow sizes are physical pixels; scale the design size by the monitor's DPI.
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1180 * scale), (int)(820 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(940 * scale);
            presenter.PreferredMinimumHeight = (int)(620 * scale);
        }
        CenterOnScreen();

        App.State.OwnerWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);
        App.State.TotalsChanged += (_, _) => UpdateSidebar();
        App.State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.RecycleBinBytes)) UpdateSidebar();
        };
        App.State.RefreshDisk();
        // The Recycle Bin can be emptied, and space freed, outside Tidy; look again on coming back.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) App.State.RefreshDisk();
        };
        ApplyAppearance(App.State.Settings.Appearance);
        Root.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        AddAccelerators();

        // Only people with OneDrive set up get its page.
        OneDriveItem.Visibility = App.State.HasOneDrive ? Visibility.Visible : Visibility.Collapsed;
        ContentFrame.Navigate(typeof(OverviewPage));
        UpdateSidebar();
    }

    public void NavigateTo(string tag)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (item is not null) Nav.SelectedItem = item;
    }

    public void ApplyAppearance(AppearanceMode mode)
    {
        Root.RequestedTheme = mode switch
        {
            AppearanceMode.System => ElementTheme.Default,
            AppearanceMode.Light => ElementTheme.Light,
            AppearanceMode.Dark => ElementTheme.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var bar = AppWindow.TitleBar;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x10, 0, 0, 0);
    }

    private void UpdateSidebar()
    {
        var state = App.State;
        CachesSize.Text = state.AppCacheItems.Count > 0 ? Format.Bytes(state.AppCachesBytes) : "";
        DevSize.Text = state.DevToolItems.Count > 0 ? Format.Bytes(state.DevToolsBytes) : "";
        LargeSize.Text = state.LargeFiles.Count > 0 ? Format.Bytes(state.LargeFilesBytes) : "";
        DuplicatesSize.Text = state.Duplicates.Count > 0 ? Format.Bytes(state.DuplicatesBytes) : "";
        InstallersSize.Text = state.Installers.Count > 0 ? Format.Bytes(state.InstallersBytes) : "";
        OneDriveSize.Text = state.OneDriveFiles.Count > 0 ? Format.Bytes(state.OneDriveBytes) : "";
        WindowsSize.Text = state.WindowsBytes > 0 ? Format.Bytes(state.WindowsBytes) : "";
        // Only people with WSL or Docker get the page; the size is what compacting gives back.
        VirtualDisksItem.Visibility = state.VirtualDisks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        VirtualDisksSize.Text = state.VirtualDisksReclaimableBytes > 0 ? Format.Bytes(state.VirtualDisksReclaimableBytes) : "";
        AppsSize.Text = state.Apps.Count > 0 ? Format.Bytes(state.AppsBytes) : "";
        RecycleBinSize.Text = Format.Bytes(state.RecycleBinBytes);
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && tag != _currentTag && Pages.TryGetValue(tag, out var target))
        {
            _currentTag = tag;
            ContentFrame.Navigate(target.Page, target.Parameter, new EntranceNavigationTransitionInfo());
        }
    }

    private void OnRecycleBinClick(object sender, RoutedEventArgs e) => Shell.OpenRecycleBin();

    private void AddAccelerators()
    {
        var scanAll = new KeyboardAccelerator { Key = VirtualKey.R, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        scanAll.Invoked += (sender, e) =>
        {
            e.Handled = true;
            _ = App.State.ScanEverythingAsync();
        };
        Root.KeyboardAccelerators.Add(scanAll);
        var settings = new KeyboardAccelerator { Key = (VirtualKey)0xBC, Modifiers = VirtualKeyModifiers.Control };
        settings.Invoked += (_, e) =>
        {
            e.Handled = true;
            NavigateTo("settings");
        };
        Root.KeyboardAccelerators.Add(settings);
    }

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
