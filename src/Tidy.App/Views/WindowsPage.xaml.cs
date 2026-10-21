using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tidy.App.ViewModels;

namespace Tidy.App.Views;

public sealed partial class WindowsPage : Page
{
    public WindowsViewModel Vm { get; } = App.Windows;

    public WindowsPage()
    {
        InitializeComponent();
    }

    // x:Bind function bindings. Instance methods: the generated code calls them on the page.
    public bool Not(bool value) => !value;

    public Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public InfoBarSeverity ResultSeverity(bool hasFailures) =>
        hasFailures ? InfoBarSeverity.Warning : InfoBarSeverity.Success;

    private void OnRowCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: WindowsCleanupItemViewModel item } box) item.IsSelected = box.IsChecked == true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WindowsCleanupItemViewModel { CanSelect: true } item) item.IsSelected = !item.IsSelected;
    }

    private async void OnCheck(object sender, RoutedEventArgs e) => await Vm.CheckAsync();

    private async void OnClean(object sender, RoutedEventArgs e)
    {
        var items = Vm.SelectedItems();
        if (items.Count == 0) return;
        if (await ConfirmDialog.ConfirmWindowsCleanupAsync(XamlRoot, items)) await Vm.CleanAsync(items);
    }
}
