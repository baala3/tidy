using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tidy.App.Services;
using Tidy.App.ViewModels;
using Tidy.Core.VirtualDisks;

namespace Tidy.App.Views;

public sealed partial class VirtualDisksPage : Page
{
    public VirtualDisksViewModel Vm { get; } = App.VirtualDisks;

    public VirtualDisksPage()
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
        if (sender is CheckBox { Tag: VirtualDiskViewModel disk } box) disk.IsSelected = box.IsChecked == true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VirtualDiskViewModel { CanSelect: true } disk) disk.IsSelected = !disk.IsSelected;
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await Vm.ScanAsync();

    private async void OnCompact(object sender, RoutedEventArgs e)
    {
        var disks = Vm.SelectedDisks();
        if (disks.Count == 0) return;
        var running = await Task.Run(Wsl.Running);
        var docker = await Task.Run(() => Tidy.Core.Platform.RunningApps.AnyRunning(["Docker Desktop"]));
        if (await ConfirmDialog.ConfirmCompactAsync(XamlRoot, disks, running, docker)) await Vm.CompactAsync(disks);
    }

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VirtualDiskViewModel disk }) Shell.ShowInExplorer(disk.Disk.Path);
    }
}
