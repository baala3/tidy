using Tidy.Core.Apps;
using Tidy.Core.Models;

namespace Tidy.Core.Tests;

public class AppCatalogTests
{
    private static readonly AppCatalog Catalog = AppCatalog.Default;

    [Theory]
    [InlineData("Microsoft.XboxIdentityProvider")]
    [InlineData("Microsoft.Xbox.TCUI")]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.SecHealthUI")]
    [InlineData("Microsoft.HEIFImageExtension")]
    [InlineData("Microsoft.VP9VideoExtensions")]
    [InlineData("Microsoft.VCLibs.140.00")]
    [InlineData("NVIDIACorp.NVIDIAControlPanel")]
    public void PackagesOtherAppsNeedAreNeverOffered(string name) => Assert.True(Catalog.IsProtectedPackage(name));

    [Theory]
    [InlineData("Microsoft.Windows.Photos")]
    [InlineData("Microsoft.WindowsCalculator")]
    [InlineData("Microsoft.BingNews")]
    [InlineData("king.com.CandyCrushSaga")]
    public void OrdinaryAppsAreOffered(string name) => Assert.False(Catalog.IsProtectedPackage(name));

    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810")]
    [InlineData("Microsoft Edge WebView2 Runtime")]
    [InlineData("Microsoft Windows Desktop Runtime - 8.0.4 (x64)")]
    [InlineData("Realtek High Definition Audio Driver")]
    public void DesktopDependenciesAreNeverOffered(string name) => Assert.True(Catalog.IsProtectedDesktopApp(name));

    [Fact]
    public void PreinstalledMatchesByPrefixWithItsTier()
    {
        Assert.Equal(SafetyLevel.Safe, Catalog.PreinstalledFor("king.com.CandyCrushSodaSaga")!.Safety);
        Assert.Equal(SafetyLevel.CheckFirst, Catalog.PreinstalledFor("Microsoft.GamingApp")!.Safety);
        Assert.Null(Catalog.PreinstalledFor("Microsoft.WindowsCalculator"));
    }

    [Fact]
    public void NoPreinstalledEntryIsAlsoProtected()
    {
        // A catalog row the protection list hides would silently never show.
        foreach (var entry in Catalog.Preinstalled)
        {
            Assert.False(Catalog.IsProtectedPackage(entry.Match + "X"), entry.Match);
        }
    }
}

public class AppRemoverTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\uninst.exe\" /S", "C:\\Program Files\\App\\uninst.exe", "/S")]
    [InlineData("C:\\Program Files\\App\\uninst.exe --remove", "C:\\Program Files\\App\\uninst.exe", "--remove")]
    [InlineData("MsiExec.exe /I{1234-ABCD}", "MsiExec.exe", "/X{1234-ABCD}")]
    [InlineData("MsiExec.exe /X{1234-ABCD}", "MsiExec.exe", "/X{1234-ABCD}")]
    public void SplitsUninstallCommands(string command, string file, string arguments)
    {
        var (f, a) = AppRemover.SplitCommand(command);
        Assert.Equal(file, f);
        Assert.Equal(arguments, a);
    }
}

public class InstalledAppTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    private static InstalledApp App(DateTime? used, DateTime? installed, bool running = false) => new()
    {
        Id = "x", Kind = AppKind.Desktop, Name = "X", LastUsedUtc = used, InstalledUtc = installed, IsRunning = running,
    };

    [Fact]
    public void UnusedComesFromTheLastLaunch()
    {
        Assert.True(App(Now.AddDays(-200), Now.AddDays(-400)).IsUnusedFor(90, Now));
        Assert.False(App(Now.AddDays(-10), Now.AddDays(-400)).IsUnusedFor(90, Now));
    }

    [Fact]
    public void NoRecordedLaunchCountsOnlyOnceInstalledThatLong()
    {
        Assert.True(App(null, Now.AddDays(-200)).IsUnusedFor(90, Now));
        Assert.False(App(null, Now.AddDays(-5)).IsUnusedFor(90, Now));
        Assert.False(App(null, null).IsUnusedFor(90, Now));
    }

    [Fact]
    public void RunningAppIsNeverUnused() =>
        Assert.False(App(Now.AddDays(-500), Now.AddDays(-900), running: true).IsUnusedFor(90, Now));

    [Fact]
    public void LaunchLogNamesAreRot13() =>
        Assert.Equal("Microsoft.WindowsCalculator", LaunchHistory.Rot13(LaunchHistory.Rot13("Microsoft.WindowsCalculator")));

    [Theory]
    [InlineData("\"C:\\Windows\\notepad.exe\",0", "C:\\Windows\\notepad.exe")]
    [InlineData("C:\\Windows\\notepad.exe", "C:\\Windows\\notepad.exe")]
    [InlineData("%SystemRoot%\\notepad.exe,-1", "C:\\Windows\\notepad.exe")]
    [InlineData("C:\\nowhere\\missing.exe", null)]
    public void DisplayIconBecomesAFile(string value, string? expected) =>
        Assert.Equal(expected, AppScanner.IconFromDisplayIcon(value), ignoreCase: true);
}

public class NestedSizeTests
{
    [Fact]
    public void GamesInsideALauncherAreCountedOnce()
    {
        var steam = new InstalledApp { Id = "s", Kind = AppKind.Desktop, Name = "Steam", SizeBytes = 100, InstallLocation = @"C:\Steam" };
        var game = new InstalledApp { Id = "g", Kind = AppKind.Desktop, Name = "Game", SizeBytes = 70, InstallLocation = @"C:\Steam\steamapps\common\Game" };
        var other = new InstalledApp { Id = "o", Kind = AppKind.Desktop, Name = "SteamTools", SizeBytes = 5, InstallLocation = @"C:\SteamTools" };
        var result = AppScanner.WithoutNestedSizes([steam, game, other]).ToDictionary(a => a.Name, a => a.SizeBytes);
        Assert.Equal(30, result["Steam"]);
        Assert.Equal(70, result["Game"]);
        Assert.Equal(5, result["SteamTools"]);
    }
}
