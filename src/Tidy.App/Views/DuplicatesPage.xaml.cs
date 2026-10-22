using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Tidy.App.Services;
using Tidy.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tidy.App.Views;

public sealed partial class DuplicatesPage : Page
{
    public DuplicatesViewModel Vm { get; } = App.Duplicates;

    public DuplicatesPage()
    {
        InitializeComponent();
        GroupedCopies.Source = Vm.Visible;
        SortBox.ItemsSource = DuplicatesViewModel.SortOptions;
        SortBox.SelectedIndex = Vm.SortIndex;
    }

    // x:Bind function bindings. Instance methods: the generated code calls them on the page.
    public bool Not(bool value) => !value;

    public Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public InfoBarSeverity ResultSeverity(bool hasFailures) =>
        hasFailures ? InfoBarSeverity.Warning : InfoBarSeverity.Success;

    private void OnSortChanged(object sender, SelectionChangedEventArgs e) => Vm.SortIndex = Math.Max(0, SortBox.SelectedIndex);

    private void OnSelectExtras(object sender, RoutedEventArgs e)
    {
        Vm.ToggleExtras();
        SelectExtras.IsChecked = Vm.SelectExtrasState;
    }

    private void OnRowCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: DuplicateCopyViewModel copy } box) copy.IsSelected = box.IsChecked == true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DuplicateCopyViewModel { CanSelect: true } copy) copy.IsSelected = !copy.IsSelected;
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DuplicateCopyViewModel copy }) Shell.ShowInExplorer(copy.Path);
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await Vm.ScanAsync();

    private async void OnRecycle(object sender, RoutedEventArgs e)
    {
        var removals = Vm.SelectedRemovals();
        if (removals.Count == 0) return;
        if (await ConfirmDialog.ConfirmDuplicatesAsync(XamlRoot, removals, App.State.Duplicates)) await Vm.RecycleAsync(removals);
    }

    private void OnOpenRecycleBin(object sender, RoutedEventArgs e) => Shell.OpenRecycleBin();

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DuplicateCopyViewModel copy }) Shell.Open(copy.Path);
    }

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DuplicateCopyViewModel copy }) Shell.ShowInExplorer(copy.Path);
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DuplicateCopyViewModel copy }) return;
        var package = new DataPackage();
        package.SetText(copy.Path);
        Clipboard.SetContent(package);
    }
}
