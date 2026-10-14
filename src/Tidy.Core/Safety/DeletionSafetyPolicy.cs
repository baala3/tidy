using Tidy.Core.Catalog;
using Tidy.Core.Models;
using Tidy.Core.Platform;

namespace Tidy.Core.Safety;

public enum SafetyDecision
{
    Allow,
    /// <summary>On the never-delete list. Dropped silently from any selection.</summary>
    BlockedNeverDelete,
    /// <summary>Not on the allowlist. Skipped and reported as "skipped for safety".</summary>
    BlockedNotAllowlisted,
    /// <summary>A link or junction sits on the path, so the removal could land somewhere else.</summary>
    BlockedLink,
    /// <summary>The path is gone, or is not a plain local path.</summary>
    BlockedInvalid,
}

/// <summary>
/// Strict allow/deny policy gating every removal Tidy makes. A path is removable only
/// when it is on the allowlist below AND clear of every never-delete rule. Anything not
/// explicitly allowed is refused. The allowlist is kept here, apart from catalog.json, on
/// purpose: adding a catalog entry is not enough to make a folder removable, so widening
/// what Tidy deletes always takes a second, deliberate edit in this file.
/// </summary>
public sealed class DeletionSafetyPolicy(UserLocations locations)
{
    public UserLocations Locations { get; } = locations;

    /// <summary>
    /// Folders Tidy may remove, with <c>*</c> matching one path segment. A path is
    /// allowed when it equals a pattern or sits below one. Each entry resolves to a
    /// cache, a download cache or a log folder: nothing here holds user data.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedPatterns =
    [
        // Contents only: the folders themselves are on the never-delete list.
        @"{Temp}\*",
        @"{Local}\CrashDumps\*",
        @"{Local}\Microsoft\Windows\WER\ReportArchive\*",
        @"{Local}\Microsoft\Windows\WER\ReportQueue\*",
        @"{Local}\Microsoft\Windows\INetCache\*",
        @"{Local}\D3DSCache\*",
        @"{Local}\NVIDIA\DXCache\*",
        @"{Local}\NVIDIA\GLCache\*",
        @"{LocalLow}\NVIDIA\PerDriverVersion\DXCache\*",
        @"{LocalLow}\NVIDIA\PerDriverVersion\GLCache\*",
        @"{Local}\AMD\DxCache\*",
        @"{Local}\AMD\DxcCache\*",
        @"{Local}\AMD\GLCache\*",
        @"{Local}\AMD\VkCache\*",
        @"{LocalLow}\Intel\ShaderCache\*",

        // Browsers: the disk caches only. Profiles, cookies, passwords and history
        // sit next to these and are never matched.
        @"{Local}\Google\Chrome\User Data\*\Cache",
        @"{Local}\Google\Chrome\User Data\*\Code Cache",
        @"{Local}\Google\Chrome\User Data\*\GPUCache",
        @"{Local}\Google\Chrome\User Data\ShaderCache",
        @"{Local}\Google\Chrome\User Data\GrShaderCache",
        @"{Local}\Google\Chrome\User Data\GraphiteDawnCache",
        @"{Local}\Microsoft\Edge\User Data\*\Cache",
        @"{Local}\Microsoft\Edge\User Data\*\Code Cache",
        @"{Local}\Microsoft\Edge\User Data\*\GPUCache",
        @"{Local}\Microsoft\Edge\User Data\ShaderCache",
        @"{Local}\Microsoft\Edge\User Data\GrShaderCache",
        @"{Local}\Microsoft\Edge\User Data\GraphiteDawnCache",
        @"{Local}\BraveSoftware\Brave-Browser\User Data\*\Cache",
        @"{Local}\BraveSoftware\Brave-Browser\User Data\*\Code Cache",
        @"{Local}\BraveSoftware\Brave-Browser\User Data\*\GPUCache",
        @"{Local}\BraveSoftware\Brave-Browser\User Data\ShaderCache",
        @"{Local}\BraveSoftware\Brave-Browser\User Data\GrShaderCache",
        @"{Local}\Vivaldi\User Data\*\Cache",
        @"{Local}\Vivaldi\User Data\*\Code Cache",
        @"{Local}\Vivaldi\User Data\*\GPUCache",
        @"{Local}\Opera Software\Opera Stable\Cache",
        @"{Local}\Opera Software\Opera GX Stable\Cache",
        @"{Local}\Mozilla\Firefox\Profiles\*\cache2",
        @"{Local}\Mozilla\Firefox\Profiles\*\startupCache",
        @"{Local}\Steam\htmlcache",
        // Default Common media cache locations only, never a project or chosen scratch disk.
        @"{Roaming}\Adobe\Common\Media Cache Files",
        @"{Roaming}\Adobe\Common\Media Cache",

        // Developer download and build caches.
        @"{Local}\npm-cache",
        @"{Local}\pnpm\store",
        @"{Local}\pnpm-cache",
        @"{Local}\Yarn\Cache",
        @"{Local}\Yarn\Berry\cache",
        @"{Home}\.bun\install\cache",
        // Not `{Local}\deno` whole: `location_data` holds Deno apps' local storage.
        @"{Local}\deno\deps",
        @"{Local}\deno\gen",
        @"{Local}\deno\npm",
        @"{Local}\deno\remote",
        @"{Local}\node\corepack",
        @"{Local}\electron\Cache",
        @"{Local}\electron-builder\Cache",
        @"{Local}\NuGet\v3-cache",
        @"{Local}\NuGet\plugins-cache",
        // AUDIT: re-downloadable but slow; Check First by its catalog tier.
        @"{Home}\.nuget\packages",
        @"{Home}\.gradle\caches",
        @"{Home}\.gradle\wrapper\dists",
        @"{Home}\.m2\repository",
        @"{Home}\.ivy2\cache",
        // Caches only. The rest of `.android` is emulators, the adb key and debug.keystore.
        @"{Home}\.android\cache",
        @"{Home}\.android\build-cache",
        // Not `.cargo\bin`, which holds installed tools.
        @"{Home}\.cargo\registry",
        @"{Home}\.cargo\git",
        @"{Local}\go-build",
        @"{Home}\go\pkg\mod\cache",
        @"{Local}\pip\Cache",
        @"{Local}\uv\cache",
        // Not `pypoetry\Cache\virtualenvs`: Poetry keeps its managed environments there.
        @"{Local}\pypoetry\Cache\cache",
        @"{Local}\pypoetry\Cache\artifacts",
        @"{Home}\miniconda3\pkgs",
        @"{Home}\anaconda3\pkgs",
        @"{Home}\miniforge3\pkgs",
        @"{Home}\mambaforge\pkgs",
        @"{Home}\.conda\pkgs",
        @"{Home}\.cache\pre-commit",
        @"{Local}\Composer",
        @"{Local}\vcpkg\archives",
        @"{Home}\scoop\cache",
        @"{Roaming}\terraform.d\plugin-cache",
        @"{Local}\ms-playwright",
        @"{Home}\.cache\puppeteer",
        @"{Local}\Cypress\Cache",

        // Editors. Settings, extensions and workspace state live next to these.
        @"{Roaming}\Code\Cache",
        @"{Roaming}\Code\CachedData",
        @"{Roaming}\Code\CachedExtensionVSIXs",
        @"{Roaming}\Code\Code Cache",
        @"{Roaming}\Code\GPUCache",
        @"{Roaming}\Code\DawnGraphiteCache",
        @"{Roaming}\Code\DawnWebGPUCache",
        @"{Roaming}\Cursor\Cache",
        @"{Roaming}\Cursor\CachedData",
        @"{Roaming}\Cursor\CachedExtensionVSIXs",
        @"{Roaming}\Cursor\Code Cache",
        @"{Roaming}\Cursor\GPUCache",
        @"{Roaming}\Cursor\DawnGraphiteCache",
        @"{Roaming}\Cursor\DawnWebGPUCache",
        // `caches` only; LocalHistory, plugins and settings are siblings.
        @"{Local}\JetBrains\*\caches",
        @"{Local}\Microsoft\VisualStudio\*\ComponentModelCache",
    ];

    /// <summary>
    /// Fragments that mark a path as credential, identity or password-manager state.
    /// Matched against every segment below the profile folder. Over-matching is the
    /// safe direction: the folder is simply not offered.
    /// </summary>
    public static readonly IReadOnlyList<string> ProtectedFragments =
    [
        "1password", "agilebits", "bitwarden", "lastpass", "dashlane", "keepass", "keeper",
        "authy", "yubico", "keychain", "credential", "vault", "identitycache", "oneauth",
        "tokenbroker", "aad.brokerplugin", "webauthn", "passkey", "wallet",
    ];

    private readonly IReadOnlyList<string[]> _allowed = AllowedPatterns
        .Select(p => PathUtil.Segments(CleanupCatalog.ResolveTokens(p, locations)))
        .ToList();

    /// <summary>Folders that may never be removed themselves, though allowed folders inside them may.</summary>
    public IEnumerable<string> NeverDeleteExact()
    {
        var l = Locations;
        yield return l.Home;
        yield return l.LocalAppData;
        yield return l.RoamingAppData;
        yield return l.LocalLow;
        yield return Path.GetDirectoryName(l.LocalAppData)!; // AppData
        yield return l.Temp;
        foreach (var folder in l.PersonalFolders) yield return folder;
        foreach (var root in l.OneDriveRoots) yield return root;
        foreach (var root in l.DeveloperRoots) yield return root;
        foreach (var entry in CleanupCatalog.Default.Entries.Where(e => e.ContentsOnly))
        {
            foreach (var p in entry.Paths) yield return CleanupCatalog.ResolveTokens(p, l);
        }
    }

    /// <summary>Locations whose path or anything below it is never removed.</summary>
    public IEnumerable<string> NeverDeletePrefixes()
    {
        var l = Locations;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Path.Combine(l.RoamingAppData, "Microsoft", "Credentials");
        yield return Path.Combine(l.RoamingAppData, "Microsoft", "Protect");
        yield return Path.Combine(l.RoamingAppData, "Microsoft", "Crypto");
        yield return Path.Combine(l.RoamingAppData, "Microsoft", "SystemCertificates");
        yield return Path.Combine(l.RoamingAppData, "Microsoft", "Windows", "Start Menu");
        yield return Path.Combine(l.LocalAppData, "Microsoft", "Credentials");
        yield return Path.Combine(l.LocalAppData, "Microsoft", "Vault");
        // Store apps manage their own data and break in odd ways when it moves.
        yield return Path.Combine(l.LocalAppData, "Packages");
        yield return l.TidyData;
    }

    /// <summary>Whether a cache folder may be removed. Every removal goes through here.</summary>
    public SafetyDecision Evaluate(string rawPath)
    {
        var path = PathUtil.Normalize(rawPath);
        // A spelling that normalizes to something else (`..`, `.`, doubled separators) is refused, not reinterpreted.
        if (path is null || !Path.TrimEndingDirectorySeparator(rawPath).Equals(path, StringComparison.OrdinalIgnoreCase)) return SafetyDecision.BlockedInvalid;
        if (!Path.Exists(path)) return SafetyDecision.BlockedInvalid;
        if (Path.GetPathRoot(path)!.Equals(path, StringComparison.OrdinalIgnoreCase)) return SafetyDecision.BlockedNeverDelete;

        var blocked = NeverDeleteCheck(path);
        if (blocked is { } decision) return decision;

        if (!IsAllowlisted(path) && !IsChromiumAppCache(path) && !IsProjectArtifact(path)) return SafetyDecision.BlockedNotAllowlisted;
        // Any link anywhere on the path could redirect the removal, so refuse outright.
        return PathUtil.HasReparsePointBetween(path, Path.GetPathRoot(path)!) ? SafetyDecision.BlockedLink : SafetyDecision.Allow;
    }

    private SafetyDecision? NeverDeleteCheck(string path)
    {
        if (NeverDeleteExact().Any(p => PathUtil.AreSame(path, p))) return SafetyDecision.BlockedNeverDelete;
        if (NeverDeletePrefixes().Any(p => p.Length > 0 && PathUtil.IsSameOrUnder(path, p))) return SafetyDecision.BlockedNeverDelete;
        if (HasProtectedFragment(path)) return SafetyDecision.BlockedNeverDelete;
        return null;
    }

    private bool HasProtectedFragment(string path)
    {
        var relative = PathUtil.IsStrictlyUnder(path, Locations.Home) ? path[(Locations.Home.Length + 1)..] : path;
        var lower = relative.ToLowerInvariant();
        return ProtectedFragments.Any(lower.Contains);
    }

    public bool IsAllowlisted(string path)
    {
        var segments = PathUtil.Segments(path);
        return _allowed.Any(pattern =>
            segments.Length >= pattern.Length &&
            pattern.Select((p, i) => p == "*" || string.Equals(p, segments[i], StringComparison.OrdinalIgnoreCase)).All(m => m));
    }

    /// <summary>
    /// A Chromium or Electron app's disk cache directly inside its AppData folder, like
    /// <c>%APPDATA%\discord\Cache</c>. Only folders with the cache's own file layout
    /// qualify, so a folder that merely shares the name is never offered.
    /// </summary>
    public bool IsChromiumAppCache(string path)
    {
        var parent = Path.GetDirectoryName(path);
        var grandparent = parent is null ? null : Path.GetDirectoryName(parent);
        if (grandparent is null) return false;
        if (!PathUtil.AreSame(grandparent, Locations.RoamingAppData) && !PathUtil.AreSame(grandparent, Locations.LocalAppData)) return false;
        var name = Path.GetFileName(path);
        if (!CleanupCatalog.Default.ChromiumApps.Folders.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
        return HasChromiumCacheLayout(path);
    }

    public static bool HasChromiumCacheLayout(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("Code Cache", StringComparison.OrdinalIgnoreCase))
        {
            return Directory.Exists(Path.Combine(path, "js")) || Directory.Exists(Path.Combine(path, "wasm"));
        }
        if (name.Equals("Cache", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(path, "Cache_Data")))
        {
            return File.Exists(Path.Combine(path, "Cache_Data", "index"));
        }
        return File.Exists(Path.Combine(path, "index")) && File.Exists(Path.Combine(path, "data_0"));
    }

    /// <summary>
    /// A rebuildable project folder (<c>node_modules</c>, <c>target</c>, ...) whose project
    /// is really there. Re-checked from disk at delete time, so a folder that stopped
    /// qualifying since the scan is refused.
    /// </summary>
    public bool IsProjectArtifact(string path) => MatchProjectArtifact(path) is not null;

    public (ProjectArtifactRule Rule, string Root)? MatchProjectArtifact(string path)
    {
        if (!IsInProjectArea(path)) return null;
        foreach (var (rule, root) in ProjectArtifactCatalog.Candidates(path))
        {
            if (!IsInProjectArea(root)) continue;
            if (!Directory.Exists(path)) continue;
            if (!rule.MatchesRoot(root) || rule.RefusesArtifact(path) || !rule.CanRebuild(path)) continue;
            return (rule, root);
        }
        return null;
    }

    /// <summary>
    /// Projects live under the profile (outside AppData) or under a developer root
    /// like <c>C:\dev</c>. Nowhere else is a <c>build</c> or <c>bin</c> folder offered.
    /// </summary>
    public bool CanHoldProjects(string root) =>
        IsInProjectArea(Path.Combine(root, "artifact"));

    public bool IsInProjectArea(string path)
    {
        var appData = Path.GetDirectoryName(Locations.LocalAppData)!;
        if (PathUtil.IsSameOrUnder(path, appData)) return false;
        if (PathUtil.IsStrictlyUnder(path, Locations.Home)) return true;
        return Locations.DeveloperRoots.Any(r => PathUtil.IsStrictlyUnder(path, r));
    }
}
