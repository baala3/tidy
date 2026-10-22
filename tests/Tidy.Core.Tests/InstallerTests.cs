using System.IO.Compression;
using Tidy.Core.Apps;
using Tidy.Core.Models;
using Tidy.Core.Safety;
using Tidy.Core.Scanning;

namespace Tidy.Core.Tests;

public sealed class InstallerTests : IDisposable
{
    private readonly FakeProfile _profile = new();

    public void Dispose() => _profile.Dispose();

    private static InstalledApp Desktop(string name, string publisher, string version = "1.0", string? id = null, string? location = null) => new()
    {
        Id = id ?? $@"HKLM64\{name}",
        Kind = AppKind.Desktop,
        Name = name,
        Publisher = publisher,
        Version = version,
        InstallLocation = location,
    };

    private static InstalledApp? Match(string file, InstallerIdentity? identity, params InstalledApp[] apps) =>
        InstallerMatcher.Match(file, identity, apps);

    [Theory]
    [InlineData("Firefox", "Mozilla", "Mozilla Firefox (x64 en-US)", "Mozilla")]
    [InlineData("Firefox", "Mozilla Foundation", "Mozilla Firefox (x64 en-US)", "Mozilla")]
    [InlineData("Steam", "", "Steam", "Valve Corporation")]
    [InlineData("Microsoft Visual Studio Code", "Microsoft Corporation", "Microsoft Visual Studio Code (User)", "Microsoft Corporation")]
    [InlineData("Discord", "Discord Inc.", "Discord", "Discord Inc.")]
    [InlineData("Docker Desktop", "Docker Inc.", "Docker Desktop", "Docker Inc.")]
    [InlineData("7-Zip", "Igor Pavlov", "7-Zip 24.08 (x64)", "Igor Pavlov")]
    public void MatchesTheSameProduct(string product, string company, string app, string publisher) =>
        Assert.NotNull(Match(@"C:\Users\a\Downloads\setup.exe", new InstallerIdentity { ProductName = product, Publisher = company }, Desktop(app, publisher)));

    [Theory]
    [InlineData("Docker Desktop", "Docker Inc.", "GitHub Desktop", "GitHub, Inc.")]
    [InlineData("Firefox", "Mozilla", "Mozilla Maintenance Service", "Mozilla")]
    [InlineData("Zoom", "Zoom Video Communications, Inc.", "Zoom Workplace", "Zoom Video Communications, Inc.")]
    [InlineData("GitHub CLI", "GitHub, Inc.", "GitHub Desktop", "GitHub, Inc.")]
    [InlineData("Setup", "", "Setup Helper", "")]
    public void LeavesOtherProductsAlone(string product, string company, string app, string publisher) =>
        Assert.Null(Match(@"C:\Users\a\Downloads\setup.exe", new InstallerIdentity { ProductName = product, Publisher = company }, Desktop(app, publisher)));

    [Fact]
    public void FallsBackToTheFileName() =>
        Assert.NotNull(Match(@"C:\Users\a\Downloads\ChromeSetup.exe", new InstallerIdentity { ProductName = "Google Update Setup", Publisher = "Google LLC" },
            Desktop("Google Chrome", "Google LLC")));

    [Fact]
    public void MatchesAnMsiByItsProductCode()
    {
        const string code = "{23170F69-40C1-2702-2408-000001000000}";
        var app = Desktop("Whatever It Is Called", "Someone", id: $@"HKLM64\{code}");
        Assert.Same(app, Match(@"C:\Users\a\Downloads\x.msi", new InstallerIdentity { ProductCode = code.ToLowerInvariant() }, app));
    }

    [Fact]
    public void MatchesAnMsixByItsPackageName()
    {
        var store = new InstalledApp
        {
            Id = "Microsoft.WindowsTerminal_1.21.0.0_x64__8wekyb3d8bbwe",
            Kind = AppKind.Store,
            Name = "Terminal",
            PackageFamilyName = "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
        };
        Assert.Same(store, Match(@"C:\Users\a\Downloads\t.msixbundle", new InstallerIdentity { PackageName = "Microsoft.WindowsTerminal" }, store));
        Assert.Null(Match(@"C:\Users\a\Downloads\t.msixbundle", new InstallerIdentity { PackageName = "Microsoft.WindowsTerminalPreview", ProductName = "Terminal" }, store));
    }

    [Fact]
    public void AProgramInsideAnAppsFolderIsPartOfTheApp() =>
        Assert.Null(Match(@"C:\Users\a\Downloads\Tools\steam.exe", new InstallerIdentity { ProductName = "Steam" },
            Desktop("Steam", "Valve", location: @"C:\Users\a\Downloads\Tools")));

    [Theory]
    [InlineData("158.0", "158.0", 0)]
    [InlineData("1.95.3", "1.96.0", -1)]
    [InlineData("v2.10", "2.9.91", 1)]
    [InlineData("4.80.0", "4.80.0.12345", -1)]
    public void ComparesVersionsByTheirNumbers(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(InstallerMatcher.CompareVersions(a, b)));

    [Fact]
    public void ScansThePersonalFoldersForInstallersOfInstalledApps()
    {
        // This test's own program has a version resource naming it Tidy.Core.Tests.
        var exe = Environment.ProcessPath!;
        var setup = Path.Combine(_profile.Locations.Downloads, "TidyCoreTestsSetup.exe");
        File.Copy(exe, setup);
        var installed = Desktop("Tidy.Core.Tests", "Tidy.Core.Tests");
        var other = Desktop("Notepad++", "Notepad++ Team");

        var found = Assert.Single(new InstallerScanner(new LargeFilePolicy(_profile.Locations)).Scan([installed, other], TestContext.Current.CancellationToken));

        Assert.Equal(setup, found.File.Path);
        Assert.Same(installed, found.App);
    }

    /// <summary>Firefox leaves a launcher named Firefox.exe on the Desktop: same product, but it starts the app.</summary>
    [Fact]
    public void AProgramThatOnlySharesTheAppsNameIsNotAnInstaller()
    {
        var desktop = Directory.CreateDirectory(_profile.Locations.Desktop).FullName;
        File.Copy(Environment.ProcessPath!, Path.Combine(desktop, "Tidy.Core.Tests.exe"));

        Assert.Empty(new InstallerScanner(new LargeFilePolicy(_profile.Locations)).Scan([Desktop("Tidy.Core.Tests", "Tidy.Core.Tests")], TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("SteamSetup", true)]
    [InlineData("Firefox Installer", true)]
    [InlineData("npp.8.6.Installer.x64", true)]
    [InlineData("desktop-launcher", false)]
    [InlineData("Firefox", false)]
    public void RecognizesInstallerNames(string name, bool expected) => Assert.Equal(expected, InstallerIdentity.NamesAnInstaller(name));

    [Fact]
    public void ReadsAnMsixManifest()
    {
        var package = Path.Combine(_profile.Locations.Downloads, "Terminal.msix");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open()))
        {
            writer.Write("""
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Identity Name="Microsoft.WindowsTerminal" Publisher="CN=Microsoft" Version="1.21.2911.0" />
                  <Properties><DisplayName>Terminal</DisplayName></Properties>
                </Package>
                """);
        }

        var identity = InstallerIdentity.Read(package);

        Assert.Equal("Microsoft.WindowsTerminal", identity?.PackageName);
        Assert.Equal("1.21.2911.0", identity?.Version);
        Assert.Equal("Terminal", identity?.ProductName);
    }

    [Fact]
    public void OlderInstallersSayTheyAreOlder()
    {
        var file = new LargeFile(@"C:\Users\a\Downloads\x.exe", 10, DateTime.UtcNow, LargeFileKind.Installer, "Downloads");
        Assert.Equal(InstallerAge.Older, new InstallerFile(file, Desktop("X", "", "2.0"), "1.5").Age);
        Assert.Equal(InstallerAge.Newer, new InstallerFile(file, Desktop("X", "", "2.0"), "2.1").Age);
        Assert.Equal(InstallerAge.Unknown, new InstallerFile(file, Desktop("X", "", "2.0"), null).Age);
    }
}
