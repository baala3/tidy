using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Tidy.App.Services;
using Tidy.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tidy.App.Views;

public sealed partial class LeftoversPage : Page
{
    public LeftoversViewModel Vm { get; } = App.Leftovers;

    public LeftoversPage()
    {
        InitializeComponent();
        SortBox.ItemsSource = LeftoversViewModel.SortOptions;
        SortBox.SelectedIndex = Vm.SortIndex;
    }

    // x:Bind function bindings. Instance methods: the generated code calls them on the page.
    public bool Not(bool value) => !value;

    public Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public bool? IsFilter(LeftoverFilter filter, int which) => (int)filter == which;

    public InfoBarSeverity ResultSeverity(bool hasFailures) =>
        hasFailures ? InfoBarSeverity.Warning : InfoBarSeverity.Success;

    private void OnFilterAll(object sender, RoutedEventArgs e) => SetFilter(LeftoverFilter.All);
    private void OnFilterJustRemoved(object sender, RoutedEventArgs e) => SetFilter(LeftoverFilter.JustRemoved);

    private void SetFilter(LeftoverFilter filter)
    {
        Vm.Filter = filter;
        Bindings.Update();
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs e) => Vm.SortIndex = Math.Max(0, SortBox.SelectedIndex);

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        Vm.ToggleAll();
        SelectAll.IsChecked = Vm.SelectAllState;
    }

    private void OnRowCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: LeftoverViewModel leftover } box) leftover.IsSelected = box.IsChecked == true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LeftoverViewModel { CanSelect: true } leftover) leftover.IsSelected = !leftover.IsSelected;
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LeftoverViewModel leftover }) Shell.ShowInExplorer(leftover.Path);
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await Vm.ScanAsync();

    private async void OnRecycle(object sender, RoutedEventArgs e)
    {
        var leftovers = Vm.SelectedLeftovers();
        if (leftovers.Count == 0) return;
        if (await ConfirmDialog.ConfirmLeftoversAsync(XamlRoot, leftovers)) await Vm.RecycleAsync(leftovers);
    }

    private void OnOpenRecycleBin(object sender, RoutedEventArgs e) => Shell.OpenRecycleBin();

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LeftoverViewModel leftover }) Shell.ShowInExplorer(leftover.Path);
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LeftoverViewModel leftover }) return;
        var package = new DataPackage();
        package.SetText(leftover.Path);
        Clipboard.SetContent(package);
    }
}
