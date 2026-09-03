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

    [Fact]
    public void TheWebDoorIsALauncherNotATab()
    {
        var xaml = ReadSource("MainWindow", "MainWindow.xaml");
        var door = Regex.Match(xaml, "<Button x:Name=\"DoorWebApp\".*?</Button>", RegexOptions.Singleline);
        Assert.True(door.Success, "DoorWebApp is gone from MainWindow.xaml");

        // Its own handler: NavDoor_Click on a Tag with no NavDoorMap row is a logged no-op,
        // which for this door would mean a dead click on the one thing it exists to do.
        Assert.Contains("Click=\"DoorWebApp_Click\"", door.Value);
        Assert.DoesNotContain("Click=\"NavDoor_Click\"", door.Value);

        var tabNav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");

        // NOT in NavDoorMap - a map row drags in a default tab, a ShowTab case and a palette
        // door row (PaletteDoorParityTests), none of which a browser link has.
        var map = Regex.Match(tabNav, @"NavDoorMap =\s*\{.*?\};", RegexOptions.Singleline);
        Assert.True(map.Success, "NavDoorMap has moved or changed shape");
        Assert.DoesNotContain("webapp", map.Value);

        // In the launcher list and the parts switch instead, so the rail walker can animate it.
        Assert.Contains("NavLauncherDoors = { \"webapp\" }", tabNav);
        Assert.Contains("\"webapp\" => (DoorWebApp, null, null)", tabNav);

        // The click goes through BrowserLauncher (clipboard fallback for the machines with no
        // default browser) at the canonical destination.
        Assert.Contains("WebAppUrl = \"https://app.cclabs.app\"", tabNav);
    }

    [Fact]
    public void TheWebDoorSitsAboveSettingsInThePinnedCluster()
    {
        var xaml = ReadSource("MainWindow", "MainWindow.xaml");

        var spiral = xaml.IndexOf("x:Name=\"SpiralRail\"", StringComparison.Ordinal);
        var web = xaml.IndexOf("x:Name=\"DoorWebApp\"", StringComparison.Ordinal);
        var settings = xaml.IndexOf("x:Name=\"DoorSettings\"", StringComparison.Ordinal);

        Assert.True(spiral >= 0 && web >= 0 && settings >= 0, "a pinned-cluster landmark is gone");
        Assert.True(spiral < web && web < settings,
            "DoorWebApp left its slot between the divider and DoorSettings in the pinned cluster");
    }

    [Fact]
    public void TheRailWalksLauncherDoorsAndFollowsTheirModArt()
    {
        var navRail = ReadSource("MainWindow", "MainWindow.NavRail.cs");

        // CacheNavDoorRows must walk NavDoorMap + the launchers: a door missing from the walk
        // renders frozen at its authored collapsed size while every row around it grows.
        var cache = Regex.Match(navRail, @"private void CacheNavDoorRows\(\).*?\n        \}", RegexOptions.Singleline);
        Assert.True(cache.Success, "CacheNavDoorRows has moved or changed shape");
        Assert.Contains("Concat(NavLauncherDoors)", cache.Value);

        // The hover nudge subscribes from ChromeFx's list; a row left out just feels dead.
        var chromeFx = ReadSource("MainWindow", "MainWindow.ChromeFx.cs");
        Assert.Contains("DoorWebApp", chromeFx);

        // And the art is a permanent mod slot like every other door.
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
    //  4. the mod picker waits out the upgrade tour
    // =====================================================================================

    [Fact]
    public void TheUpgradersModPickerWaitsOutTheTour()
    {
        // What's New clears IsStartupDialogShowing in its finally BEFORE the queued tour action
        // runs, so a wait predicate without the tutorial check let the picker open modally on
        // top of the running spotlight (flagged in the 0812 build review).
        var main = ReadSource("MainWindow", "MainWindow.xaml.cs");
        var picker = Regex.Match(main,
            @"await Task\.Delay\(1500\);.*?ModPickerDialog\.ShowIfNeeded\(this, preselectActiveMod: true\);",
            RegexOptions.Singleline);
        Assert.True(picker.Success, "the upgrader mod-picker block has moved or changed shape");

        Assert.Equal(2, Regex.Matches(picker.Value, Regex.Escape("App.Tutorial?.IsActive")).Count);
    }
}
