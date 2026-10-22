using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Tidy.App.Services;
using Tidy.Core.Models;

namespace Tidy.App.ViewModels;

public sealed partial class InstallerViewModel(InstallerFile installer) : ObservableObject
{
    public InstallerFile Installer { get; } = installer;

    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial ImageSource? Icon { get; private set; }

    public string Path => Installer.File.Path;
    public string Name => Installer.File.Name;
    public long SizeBytes => Installer.File.SizeBytes;
    public string SizeText => Format.Bytes(SizeBytes);
    public string ModifiedText => Installer.File.LastWriteTimeText();
    public string Subtitle => $"{Installer.File.SourceLabel} · {DisplayFolder()}";
    public bool HasNoIcon => Icon is null;

    /// <summary>"Mozilla Firefox 158.0 is installed".</summary>
    public string InstalledText => Installer.App.Version.Length > 0
        ? $"{Installer.App.Name} {Installer.App.Version} is installed"
        : $"{Installer.App.Name} is installed";

    public bool IsNewer => Installer.Age == InstallerAge.Newer;
    /// <summary>The date shows where a newer installer shows its tag instead.</summary>
    public bool ShowModified => !IsNewer;

    public string AgeText => Installer.Age switch
    {
        // Older by number is not always older: a small download-only installer numbers itself.
        InstallerAge.Older => $"This installer is version {Installer.Version}",
        InstallerAge.Same => "This installer is the same version",
        InstallerAge.Newer => $"This installer is version {Installer.Version}, newer than what you have. You may still want to run it.",
        InstallerAge.Unknown => "",
        _ => throw new ArgumentOutOfRangeException(nameof(installer)),
    };

    public bool HasAgeText => AgeText.Length > 0;

    private bool _iconRequested;

    /// <summary>Loads the installer's own icon the first time its row is shown.</summary>
    public async void LoadIcon()
    {
        if (_iconRequested) return;
        _iconRequested = true;
        Icon = await ShellIcons.ImageAsync(Path, 64);
        OnPropertyChanged(nameof(HasNoIcon));
    }

    private string DisplayFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Installer.File.Folder;
        return folder.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + folder[home.Length..] : folder;
    }
}
