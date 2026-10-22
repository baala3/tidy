using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Deletion;

namespace Tidy.App.ViewModels;

/// <summary>
/// Windows: update leftovers, old Windows and crash dumps, cleaned by Windows' own Disk Cleanup.
/// Both checking and cleaning need administrator rights, and what's cleaned can't go to the
/// Recycle Bin, so nothing is selected for you.
/// </summary>
public sealed partial class WindowsViewModel : ObservableObject
{
    private readonly AppState _state;

    [ObservableProperty] public partial bool IsResultOpen { get; set; }
    [ObservableProperty] public partial string ResultTitle { get; private set; } = "";
    [ObservableProperty] public partial string ResultMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ResultHasFailures { get; private set; }
    public ObservableCollection<CleanFailure> ResultFailures { get; } = [];

    public WindowsViewModel(AppState state)
    {
        _state = state;
        foreach (var item in Items) item.PropertyChanged += OnItemChanged;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsCheckingWindows) or nameof(AppState.IsCleaningWindows)
                or nameof(AppState.WindowsCheckedUtc) or nameof(AppState.WindowsProgress)) RaiseDerived();
        };
    }

    public IReadOnlyList<WindowsCleanupItemViewModel> Items => _state.WindowsItems;

    public bool IsChecking => _state.IsCheckingWindows;
    public bool IsCleaning => _state.IsCleaningWindows;
    public bool IsBusy => IsChecking || IsCleaning;
    public bool IsChecked => _state.WindowsCheckedUtc is not null;

    private IEnumerable<WindowsCleanupItemViewModel> Selected => Items.Where(i => i.IsSelected && i.CanSelect);
    public int SelectedCount => Selected.Count();
    public bool CanClean => SelectedCount > 0 && !IsBusy;
    public string CleanButtonText => IsCleaning ? "Cleaning…"
        : SelectedCount == 0 ? "Clean Selected"
        : $"Clean Selected ({Format.Bytes(Selected.Sum(i => i.SizeBytes ?? 0))})";
    public string CheckButtonText => IsChecked ? "Check Again" : "Check Sizes";

    public string Summary
    {
        get
        {
            if (IsBusy) return _state.WindowsProgress;
            if (!IsChecked) return "Not checked yet · needs administrator permission";
            var found = Items.Count(i => i.HasSize);
            return found == 0 ? $"Nothing to clean · checked {Format.RelativeTime(_state.WindowsCheckedUtc!.Value, DateTime.UtcNow)}"
                : $"{Format.Count(found, "item", "items")} · {Format.Bytes(_state.WindowsBytes)} · checked {Format.RelativeTime(_state.WindowsCheckedUtc!.Value, DateTime.UtcNow)}";
        }
    }

    public IReadOnlyList<WindowsCleanupItemViewModel> SelectedItems() => Selected.ToList();

    public async Task CheckAsync()
    {
        var problem = await _state.CheckWindowsAsync();
        if (problem is not null) ShowProblem(problem);
        else IsResultOpen = false;
    }

    public async Task CleanAsync(IReadOnlyList<WindowsCleanupItemViewModel> items)
    {
        var (result, problem) = await _state.CleanWindowsAsync(items);
        if (problem is not null && result.Removed.Count == 0)
        {
            ShowProblem(problem);
            return;
        }
        ResultFailures.Clear();
        foreach (var failure in result.Failures) ResultFailures.Add(failure);
        ResultHasFailures = result.Failures.Count > 0;
        ResultTitle = result.Removed.Count == 0 ? "Nothing was cleaned" : $"Freed {Format.Bytes(result.FreedBytes)}";
        ResultMessage = result.Removed.Count == 0
            ? "The reasons are below."
            : $"Windows cleaned {Format.Count(result.Removed.Count, "item", "items")}.";
        IsResultOpen = true;
    }

    private void ShowProblem(string problem)
    {
        ResultFailures.Clear();
        ResultHasFailures = false;
        ResultTitle = "Nothing changed";
        ResultMessage = problem;
        IsResultOpen = true;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => RaiseDerived();

    private void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(IsChecking), nameof(IsCleaning), nameof(IsBusy), nameof(IsChecked), nameof(SelectedCount), nameof(CanClean),
            nameof(CleanButtonText), nameof(CheckButtonText), nameof(Summary),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
