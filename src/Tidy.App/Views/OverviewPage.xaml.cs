using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Tidy.App.Services;

namespace Tidy.App.Views;

/// <summary>
/// Where the space went: one bar for the whole drive, then a row per category. Each
/// byte is counted once. Caches live in AppData, projects and large files in the user's
/// own folders, and Large Files skips project folders, so the segments never overlap.
/// </summary>
public sealed partial class OverviewPage : Page
{
    private sealed record Segment(string Key, string Title, string Subtitle, long Bytes, string Brush, string TileBrush, string Glyph, string? Tag, bool ReviewFirst);

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };

    public OverviewPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            App.State.TotalsChanged += OnTotalsChanged;
            App.State.PropertyChanged += OnStateChanged;
            _clock.Tick += OnClockTick;
            _clock.Start();
            Render();
        };
        Unloaded += (_, _) =>
        {
            App.State.TotalsChanged -= OnTotalsChanged;
            App.State.PropertyChanged -= OnStateChanged;
            _clock.Tick -= OnClockTick;
            _clock.Stop();
        };
        ActualThemeChanged += (_, _) => Render();
    }

    private void OnTotalsChanged(object? sender, EventArgs e) => Render();
    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Render();
    private void OnClockTick(object? sender, object e) => Render();

    private void Render()
    {
        var state = App.State;
        var disk = state.Disk;
        var scanning = state.IsScanningEverything || state.IsScanningCaches || state.IsScanningProjects || state.IsScanningLargeFiles || state.IsScanningDuplicates || state.IsScanningApps;

        ScanIcon.Visibility = scanning ? Visibility.Collapsed : Visibility.Visible;
        ScanRing.IsActive = scanning;
        ScanAllText.Text = scanning ? "Scanning…" : "Scan Everything";
        ScanAllButton.IsEnabled = !scanning;
        var lastScan = new[] { state.CachesScannedUtc, state.ProjectsScannedUtc, state.LargeFilesScannedUtc, state.DuplicatesScannedUtc, state.AppsScannedUtc }.Where(t => t is not null).Max();
        ScannedText.Text = scanning ? "Scanning one category at a time…"
            : lastScan is { } t ? $"Scanned {Format.RelativeTime(t, DateTime.UtcNow)}" : "Not scanned yet";

        UsedText.Text = $"{Format.Bytes(disk.UsedBytes)} used";
        TotalText.Text = $"  of {Format.Bytes(disk.TotalBytes)} on {disk.Drive}";
        FreeText.Text = $"{Format.Bytes(disk.FreeBytes)} free";

        var caches = state.AppCachesBytes;
        var dev = state.DevToolsBytes;
        var large = state.LargeFilesBytes;
        var apps = state.AppsBytes;
        var known = caches + dev + large + apps;
        var everythingElse = Math.Max(0, disk.UsedBytes - known);

        var categories = new List<Segment>
        {
            new("caches", "App Caches", CategorySubtitle(state.AppCacheItems.Count, state.AppCachesSafeBytes, state.CachesScannedUtc, state.IsScanningCaches),
                caches, "ChartAppCachesBrush", "ChartAppCachesTileBrush", Glyphs.AppCaches, "caches", false),
            new("dev", "Dev Tools", CategorySubtitle(state.DevToolItems.Count, state.DevToolsSafeBytes, state.ProjectsScannedUtc, state.IsScanningDevTools),
                dev, "ChartDevToolsBrush", "ChartDevToolsTileBrush", Glyphs.DevTools, "dev", false),
            new("large", "Large Files", LargeSubtitle(), large, "ChartLargeFilesBrush", "ChartLargeFilesTileBrush", Glyphs.LargeFiles, "large", state.LargeFiles.Count > 0),
            new("apps", "Apps", AppsSubtitle(), apps, "ChartAppsBrush", "ChartAppsTileBrush", Glyphs.Apps, "apps", state.Apps.Count > 0),
        };
        var others = new List<Segment>
        {
            new("else", "Everything else", "Windows, your documents and photos, and files Tidy doesn't sort",
                everythingElse, "ChartEverythingElseBrush", "ChartEverythingElseBrush", Glyphs.Ellipsis, null, false),
            new("free", "Free", "Available for new files", disk.FreeBytes, "ChartFreeSpaceBrush", "", "", null, false),
        };

        RenderBar(categories.Concat(others).ToList(), disk.TotalBytes);
        RenderRows(CategoryRows, categories, disk.TotalBytes);
        RenderRows(OtherRows, others, disk.TotalBytes);

        RecycleText.Text = state.RecycleBinBytes > 0
            ? $"{Format.Bytes(state.RecycleBinBytes)} of the used space is already in the Recycle Bin. Emptying it frees that space."
            : "The Recycle Bin is empty.";
    }

    private static string CategorySubtitle(int count, long safeBytes, DateTime? scanned, bool scanning)
    {
        if (count == 0) return scanning ? "Scanning…" : scanned is null ? "Not scanned yet" : "Nothing to clean";
        return safeBytes > 0 ? $"{Format.Bytes(safeBytes)} safe to clean" : Format.Count(count, "item to check", "items to check");
    }

    private static string LargeSubtitle()
    {
        var state = App.State;
        if (state.LargeFiles.Count == 0)
        {
            return state.IsScanningLargeFiles ? "Scanning…" : state.LargeFilesScannedUtc is null ? "Not scanned yet" : "No files over 5 MB";
        }
        return Format.Count(state.LargeFiles.Count, "file over 5 MB", "files over 5 MB");
    }

    private static string AppsSubtitle()
    {
        var state = App.State;
        if (state.Apps.Count == 0) return state.IsScanningApps ? "Scanning…" : state.AppsScannedUtc is null ? "Not scanned yet" : "No apps found";
        var parts = new List<string>();
        var extras = state.Apps.Count(a => a.IsPreinstalled);
        var unused = state.IsLaunchTrackingEnabled ? state.Apps.Count(a => a.IsUnused) : 0;
        if (extras > 0) parts.Add(Format.Count(extras, "preinstalled extra", "preinstalled extras"));
        if (unused > 0) parts.Add($"{unused:N0} not opened in {ViewModels.AppViewModel.UnusedDays} days");
        return parts.Count > 0 ? string.Join(" · ", parts) : Format.Count(state.Apps.Count, "app", "apps");
    }

    private void RenderBar(List<Segment> segments, long total)
    {
        Bar.Children.Clear();
        Bar.ColumnDefinitions.Clear();
        if (total <= 0) return;
        // A sliver still gets a few pixels, so a scanned category never vanishes from the bar.
        var shown = segments.Where(s => s.Bytes > 0).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var segment = shown[i];
            var fraction = (double)segment.Bytes / total;
            Bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.004), GridUnitType.Star) });
            var first = i == 0;
            var last = i == shown.Count - 1;
            var rect = new Border
            {
                Background = Brush(segment.Brush),
                CornerRadius = new CornerRadius(first ? 7 : 2, last ? 7 : 2, last ? 7 : 2, first ? 7 : 2),
            };
            ToolTipService.SetToolTip(rect, $"{segment.Title}: {Format.Bytes(segment.Bytes)}");
            Grid.SetColumn(rect, i);
            Bar.Children.Add(rect);
        }
    }

    private void RenderRows(StackPanel host, List<Segment> segments, long total)
    {
        host.Children.Clear();
        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                host.Children.Add(new Rectangle { Height = 1, Fill = Brush("BorderSubtleBrush"), Margin = new Thickness(76, 0, 20, 0) });
            }
            host.Children.Add(Row(segments[i], total));
        }
    }

    private Grid Row(Segment segment, long total)
    {
        var grid = new Grid { Padding = new Thickness(20, 14, 24, 14), ColumnSpacing = 16, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        FrameworkElement tile = segment.Key == "free"
            ? new Rectangle
            {
                Width = 40, Height = 40, RadiusX = 9, RadiusY = 9, StrokeThickness = 1.5,
                Stroke = Brush("TextTertiaryBrush"), StrokeDashArray = [3, 2],
            }
            : new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(9),
                Background = Brush(segment.TileBrush),
                Child = new FontIcon { Glyph = segment.Glyph, FontSize = 19, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
            };
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        titleLine.Children.Add(new TextBlock { Text = segment.Title, Style = (Style)Application.Current.Resources["RowTitleStyle"], FontSize = 15 });
        if (segment.ReviewFirst)
        {
            titleLine.Children.Add(new Border
            {
                Padding = new Thickness(7, 1, 7, 2), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center,
                Background = Brush("StatusCheckFillBrush"),
                Child = new TextBlock { Text = "Review first", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("StatusCheckTextBrush") },
            });
        }
        text.Children.Add(titleLine);
        text.Children.Add(new TextBlock { Text = segment.Subtitle, Style = (Style)Application.Current.Resources["SecondaryTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var percent = new TextBlock
        {
            Text = total > 0 ? Format.Percent((double)segment.Bytes / total) : "",
            Foreground = Brush("TextTertiaryBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(percent, 2);
        grid.Children.Add(percent);

        var size = new TextBlock
        {
            Text = Format.Bytes(segment.Bytes), Style = (Style)Application.Current.Resources["SizeTextStyle"], FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(size, 3);
        grid.Children.Add(size);

        if (segment.Tag is { } tag)
        {
            AutomationPropertiesHelper.Name(grid, $"{segment.Title}, {Format.Bytes(segment.Bytes)}");
            grid.PointerEntered += (_, _) => grid.Background = Brush("SurfaceCardHoverBrush");
            grid.PointerExited += (_, _) => grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            grid.Tapped += (_, _) => App.Window.NavigateTo(tag);
            grid.IsTabStop = true;
            grid.KeyDown += (_, e) =>
            {
                if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) App.Window.NavigateTo(tag);
            };
        }
        return grid;
    }

    /// <summary>A brush for whichever theme this page is showing right now.</summary>
    private Brush Brush(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        return (Brush)((ResourceDictionary)Application.Current.Resources.ThemeDictionaries[theme])[key];
    }

    private async void OnScanEverything(object sender, RoutedEventArgs e) => await App.State.ScanEverythingAsync();

    private void OnOpenRecycleBin(object sender, RoutedEventArgs e) => Shell.OpenRecycleBin();
}

internal static class AutomationPropertiesHelper
{
    public static void Name(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
}
