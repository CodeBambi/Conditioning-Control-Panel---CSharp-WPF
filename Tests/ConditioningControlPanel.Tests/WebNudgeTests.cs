using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The v6.8.0 web nudges: the Web App launcher door above Settings, the One Account banner
/// beat, the one-account intro card with the popup's first CTA, and the mod-picker/upgrade-tour
/// collision fix.
///
/// <para>Source-text assertions, same rationale as <see cref="HeaderBannerTests"/>: the surfaces
/// are MainWindow partials that cannot be instantiated in a unit test. What is pinned here is
/// the wiring that fails SILENTLY when it rots: a launcher door left out of a walker renders
/// frozen at its authored size, a Tag routed to NavDoor_Click is a logged no-op, and a missing
/// loc key renders as the raw key string in eight languages.</para>
/// </summary>
public class WebNudgeTests
{
    private static string ReadSource(params string[] parts) => SourceRoots.ReadProductFile(parts);

    // =====================================================================================
    //  1. the Web App door is a launcher, not a tab
    // =====================================================================================

    /// <summary>Nav rework (2026-10-06): the Web App door LEFT the rail (Play > Games carries
    /// a Web App tile). The launcher itself survives as OpenWebAppFromNav at the canonical URL,
    /// through BrowserLauncher (clipboard fallback for machines with no default browser).</summary>
    [Fact]
    public void TheWebAppLeftTheRailButKeepsItsLauncher()
    {
        var xaml = ReadSource("MainWindow", "MainWindow.xaml");
        Assert.DoesNotContain("x:Name=\"DoorWebApp\"", xaml);

        var tabNav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");
        Assert.Contains("WebAppUrl = \"https://app.cclabs.app\"", tabNav);
        var open = Regex.Match(tabNav, @"internal void OpenWebAppFromNav\(\).*?\n        \}", RegexOptions.Singleline);
        Assert.True(open.Success, "OpenWebAppFromNav is gone");
        Assert.Contains("BrowserLauncher.OpenUrlOrPrompt(WebAppUrl", open.Value);
        Assert.Contains("RetireWebBannerBeat()", open.Value);
    }

    /// <summary>The rail foot's order is frozen: Circe's tab, the fuse chip, the Settings gear,
    /// EMI's dock, then Friends last (the owner: the bottom of the rail is the profile bubble,
    /// the gear sits where 7.0.5 had it).</summary>
    [Fact]
    public void ThePinnedFootOrderIsFrozen()
    {
        var xaml = ReadSource("MainWindow", "MainWindow.xaml");
        var order = new[] { "ChasterRail", "FuseRailChip", "DoorSettings", "EmiDockChip", "FriendsChip" }
            .Select(n => xaml.IndexOf("x:Name=\"" + n + "\"", StringComparison.Ordinal)).ToArray();
        Assert.All(order, i => Assert.True(i >= 0, "a pinned-foot landmark is gone"));
        for (int i = 1; i < order.Length; i++)
            Assert.True(order[i - 1] < order[i], "the rail foot is out of order");
        // ...and the gear is below the last section row.
        Assert.True(xaml.IndexOf("x:Name=\"DoorLibrary\"", StringComparison.Ordinal) < order[0]);
    }

    /// <summary>The web app art stays a permanent mod slot (the Play > Games tile may wear it).</summary>
    [Fact]
    public void TheWebAppArtStaysAModSlot()
    {
        var slots = ReadSource("Windows", "ModCreatorWindow.UiArt.cs");
        Assert.Contains("nav/door_webapp.png", slots);
    }

    // =====================================================================================
    //  2. localization - four keys, nine strict-JSON files
    // =====================================================================================

    [Fact]
    public void EveryLanguageCarriesTheWebNudgeKeysAndStillParsesStrictly()
    {
        var langDir = SourceRoots.LanguagesDirectory;
        var files = Directory.GetFiles(langDir, "*.json");
        Assert.Equal(9, files.Length);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var key in new[]
                     {
                         "\"nav_door_webapp\"", "\"tooltip_nav_door_webapp\"",
                         "\"label_banner_web_xp\"", "\"label_banner_web_link\"",
                     })
                Assert.True(text.Contains(key, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} is missing {key} - that language renders the raw key");

            // The nine files have been strict-JSON clean since 2026-07-29; an edit that
            // reintroduces leniency breaks every tool that is not Newtonsoft.
            using var doc = JsonDocument.Parse(text);
        }
    }

    // =====================================================================================
    //  3. the one-account card and the popup's CTA
    // =====================================================================================

    [Fact]
    public void TheOneAccountCardLeadsWithACtaAndAQuietLater()
    {
        var intros = ReadSource("Windows", "FeatureIntroPopup.xaml.cs");
        var card = Regex.Match(intros, "\\[\"one-account\"\\].*?\\n            \\},", RegexOptions.Singleline);
        Assert.True(card.Success, "the one-account card is gone from FeatureIntros.All");

        Assert.Contains("ActionLabel = \"Open the web app\"", card.Value);
        Assert.Contains("DismissLabel = \"Later\"", card.Value);

        // The CTA button ships Collapsed so every existing card renders exactly as before.
        var xaml = ReadSource("Windows", "FeatureIntroPopup.xaml");
        var btn = Regex.Match(xaml, "<Button x:Name=\"BtnAction\".*?>", RegexOptions.Singleline);
        Assert.True(btn.Success, "BtnAction is gone from FeatureIntroPopup.xaml");
        Assert.Contains("Visibility=\"Collapsed\"", btn.Value);

        // ...and the action runs after the modal unwinds, never inside its message loop.
        var click = Regex.Match(intros, @"void BtnAction_Click\(.*?\n        \}", RegexOptions.Singleline);
        Assert.True(click.Success, "BtnAction_Click has moved or changed shape");
        Assert.Contains("BeginInvoke", click.Value);
    }

    [Fact]
    public void TheOneAccountCardRidesTheSettlePathBehindDailyFree()
    {
        // Same settle path, same owning door, queued second: the Home door's one-card-per-launch
        // budget makes daily-free introduce itself first and one-account take the NEXT quiet
        // launch. Queue order is the whole mechanism, so the order is what is pinned.
        var tabNav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");
        var hook = Regex.Match(tabNav, @"void OnDashboardTabVisibilityChanged\(.*?\n        \}", RegexOptions.Singleline);
        Assert.True(hook.Success, "OnDashboardTabVisibilityChanged has moved or changed shape");

        var daily = hook.Value.IndexOf("ShowWhenStartupSettles(\"daily-free\"", StringComparison.Ordinal);
        var oneAccount = hook.Value.IndexOf("ShowWhenStartupSettles(\"one-account\"", StringComparison.Ordinal);
        Assert.True(daily >= 0, "the daily-free settle queue is gone");
        Assert.True(oneAccount > daily, "one-account no longer queues behind daily-free");
    }

    // =====================================================================================
    //  4. the first show replaces the old startup interruptions
    // =====================================================================================

    [Fact]
    public void FirstShowReplacesStandaloneModPickerAndWaitsForTutorials()
    {
        var main = ReadSource("MainWindow", "MainWindow.xaml.cs");
        Assert.DoesNotContain("EnqueueStartupModal(\"mod-picker\"", main);
        Assert.Contains("EnqueueStartupModal(\"first-show\", 25", main);
        Assert.DoesNotContain("QueueEmiKnock(knockSeenVersion)", main);
        Assert.False(ConditioningControlPanel.Services.Startup.StartupQueueCore.CanStartModal(
            modalUp: false, updateDialogActive: false, tutorialActive: true, windowReady: true));
    }
}
