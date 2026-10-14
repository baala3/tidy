using System.Diagnostics;

namespace Tidy.App.Services;

/// <summary>Hands things over to File Explorer.</summary>
public static class Shell
{
    public static void ShowInExplorer(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            Start("explorer.exe", $"/select,\"{path}\"");
        }
        else if (Path.GetDirectoryName(path) is { } parent && Directory.Exists(parent))
        {
            Start("explorer.exe", $"\"{parent}\"");
        }
    }

    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public static void OpenRecycleBin() => Start("explorer.exe", "shell:RecycleBinFolder");

    public static void OpenUrl(string url) => Open(url);

    private static void Start(string file, string arguments) =>
        Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true })?.Dispose();
}
