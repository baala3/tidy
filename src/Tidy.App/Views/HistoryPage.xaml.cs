using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tidy.App.Services;
using Tidy.App.ViewModels;
using Tidy.Core.Scheduling;

namespace Tidy.App.Views;

/// <summary>Every clean, newest first. Read from history.json each time, since the background run adds to it too.</summary>
public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var folder = App.State.Locations.TidyData;
        var entries = await Task.Run(() => CleanHistory.Load(folder));
        Entries.ItemsSource = entries.Select(entry => new HistoryEntryViewModel(entry)).ToList();
        EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var monthAgo = DateTime.UtcNow.AddDays(-30);
        var freed = entries.Where(entry => entry.WhenUtc >= monthAgo).Sum(entry => entry.FreedBytes);
        SummaryText.Text = entries.Count == 0
            ? "What Tidy has cleaned"
            : freed > 0 ? $"{Format.Bytes(freed)} freed in the last 30 days" : "Nothing freed in the last 30 days";
    }

    // Only a list of names cut off at two lines gets a Show all link.
    private void OnItemsTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.DataContext is HistoryEntryViewModel entry) entry.IsTrimmed = sender.IsTextTrimmed;
    }

    private void OnToggleItems(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryEntryViewModel entry }) entry.IsExpanded = !entry.IsExpanded;
    }
}
