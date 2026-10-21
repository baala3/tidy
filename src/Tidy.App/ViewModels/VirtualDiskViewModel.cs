using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.VirtualDisks;

namespace Tidy.App.ViewModels;

public sealed partial class VirtualDiskViewModel(VirtualDisk disk) : ObservableObject
{
    public VirtualDisk Disk { get; } = disk;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    public string Name => Disk.Name;
    public bool CanSelect => Disk.IsWorthCompacting;
    public bool IsCompact => !CanSelect;
    public bool HasKnownSize => Disk.KnownReclaimableBytes > 0;
    public bool SizeAfterTrim => CanSelect && !HasKnownSize;
    public string Glyph => Disk.Owner == VirtualDiskOwner.Docker ? "" : "";
    public string SizeText => HasKnownSize ? Format.Bytes(Disk.KnownReclaimableBytes) : "";
    public string Subtitle => $"{OwnerText} · {Format.Bytes(Disk.FileBytes)} on disk · {Format.Bytes(Disk.Usage.InUseBytes)} in use";

    private string OwnerText => Disk.Owner switch
    {
        VirtualDiskOwner.Wsl => "Linux distro",
        VirtualDiskOwner.Docker => "Docker Desktop",
        _ => throw new ArgumentOutOfRangeException(nameof(disk)),
    };

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanSelect) IsSelected = false;
    }
}
