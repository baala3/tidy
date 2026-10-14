using System.Runtime.InteropServices;
using System.Text;

namespace Tidy.Core.Platform;

/// <summary>
/// Path comparisons the way Windows does them: case-insensitive, and always on a
/// separator boundary, so <c>C:\Users\a\Cache</c> is not "under" <c>C:\Users\a\Ca</c>.
/// </summary>
public static class PathUtil
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// A full, canonical spelling with no trailing separator (except on a drive root).
    /// Returns null for anything that is not a plain local drive path: UNC shares,
    /// device paths, relative paths and malformed input are never acted on.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return null;
        if (!Path.IsPathFullyQualified(path)) return null;
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        if (full.Length < 3 || full[1] != ':' || !char.IsAsciiLetter(full[0])) return null;
        var root = Path.GetPathRoot(full)!;
        if (full.Length > root.Length) full = full.TrimEnd('\\');
        return full;
    }

    public static bool AreSame(string a, string b) => Comparer.Equals(a, b);

    /// <summary>True when <paramref name="path"/> is <paramref name="ancestor"/> or sits below it.</summary>
    public static bool IsSameOrUnder(string path, string ancestor)
    {
        if (path.Length < ancestor.Length) return false;
        if (!path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Length == ancestor.Length) return true;
        return ancestor.EndsWith('\\') || path[ancestor.Length] == '\\';
    }

    /// <summary>True when <paramref name="path"/> sits strictly below <paramref name="ancestor"/>.</summary>
    public static bool IsStrictlyUnder(string path, string ancestor) =>
        path.Length > ancestor.Length && IsSameOrUnder(path, ancestor);

    public static string[] Segments(string path) =>
        path.Split('\\', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Whether the entry at <paramref name="path"/> is itself a reparse point
    /// (symlink, junction, OneDrive placeholder, app execution alias). Tidy never
    /// sizes through one and never removes one, because the thing it points at is
    /// not what the row describes.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> or any folder between it and <paramref name="stopAt"/>
    /// is a reparse point. Used so a link planted inside an allowed folder cannot
    /// redirect a removal somewhere else.
    /// </summary>
    public static bool HasReparsePointBetween(string path, string stopAt)
    {
        var current = path;
        while (IsStrictlyUnder(current, stopAt))
        {
            if (IsReparsePoint(current)) return true;
            current = Path.GetDirectoryName(current)!;
        }
        return false;
    }

    /// <summary>Expands 8.3 short names (<c>JOHNSM~1</c>) so paths compare equal to their long spelling.</summary>
    public static string LongPath(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetLongPathNameW(path, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLongPathNameW(string lpszShortPath, StringBuilder lpszLongPath, int cchBuffer);
}
