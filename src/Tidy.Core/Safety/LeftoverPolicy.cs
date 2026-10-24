using Tidy.Core.Leftovers;
using Tidy.Core.Platform;

namespace Tidy.Core.Safety;

/// <summary>
/// Gates removing folders apps left behind. Right before anything moves, Tidy looks at what
/// is installed and running again and finds the leftovers anew, so a folder whose app came
/// back, started running or wrote to it since the scan is refused. Folders in ProgramData and
/// Program Files are never removed: only an administrator can change them.
/// </summary>
public sealed class LeftoverPolicy(LeftoverFinder finder, Func<LeftoverInputs> readInputs)
{
    /// <summary>A check for each of <paramref name="paths"/>: null to go ahead, or why not.</summary>
    public Func<string, string?> Refusals(IReadOnlyList<string> paths)
    {
        var only = paths.Select(PathUtil.Normalize).OfType<string>().ToHashSet(PathUtil.Comparer);
        var found = finder.Find(readInputs(), only).ToDictionary(f => f.Path, PathUtil.Comparer);
        return path =>
        {
            if (!Directory.Exists(path)) return "It no longer exists.";
            if (!found.TryGetValue(path, out var folder)) return "An app on this PC may still use it, so Tidy left it alone.";
            return folder.NeedsAdministrator ? "Only an administrator can remove it, so Tidy left it alone." : null;
        };
    }
}
