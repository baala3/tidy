using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Catalog;
using Tidy.Core.Models;

namespace Tidy.App.ViewModels;

/// <summary>One Disk Cleanup handler on the Windows page. Its size is unknown until checked.</summary>
public sealed partial class WindowsCleanupItemViewModel(WindowsCleanupEntry entry) : ObservableObject
{
    public WindowsCleanupEntry Entry { get; } = entry;

    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial long? SizeBytes { get; set; }
    [ObservableProperty] public partial bool IsWorking { get; set; }

    public string Key => Entry.Key;
    public string Name => Entry.Name;
    public string Explanation => Entry.Explanation;
    public bool IsSafe => Entry.Safety == SafetyLevel.Safe;
    public bool IsCheckFirst => Entry.Safety == SafetyLevel.CheckFirst;
    public bool CanSelect => SizeBytes > 0;
    public bool HasSize => SizeBytes > 0;
    public bool ShowsNote => !HasSize && !IsWorking;
    public string SizeText => SizeBytes is > 0 and var bytes ? Format.Bytes(bytes) : "";
    public string NoteText => SizeBytes is null ? "Not checked" : "Nothing to clean";

    partial void OnSizeBytesChanged(long? value)
    {
        if (!CanSelect) IsSelected = false;
        RaiseAll();
    }

    partial void OnIsWorkingChanged(bool value) => RaiseAll();

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanSelect) IsSelected = false;
    }

    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(CanSelect), nameof(HasSize), nameof(ShowsNote), nameof(SizeText), nameof(NoteText) }) OnPropertyChanged(name);
    }
}
