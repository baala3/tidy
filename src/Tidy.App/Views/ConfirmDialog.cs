using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tidy.App.Services;
using Tidy.App.ViewModels;

namespace Tidy.App.Views;

/// <summary>
/// The review step before anything moves. It lists every row with its size, calls out
/// Check First rows, and defaults to Cancel, so a stray Enter never cleans.
/// </summary>
public static class ConfirmDialog
{
    public static Task<bool> ConfirmCleanAsync(XamlRoot root, IReadOnlyList<ItemViewModel> items)
    {
        var rows = items.OrderByDescending(i => i.SizeBytes)
            .Select(i => (Title: i.Item.ProjectName is { } project ? $"{i.Title} · {project}" : i.Title, i.SizeBytes, i.IsCheckFirst));
        var checkFirst = items.Count(i => i.IsCheckFirst);
        var note = checkFirst == 0
            ? "Everything here is Safe to Clean: its app rebuilds it by itself."
            : $"{Format.Count(checkFirst, "item is", "items are")} marked Check First. They come back, but it can cost time, a download or a sign-in. Read their notes if you are unsure.";
        if (items.Any(i => i.Item.IsDeviceBackup))
        {
            note += " A device backup doesn't rebuild itself: it comes back only from the Recycle Bin, or by backing up that device again.";
        }
        return ShowAsync(root,
            $"Move {Format.Count(items.Count, "item", "items")} to the Recycle Bin?",
            rows, items.Sum(i => i.SizeBytes), note);
    }

    public static Task<bool> ConfirmLargeFilesAsync(XamlRoot root, IReadOnlyList<LargeFileViewModel> files) =>
        ShowAsync(root,
            $"Move {Format.Count(files.Count, "file", "files")} to the Recycle Bin?",
            files.OrderByDescending(f => f.SizeBytes).Select(f => (f.Name, f.SizeBytes, false)),
            files.Sum(f => f.SizeBytes),
            "These are your own files. Tidy only moves them to the Recycle Bin, so you can restore any of them until you empty it.");

    public static Task<bool> ConfirmDuplicatesAsync(XamlRoot root, IReadOnlyList<Core.Deletion.DuplicateRemoval> removals, IEnumerable<DuplicateGroupViewModel> groups)
    {
        var copies = groups.SelectMany(g => g.Copies).ToDictionary(c => c.Path, StringComparer.OrdinalIgnoreCase);
        var rows = removals.Select(r => copies[r.Path]).OrderByDescending(c => c.SizeBytes).ToList();
        return ShowAsync(root,
            $"Move {Format.Count(rows.Count, "copy", "copies")} to the Recycle Bin?",
            rows.Select(c => ($"{c.Name} · {c.Copy.File.SourceLabel}", c.SizeBytes, false)),
            rows.Sum(c => c.SizeBytes),
            "One copy of each file stays where it is. Right before moving a copy, Tidy compares it byte for byte with the one that stays, and leaves it alone if anything changed.");
    }

    public static Task<bool> ConfirmInstallersAsync(XamlRoot root, IReadOnlyList<InstallerViewModel> installers) =>
        ShowAsync(root,
            $"Move {Format.Count(installers.Count, "installer", "installers")} to the Recycle Bin?",
            installers.OrderByDescending(i => i.SizeBytes).Select(i => (i.Name, i.SizeBytes, i.IsNewer)),
            installers.Sum(i => i.SizeBytes),
            installers.Any(i => i.IsNewer)
                ? "The apps stay installed. Installers marked Check First are newer than what you have, so you may still want to run them."
                : "The apps stay installed. Only these installer files move, and you can restore any of them from the Recycle Bin.");

    public static Task<bool> ConfirmAppsAsync(XamlRoot root, IReadOnlyList<AppViewModel> apps)
    {
        var store = apps.Count(a => a.App.Kind == Core.Apps.AppKind.Store);
        var desktop = apps.Count - store;
        var notes = new List<string> { "Uninstalled apps can't go to the Recycle Bin." };
        if (store > 0) notes.Add("Store apps are removed for your account. You can reinstall them from the Microsoft Store.");
        if (desktop > 0) notes.Add("Each desktop app opens its own uninstaller, one at a time. Windows may ask for permission.");
        return ShowAsync(root,
            $"Uninstall {Format.Count(apps.Count, "app", "apps")}?",
            apps.OrderByDescending(a => a.SizeBytes).Select(a => (a.Name, a.SizeBytes, a.IsCheckFirst)),
            apps.Sum(a => a.SizeBytes),
            string.Join(" ", notes),
            primary: "Uninstall",
            summary: $"Frees about {Format.Bytes(apps.Sum(a => a.SizeBytes))}.");
    }

    private static async Task<bool> ShowAsync(XamlRoot root, string title, IEnumerable<(string Title, long Size, bool CheckFirst)> rows, long total, string note,
        string primary = "Move to Recycle Bin", string? summary = null)
    {
        var resources = Application.Current.Resources;
        var list = new StackPanel { Spacing = 2 };
        foreach (var (rowTitle, size, isCheckFirst) in rows)
        {
            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            name.Children.Add(new TextBlock { Text = rowTitle, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 330 });
            if (isCheckFirst)
            {
                var tag = new Border
                {
                    Padding = new Thickness(6, 1, 6, 1),
                    CornerRadius = new CornerRadius(4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Check First", FontSize = 11, FontWeight = FontWeights.SemiBold },
                };
                tag.SetValue(Border.BackgroundProperty, Theme(root, "StatusCheckFillBrush"));
                ((TextBlock)tag.Child).Foreground = Theme(root, "StatusCheckTextBrush");
                name.Children.Add(tag);
            }
            grid.Children.Add(name);
            var sizeText = new TextBlock { Text = Format.Bytes(size), Foreground = Theme(root, "TextSecondaryBrush") };
            Grid.SetColumn(sizeText, 1);
            grid.Children.Add(sizeText);
            list.Children.Add(grid);
        }

        var content = new StackPanel { Spacing = 12, MinWidth = 440 };
        content.Children.Add(new TextBlock
        {
            Text = summary ?? $"Frees about {Format.Bytes(total)}. You can restore anything from the Recycle Bin.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new Border
        {
            BorderBrush = Theme(root, "BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 6),
            Child = new ScrollViewer { Content = list, MaxHeight = 260 },
        });
        content.Children.Add(new TextBlock
        {
            Text = note,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme(root, "TextSecondaryBrush"),
            Style = (Style)resources["CaptionTextBlockStyle"],
        });

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)resources["DestructiveButtonStyle"],
            CloseButtonStyle = (Style)resources["SecondaryButtonStyle"],
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>A theme brush for whichever theme the window is showing, not the app's default.</summary>
    private static Brush Theme(XamlRoot root, string key)
    {
        var theme = (root.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        var dictionaries = Application.Current.Resources.ThemeDictionaries;
        return (Brush)((ResourceDictionary)dictionaries[theme])[key];
    }
}
