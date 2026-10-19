using System.Security.Cryptography;

namespace Tidy.Core.Scanning;

/// <summary>Reads files to tell whether their contents match. Never writes.</summary>
public static class FileContent
{
    /// <summary>How much of each end <see cref="SampleHash"/> reads.</summary>
    public const int SampleBytes = 64 * 1024;

    private const int BufferBytes = 1024 * 1024;

    /// <summary>
    /// A hash of the first and last 64 KB. Files that differ there differ, so this rules out
    /// most look-alikes of the same size before anything is read in full.
    /// </summary>
    public static string? SampleHash(string path, CancellationToken cancellation = default) => Read(path, stream =>
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SampleBytes];
        hash.AppendData(buffer, 0, stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false));
        if (stream.Length > SampleBytes * 2)
        {
            stream.Seek(-SampleBytes, SeekOrigin.End);
            hash.AppendData(buffer, 0, stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false));
        }
        else if (stream.Length > SampleBytes)
        {
            hash.AppendData(buffer, 0, stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false));
        }
        cancellation.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    });

    /// <summary>SHA-256 of the whole file, or null when it can't be read.</summary>
    public static string? FullHash(string path, CancellationToken cancellation = default) => Read(path, stream =>
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferBytes];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    });

    /// <summary>Whether two files hold exactly the same bytes, compared in full.</summary>
    public static bool SameContent(string a, string b, CancellationToken cancellation = default)
    {
        try
        {
            using var first = Open(a);
            using var second = Open(b);
            if (first.Length != second.Length) return false;
            var left = new byte[BufferBytes];
            var right = new byte[BufferBytes];
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                var read = first.ReadAtLeast(left, left.Length, throwOnEndOfStream: false);
                var other = second.ReadAtLeast(right, read, throwOnEndOfStream: false);
                if (read != other) return false;
                if (read == 0) return true;
                if (!left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, read))) return false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static T? Read<T>(string path, Func<FileStream, T> read) where T : class
    {
        try
        {
            using var stream = Open(path);
            return read(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferBytes, FileOptions.SequentialScan);
}
