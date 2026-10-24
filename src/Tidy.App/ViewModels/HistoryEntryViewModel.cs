using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Scheduling;

namespace Tidy.App.ViewModels;

/// <summary>One row on the History page. A long list of names shows two lines until the user asks for all of it.</summary>
public sealed partial class HistoryEntryViewModel(HistoryEntry entry) : ObservableObject
{
    public HistoryEntry Entry { get; } = entry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemsMaxLines), nameof(ShowsToggle), nameof(ToggleText))]
    public partial bool IsExpanded { get; set; }

    /// <summary>The names didn't fit in two lines.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsToggle))]
    public partial bool IsTrimmed { get; set; }

    public string Title => Entry.What;
    public bool IsScheduled => Entry.Source == CleanSource.Scheduled;

    /// <summary>The icon of the page the clean ran on, as the sidebar shows it.</summary>
    public string Glyph => IsScheduled ? Glyphs.Schedule : Entry.What switch
    {
        "App Caches" => Glyphs.AppCaches,
        "Dev Tools" => Glyphs.DevTools,
        "Windows" => Glyphs.Windows,
        "WSL & Docker" => Glyphs.VirtualDisks,
        "Large Files" => Glyphs.LargeFiles,
        "Duplicates" => Glyphs.Duplicates,
        "Installers" => Glyphs.Installers,
        "OneDrive" => Glyphs.OneDrive,
        "Apps" => Glyphs.Apps,
        _ => Glyphs.RecycleBin,
    };

    public string SizeText => Entry.FreedBytes > 0 ? Format.Bytes(Entry.FreedBytes) : "";
    public int ItemsMaxLines => IsExpanded ? 0 : 2;
    public bool ShowsToggle => IsTrimmed || IsExpanded;
    public string ToggleText => IsExpanded ? "Show less" : "Show all";

    public string Details
    {
        get
        {
            var parts = new List<string>
            {
                Entry.WhenUtc.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt", CultureInfo.CurrentCulture),
                Format.Count(Entry.ItemCount, "item", "items"),
            };
            if (Entry.Failed > 0) parts.Add($"{Entry.Failed.ToString("N0", CultureInfo.CurrentCulture)} left alone");
            return string.Join(" · ", parts);
        }
    }

    public string ItemsText
    {
        get
        {
            var text = string.Join(", ", Entry.Items);
            var more = Entry.ItemCount - Entry.Items.Count;
            return more > 0 ? $"{text} and {Format.Count(more, "more", "more")}" : text;
        }
    }
}
