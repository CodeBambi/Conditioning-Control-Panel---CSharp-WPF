using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The installer ships a NEUTRAL baseline; themed vocabulary arrives with a mod. These tests pin
/// the parts of that promise that are one careless edit away from being false again - a default
/// that quietly goes back to the BambiSleep list would put the themed triggers in front of a user
/// who never chose a mod, and nothing else in the suite would notice.
/// </summary>
public class NeutralInstallerBaselineTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppText(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    /// <summary>
    /// Words a stranger reading a fresh install's phrase list must not find there. Substring match,
    /// case-insensitive, so "BAMBI SLEEP" and "COCK ZOMBIE NOW" are both caught by one entry each.
    /// </summary>
    private static readonly string[] ThemedWords =
        { "BAMBI", "BIMBO", "COCK", "SISSY", "SLUT", "GOOD GIRL", "GIGGLETIME" };

    private static void AssertNeutral(System.Collections.Generic.IEnumerable<string> phrases, string what)
    {
        foreach (var phrase in phrases)
        foreach (var word in ThemedWords)
            Assert.False(phrase.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0,
                $"{what} carries the themed phrase '{phrase}' - the out-of-box baseline is neutral");
    }

    [Fact]
    public void TheCcpDefaultManifestIsNeutral()
    {
        AssertNeutral(BuiltInMods.CCPDefault.SubliminalPool!.Keys, "CCPDefault.SubliminalPool");
        AssertNeutral(BuiltInMods.CCPDefault.LockCardPhrases!.Keys, "CCPDefault.LockCardPhrases");
        AssertNeutral(BuiltInMods.CCPDefault.CustomTriggers!, "CCPDefault.CustomTriggers");
    }

    /// <summary>
    /// The themed lists still exist - they just belong to the mod that owns them. If this ever goes
    /// green with an empty pool, someone has neutralised BambiSleep itself rather than the default.
    /// </summary>
    [Fact]
    public void TheBambiManifestStillOwnsTheThemedVocabulary()
    {
        Assert.Contains("BAMBI SLEEP", BuiltInMods.BambiSleep.SubliminalPool!.Keys);
        Assert.True(BuiltInMods.BambiSleep.SubliminalPool!.Count >= 20,
            "the BambiSleep pool lost phrases - it is the mod's content, not a default to trim");
    }

    /// <summary>
    /// AppSettings' fresh-install pools are COPIES of the neutral manifest, never the themed one and
    /// never a duplicated literal list (a second copy is a second thing to forget).
    /// </summary>
    [Fact]
    public void FreshInstallPoolsAreSeededFromTheNeutralManifest()
    {
        var settings = AppText("Models", "AppSettings.cs");

        Assert.Contains("_subliminalPool = new(BuiltInMods.CCPDefault.SubliminalPool", settings, StringComparison.Ordinal);
        Assert.Contains("_lockCardPhrases = new(BuiltInMods.CCPDefault.LockCardPhrases", settings, StringComparison.Ordinal);
        Assert.Contains("_customTriggers = new(BuiltInMods.CCPDefault.CustomTriggers", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInMods.BambiSleep.SubliminalPool", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// The "which phrases are defaults" lookup behind the Manage Messages editor. Both copies of it
    /// fall back to the NEUTRAL pool when the mod layer is not up yet; either one still naming
    /// BambiSleep would tell an unmodded user the themed list is their factory set.
    /// </summary>
    [Fact]
    public void TheManageMessagesEditorFallsBackToTheNeutralPool()
    {
        foreach (var text in new[]
                 {
                     AppText("MainWindow", "MainWindow.UiUpdates.cs"),
                     AppText("Features", "SubliminalFeatureControl.xaml.cs"),
                 })
        {
            Assert.Contains("?? Models.BuiltInMods.CCPDefault.SubliminalPool", text, StringComparison.Ordinal);
            Assert.DoesNotContain("?? Models.BuiltInMods.BambiSleep.SubliminalPool", text, StringComparison.Ordinal);
        }
    }
}
