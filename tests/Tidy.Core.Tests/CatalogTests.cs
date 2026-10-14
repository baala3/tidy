using Tidy.Core.Catalog;
using Tidy.Core.Models;
using Tidy.Core.Safety;

namespace Tidy.Core.Tests;

public class CatalogTests
{
    [Fact]
    public void EveryEntryLoadsWithAKnownTierAndScope()
    {
        var catalog = CleanupCatalog.Default;
        Assert.NotEmpty(catalog.Entries);
        foreach (var entry in catalog.Entries)
        {
            Assert.True(Enum.IsDefined(entry.Safety), entry.Key);
            Assert.True(Enum.IsDefined(entry.Category), entry.Key);
            Assert.False(string.IsNullOrWhiteSpace(entry.Explanation), entry.Key);
            Assert.NotEmpty(entry.Paths);
        }
    }

    [Fact]
    public void KeysAreUnique()
    {
        var keys = CleanupCatalog.Default.Entries.Select(e => e.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>
    /// The catalog proposes, the policy decides. A catalog path the allowlist does not
    /// cover would be scanned and then silently dropped, so it is a bug either way.
    /// </summary>
    [Fact]
    public void EveryCatalogPathIsCoveredByTheAllowlist()
    {
        using var profile = new FakeProfile();
        var policy = new DeletionSafetyPolicy(profile.Locations);
        foreach (var entry in CleanupCatalog.Default.Entries)
        {
            foreach (var pattern in entry.Paths)
            {
                var resolved = CleanupCatalog.ResolveTokens(pattern, profile.Locations).Replace("*", "Profile 1", StringComparison.Ordinal);
                var probe = entry.ContentsOnly ? Path.Combine(resolved, "child") : resolved;
                Assert.True(policy.IsAllowlisted(probe), $"{entry.Key}: {pattern} is not on the allowlist");
            }
        }
    }

    [Fact]
    public void EveryArtifactRuleHasCatalogText()
    {
        foreach (var rule in ProjectArtifactCatalog.Rules)
        {
            var text = CleanupCatalog.Default.ArtifactTextFor(rule.Kind);
            Assert.False(string.IsNullOrWhiteSpace(text.Name));
        }
    }

    [Fact]
    public void ContentsOnlyFoldersCanNeverBeRemovedThemselves()
    {
        using var profile = new FakeProfile();
        var policy = new DeletionSafetyPolicy(profile.Locations);
        foreach (var entry in CleanupCatalog.Default.Entries.Where(e => e.ContentsOnly))
        {
            foreach (var pattern in entry.Paths)
            {
                var folder = CleanupCatalog.ResolveTokens(pattern, profile.Locations);
                Directory.CreateDirectory(folder);
                Assert.Equal(SafetyDecision.BlockedNeverDelete, policy.Evaluate(folder));
            }
        }
    }
}
