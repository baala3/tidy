using Tidy.Core.Deletion;
using Tidy.Core.Models;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;

namespace Tidy.Core.Tests;

public sealed class DuplicateTests : IDisposable
{
    private const int Mb = 1024 * 1024;
    private readonly FakeProfile _profile = new();
    private readonly List<string> _recycled = [];

    public void Dispose() => _profile.Dispose();

    private List<DuplicateGroup> Scan() =>
        new DuplicateScanner(new LargeFilePolicy(_profile.Locations)).Scan(cancellation: TestContext.Current.CancellationToken).ToList();

    private FileDeleter Deleter() =>
        new(new DeletionSafetyPolicy(_profile.Locations), new LargeFilePolicy(_profile.Locations))
        {
            RecycleOverride = path =>
            {
                _recycled.Add(path);
                return null;
            },
        };

    /// <summary>Writes <paramref name="bytes"/> of content that depends on <paramref name="seed"/>.</summary>
    private string Content(string relative, int bytes, int seed)
    {
        var path = _profile.P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void FindsIdenticalFilesAcrossThePersonalFolders()
    {
        var original = Content(@"home\Documents\trip.mp4", 2 * Mb, seed: 1);
        var copy = Content(@"home\Downloads\trip.mp4", 2 * Mb, seed: 1);

        var group = Assert.Single(Scan());

        Assert.Equal([original, copy], group.Copies.Select(c => c.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(2 * Mb, group.WastedBytes);
        Assert.Equal(original, group.SuggestedKeep.Path);
    }

    [Fact]
    public void SameSizeIsNotEnoughWhenTheMiddleDiffers()
    {
        var a = Content(@"home\Documents\a.bin", 3 * Mb, seed: 7);
        var b = _profile.P(@"home\Pictures\b.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        var bytes = File.ReadAllBytes(a);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(b, bytes);

        Assert.Empty(Scan());
    }

    [Fact]
    public void HardLinksAreOneFileNotTwo()
    {
        var file = Content(@"home\Documents\big.iso", 2 * Mb, seed: 3);
        FakeProfile.HardLink(_profile.P(@"home\Desktop\big.iso"), file);

        Assert.Empty(Scan());
    }

    [Fact]
    public void SmallFilesAndProjectFoldersAreLeftOut()
    {
        Content(@"home\Documents\small.txt", 100_000, seed: 4);
        Content(@"home\Downloads\small.txt", 100_000, seed: 4);
        Content(@"home\Documents\app\node_modules\lib.node", 2 * Mb, seed: 5);
        Content(@"home\Downloads\lib.node", 2 * Mb, seed: 5);

        Assert.Empty(Scan());
    }

    [Fact]
    public void ThreeCopiesWasteTwo()
    {
        Content(@"home\Pictures\a.jpg", 2 * Mb, seed: 6);
        Content(@"home\Pictures\a (1).jpg", 2 * Mb, seed: 6);
        Content(@"home\Pictures\a - Copy.jpg", 2 * Mb, seed: 6);

        var group = Assert.Single(Scan());
        Assert.Equal(4 * Mb, group.WastedBytes);
        Assert.Equal("a.jpg", group.SuggestedKeep.File.Name);
    }

    [Theory]
    [InlineData("photo (1).jpg", true)]
    [InlineData("report - Copy.pdf", true)]
    [InlineData("report - Copy (2).pdf", true)]
    [InlineData("Copy of notes.txt", true)]
    [InlineData("song copy 2.mp3", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("copycat.mp4", false)]
    [InlineData("Season (2019).mkv", false)]
    public void RecognizesCopyNames(string name, bool expected) => Assert.Equal(expected, DuplicateGroup.HasCopyMarker(name));

    [Fact]
    public void RemovesAnExtraCopyThatStillMatches()
    {
        var keep = Content(@"home\Documents\a.bin", 2 * Mb, seed: 8);
        var extra = Content(@"home\Downloads\a.bin", 2 * Mb, seed: 8);

        var result = Deleter().RunDuplicates([new DuplicateRemoval(extra, keep)], IntPtr.Zero, null);

        Assert.Equal([extra], _recycled);
        Assert.Equal(2 * Mb, result.FreedBytes);
    }

    [Fact]
    public void LeavesACopyThatChangedSinceTheScan()
    {
        var keep = Content(@"home\Documents\a.bin", 2 * Mb, seed: 9);
        var extra = Content(@"home\Downloads\a.bin", 2 * Mb, seed: 10);

        var result = Deleter().RunDuplicates([new DuplicateRemoval(extra, keep)], IntPtr.Zero, null);

        Assert.Empty(_recycled);
        Assert.Contains("no longer matches", Assert.Single(result.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverRemovesACopyWhoseKeeperIsGone()
    {
        var keep = Content(@"home\Documents\a.bin", 2 * Mb, seed: 11);
        var extra = Content(@"home\Downloads\a.bin", 2 * Mb, seed: 11);
        File.Delete(keep);

        Deleter().RunDuplicates([new DuplicateRemoval(extra, keep)], IntPtr.Zero, null);

        Assert.Empty(_recycled);
    }

    [Fact]
    public void NeverRemovesTheCopyItKeeps()
    {
        var file = Content(@"home\Documents\a.bin", 2 * Mb, seed: 12);

        Deleter().RunDuplicates([new DuplicateRemoval(file, file)], IntPtr.Zero, null);

        Assert.Empty(_recycled);
    }

    [Fact]
    public void NeverRemovesAHardLinkOfTheKeeper()
    {
        var keep = Content(@"home\Documents\a.bin", 2 * Mb, seed: 13);
        var link = _profile.P(@"home\Downloads\a.bin");
        FakeProfile.HardLink(link, keep);

        var result = Deleter().RunDuplicates([new DuplicateRemoval(link, keep)], IntPtr.Zero, null);

        Assert.Empty(_recycled);
        Assert.Contains("same file", Assert.Single(result.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatesStayInsideThePersonalFolders()
    {
        var keep = Content(@"home\Documents\a.bin", 2 * Mb, seed: 14);
        var outside = Content(@"home\AppData\Local\a.bin", 2 * Mb, seed: 14);

        Deleter().RunDuplicates([new DuplicateRemoval(outside, keep)], IntPtr.Zero, null);

        Assert.Empty(_recycled);
    }
}
