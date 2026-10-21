using Tidy.Core.Models;

namespace Tidy.App.Services;

/// <summary>Segoe Fluent Icons code points for each kind of row.</summary>
public static class Glyphs
{
    public const string Overview = "\uE80F";
    public const string AppCaches = "\uE7B8";
    public const string DevTools = "\uE943";
    public const string LargeFiles = "\uE8A5";
    public const string Apps = "\uE71D";
    public const string OneDrive = "\uE753";
    public const string VirtualDisks = "\uE756";
    public const string Settings = "\uE713";
    public const string About = "\uE946";
    public const string RecycleBin = "\uE74D";
    public const string Scan = "\uE72C";
    public const string Folder = "\uE8B7";
    public const string Project = "\uE8F1";
    public const string Safe = "\uE73E";
    public const string CheckFirst = "\uE897";
    public const string Warning = "\uE7BA";
    public const string Ellipsis = "\uE712";
    public const string Search = "\uE721";
    public const string Sort = "\uE8CB";

    public static string ForKind(string kind) => kind switch
    {
        "browser" => "\uE774",
        "temp" => "\uE823",
        "logs" => "\uE9D9",
        "graphics" => "\uE7F4",
        "games" => "\uE7FC",
        "media" => "\uE714",
        "packages" => "\uE7B8",
        "build" => "\uE90F",
        "editor" => "\uE943",
        "devices" => "\uE8EA",
        "project" => Project,
        "app" => "\uE737",
        _ => Folder,
    };

    public static string ForFile(LargeFileKind kind) => kind switch
    {
        LargeFileKind.Video => "\uE714",
        LargeFileKind.Audio => "\uE8D6",
        LargeFileKind.Image => "\uEB9F",
        LargeFileKind.Pdf => "\uEA90",
        LargeFileKind.Archive => "\uF012",
        LargeFileKind.DiskImage => "\uEDA2",
        LargeFileKind.Installer => "\uE896",
        LargeFileKind.Document => "\uE8A5",
        LargeFileKind.AiModel => "\uE99A",
        LargeFileKind.Other => "\uE7C3",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Label(LargeFileKind kind) => kind switch
    {
        LargeFileKind.Video => "Videos",
        LargeFileKind.Audio => "Audio",
        LargeFileKind.Image => "Images",
        LargeFileKind.Pdf => "PDFs",
        LargeFileKind.Archive => "Archives",
        LargeFileKind.DiskImage => "Disk images",
        LargeFileKind.Installer => "Installers",
        LargeFileKind.Document => "Documents",
        LargeFileKind.AiModel => "AI models",
        LargeFileKind.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
