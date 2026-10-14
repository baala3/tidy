using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Deletion;
using Tidy.Core.Models;

namespace Tidy.App.ViewModels;

public sealed record SizeOption(string Label, long Bytes);

public sealed record AgeOption(string Label, int Days);

public sealed partial class KindChip(LargeFileKind? kind, string label, int count) : ObservableObject
{
    public LargeFileKind? Kind { get; } = kind;
    public string Text { get; } = $"{label}  {count}";

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

/// <summary>
/// Large Files: personal files, so nothing is ever pre-selected and nothing here takes
/// part in a one-click clean. Each file is chosen by hand.
/// </summary>
public sealed partial class LargeFilesViewModel : ObservableObject
{
    private readonly AppState _state;

    public static readonly IReadOnlyList<SizeOption> SizeOptions =
    [
        new("Over 5 MB", 5L << 20), new("Over 10 MB", 10L << 20), new("Over 50 MB", 50L << 20),
        new("Over 100 MB", 100L << 20), new("Over 500 MB", 500L << 20), new("Over 1 GB", 1L << 30),
    ];

    public static readonly IReadOnlyList<AgeOption> AgeOptions =
    [
        new("Any time", 0), new("Over 1 month ago", 30), new("Over 3 months ago", 90),
        new("Over 6 months ago", 180), new("Over 1 year ago", 365),
    ];

    public static readonly IReadOnlyList<string> SortOptions = ["Largest", "Newest", "Oldest", "Name"];

    public BulkObservableCollection<LargeFileViewModel> Visible { get; } = [];
    public ObservableCollection<KindChip> Chips { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SizeIndex { get; set; }
    [ObservableProperty] public partial int AgeIndex { get; set; }
    [ObservableProperty] public partial int SortIndex { get; set; }
    [ObservableProperty] public partial LargeFileKind? KindFilter { get; set; }
    [ObservableProperty] public partial bool IsCleaning { get; private set; }

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; private set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; private set; }
    public ObservableCollection<CleanFailure> ResultFailures { get; } = [];

    public LargeFilesViewModel(AppState state)
    {
        _state = state;
        _state.LargeFiles.CollectionChanged += OnFilesChanged;
        foreach (var file in _state.LargeFiles) file.PropertyChanged += OnFileChanged;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsScanningLargeFiles) or nameof(AppState.LargeFilesScannedUtc)) RaiseDerived();
        };
        Rebuild();
    }

    public bool IsScanning => _state.IsScanningLargeFiles;
    public bool IsBusy => IsScanning || IsCleaning;
    public bool IsFirstScan => _state.LargeFiles.Count == 0 && IsScanning;
    public bool IsEmpty => _state.LargeFiles.Count == 0 && !IsScanning;
    public bool HasFiles => _state.LargeFiles.Count > 0;
    public bool NothingMatches => HasFiles && Visible.Count == 0;

    private IEnumerable<LargeFileViewModel> Selected => _state.LargeFiles.Where(f => f.IsSelected);
    public int SelectedCount => Selected.Count();
    public bool CanRecycle => SelectedCount > 0 && !IsBusy;
    public string RecycleButtonText => SelectedCount == 0
        ? "Move to Recycle Bin"
        : $"Move to Recycle Bin ({Format.Bytes(Selected.Sum(f => f.SizeBytes))})";

    public string Summary
    {
        get
        {
            if (_state.LargeFiles.Count == 0)
            {
                return IsScanning ? "Scanning…" : _state.LargeFilesScannedUtc is null ? "Not scanned yet" : "No files over 5 MB";
            }
            var shown = Visible.Count == _state.LargeFiles.Count
                ? Format.Count(Visible.Count, "file", "files")
                : $"{Visible.Count:N0} of {Format.Count(_state.LargeFiles.Count, "file", "files")}";
            return $"{shown} · {Format.Bytes(Visible.Sum(f => f.SizeBytes))}";
        }
    }

    public IReadOnlyList<LargeFileViewModel> SelectedFiles() => Selected.ToList();

    public void ClearSelection()
    {
        foreach (var file in Selected.ToList()) file.IsSelected = false;
    }

    public Task ScanAsync() => _state.ScanLargeFilesAsync();

    public async Task RecycleAsync(IReadOnlyList<LargeFileViewModel> files)
    {
        IsCleaning = true;
        RaiseDerived();
        try
        {
            var result = await _state.RecycleLargeFilesAsync(files);
            ResultFailures.Clear();
            foreach (var failure in result.Failures) ResultFailures.Add(failure);
            ResultHasFailures = result.Failures.Count > 0;
            ResultTitle = result.Removed.Count == 0 ? "Nothing was moved" : $"Freed {Format.Bytes(result.FreedBytes)}";
            ResultMessage = result.Removed.Count == 0
                ? "Tidy left every file where it was. The reasons are below."
                : $"Moved {Format.Count(result.Removed.Count, "file", "files")} to the Recycle Bin. You can restore them from there.";
            IsResultOpen = true;
        }
        finally
        {
            IsCleaning = false;
            RaiseDerived();
        }
    }

    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnSizeIndexChanged(int value) => Rebuild();
    partial void OnAgeIndexChanged(int value) => Rebuild();
    partial void OnSortIndexChanged(int value) => Rebuild();
    partial void OnKindFilterChanged(LargeFileKind? value) => Rebuild();

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (LargeFileViewModel f in e.NewItems ?? Array.Empty<LargeFileViewModel>()) f.PropertyChanged += OnFileChanged;
        foreach (LargeFileViewModel f in e.OldItems ?? Array.Empty<LargeFileViewModel>()) f.PropertyChanged -= OnFileChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var f in _state.LargeFiles) f.PropertyChanged += OnFileChanged;
        }
        Rebuild();
    }

    private void OnFileChanged(object? sender, PropertyChangedEventArgs e) => RaiseDerived();

    private void Rebuild()
    {
        var minimum = SizeOptions[Math.Clamp(SizeIndex, 0, SizeOptions.Count - 1)].Bytes;
        var ageDays = AgeOptions[Math.Clamp(AgeIndex, 0, AgeOptions.Count - 1)].Days;
        var cutoff = DateTime.UtcNow.AddDays(-ageDays);
        var query = SearchText.Trim();

        var matching = _state.LargeFiles
            .Where(f => f.SizeBytes >= minimum)
            .Where(f => ageDays == 0 || f.File.LastModifiedUtc < cutoff)
            .Where(f => query.Length == 0 ||
                f.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                f.Folder.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                f.File.SourceLabel.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        RebuildChips(matching);
        IEnumerable<LargeFileViewModel> shown = KindFilter is { } kind ? matching.Where(f => f.File.Kind == kind) : matching;
        shown = SortIndex switch
        {
            1 => shown.OrderByDescending(f => f.File.LastModifiedUtc),
            2 => shown.OrderBy(f => f.File.LastModifiedUtc),
            3 => shown.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => shown.OrderByDescending(f => f.SizeBytes),
        };

        Visible.ReplaceAll(shown);
        RaiseDerived();
    }

    private void RebuildChips(List<LargeFileViewModel> matching)
    {
        Chips.Clear();
        Chips.Add(new KindChip(null, "All", matching.Count) { IsActive = KindFilter is null });
        foreach (var group in matching.GroupBy(f => f.File.Kind).OrderBy(g => g.Key))
        {
            Chips.Add(new KindChip(group.Key, Glyphs.Label(group.Key), group.Count()) { IsActive = KindFilter == group.Key });
        }
        if (KindFilter is { } kind && matching.All(f => f.File.Kind != kind)) KindFilter = null;
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsFirstScan));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(NothingMatches));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanRecycle));
        OnPropertyChanged(nameof(RecycleButtonText));
        OnPropertyChanged(nameof(Summary));
    }
}
