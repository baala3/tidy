using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Tidy.App.Services;
using Tidy.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tidy.App.Views;

public sealed partial class InstallersPage : Page
{
    public InstallersViewModel Vm { get; } = App.Installers;

    public InstallersPage()
    {
        InitializeComponent();
        SortBox.ItemsSource = InstallersViewModel.SortOptions;
        SortBox.SelectedIndex = Vm.SortIndex;
    }

    // x:Bind function bindings. Instance methods: the generated code calls them on the page.
    public bool Not(bool value) => !value;

    public Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public InfoBarSeverity ResultSeverity(bool hasFailures) =>
        hasFailures ? InfoBarSeverity.Warning : InfoBarSeverity.Success;

    private void OnSortChanged(object sender, SelectionChangedEventArgs e) => Vm.SortIndex = Math.Max(0, SortBox.SelectedIndex);

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        Vm.ToggleAll();
        SelectAll.IsChecked = Vm.SelectAllState;
    }

    private void OnRowCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: InstallerViewModel installer } box) installer.IsSelected = box.IsChecked == true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is InstallerViewModel installer) installer.IsSelected = !installer.IsSelected;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is InstallerViewModel installer) installer.LoadIcon();
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InstallerViewModel installer }) Shell.ShowInExplorer(installer.Path);
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await Vm.ScanAsync();

    private async void OnRecycle(object sender, RoutedEventArgs e)
    {
        var installers = Vm.SelectedInstallers();
        if (installers.Count == 0) return;
        if (await ConfirmDialog.ConfirmInstallersAsync(XamlRoot, installers)) await Vm.RecycleAsync(installers);
    }

    private void OnOpenRecycleBin(object sender, RoutedEventArgs e) => Shell.OpenRecycleBin();

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: InstallerViewModel installer }) Shell.ShowInExplorer(installer.Path);
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: InstallerViewModel installer }) return;
        var package = new DataPackage();
        package.SetText(installer.Path);
        Clipboard.SetContent(package);
    }
}
