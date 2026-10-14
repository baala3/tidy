using Tidy.Core.Deletion;
using Tidy.Core.Safety;

namespace Tidy.Core.Tests;

public sealed class FileDeleterTests : IDisposable
{
    private readonly FakeProfile _profile = new();
    private readonly List<string> _recycled = [];

    public void Dispose() => _profile.Dispose();

    private FileDeleter Deleter() =>
        new(new DeletionSafetyPolicy(_profile.Locations), new LargeFilePolicy(_profile.Locations))
        {
            RecycleOverride = path =>
            {
                _recycled.Add(path);
                return null;
            },
        };

    [Fact]
    public void ReChecksEveryPathAtDeleteTime()
    {
        var cache = _profile.Dir(@"home\AppData\Local\npm-cache");
        _profile.File(@"home\AppData\Local\npm-cache\_cacache\blob", 2000);
        var documents = _profile.Locations.Documents;

        var result = Deleter().Run([cache, documents], DeletionKind.Cache, IntPtr.Zero, null);

        Assert.Equal([cache], _recycled);
        Assert.Equal(2000, result.FreedBytes);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(documents, failure.Path);
    }

    [Fact]
    public void LargeFileDeletionStaysInsideThePersonalFolders()
    {
        var video = _profile.File(@"home\Downloads\movie.mkv", 5000);
        var outside = _profile.File(@"home\AppData\Local\big.bin", 5000);
        var inProject = _profile.File(@"home\Documents\app\node_modules\big.node", 5000);

        var result = Deleter().Run([video, outside, inProject], DeletionKind.LargeFile, IntPtr.Zero, null);

        Assert.Equal([video], _recycled);
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public void CachePolicyNeverAppliesToPersonalFiles()
    {
        var video = _profile.File(@"home\Downloads\movie.mkv", 5000);
        Deleter().Run([video], DeletionKind.Cache, IntPtr.Zero, null);
        Assert.Empty(_recycled);
    }
}
