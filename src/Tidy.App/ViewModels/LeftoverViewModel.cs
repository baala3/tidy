using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Leftovers;

namespace Tidy.App.ViewModels;

public sealed partial class LeftoverViewModel(LeftoverFolder folder) : ObservableObject
{
    public LeftoverFolder Folder { get; } = folder;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>Set for leftovers of apps uninstalled from Tidy since it opened.</summary>
    public bool IsFromJustRemoved { get; init; }

    public string Path => Folder.Path;
    public string Name => System.IO.Path.GetFileName(Folder.Path);
    public long SizeBytes => Folder.SizeBytes;
    public string SizeText => SizeBytes > 0 ? Format.Bytes(SizeBytes) : "Empty";
    public bool NeedsAdministrator => Folder.NeedsAdministrator;
    public bool CanSelect => !NeedsAdministrator;
    public bool IsCheckFirst => !NeedsAdministrator;
    public string ModifiedText => "Changed " + Folder.LastModifiedUtc.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    public string Subtitle => $"From {Folder.AppName} · {DisplayFolder()}";
    public string CheckTooltip => CanSelect ? "" : "Only an administrator can remove it. Show it in File Explorer to remove it yourself.";

    /// <summary>Why Tidy thinks the folder was the app's, in the user's words.</summary>
    public string EvidenceText
    {
        get
        {
            var app = Folder.AppName;
            var why = Folder.Evidence switch
            {
                LeftoverEvidence.RegisteredFolder => $"{app} told Windows this folder was its own.",
                LeftoverEvidence.PublisherAndName => $"Named after {app}, in its publisher's folder.",
                LeftoverEvidence.InstallFolderName => $"Named like the folder {app} was installed in.",
                LeftoverEvidence.Name => $"Named after {app}.",
                LeftoverEvidence.OrphanedInstall => "Its uninstaller is still here, but Windows no longer lists the app.",
                LeftoverEvidence.KnownAppFolder => $"Where {app} keeps its data.",
                _ => throw new ArgumentOutOfRangeException(nameof(folder)),
            };
            var gone = Folder.AppLastSeenUtc is { } seen
                ? $" {app} is no longer installed; Tidy last saw it {Format.Age(seen, DateTime.UtcNow)}."
                : " No installed app uses it.";
            return why + gone;
        }
    }

    private string DisplayFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var parent = System.IO.Path.GetDirectoryName(Folder.Path) ?? Folder.Path;
        return parent.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + parent[home.Length..] : parent;
    }
}
