using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tidy.App.Services;
using Tidy.App.ViewModels;

namespace Tidy.App.Views;

public sealed partial class AppsPage : Page
{
    public AppsViewModel Vm { get; } = App.Apps;

    public AppsPage()
    {
        InitializeComponent();
        SortBox.ItemsSource = AppsViewModel.SortOptions;
        SortBox.SelectedIndex = Vm.SortIndex;
    }

    // x:Bind function bindings. Instance methods: the generated code calls them on the page.
    public bool Not(bool value) => !value;

    public Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public bool? IsFilter(AppFilter filter, int which) => (int)filter == which;

    public InfoBarSeverity ResultSeverity(bool hasFailures) => hasFailures ? InfoBarSeverity.Warning : InfoBarSeverity.Success;

    private void OnFilterAll(object sender, RoutedEventArgs e) => SetFilter(AppFilter.All);
    private void OnFilterPreinstalled(object sender, RoutedEventArgs e) => SetFilter(AppFilter.Preinstalled);
    private void OnFilterUnused(object sender, RoutedEventArgs e) => SetFilter(AppFilter.Unused);

    private void SetFilter(AppFilter filter)
    {
        Vm.Filter = filter;
        Bindings.Update();
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs e) => Vm.SortIndex = Math.Max(0, SortBox.SelectedIndex);

    private void OnSelectSafe(object sender, RoutedEventArgs e)
    {
        Vm.ToggleSafeExtras();
        SelectSafe.IsChecked = Vm.SelectSafeExtrasState;
    }

    private void OnRowCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: AppViewModel app } box) app.IsSelected = box.IsChecked == true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppViewModel app) app.IsSelected = !app.IsSelected;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is AppViewModel app) app.LoadIcon();
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await Vm.ScanAsync();

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        var apps = Vm.SelectedApps();
        if (apps.Count == 0) return;
        if (await ConfirmDialog.ConfirmAppsAsync(XamlRoot, apps)) await Vm.RemoveAsync(apps);
    }

    private void OnOpenPrivacy(object sender, RoutedEventArgs e) => Shell.OpenUrl("ms-settings:privacy-general");
}
