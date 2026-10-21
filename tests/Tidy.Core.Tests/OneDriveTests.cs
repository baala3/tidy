using Tidy.Core.Cloud;
using Tidy.Core.Deletion;
using Tidy.Core.Platform;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;

namespace Tidy.Core.Tests;

public sealed class OneDriveTests : IDisposable
{
    private const int Mb = 1024 * 1024;
    private readonly FakeProfile _profile = new();
    private readonly TestSyncRoot _oneDrive;
    private readonly OneDrivePolicy _policy;

    public OneDriveTests()
    {
        _oneDrive = new TestSyncRoot(_profile.P(@"home\OneDrive"));
        _policy = new OneDrivePolicy(_profile.Locations with { OneDriveRoots = [PathUtil.Normalize(_oneDrive.Path)!] });
    }

    public void Dispose()
    {
        _oneDrive.Dispose();
        _profile.Dispose();
    }

    private List<string> Scan() =>
        new OneDriveScanner(_policy).Scan(TestContext.Current.CancellationToken).Select(f => f.Path).ToList();

    private CleanResult Free(params string[] paths) =>
        new CloudSpaceFreer(_policy).Run(paths, null, TestContext.Current.CancellationToken);

    [Fact]
    public void ListsSyncedFilesThatTakeSpaceHere()
    {
        var photo = _oneDrive.Placeholder(@"Pictures\Camera Roll\IMG_0001.jpg", 2 * Mb);
        _oneDrive.Placeholder(@"Pictures\small.jpg", 100_000);

        Assert.Equal([photo], Scan());
    }

    [Fact]
    public void LeavesOutFilesThatAreNotSafelyInTheCloud()
    {
        _oneDrive.Placeholder(@"Documents\uploading.mp4", 2 * Mb, inSync: false);
        var pinned = _oneDrive.Placeholder(@"Documents\keep.mp4", 2 * Mb);
        TestSyncRoot.SetPinState(pinned, CloudPinState.Pinned);
        var plain = Path.Combine(_oneDrive.Path, @"Documents\not-a-placeholder.mp4");
        File.WriteAllBytes(plain, new byte[2 * Mb]);

        Assert.Empty(Scan());
    }

    [Fact]
    public void FreeingUpSpaceMarksTheFileOnlineOnlyForOneDrive()
    {
        var video = _oneDrive.Placeholder(@"Videos\trip.mp4", 3 * Mb);

        var result = Free(video);

        Assert.Equal([PathUtil.Normalize(video)!], result.Removed);
        Assert.Equal(3 * Mb, result.FreedBytes);
        Assert.True(File.Exists(video));
        Assert.Equal(CloudPinState.Unpinned, CloudFiles.Info(video)?.PinState);
        // OneDrive drops the copy from here; until it does, the file isn't offered twice.
        Assert.Empty(Scan());
        Assert.Contains("already freeing", Assert.Single(Free(video).Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverFreesAFileThatHasNotFinishedSyncing()
    {
        var file = _oneDrive.Placeholder(@"Documents\draft.docx", 2 * Mb, inSync: false);

        var failure = Assert.Single(Free(file).Failures);

        Assert.Contains("finished syncing", failure.Reason, StringComparison.Ordinal);
        Assert.Equal(2 * Mb, CloudFiles.OnDiskBytes(file));
    }

    [Fact]
    public void NeverFreesAFileSetToStayOnThisDevice()
    {
        var file = _oneDrive.Placeholder(@"Documents\offline.pdf", 2 * Mb);
        TestSyncRoot.SetPinState(file, CloudPinState.Pinned);

        Assert.Single(Free(file).Failures);
        Assert.Equal(2 * Mb, CloudFiles.OnDiskBytes(file));
    }

    [Fact]
    public void OnlyTouchesOneDrive()
    {
        var outside = _profile.File(@"home\Documents\report.pdf", 2 * Mb);

        Assert.Contains("isn't in a OneDrive folder", Assert.Single(Free(outside).Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileOnlyOneDriveHasNoCopyOfStaysPut()
    {
        var plain = Path.Combine(_oneDrive.Path, "local-only.zip");
        File.WriteAllBytes(plain, new byte[2 * Mb]);

        Assert.Single(Free(plain).Failures);
        Assert.Equal(2 * Mb, CloudFiles.OnDiskBytes(plain));
    }

    /// <summary>
    /// Every process but the sync app sees placeholders disguised as plain files. These tests
    /// run inside the provider, which always sees them, so this pins the opt-in Tidy needs.
    /// </summary>
    [Fact]
    public void ExposesPlaceholdersOnlyWhileAsked()
    {
        var before = CloudFiles.ThreadPlaceholderMode();
        using (CloudFiles.ExposePlaceholders())
        {
            Assert.Equal(2, CloudFiles.ThreadPlaceholderMode());
        }
        Assert.Equal(before, CloudFiles.ThreadPlaceholderMode());
    }

    [Fact]
    public void AFolderLeftBehindAfterSigningOutIsNotOneDrive()
    {
        var leftover = _profile.Dir(@"home\OneDrive - Old");
        var policy = new OneDrivePolicy(_profile.Locations with { OneDriveRoots = [PathUtil.Normalize(_oneDrive.Path)!, leftover] });

        Assert.Equal([PathUtil.Normalize(_oneDrive.Path)!], policy.Roots);
    }
}
