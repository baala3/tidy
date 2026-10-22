using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Tidy.App.Services;
using Tidy.Core.Models;

namespace Tidy.App.ViewModels;

public sealed partial class LargeFileViewModel(LargeFile file) : ObservableObject
{
    public LargeFile File { get; } = file;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Name => File.Name;
    public string Path => File.Path;
    public string Folder => File.Folder;
    public long SizeBytes => File.SizeBytes;
    public string SizeText => Format.Bytes(File.SizeBytes);
    public string Glyph => Glyphs.ForFile(File.Kind);
    public string KindLabel => Glyphs.Label(File.Kind);
    public string ModifiedText => File.LastWriteTimeText();
    public string Subtitle => $"{File.SourceLabel} · {DisplayFolder()}";

    /// <summary>"~\OneDrive\Pictures": the folder alone, where the source is already in the path.</summary>
    public string FolderText => DisplayFolder();

    private string DisplayFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return File.Folder.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + File.Folder[home.Length..] : File.Folder;
    }
}

internal static class LargeFileText
{
    public static string LastWriteTimeText(this LargeFile file) =>
        "Modified " + file.LastModifiedUtc.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
}
