using Tidy.Core.Apps;
using Tidy.Core.Models;
using Tidy.Core.Safety;

namespace Tidy.Core.Scanning;

/// <summary>
/// Installers in Documents, Desktop, Downloads, Videos, Music and Pictures for apps that are
/// already installed. Installers for anything else are left out: they may be how the user
/// means to install it later. So are programs that only share an app's name, like a launcher.
/// </summary>
public sealed class InstallerScanner(LargeFilePolicy policy)
{
    private static readonly HashSet<LargeFileKind> Installers = [LargeFileKind.Installer];

    public IEnumerable<InstallerFile> Scan(IReadOnlyList<InstalledApp> apps, CancellationToken cancellation = default)
    {
        var files = new LargeFileScanner(policy) { MinimumBytes = 1, Kinds = Installers }.Scan(cancellation);
        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            if (InstallerIdentity.Read(file.Path) is not { IsInstaller: true } identity) continue;
            if (InstallerMatcher.Match(file.Path, identity, apps) is { } app) yield return new InstallerFile(file, app, identity.Version);
        }
    }
}
