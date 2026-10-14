using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Models;

namespace Tidy.App.ViewModels;

/// <summary>One App Caches or Dev Tools row.</summary>
public sealed partial class ItemViewModel(ScanItem item) : ObservableObject
{
    [ObservableProperty]
    public partial ScanItem Item { get; set; } = item;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Marks rows a running rescan has not reported yet; whatever is left unseen at the end is gone.</summary>
    public bool SeenInScan { get; set; } = true;

    public string Id => Item.Id;
    public string Title => Item.Title;
    public string Explanation => Item.Explanation;
    public string SizeText => Format.Bytes(Item.SizeBytes);
    public long SizeBytes => Item.SizeBytes;
    public bool IsSafe => Item.Safety == SafetyLevel.Safe;
    public bool IsCheckFirst => Item.Safety == SafetyLevel.CheckFirst;
    public string Glyph => Glyphs.ForKind(Item.Kind);
    public bool IsBlocked => Item.BlockedReason is not null;
    public bool CanSelect => !IsBlocked;
    public string BlockedReason => Item.BlockedReason ?? "";
    public bool HasRestoreHint => Item.RestoreHint is not null;
    public string RestoreHint => Item.RestoreHint is { } hint ? "To bring it back: " + hint : "";
    public string FirstPath => Item.Paths[0];
    public string AccessibleName => Item.ProjectName is { } project ? $"{Title} in {project}" : Title;

    public string PathsTooltip => Item.Paths.Count == 1
        ? Item.Paths[0]
        : string.Join(Environment.NewLine, Item.Paths.Take(12)) + (Item.Paths.Count > 12 ? $"{Environment.NewLine}and {Item.Paths.Count - 12} more" : "");

    public string ModifiedText => Item.LastModifiedUtc is { } t ? "Changed " + Format.Age(t, DateTime.UtcNow) : "";

    partial void OnItemChanged(ScanItem value)
    {
        OnPropertyChanged(string.Empty);
        if (IsBlocked) IsSelected = false;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && IsBlocked) IsSelected = false;
    }
}
