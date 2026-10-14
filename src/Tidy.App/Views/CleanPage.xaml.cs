using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tidy.App.Services;
using Tidy.App.ViewModels;
using Tidy.Core.Models;
using Windows.ApplicationModel.DataTransfer;

namespace Tidy.App.Views;

/// <summary>App Caches and Dev Tools. The category arrives as the navigation parameter.</summary>
public sealed partial class CleanPage : Page
{
    public CleanListViewModel Vm { get; private set; } = null!;

    public CleanPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Vm = App.CleanList((ScanCategory)e.Parameter);
        GroupedItems.Source = Vm.Groups;
        SortBox.SelectedIndex = (int)Vm.Sort;
        Bindings.Update();
    }

    // x:Bind function bindings. Instance methods: the generated code calls them on the page.
    public Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public bool CanScan(bool busy) => !busy;

    public bool? IsFilter(SafetyFilter filter, int which) => (int)filter == which;

    public InfoBarSeverity ResultSeverity(bool hasFailures) =>
        hasFailures ? InfoBarSeverity.Warning : InfoBarSeverity.Success;

    private void OnFilterAll(object sender, RoutedEventArgs e) => SetFilter(SafetyFilter.All);
    private void OnFilterSafe(object sender, RoutedEventArgs e) => SetFilter(SafetyFilter.Safe);
    private void OnFilterCheckFirst(object sender, RoutedEventArgs e) => SetFilter(SafetyFilter.CheckFirst);

    private void SetFilter(SafetyFilter filter)
    {
        Vm.Filter = filter;
        // A toggle button flips itself on click; put every chip back in step with the filter.
        Bindings.Update();
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && SortBox.SelectedIndex >= 0) Vm.Sort = (SortOrder)SortBox.SelectedIndex;
    }

    private void OnSelectAllSafe(object sender, RoutedEventArgs e)
    {
        Vm.SelectAllSafeState = true;
        SelectAllSafe.IsChecked = Vm.SelectAllSafeState;
    }

    private void OnAlsoSelectCheckFirst(object sender, RoutedEventArgs e) => Vm.SelectAllCheckFirst();

    private void OnRowCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: ItemViewModel item } box) item.IsSelected = box.IsChecked == true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ItemViewModel { CanSelect: true } item) item.IsSelected = !item.IsSelected;
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await Vm.ScanAsync();

    private async void OnClean(object sender, RoutedEventArgs e)
    {
        var items = Vm.SelectedItems();
        if (items.Count == 0) return;
        if (await ConfirmDialog.ConfirmCleanAsync(XamlRoot, items)) await Vm.CleanAsync(items);
    }

    private void OnOpenRecycleBin(object sender, RoutedEventArgs e) => Shell.OpenRecycleBin();

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ItemViewModel item }) Shell.ShowInExplorer(item.FirstPath);
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ItemViewModel item }) return;
        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, item.Item.Paths));
        Clipboard.SetContent(package);
    }
}
