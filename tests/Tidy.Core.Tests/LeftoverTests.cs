using Tidy.Core.Apps;
using Tidy.Core.Catalog;
using Tidy.Core.Deletion;
using Tidy.Core.Leftovers;
using Tidy.Core.Platform;
using Tidy.Core.Safety;

namespace Tidy.Core.Tests;

public sealed class LeftoverTests : IDisposable
{
    private readonly FakeProfile _profile = new();
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly DateTime _longAgo = DateTime.UtcNow.AddDays(-200);

    public void Dispose() => _profile.Dispose();

    private string Roaming(string name) => Path.Combine(_profile.Locations.RoamingAppData, name);
    private string Local(string name) => Path.Combine(_profile.Locations.LocalAppData, name);
    private string Programs(string name) => Path.Combine(_profile.Locations.LocalAppData, "Programs", name);

    private static SharedRoots Shared(FakeProfile profile) => new(profile.P("ProgramData"), [profile.P("Program Files")]);

    private LeftoverFinder Finder() => new(new DeletionSafetyPolicy(_profile.Locations), Shared(_profile));

    private LeftoverInputs Inputs(
        IEnumerable<AppFootprint>? installed = null, IEnumerable<RememberedApp>? gone = null,
        IEnumerable<KnownLeftover>? known = null, IEnumerable<string>? running = null) => new()
        {
            Installed = installed?.ToList() ?? [],
            Gone = gone?.ToList() ?? [],
            KnownFolders = known?.ToList() ?? [],
            RunningExecutables = running?.ToList() ?? [],
            NowUtc = _now,
        };

    private List<LeftoverFolder> Find(LeftoverInputs inputs) => Finder().Find(inputs).ToList();

    private RememberedApp Gone(string name, string publisher = "", params string[] folders) =>
        new() { Name = name, Publisher = publisher, Folders = [.. folders], LastSeenUtc = _now.AddDays(-1) };

    /// <summary>A folder with one file in it, written <paramref name="writtenUtc"/> or just now.</summary>
    private static string Folder(string path, DateTime? writtenUtc = null, string file = "data.bin")
    {
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, file), new byte[100]);
        if (writtenUtc is { } t) FakeProfile.Age(path, t);
        return path;
    }

    internal static LeftoverPolicy NoLeftovers(FakeProfile profile) =>
        new(new LeftoverFinder(new DeletionSafetyPolicy(profile.Locations), Shared(profile)), () => new LeftoverInputs
        {
            Installed = [],
            Gone = [],
            KnownFolders = [],
            RunningExecutables = [],
            NowUtc = DateTime.UtcNow,
        });

    // MARK: Evidence

    [Fact]
    public void FindsAGoneAppsRegisteredFolderAndTheFolderNamedAfterIt()
    {
        var install = Folder(Local("Discord"), file: ".dead");
        var data = Folder(Roaming("discord"));
        Folder(Roaming("DiscordBotMaker"));

        var found = Find(Inputs(gone: [Gone("Discord", "Discord Inc.", install)]));

        Assert.Equal([install, data], found.Select(f => f.Path).Order());
        Assert.Equal(LeftoverEvidence.RegisteredFolder, found.Single(f => f.Path == install).Evidence);
        Assert.All(found, f => Assert.Equal("Discord", f.AppName));
    }

    [Fact]
    public void FindsTheFolderNamedLikeTheAppsInstallFolder()
    {
        var install = Folder(Programs("acme-notes"));
        var data = Folder(Roaming("acme-notes"));

        var found = Find(Inputs(gone: [Gone("Acme Notes for Windows", "Acme", install)]));

        Assert.Contains(found, f => f.Path == data && f.Evidence == LeftoverEvidence.InstallFolderName);
    }

    [Fact]
    public void FindsPublisherAndNameTogether()
    {
        var thunderbird = Folder(Roaming(@"Mozilla\Thunderbird"));
        Folder(Roaming(@"Mozilla\Firefox"));

        var found = Find(Inputs(gone: [Gone("Mozilla Thunderbird (x64 en-US)", "Mozilla")]));

        var only = Assert.Single(found);
        Assert.Equal(thunderbird, only.Path);
        Assert.Equal(LeftoverEvidence.PublisherAndName, only.Evidence);
    }

    [Fact]
    public void SharingAWordWithTheAppIsNotEnough()
    {
        Folder(Roaming("Foo"));
        Folder(Roaming("Studio"));
        Folder(Local("FooStudioHelper"));

        Assert.Empty(Find(Inputs(gone: [Gone("Foo Studio")])));
    }

    [Fact]
    public void VersionsAndArchitecturesDoNotStopAMatch()
    {
        var data = Folder(Roaming("7-Zip"));

        var found = Find(Inputs(gone: [Gone("7-Zip 23.01 (x64)", "Igor Pavlov")]));

        Assert.Equal(data, Assert.Single(found).Path);
    }

    [Fact]
    public void GenericAndWindowsFoldersAreNeverOffered()
    {
        Folder(Local("Temp"));
        Folder(Local("Microsoft"));
        Folder(Local("Packages"));
        Folder(Local("Programs"));
        Folder(Local("Tidy"));

        var found = Find(Inputs(gone: [Gone("Temp"), Gone("Microsoft"), Gone("Packages"), Gone("Programs"), Gone("Tidy")]));

        Assert.Empty(found);
    }

    // MARK: Claims

    [Fact]
    public void AFolderAnInstalledAppRegisteredIsNeverOffered()
    {
        var shared = Folder(Programs("Suite"));

        var found = Find(Inputs(
            installed: [new AppFootprint { Name = "Suite Pro", Folders = [shared] }],
            gone: [Gone("Suite Lite", folders: shared)]));

        Assert.Empty(found);
    }

    [Fact]
    public void APublishersSharedFolderStaysWhileAnyOfItsAppsIsInstalled()
    {
        var editor = Folder(Roaming(@"Acme\Acme Editor"));
        Folder(Roaming("Acme"));

        var found = Find(Inputs(
            installed: [new AppFootprint { Name = "Acme Viewer", Publisher = "Acme Inc." }],
            gone: [Gone("Acme Editor", "Acme Inc.", Roaming("Acme"))]));

        Assert.Equal(editor, Assert.Single(found).Path);
    }

    [Fact]
    public void AnInstalledAppStartingWithTheFolderNameClaimsIt()
    {
        Folder(Roaming("Docker"));

        Assert.Empty(Find(Inputs(installed: [new AppFootprint { Name = "Docker Desktop" }], gone: [Gone("Docker")])));
    }

    [Fact]
    public void AFolderInsideAnInstalledAppsFullNameIsClaimed()
    {
        Folder(Roaming("Code"), _longAgo);

        var found = Find(Inputs(
            installed: [new AppFootprint { Name = "Microsoft Visual Studio Code (User)", Publisher = "Microsoft Corporation" }],
            gone: [Gone("Code")]));

        Assert.Empty(found);
    }

    [Fact]
    public void ARunningProgramClaimsItsFolderAndItsName()
    {
        var program = Folder(Programs("Herdr"), file: "herdr.exe");
        Folder(Roaming("herdr"));

        var found = Find(Inputs(gone: [Gone("Herdr", folders: program)], running: [Path.Combine(program, "herdr.exe")]));

        Assert.Empty(found);
    }

    [Fact]
    public void AProgramFolderWithoutAnUninstallEntryClaimsItsName()
    {
        Folder(Programs("Portable Tool"));
        Folder(Roaming("Portable Tool"), _longAgo);

        Assert.Empty(Find(Inputs(gone: [Gone("Portable Tool")])));
    }

    // MARK: Without Tidy having seen the app

    [Fact]
    public void AnOrphanedInstallIsOfferedWithItsDataOnceItHasBeenQuiet()
    {
        var install = Folder(Programs("OldApp"), _longAgo, file: "unins000.exe");
        var data = Folder(Roaming("OldApp"), _longAgo);
        Folder(Programs("FreshApp"), file: "Uninstall FreshApp.exe");
        Folder(Roaming("FreshApp"), _longAgo);

        var found = Find(Inputs());

        Assert.Equal([install, data], found.Select(f => f.Path).Order());
        Assert.Equal(LeftoverEvidence.OrphanedInstall, found.Single(f => f.Path == install).Evidence);
    }

    [Fact]
    public void ASquirrelAppLeavesADeadFileBehind()
    {
        var install = Folder(Local("Chatter"), _longAgo, file: ".dead");

        Assert.Equal(install, Assert.Single(Find(Inputs())).Path);
    }

    [Fact]
    public void AnUninstallerFolderAnInstalledAppRegisteredIsNotOrphaned()
    {
        var install = Folder(Programs("LiveApp"), _longAgo, file: "unins000.exe");

        Assert.Empty(Find(Inputs(installed: [new AppFootprint { Name = "Live App", Folders = [install] }])));
    }

    [Fact]
    public void KnownAppFoldersAreOfferedOnlyWhenQuietAndTheAppIsGone()
    {
        var slack = Folder(Roaming("Slack"), _longAgo);
        Folder(Roaming("Zoom"));
        KnownLeftover[] known =
        [
            new() { App = "Slack", Folders = [@"{Roaming}\Slack"] },
            new() { App = "Zoom", Folders = [@"{Roaming}\Zoom"] },
        ];

        Assert.Equal(slack, Assert.Single(Find(Inputs(known: known))).Path);
        Assert.Empty(Find(Inputs(known: known, installed: [new AppFootprint { Name = "Slack (Machine - MSI)" }])));
    }

    // MARK: Safety

    [Fact]
    public void AFolderHoldingAWalletOrCredentialsIsNeverOffered()
    {
        Folder(Roaming(@"Exodus\exodus.wallet"));
        Folder(Roaming(@"Notes\Credentials"));

        Assert.Empty(Find(Inputs(gone: [Gone("Exodus"), Gone("Notes")])));
    }

    [Fact]
    public void ALinkIsNeverOffered()
    {
        var target = Folder(_profile.P(@"elsewhere\Linky"));
        FakeProfile.Junction(Roaming("Linky"), target);

        Assert.Empty(Find(Inputs(gone: [Gone("Linky")])));
    }

    [Fact]
    public void ProgramDataAndProgramFilesNeedAnAdministrator()
    {
        var data = Folder(_profile.P(@"ProgramData\Acme Tool"));
        var install = Folder(_profile.P(@"Program Files\Acme Tool"));
        var mine = Folder(Roaming("Acme Tool"));

        var found = Find(Inputs(gone: [Gone("Acme Tool", folders: install)])).ToDictionary(f => f.Path);

        Assert.True(found[data].NeedsAdministrator);
        Assert.True(found[install].NeedsAdministrator);
        Assert.False(found[mine].NeedsAdministrator);
    }

    [Fact]
    public void AFolderInsideAnotherLeftoverGoesWithIt()
    {
        var install = Folder(Programs("Nested"));
        Folder(Path.Combine(install, "Nested"));

        Assert.Equal(install, Assert.Single(Find(Inputs(gone: [Gone("Nested", folders: install)]))).Path);
    }

    // MARK: Removal

    [Fact]
    public void RemovalChecksAgainWhatIsInstalledNow()
    {
        var data = Folder(Roaming("Gizmo"));
        var admin = Folder(_profile.P(@"ProgramData\Gizmo"));
        var installed = new List<AppFootprint>();
        var policy = new LeftoverPolicy(Finder(), () => Inputs(installed: installed, gone: [Gone("Gizmo")]));
        var recycled = new List<string>();
        var deleter = new FileDeleter(new DeletionSafetyPolicy(_profile.Locations), new LargeFilePolicy(_profile.Locations), policy)
        {
            RecycleOverride = path =>
            {
                recycled.Add(path);
                return null;
            },
        };

        var result = deleter.RunLeftovers([data, admin], IntPtr.Zero, null);
        Assert.Equal([data], recycled);
        Assert.Equal(admin, Assert.Single(result.Failures).Path);

        // Reinstalled since the scan: its folder is in use again.
        recycled.Clear();
        installed.Add(new AppFootprint { Name = "Gizmo" });
        result = deleter.RunLeftovers([data], IntPtr.Zero, null);
        Assert.Empty(recycled);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void RemovalRefusesFoldersThatWereNeverLeftovers()
    {
        var documents = _profile.Locations.Documents;
        var someoneElses = Folder(Roaming("Unrelated"));
        var recycled = new List<string>();
        var deleter = new FileDeleter(new DeletionSafetyPolicy(_profile.Locations), new LargeFilePolicy(_profile.Locations), NoLeftovers(_profile))
        {
            RecycleOverride = path =>
            {
                recycled.Add(path);
                return null;
            },
        };

        var result = deleter.RunLeftovers([documents, someoneElses, _profile.Locations.RoamingAppData], IntPtr.Zero, null);

        Assert.Empty(recycled);
        Assert.Equal(3, result.Failures.Count);
    }

    // MARK: Catalog

    [Fact]
    public void KnownAppFoldersAreOrdinaryPerUserFolders()
    {
        var policy = new DeletionSafetyPolicy(_profile.Locations);
        var roots = new[] { _profile.Locations.RoamingAppData, _profile.Locations.LocalAppData, _profile.Locations.LocalLow };
        Assert.NotEmpty(AppCatalog.Default.Leftovers);
        foreach (var entry in AppCatalog.Default.Leftovers)
        {
            foreach (var pattern in entry.Folders)
            {
                var path = CleanupCatalog.ResolveTokens(pattern, _profile.Locations);
                Assert.Contains(roots, r => PathUtil.IsStrictlyUnder(path, r));
                Assert.False(NameKeys.IsGenericFolder(path), pattern);
                Assert.False(DeletionSafetyPolicy.IsProtectedName(pattern), pattern);
                Directory.CreateDirectory(path);
                Assert.False(policy.IsNeverDelete(path), pattern);
            }
        }
    }
}

public class NameKeyTests
{
    [Theory]
    [InlineData("7-Zip 23.01 (x64)", "7zip")]
    [InlineData("Zoom Workplace (64-bit)", "zoomworkplace")]
    [InlineData("Python 3.12.1 64-bit", "python")]
    [InlineData("Mozilla Firefox (x64 en-US)", "mozillafirefox")]
    [InlineData("PyCharm2023.1", "pycharm20231")]
    public void BareLeavesOutVersionsAndArchitectures(string name, string expected) => Assert.Equal(expected, NameKeys.Bare(name));

    [Fact]
    public void PublishersLoseTheirCompanyWords()
    {
        Assert.Contains("discord", NameKeys.OfPublisher("Discord Inc."));
        Assert.Contains("blender", NameKeys.OfPublisher("Blender Foundation"));
    }

    [Fact]
    public void AnAppNamedAfterItsPublisherIsKnownByTheRest() =>
        Assert.Equal(["firefox"], NameKeys.OfNameWithoutPublisher("Mozilla Firefox", "Mozilla Corporation"));

    [Fact]
    public void ShortNamesSayNothing() => Assert.Empty(NameKeys.Of("EA"));
}

public sealed class KnownAppsTests : IDisposable
{
    private readonly FakeProfile _profile = new();

    public void Dispose() => _profile.Dispose();

    private static AppFootprint App(string name, string publisher = "", bool listed = true) =>
        new() { Name = name, Publisher = publisher, IsListed = listed, Folders = [$@"C:\Apps\{name}"] };

    [Fact]
    public void AnAppThatDisappearsIsGone()
    {
        var known = new KnownApps();
        var now = DateTime.UtcNow;
        known.Record([App("Discord", "Discord Inc."), App("Slack")], now);

        var gone = known.Gone([App("Slack")]);

        Assert.Equal("Discord", Assert.Single(gone).Name);
        Assert.Equal([@"C:\Apps\Discord"], gone[0].Folders);
    }

    [Fact]
    public void AnUpdateIsNotAnUninstall()
    {
        var known = new KnownApps();
        known.Record([App("Python 3.12.1 (64-bit)", "Python Software Foundation")], DateTime.UtcNow);

        Assert.Empty(known.Gone([App("Python 3.12.2 (64-bit)", "Python Software Foundation")]));
    }

    [Fact]
    public void ComponentsAndRuntimesAreNotRemembered()
    {
        var known = new KnownApps();
        known.Record([App("Microsoft Visual C++ 2022 Redistributable", listed: false)], DateTime.UtcNow);

        Assert.Empty(known.Apps);
    }

    [Fact]
    public void LongGoneAppsAreForgotten()
    {
        var known = new KnownApps();
        var now = DateTime.UtcNow;
        known.Record([App("Ancient")], now.AddDays(-KnownApps.KeepDays - 1));
        known.Record([App("Recent")], now);

        Assert.Equal(["Recent"], known.Apps.Select(a => a.Name));
    }

    [Fact]
    public void SurvivesASaveAndLoad()
    {
        var known = new KnownApps();
        known.Record([App("Discord", "Discord Inc.")], DateTime.UtcNow);
        known.Save(_profile.Locations.TidyData);

        var loaded = KnownApps.Load(_profile.Locations.TidyData);

        Assert.Equal("Discord", Assert.Single(loaded.Apps).Name);
        Assert.Equal("Discord Inc.", loaded.Apps[0].Publisher);
    }
}
