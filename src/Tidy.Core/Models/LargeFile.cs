namespace Tidy.Core.Models;

public enum LargeFileKind
{
    Video,
    Audio,
    Image,
    Pdf,
    Archive,
    DiskImage,
    Installer,
    Document,
    AiModel,
    Other,
}

public sealed record LargeFile(string Path, long SizeBytes, DateTime LastModifiedUtc, LargeFileKind Kind, string SourceLabel)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";

    private static readonly Dictionary<string, LargeFileKind> KindsByExtension = Build();

    public static LargeFileKind KindFor(string path) =>
        KindsByExtension.TryGetValue(System.IO.Path.GetExtension(path), out var kind) ? kind : LargeFileKind.Other;

    private static Dictionary<string, LargeFileKind> Build()
    {
        var map = new Dictionary<string, LargeFileKind>(StringComparer.OrdinalIgnoreCase);
        void Add(LargeFileKind kind, params string[] extensions)
        {
            foreach (var e in extensions) map["." + e] = kind;
        }
        Add(LargeFileKind.Video, "mp4", "mov", "mkv", "avi", "wmv", "webm", "m4v", "mpg", "mpeg", "flv", "mts", "m2ts", "3gp");
        Add(LargeFileKind.Audio, "mp3", "wav", "flac", "aac", "m4a", "ogg", "wma", "aiff", "opus");
        Add(LargeFileKind.Image, "jpg", "jpeg", "png", "gif", "bmp", "tif", "tiff", "heic", "webp", "psd", "raw", "cr2", "cr3", "nef", "arw", "dng");
        Add(LargeFileKind.Pdf, "pdf");
        Add(LargeFileKind.Archive, "zip", "rar", "7z", "tar", "gz", "tgz", "xz", "bz2", "zst", "cab");
        Add(LargeFileKind.DiskImage, "iso", "img", "vhd", "vhdx", "vmdk", "qcow2", "wim", "esd");
        Add(LargeFileKind.Installer, "exe", "msi", "msix", "msixbundle", "appx", "appxbundle");
        Add(LargeFileKind.Document, "doc", "docx", "xls", "xlsx", "ppt", "pptx", "key", "pages", "numbers", "txt", "csv", "json", "log", "epub");
        Add(LargeFileKind.AiModel, "gguf", "safetensors", "ckpt", "onnx", "pt", "pth", "ggml");
        return map;
    }
}
