using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using Tidy.Core.Platform;

namespace Tidy.Core.VirtualDisks;

/// <summary>What WSL knows about, read from where WSL itself keeps it.</summary>
public static class Wsl
{
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    /// <summary>WSL 2 distros registered for this user. WSL 1 distros have no disk image.</summary>
    public static IReadOnlyList<WslDistro> Distros()
    {
        var distros = new List<WslDistro>();
        using var lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
        if (lxss is null) return distros;
        foreach (var id in lxss.GetSubKeyNames())
        {
            using var key = lxss.OpenSubKey(id);
            if (key?.GetValue("DistributionName") is not string name || key.GetValue("BasePath") is not string basePath) continue;
            if (key.GetValue("Version") is int version && version != 2) continue;
            var folder = PathUtil.Normalize(basePath.StartsWith(@"\\?\", StringComparison.Ordinal) ? basePath[4..] : basePath);
            if (folder is null) continue;
            distros.Add(new WslDistro(name, folder, key.GetValue("VhdFileName") as string ?? "ext4.vhdx"));
        }
        return distros;
    }

    /// <summary>Distros running now. <c>wsl.exe</c> writes UTF-16.</summary>
    public static IReadOnlyList<string> Running() =>
        Run("wsl.exe", "--list --running --quiet", TimeSpan.FromSeconds(15), Encoding.Unicode) is { ExitCode: 0 } result
            ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

    /// <summary>Runs a program with no window and waits for it, up to <paramref name="timeout"/>.</summary>
    public static CommandResult Run(string file, string arguments, TimeSpan timeout, Encoding? encoding = null)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding ?? Encoding.UTF8,
                StandardErrorEncoding = encoding ?? Encoding.UTF8,
            });
            if (process is null) return new CommandResult(-1, "");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                return new CommandResult(-1, "timed out");
            }
            return new CommandResult(process.ExitCode, output.Result + error.Result);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CommandResult(-1, e.Message);
        }
    }
}
