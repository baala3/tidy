using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Tidy.App.Services;
using Tidy.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Tidy.App.Views;

public sealed partial class OneDrivePage : Page
{
    public OneDriveViewModel Vm { get; } = App.OneDrive;

    public OneDrivePage()
    {
        InitializeComponent();
        SizeBox.ItemsSource = Vm.SizeOptions.Select(o => o.Label).ToList();
        AgeBox.ItemsSource = FileListViewModel.AgeOptions.Select(o => o.Label).ToList();
        SortBox.ItemsSource = FileListViewModel.SortOptions;
        SizeBox.SelectedIndex = Vm.SizeIndex;
        AgeBox.SelectedIndex = Vm.AgeIndex;
        SortBox.SelectedIndex = Vm.SortIndex;
    }

    // x:Bind function bindings. Instance methods: the generated code calls them on the page.
    public bool Not(bool value) => !value;

    public Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public InfoBarSeverity ResultSeverity(bool hasFailures) =>
        hasFailures ? InfoBarSeverity.Warning : InfoBarSeverity.Success;

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e) => Vm.SizeIndex = Math.Max(0, SizeBox.SelectedIndex);
    private void OnAgeChanged(object sender, SelectionChangedEventArgs e) => Vm.AgeIndex = Math.Max(0, AgeBox.SelectedIndex);
    private void OnSortChanged(object sender, SelectionChangedEventArgs e) => Vm.SortIndex = Math.Max(0, SortBox.SelectedIndex);

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KindChip chip }) Vm.KindFilter = chip.Kind;
    }

    private void OnSelectShown(object sender, RoutedEventArgs e)
    {
        Vm.ToggleShown();
        SelectShown.IsChecked = Vm.SelectShownState;
    }

    private void OnRowCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: LargeFileViewModel file } box) file.IsSelected = box.IsChecked == true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LargeFileViewModel file) file.IsSelected = !file.IsSelected;
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LargeFileViewModel file }) Shell.ShowInExplorer(file.Path);
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await Vm.ScanAsync();

    private async void OnFreeUp(object sender, RoutedEventArgs e)
    {
        var files = Vm.SelectedFiles();
        if (files.Count == 0) return;
        if (await ConfirmDialog.ConfirmFreeUpAsync(XamlRoot, files)) await Vm.FreeUpAsync(files);
    }

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LargeFileViewModel file }) Shell.ShowInExplorer(file.Path);
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LargeFileViewModel file }) return;
        var package = new DataPackage();
        package.SetText(file.Path);
        Clipboard.SetContent(package);
    }
}
