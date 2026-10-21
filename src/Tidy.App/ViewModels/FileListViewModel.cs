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
/// A list of the user's own files with search, size, age, type and sort filters, as Large
/// Files and OneDrive show them. Nothing is ever pre-selected: each file is chosen by hand.
/// </summary>
public abstract partial class FileListViewModel : ObservableObject
{
    public static readonly IReadOnlyList<AgeOption> AgeOptions =
    [
        new("Any time", 0), new("Over 1 month ago", 30), new("Over 3 months ago", 90),
        new("Over 6 months ago", 180), new("Over 1 year ago", 365),
    ];

    public static readonly IReadOnlyList<string> SortOptions = ["Largest", "Newest", "Oldest", "Name"];

    private readonly ObservableCollection<LargeFileViewModel> _files;

    public IReadOnlyList<SizeOption> SizeOptions { get; }
    public BulkObservableCollection<LargeFileViewModel> Visible { get; } = [];
    public ObservableCollection<KindChip> Chips { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SizeIndex { get; set; }
    [ObservableProperty] public partial int AgeIndex { get; set; }
    [ObservableProperty] public partial int SortIndex { get; set; }
    [ObservableProperty] public partial LargeFileKind? KindFilter { get; set; }
    [ObservableProperty] public partial bool IsWorking { get; private set; }

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; protected set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; protected set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; protected set; }
    public ObservableCollection<CleanFailure> ResultFailures { get; } = [];

    protected FileListViewModel(ObservableCollection<LargeFileViewModel> files, IReadOnlyList<SizeOption> sizeOptions)
    {
        _files = files;
        SizeOptions = sizeOptions;
        _files.CollectionChanged += OnFilesChanged;
        foreach (var file in _files) file.PropertyChanged += OnFileChanged;
    }

    protected IReadOnlyList<LargeFileViewModel> Files => _files;

    public abstract bool IsScanning { get; }
    public bool IsBusy => IsScanning || IsWorking;
    public bool IsFirstScan => _files.Count == 0 && IsScanning;
    public bool IsEmpty => _files.Count == 0 && !IsScanning;
    public bool HasFiles => _files.Count > 0;
    public bool NothingMatches => HasFiles && Visible.Count == 0;

    protected IEnumerable<LargeFileViewModel> Selected => _files.Where(f => f.IsSelected);
    public int SelectedCount => Selected.Count();
    public long SelectedBytes => Selected.Sum(f => f.SizeBytes);
    public virtual bool CanAct => SelectedCount > 0 && !IsBusy;

    /// <summary>"12 files · 3.4 GB", or "4 of 12 files · 1.1 GB" while filters hide some.</summary>
    protected string ShownText =>
        (Visible.Count == _files.Count
            ? Format.Count(Visible.Count, "file", "files")
            : $"{Visible.Count:N0} of {Format.Count(_files.Count, "file", "files")}") + $" · {Format.Bytes(Visible.Sum(f => f.SizeBytes))}";

    public IReadOnlyList<LargeFileViewModel> SelectedFiles() => Selected.ToList();

    public void ClearSelection()
    {
        foreach (var file in Selected.ToList()) file.IsSelected = false;
    }

    /// <summary>Runs the page's action with the busy state and result bar around it.</summary>
    protected async Task RunAsync(Func<Task<CleanResult>> action, Action<CleanResult> describe)
    {
        IsWorking = true;
        RaiseDerived();
        try
        {
            var result = await action();
            ResultFailures.Clear();
            foreach (var failure in result.Failures) ResultFailures.Add(failure);
            ResultHasFailures = result.Failures.Count > 0;
            describe(result);
            IsResultOpen = true;
        }
        finally
        {
            IsWorking = false;
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
            foreach (var f in _files) f.PropertyChanged += OnFileChanged;
        }
        Rebuild();
    }

    private void OnFileChanged(object? sender, PropertyChangedEventArgs e) => RaiseDerived();

    protected void Rebuild()
    {
        var minimum = SizeOptions[Math.Clamp(SizeIndex, 0, SizeOptions.Count - 1)].Bytes;
        var ageDays = AgeOptions[Math.Clamp(AgeIndex, 0, AgeOptions.Count - 1)].Days;
        var cutoff = DateTime.UtcNow.AddDays(-ageDays);
        var query = SearchText.Trim();

        var matching = _files
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

    protected virtual void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(IsScanning), nameof(IsBusy), nameof(IsFirstScan), nameof(IsEmpty), nameof(HasFiles), nameof(NothingMatches),
            nameof(SelectedCount), nameof(SelectedBytes), nameof(CanAct),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
