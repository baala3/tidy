using Tidy.Core.Catalog;
using Tidy.Core.Models;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;

namespace Tidy.Core.Tests;

public sealed class ProjectScannerTests : IDisposable
{
    private readonly FakeProfile _profile = new();
    private static readonly DateTime LongAgo = DateTime.UtcNow.AddYears(-2);

    public void Dispose() => _profile.Dispose();

    private ProjectScanner Scanner(int staleDays, params string[] workingDirs) =>
        new(new DeletionSafetyPolicy(_profile.Locations), CleanupCatalog.Default)
        {
            StaleDays = staleDays,
            WorkingDirectories = () => workingDirs.ToHashSet(StringComparer.OrdinalIgnoreCase),
        };

    private string NodeProject(string name, bool lockfile = true)
    {
        _profile.File($@"dev\{name}\package.json");
        if (lockfile) _profile.File($@"dev\{name}\package-lock.json");
        _profile.File($@"dev\{name}\src\index.js");
        _profile.File($@"dev\{name}\node_modules\left-pad\index.js", 4096);
        return _profile.P($@"dev\{name}");
    }

    [Fact]
    public void StaleProjectIsListedSafe()
    {
        var root = NodeProject("old-app");
        FakeProfile.Age(root, LongAgo);
        var item = Assert.Single(Scanner(180).Scan(TestContext.Current.CancellationToken));
        Assert.Equal(Path.Combine(root, "node_modules"), Assert.Single(item.Paths));
        Assert.Equal(SafetyLevel.Safe, item.Safety);
        Assert.Equal("old-app", item.ProjectName);
        Assert.Equal(4096, item.SizeBytes);
    }

    [Fact]
    public void RecentlyEditedSourceKeepsTheProjectHidden()
    {
        var root = NodeProject("busy-app");
        FakeProfile.Age(root, LongAgo);
        // Dependencies untouched for years, but someone edited the code today.
        File.SetLastWriteTimeUtc(Path.Combine(root, "src", "index.js"), DateTime.UtcNow);
        Assert.Empty(Scanner(180).Scan(TestContext.Current.CancellationToken));
        Assert.Single(Scanner(0).Scan(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ProjectWithAProcessInsideIsHidden()
    {
        var root = NodeProject("served-app");
        FakeProfile.Age(root, LongAgo);
        Assert.Empty(Scanner(0, Path.Combine(root, "src")).Scan(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GitLockHidesTheProject()
    {
        var root = NodeProject("committing-app");
        _profile.File(@"dev\committing-app\.git\HEAD", writtenUtc: LongAgo);
        _profile.File(@"dev\committing-app\.git\index.lock");
        FakeProfile.Age(Path.Combine(root, "node_modules"), LongAgo);
        Assert.Empty(Scanner(0).Scan(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MissingLockfileDropsToCheckFirst()
    {
        var root = NodeProject("loose-app", lockfile: false);
        FakeProfile.Age(root, LongAgo);
        Assert.Equal(SafetyLevel.CheckFirst, Assert.Single(Scanner(180).Scan(TestContext.Current.CancellationToken)).Safety);
    }

    [Fact]
    public void DotNetBinAndObjAreBothFound()
    {
        _profile.File(@"dev\Tool\Tool.csproj");
        _profile.File(@"dev\Tool\bin\Debug\Tool.dll", 2048);
        _profile.File(@"dev\Tool\obj\project.assets.json", 1024);
        FakeProfile.Age(_profile.P(@"dev\Tool"), LongAgo);
        var items = Scanner(180).Scan(TestContext.Current.CancellationToken).ToList();
        Assert.Equal(["bin", "obj"], items.Select(i => Path.GetFileName(i.Paths[0])).Order());
    }
}
