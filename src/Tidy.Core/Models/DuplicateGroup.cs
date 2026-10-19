using System.Text.RegularExpressions;

namespace Tidy.Core.Models;

public sealed record DuplicateCopy(LargeFile File, DateTime CreatedUtc)
{
    public string Path => File.Path;
}

/// <summary>
/// Files with identical contents. Removing every copy but one frees
/// <see cref="WastedBytes"/>; the one left is up to the user, with
/// <see cref="SuggestedKeep"/> as the starting point.
/// </summary>
public sealed partial record DuplicateGroup(string Hash, long SizeBytes, IReadOnlyList<DuplicateCopy> Copies)
{
    public long WastedBytes => SizeBytes * (Copies.Count - 1);

    /// <summary>
    /// The copy that looks like the original: no "(1)" or "- Copy" in its name, kept somewhere
    /// other than Downloads or the Desktop, created first, with the shortest path.
    /// </summary>
    public DuplicateCopy SuggestedKeep => Copies
        .OrderBy(c => HasCopyMarker(c.File.Name))
        .ThenBy(c => SourceRank(c.File.SourceLabel))
        .ThenBy(c => c.CreatedUtc)
        .ThenBy(c => c.Path.Length)
        .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
        .First();

    /// <summary>"photo (1).jpg", "report - Copy.pdf", "Copy of notes.txt", "song copy 2.mp3". Not "Season (2019).mkv".</summary>
    public static bool HasCopyMarker(string fileName) => CopyMarker().IsMatch(System.IO.Path.GetFileNameWithoutExtension(fileName));

    /// <summary>Folders people sort things into first; the drop zones where stray copies land last.</summary>
    private static int SourceRank(string label) => label switch
    {
        "Downloads" => 2,
        "Desktop" => 1,
        _ => 0,
    };

    [GeneratedRegex(@"(\s\(\d{1,2}\)|\s-\sCopy(\s\(\d{1,2}\))?|\scopy(\s\d+)?)$|^Copy of\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CopyMarker();
}

public readonly record struct DuplicateScanProgress(int FilesCompared, int FilesToCompare);
