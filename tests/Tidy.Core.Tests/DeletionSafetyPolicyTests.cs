using Tidy.Core.Safety;

namespace Tidy.Core.Tests;

public sealed class DeletionSafetyPolicyTests : IDisposable
{
    private readonly FakeProfile _profile = new();
    private readonly DeletionSafetyPolicy _policy;

    public DeletionSafetyPolicyTests() => _policy = new DeletionSafetyPolicy(_profile.Locations);

    public void Dispose() => _profile.Dispose();

    [Fact]
    public void ProfileAndPersonalFoldersAreNeverRemovable()
    {
        var l = _profile.Locations;
        foreach (var folder in new[] { l.Home, l.LocalAppData, l.RoamingAppData, l.Temp, l.Documents, l.Downloads })
        {
            Assert.Equal(SafetyDecision.BlockedNeverDelete, _policy.Evaluate(folder));
        }
    }

    [Fact]
    public void DriveRootIsNeverRemovable() =>
        Assert.Equal(SafetyDecision.BlockedNeverDelete, _policy.Evaluate(@"C:\"));

    [Fact]
    public void TempChildrenAreAllowed()
    {
        var file = _profile.File(@"home\AppData\Local\Temp\installer.tmp");
        Assert.Equal(SafetyDecision.Allow, _policy.Evaluate(file));
    }

    [Fact]
    public void UnknownFoldersAreNotAllowlisted()
    {
        var dir = _profile.Dir(@"home\AppData\Local\SomeApp\Data");
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(dir));
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(_profile.Dir(@"home\Documents\Taxes")));
    }

    [Fact]
    public void BrowserCacheIsAllowedButTheProfileBesideItIsNot()
    {
        var cache = _profile.Dir(@"home\AppData\Local\Google\Chrome\User Data\Default\Cache");
        var profileFolder = Path.GetDirectoryName(cache)!;
        var logins = _profile.File(@"home\AppData\Local\Google\Chrome\User Data\Default\Login Data");
        Assert.Equal(SafetyDecision.Allow, _policy.Evaluate(cache));
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(profileFolder));
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(logins));
    }

    [Fact]
    public void DotDotTricksAreRefused()
    {
        _profile.Dir(@"home\AppData\Local\npm-cache");
        var sneaky = Path.Combine(_profile.Locations.LocalAppData, @"npm-cache\..\Google");
        Assert.Equal(SafetyDecision.BlockedInvalid, _policy.Evaluate(sneaky));
        Assert.Equal(SafetyDecision.BlockedInvalid, _policy.Evaluate(@"\\server\share\npm-cache"));
        Assert.Equal(SafetyDecision.BlockedInvalid, _policy.Evaluate(@"npm-cache"));
    }

    [Fact]
    public void ChromiumAppCacheNeedsTheCacheLayout()
    {
        var real = _profile.Dir(@"home\AppData\Roaming\discord\Cache");
        _profile.File(@"home\AppData\Roaming\discord\Cache\Cache_Data\index");
        var impostor = _profile.Dir(@"home\AppData\Roaming\MyNotes\Cache");
        _profile.File(@"home\AppData\Roaming\MyNotes\Cache\notes.db");
        Assert.Equal(SafetyDecision.Allow, _policy.Evaluate(real));
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(impostor));
    }

    [Fact]
    public void PasswordManagerCachesAreNeverOffered()
    {
        var cache = _profile.Dir(@"home\AppData\Roaming\Bitwarden\Cache");
        _profile.File(@"home\AppData\Roaming\Bitwarden\Cache\Cache_Data\index");
        Assert.Equal(SafetyDecision.BlockedNeverDelete, _policy.Evaluate(cache));
    }

    [Fact]
    public void JunctionInsideAnAllowedFolderIsRefused()
    {
        var target = _profile.Dir(@"home\Documents\Important");
        _profile.Dir(@"home\AppData\Local\npm-cache");
        var link = _profile.P(@"home\AppData\Local\npm-cache\_cacache");
        FakeProfile.Junction(link, target);
        Assert.Equal(SafetyDecision.BlockedLink, _policy.Evaluate(link));
        _profile.File(@"home\Documents\Important\thesis.docx");
        Assert.Equal(SafetyDecision.BlockedLink, _policy.Evaluate(Path.Combine(link, "thesis.docx")));
    }

    [Fact]
    public void ProjectArtifactNeedsItsProject()
    {
        _profile.File(@"dev\app\package.json");
        var modules = _profile.Dir(@"dev\app\node_modules");
        var orphan = _profile.Dir(@"dev\loose\node_modules");
        Assert.Equal(SafetyDecision.Allow, _policy.Evaluate(modules));
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(orphan));
    }

    [Fact]
    public void ProjectArtifactsInsideAppDataAreRefused()
    {
        _profile.File(@"home\AppData\Roaming\SomeApp\package.json");
        var modules = _profile.Dir(@"home\AppData\Roaming\SomeApp\node_modules");
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(modules));
    }

    [Fact]
    public void TerraformFolderHoldingStateIsRefused()
    {
        _profile.File(@"dev\infra\main.tf");
        var plugins = _profile.Dir(@"dev\infra\.terraform");
        Assert.Equal(SafetyDecision.Allow, _policy.Evaluate(plugins));
        _profile.File(@"dev\infra\.terraform\terraform.tfstate");
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(plugins));
    }

    [Fact]
    public void GenericBuildFolderNeedsItsBuildFile()
    {
        var build = _profile.Dir(@"home\Documents\Photos\build");
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(build));
        _profile.File(@"home\Documents\Photos\CMakeLists.txt");
        Assert.Equal(SafetyDecision.Allow, _policy.Evaluate(build));
    }

    [Fact]
    public void ABackupCanGoButTheFolderHoldingBackupsCannot()
    {
        var root = _profile.Dir(@"home\AppData\Roaming\Apple Computer\MobileSync\Backup");
        var backup = _profile.Dir(@"home\AppData\Roaming\Apple Computer\MobileSync\Backup\00008110-001A");
        Assert.Equal(SafetyDecision.BlockedNeverDelete, _policy.Evaluate(root));
        Assert.Equal(SafetyDecision.Allow, _policy.Evaluate(backup));
    }

    [Fact]
    public void TelegramsSignInStaysOffTheList()
    {
        var tdata = _profile.Dir(@"home\AppData\Roaming\Telegram Desktop\tdata");
        var cache = _profile.Dir(@"home\AppData\Roaming\Telegram Desktop\tdata\user_data\cache");
        Assert.Equal(SafetyDecision.BlockedNotAllowlisted, _policy.Evaluate(tdata));
        Assert.Equal(SafetyDecision.Allow, _policy.Evaluate(cache));
    }
}
