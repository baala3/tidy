using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace Tidy.Core.Apps;

public sealed record AppRemovalResult(InstalledApp App, bool Removed, string? Problem);

/// <summary>
/// Removes apps the way Windows does. Store apps go through the package manager, for this
/// account only, and come back from the Microsoft Store. Desktop programs run their own
/// uninstaller, the same one Settings → Apps opens, so nothing is half removed by hand.
/// Uninstalled apps cannot go to the Recycle Bin, and the UI says so before anything runs.
/// </summary>
public sealed class AppRemover(AppCatalog catalog)
{
    public async Task<AppRemovalResult> RemoveAsync(InstalledApp app, CancellationToken cancellation = default) => app.Kind switch
    {
        AppKind.Store => await RemoveStoreAppAsync(app),
        AppKind.Desktop => await RunUninstallerAsync(app, cancellation),
        _ => throw new ArgumentOutOfRangeException(nameof(app)),
    };

    private async Task<AppRemovalResult> RemoveStoreAppAsync(InstalledApp app)
    {
        var manager = new PackageManager();
        // Looked up again: the package must still be installed and still removable now.
        var package = manager.FindPackagesForUser(string.Empty).FirstOrDefault(p => p.Id.FullName == app.Id);
        if (package is null) return new AppRemovalResult(app, true, null);
        if (package.IsFramework || package.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System || catalog.IsProtectedPackage(package.Id.Name))
        {
            return new AppRemovalResult(app, false, "Windows or other apps depend on it, so Tidy left it alone.");
        }
        try
        {
            var result = await manager.RemovePackageAsync(app.Id);
            return result.IsRegistered
                ? new AppRemovalResult(app, false, string.IsNullOrWhiteSpace(result.ErrorText) ? "Windows could not remove it." : result.ErrorText.Trim())
                : new AppRemovalResult(app, true, null);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return new AppRemovalResult(app, false, $"Windows could not remove it ({e.Message.Trim()}).");
        }
    }

    private async Task<AppRemovalResult> RunUninstallerAsync(InstalledApp app, CancellationToken cancellation)
    {
        if (catalog.IsProtectedDesktopApp(app.Name)) return new AppRemovalResult(app, false, "Other apps depend on it, so Tidy left it alone.");
        // Read again from the registry, so the command run is the one installed now.
        var command = CurrentUninstallCommand(app.Id);
        if (command is null) return new AppRemovalResult(app, true, null);

        var (file, arguments) = SplitCommand(command);
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return new AppRemovalResult(app, false, "Cancelled: Windows asked for permission and it was declined.");
        }
        catch (Exception e) when (e is Win32Exception or FileNotFoundException)
        {
            return new AppRemovalResult(app, false, $"Its uninstaller could not be started ({e.Message.Trim()}).");
        }
        if (process is not null)
        {
            using (process) await process.WaitForExitAsync(cancellation);
        }
        // Many uninstallers hand off to a copy of themselves and exit at once; give the entry time to go.
        for (var i = 0; i < 10 && CurrentUninstallCommand(app.Id) is not null; i++) await Task.Delay(500, cancellation);
        return CurrentUninstallCommand(app.Id) is null
            ? new AppRemovalResult(app, true, null)
            : new AppRemovalResult(app, false, "Still installed. If its uninstaller is still open, finish it there, then scan again.");
    }

    internal static string? CurrentUninstallCommand(string id)
    {
        var slash = id.IndexOf('\\', StringComparison.Ordinal);
        if (slash < 0) return null;
        var (hive, view) = id[..slash] switch
        {
            "HKLM64" => (RegistryHive.LocalMachine, RegistryView.Registry64),
            "HKLM32" => (RegistryHive.LocalMachine, RegistryView.Registry32),
            "HKCU" => (RegistryHive.CurrentUser, RegistryView.Registry64),
            _ => throw new ArgumentException($"Unknown app id {id}", nameof(id)),
        };
        using var key = RegistryKey.OpenBaseKey(hive, view).OpenSubKey($@"{AppScanner.UninstallKey}\{id[(slash + 1)..]}");
        return key?.GetValue("UninstallString") as string;
    }

    /// <summary>
    /// Splits an UninstallString into program and arguments. MSI entries are switched from
    /// /I (which opens the repair dialog for some products) to /X, which uninstalls.
    /// </summary>
    internal static (string File, string Arguments) SplitCommand(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        string file, arguments;
        if (command.StartsWith('"'))
        {
            var close = command.IndexOf('"', 1);
            file = close > 0 ? command[1..close] : command.Trim('"');
            arguments = close > 0 ? command[(close + 1)..].Trim() : "";
        }
        else
        {
            var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            var end = exe >= 0 ? exe + 4 : command.IndexOf(' ', StringComparison.Ordinal);
            if (end <= 0 || end >= command.Length) return (command, "");
            file = command[..end];
            arguments = command[end..].Trim();
        }
        if (Path.GetFileName(file).Equals("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
        {
            arguments = System.Text.RegularExpressions.Regex.Replace(arguments, @"(?i)/I\s*(\{)", "/X$1");
        }
        return (file, arguments);
    }
}
