using Tidy.Core.Apps;
using Tidy.Core.Platform;
using Tidy.Core.Safety;

namespace Tidy.Core.Leftovers;

/// <summary>Finds leftovers on this PC, and remembers what is installed for next time.</summary>
public sealed class LeftoverScanner(DeletionSafetyPolicy policy, AppCatalog catalog)
{
    private UserLocations Locations => policy.Locations;

    public LeftoverFinder Finder { get; } = new(policy, SharedRoots.Current());

    public List<LeftoverFolder> Scan(CancellationToken cancellation = default) =>
        Finder.Find(ReadInputs(remember: true), cancellation: cancellation).ToList();

    /// <summary>
    /// Records what is installed now. Run before uninstalling, so the folders an app
    /// registered are known even when Tidy never scanned for leftovers while it was there.
    /// </summary>
    public void Remember() => ReadInputs(remember: true);

    public LeftoverPolicy Policy() => new(Finder, () => ReadInputs(remember: false));

    private LeftoverInputs ReadInputs(bool remember)
    {
        var now = DateTime.UtcNow;
        var installed = AppFootprint.ReadInstalled(Locations, catalog);
        var known = KnownApps.Load(Locations.TidyData);
        var gone = known.Gone(installed);
        if (remember)
        {
            known.Record(installed, now);
            try
            {
                known.Save(Locations.TidyData);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return new LeftoverInputs
        {
            Installed = installed,
            Gone = gone,
            KnownFolders = catalog.Leftovers,
            RunningExecutables = RunningApps.ExecutablePaths(),
            NowUtc = now,
        };
    }
}
