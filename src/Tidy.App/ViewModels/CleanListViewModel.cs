using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Deletion;
using Tidy.Core.Models;

namespace Tidy.App.ViewModels;

public enum SafetyFilter
{
    All,
    Safe,
    CheckFirst,
}

public enum SortOrder
{
    Largest,
    Newest,
    Name,
}

/// <summary>A titled run of rows: "Tool caches", or one project with its rebuildable folders.</summary>
public sealed class ItemGroup(string title, string subtitle, string? path) : List<ItemViewModel>
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public string? Path { get; } = path;
    public bool HasTitle => Title.Length > 0;
    public bool HasSubtitle => Subtitle.Length > 0;
    public bool IsProject => Path is not null;
}

/// <summary>App Caches and Dev Tools: the same list, filters and clean flow over different rows.</summary>
public sealed partial class CleanListViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly ObservableCollection<ItemViewModel> _source;

    public ScanCategory Category { get; }
    public string Title => Category == ScanCategory.AppCaches ? "App Caches" : "Dev Tools";

    public BulkObservableCollection<ItemGroup> Groups { get; } = [];

    [ObservableProperty] public partial SafetyFilter Filter { get; set; }
    [ObservableProperty] public partial SortOrder Sort { get; set; }
    [ObservableProperty] public partial bool IsCleaning { get; private set; }

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; private set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; private set; }
    public ObservableCollection<CleanFailure> ResultFailures { get; } = [];

    public CleanListViewModel(AppState state, ScanCategory category)
    {
        _state = state;
        Category = category;
        _source = category == ScanCategory.AppCaches ? state.AppCacheItems : state.DevToolItems;
        _source.CollectionChanged += OnSourceChanged;
        foreach (var item in _source) item.PropertyChanged += OnItemChanged;
        state.PropertyChanged += OnStateChanged;
        Rebuild();
    }

    public bool IsScanning => Category == ScanCategory.AppCaches ? _state.IsScanningCaches : _state.IsScanningDevTools;
    public bool IsBusy => IsScanning || IsCleaning;
    public bool IsEmpty => _source.Count == 0 && !IsScanning;
    public bool IsFirstScan => _source.Count == 0 && IsScanning;
    public bool HasItems => _source.Count > 0;

    public int AllCount => _source.Count;
    public int SafeCount => _source.Count(i => i.IsSafe);
    public int CheckFirstCount => _source.Count(i => i.IsCheckFirst);

    private IEnumerable<ItemViewModel> Selected => _source.Where(i => i.IsSelected && i.CanSelect);
    public int SelectedCount => Selected.Count();
    public long SelectedBytes => Selected.Sum(i => i.SizeBytes);
    public bool CanClean => SelectedCount > 0 && !IsBusy;
    public string CleanButtonText => SelectedCount == 0 ? "Clean Selected" : $"Clean Selected ({Format.Bytes(SelectedBytes)})";

    public string Summary
    {
        get
        {
            if (_source.Count == 0) return IsScanning ? "Scanning…" : LastScannedText;
            return $"{Format.Count(_source.Count, "item", "items")} · {Format.Bytes(_source.Sum(i => i.SizeBytes))} recoverable";
        }
    }

    private string LastScannedText
    {
        get
        {
            var when = Category == ScanCategory.AppCaches ? _state.CachesScannedUtc : _state.ProjectsScannedUtc;
            return when is { } t ? $"Scanned {Format.RelativeTime(t, DateTime.UtcNow)}" : "Not scanned yet";
        }
    }

    public string EmptyTitle => Category == ScanCategory.AppCaches ? "No caches worth cleaning" : "No developer leftovers";

    public string EmptyMessage => Category == ScanCategory.AppCaches
        ? "Tidy found nothing it recognizes as safe to clean. It only lists folders it knows about."
        : _state.Settings.StaleDays == 0
            ? "No package caches or project build folders were found."
            : $"No package caches, and no projects untouched for {StaleText(_state.Settings.StaleDays)}. Change this in Settings.";

    private static string StaleText(int days) => days switch
    {
        30 => "a month",
        90 => "3 months",
        180 => "6 months",
        365 => "a year",
        730 => "2 years",
        _ => $"{days} days",
    };

    private IEnumerable<ItemViewModel> SelectableSafe => _source.Where(i => i.IsSafe && i.CanSelect);
    private IEnumerable<ItemViewModel> SelectableCheckFirst => _source.Where(i => i.IsCheckFirst && i.CanSelect);

    /// <summary>Checked when every safe row is selected, mixed when some are.</summary>
    public bool? SelectAllSafeState
    {
        get
        {
            var safe = SelectableSafe.ToList();
            if (safe.Count == 0) return false;
            var selected = safe.Count(i => i.IsSelected);
            return selected == 0 ? false : selected == safe.Count ? true : null;
        }
        set
        {
            var select = SelectAllSafeState != true;
            foreach (var item in SelectableSafe) item.IsSelected = select;
            if (!select)
            {
                foreach (var item in SelectableCheckFirst) item.IsSelected = false;
            }
        }
    }

    public bool HasSafeItems => SelectableSafe.Any();

    public bool ShowAlsoSelectCheckFirst => SelectAllSafeState == true && SelectableCheckFirst.Any(i => !i.IsSelected);

    public string AlsoSelectCheckFirstText
    {
        get
        {
            var rest = SelectableCheckFirst.Where(i => !i.IsSelected).ToList();
            return $"Also select {Format.Count(rest.Count, "Check First item", "Check First items")} ({Format.Bytes(rest.Sum(i => i.SizeBytes))})";
        }
    }

    public void SelectAllCheckFirst()
    {
        foreach (var item in SelectableCheckFirst) item.IsSelected = true;
    }

    public Task ScanAsync() => Category == ScanCategory.AppCaches ? _state.ScanCachesAsync() : _state.ScanDevToolsAsync();

    public IReadOnlyList<ItemViewModel> SelectedItems() => Selected.ToList();

    public async Task CleanAsync(IReadOnlyList<ItemViewModel> items)
    {
        IsCleaning = true;
        RaiseDerived();
        try
        {
            var result = await _state.CleanAsync(items);
            ShowResult(result);
        }
        finally
        {
            IsCleaning = false;
            RaiseDerived();
        }
    }

    private void ShowResult(CleanResult result)
    {
        ResultFailures.Clear();
        foreach (var failure in result.Failures) ResultFailures.Add(failure);
        ResultHasFailures = result.Failures.Count > 0;
        if (result.Removed.Count == 0)
        {
            ResultTitle = "Nothing was moved";
            ResultMessage = "Tidy left everything where it was. The reasons are below.";
        }
        else
        {
            ResultTitle = $"Freed {Format.Bytes(result.FreedBytes)}";
            ResultMessage = $"Moved {Format.Count(result.Removed.Count, "item", "items")} to the Recycle Bin." +
                (result.Failures.Count > 0 ? $" {Format.Count(result.Failures.Count, "item was", "items were")} left alone." : " You can restore anything from there.");
        }
        IsResultOpen = true;
    }

    partial void OnFilterChanged(SafetyFilter value) => Rebuild();
    partial void OnSortChanged(SortOrder value) => Rebuild();

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (ItemViewModel item in e.NewItems ?? Array.Empty<ItemViewModel>()) item.PropertyChanged += OnItemChanged;
        foreach (ItemViewModel item in e.OldItems ?? Array.Empty<ItemViewModel>()) item.PropertyChanged -= OnItemChanged;
        Rebuild();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemViewModel.IsSelected))
        {
            RaiseDerived();
        }
        else
        {
            Rebuild();
        }
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.IsScanningCaches) or nameof(AppState.IsScanningProjects) or nameof(AppState.IsScanningDevTools)
            or nameof(AppState.CachesScannedUtc) or nameof(AppState.ProjectsScannedUtc))
        {
            RaiseDerived();
        }
    }

    private void Rebuild()
    {
        IEnumerable<ItemViewModel> visible = Filter switch
        {
            SafetyFilter.All => _source,
            SafetyFilter.Safe => _source.Where(i => i.IsSafe),
            SafetyFilter.CheckFirst => _source.Where(i => i.IsCheckFirst),
            _ => throw new ArgumentOutOfRangeException(nameof(Filter)),
        };
        Func<IEnumerable<ItemViewModel>, IEnumerable<ItemViewModel>> order = Sort switch
        {
            SortOrder.Largest => items => items.OrderByDescending(i => i.SizeBytes),
            SortOrder.Newest => items => items.OrderByDescending(i => i.Item.LastModifiedUtc ?? DateTime.MinValue),
            SortOrder.Name => items => items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(Sort)),
        };

        var list = visible.ToList();
        var groups = new List<ItemGroup>();
        var tools = list.Where(i => !i.Item.IsProjectArtifact && !i.Item.IsDeviceBackup).ToList();
        if (tools.Count > 0)
        {
            var group = new ItemGroup(Category == ScanCategory.DevTools ? "Tool caches" : "", "", null);
            group.AddRange(order(tools));
            groups.Add(group);
        }
        var backups = list.Where(i => i.Item.IsDeviceBackup).ToList();
        if (backups.Count > 0)
        {
            var group = new ItemGroup("iPhone and iPad backups", "Each is a whole device. Keep the newest backup of any device you still use.", null);
            group.AddRange(order(backups));
            groups.Add(group);
        }
        var projects = list.Where(i => i.Item.IsProjectArtifact)
            .GroupBy(i => i.Item.ProjectPath!, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First().Item;
                var newest = g.Max(i => i.Item.LastModifiedUtc ?? DateTime.MinValue);
                var group = new ItemGroup(first.ProjectName!, $"{first.ProjectPath} · last changed {Format.Age(newest, DateTime.UtcNow)}", first.ProjectPath);
                group.AddRange(order(g));
                return group;
            });
        groups.AddRange(Sort == SortOrder.Name
            ? projects.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
            : projects.OrderByDescending(g => g.Sum(i => i.SizeBytes)));

        Groups.ReplaceAll(groups);
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFirstScan));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(SafeCount));
        OnPropertyChanged(nameof(CheckFirstCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CleanButtonText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SelectAllSafeState));
        OnPropertyChanged(nameof(HasSafeItems));
        OnPropertyChanged(nameof(ShowAlsoSelectCheckFirst));
        OnPropertyChanged(nameof(AlsoSelectCheckFirstText));
        OnPropertyChanged(nameof(EmptyMessage));
    }
}
