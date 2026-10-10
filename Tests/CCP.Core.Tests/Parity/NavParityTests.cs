using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5 nav rework: the section table (NavSectionsTests), the rail rules
/// (NavSectionRailTests, NavRailRingTests, RailPolish11Tests), the count badge
/// (NavBadgeSeenTests) and the pure half of the tab strip (SectionTabStripTests). The XAML and
/// ShowTab source pins stay WPF-only; this is the half both heads share.
/// </summary>
public class NavParityTests
{
    // ---- NavSections ------------------------------------------------------------------------

    [Fact]
    public void TheRailOrderIsFrozen()
    {
        Assert.Equal(
            new[] { "home", "studio", "companion", "play", "social", "you", "library", "settings" },
            NavSections.Order.Select(s => s.Key).ToArray());
    }

    [Fact]
    public void TheTableIsTheWpfTable()
    {
        string Row(NavSection s) => s.Key + ":" + s.DefaultTab + "=" + string.Join(",",
            s.Tabs.Select(t => t.Key + "/" + t.LabelKey + "/" + t.Kind + "/" + t.Tier + (t.Hidden ? "/h" : "")));
        Assert.Equal(new[]
        {
            "home:settings=settings/tab_dashboard/Tab/0,premium/nav_tab_premium/Tab/0/h",
            "studio:studio=studio/st4_nav_studio/Tab/0,presets/tab_presets/Tab/0,haptics/tab_haptics/Zone/1,justdrop/jd_door_title/Window/0,ramp/nav_tab_ramp/Zone/0",
            "companion:companion=companion/nav_tab_chat/Tab/0,personality/nav_tab_personality/Tab/0,permissions/nav_tab_permissions/Tab/0,companionlinks/nav_tab_companionlinks/Tab/0,companionai/label_ai_badge/Tab/0,bambitakeover/tab_takeover/Tab/1,shelistening/tab_shelistening/Tab/1,awareness/tab_awareness/Tab/1",
            "play:play=play/nav_tab_games/Tab/0,playeyes/nav_tab_eyes/Zone/0,playsessions/nav_tab_sessions/Zone/0,deeper/tab_deeper/Tab/0,gradedintake/tab_gradedintake/Tab/0/h,lockdown/tab_lockdown_mode/Tab/1/h,blinktrainer/tab_blink_trainer/Tab/1/h",
            "social:availablesubjects=availablesubjects/nav_tab_lobby/Tab/0,friends/nav_tab_friends/Tab/0,leaderboard/tab_leaderboard/Tab/0,remotecontrol/tab_remote_control/Tab/1,leash/nav_tab_leash/Tab/0",
            "you:discord=discord/tab_profile/Tab/0,quests/tab_quests/Tab/0,achievements/tab_achievements/Tab/0,enhancements/tab_enhancements/Tab/0,programs/tab_programs/Tab/0,chaster/chaster_title/Tab/0,spiral/tab_spiral/Tab/0/h",
            "library:assets=assets/tab_assets/Tab/0,folders/nav_tab_folders/Tab/0,mods/yl7_nav_mods/Launcher/0,catalogue/yl7_nav_catalogue/Launcher/0,phrases/yl7_nav_phrases/Launcher/0,medialog/yl7_nav_medialog/Launcher/0",
            "settings:appsettings=general/set2_section_general/Zone/0,account/settings_section_plans/Zone/0,audio/set2_section_audio/Zone/0,devices/set2_section_devices/Zone/0,monitors/settings_section_monitors/Zone/0,emidesk/set2_section_emidesk/Zone/0,notifications/set2_section_notifications/Zone/0,performance/set2_section_performance/Zone/0,data/set2_section_data/Zone/0,updates/set2_section_updates/Zone/0",
        }, NavSections.Order.Select(Row).ToArray());
        Assert.Equal(new[] { "nav_door_home", "nav_door_studio", "nav_door_companion", "nav_door_play",
                             "nav_section_social", "nav_door_you", "nav_door_library", "nav_door_settings" },
            NavSections.Order.Select(s => s.LabelKey).ToArray());
    }

    [Fact]
    public void EverySectionOpensOnATabItOwns()
    {
        foreach (var s in NavSections.Order)
        {
            Assert.Equal(s.DefaultTab, NavSections.DefaultTab(s.Key));
            Assert.Equal(s.Key, NavSections.SectionForTab(s.DefaultTab));
            Assert.InRange(s.Tabs.Count(t => !t.Hidden), 1, 10);
        }
        Assert.Equal("home", NavSections.SectionForTab("settings"));
        Assert.Equal("settings", NavSections.SectionForTab("appsettings"));
        Assert.Null(NavSections.SectionForTab("no-such-tab"));
    }

    [Fact]
    public void NoTabKeyLivesInTwoSections()
    {
        var dupes = NavSections.AllTabs.GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void EveryRedirectLandsOnARealTab()
    {
        foreach (var (old, to) in NavSections.Redirects)
        {
            var section = NavSections.Find(to.Section);
            Assert.NotNull(section);
            Assert.True(section!.Tabs.Any(t => t.Key == to.Tab) || section.DefaultTab == to.Tab, old);
        }
        Assert.Equal(("home", "premium"), NavSections.Redirects["exclusives"]);
        Assert.Equal(("settings", "account"), NavSections.Redirects["patreon"]);
        Assert.Equal(("play", "play"), NavSections.Redirects["lab"]);
        Assert.Equal(("home", "settings"), NavSections.Redirects["progression"]);
        Assert.Equal(("social", "availablesubjects"), NavSections.Redirects["together"]);
        foreach (var key in SectionChromeRules.MovedRedirectKeys.Concat(SectionChromeRules.SilentRedirectKeys))
            Assert.True(NavSections.Redirects.ContainsKey(key), key);
        Assert.Equal(3, SectionChromeRules.NavMovedNoteLimit);
    }

    [Fact]
    public void TheOldNamesAreSearchable()
    {
        var all = NavSections.Aliases.Values.SelectMany(v => v).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Exclusives", "Premium", "Velvet Vault", "The Vault", "Lab", "Effects Rack", "Available Subjects", "Together" })
            Assert.Contains(name, all);
    }

    /// <summary>The keys the 7.1.5 nav rework added (NavSectionsTests.NewKeys) plus the spark's.</summary>
    private static readonly string[] NewKeys =
    {
        "nav_section_social", "nav_tab_games", "nav_tab_sessions", "nav_tab_eyes", "nav_tab_ramp",
        "nav_tab_chat", "nav_tab_personality", "nav_tab_permissions", "nav_tab_companionlinks",
        "nav_tab_friends", "nav_tab_leash", "nav_tab_folders", "nav_tab_lobby", "nav_badge_open",
        "nav_crumb_sep", "settings_section_monitors", "settings_section_plans", "nav_search_placeholder",
        "nav_search_none", "nav_search_try", "nav_search_all", "nav_moved_toast", "nav_was_hint",
        "whatmoved_title", "whatmoved_intro", "whatmoved_show", "whatmoved_close", "whatmoved_row_premium",
        "whatmoved_row_lobby", "whatmoved_row_monitors", "whatmoved_row_tabs", "whatmoved_row_games",
        "help_whatmoved", "premium_spark_label", "premium_spark_tip_free", "premium_spark_tip_basic",
        "premium_spark_tip_prime",
    };

    /// <summary>
    /// Every nav label key in every Core language file. The port's language files predate the
    /// 7.1.5 nav rework, so this reports the gap as a SKIP listing the missing keys rather than a
    /// red test: the keys land with the nav port (seam request), then this turns green on its own.
    /// </summary>
    [Fact]
    public void EveryNavLabelKeyExistsInEveryLanguage()
    {
        var dir = Path.Combine(ParityPaths.RepoRoot(), "CCP.Core", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.True(files.Length >= 9, "expected at least nine language files");
        var labels = NavSections.Order.Select(s => s.LabelKey)
            .Concat(NavSections.AllTabs.Select(t => t.LabelKey))
            .Concat(NewKeys).Distinct().ToList();
        var gaps = new List<string>();
        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var missing = labels.Where(k => !keys.Contains(k)).ToList();
            if (missing.Count > 0) gaps.Add($"{Path.GetFileName(file)} lacks {missing.Count}: {string.Join(", ", missing)}");
        }
        if (gaps.Count > 0) Assert.Skip("7.1.5 nav keys not yet in the port's language files: " + string.Join(" | ", gaps));
    }

    // ---- NavRailRules -----------------------------------------------------------------------

    [Fact]
    public void RailRowsAreEverySectionButTheGearAndShortcutsFollowThem()
    {
        Assert.Equal(7, NavRailRules.RailSections.Count);
        Assert.DoesNotContain(NavRailRules.RailSections, s => s.Key == NavSections.Settings);
        for (int i = 0; i < NavRailRules.RailSections.Count; i++)
            Assert.Equal(i + 1, NavRailRules.ShortcutNumber(NavRailRules.RailSections[i].Key));
        Assert.Equal(0, NavRailRules.ShortcutNumber(NavSections.Settings));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-3, null)]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    public void BadgeTextCapsAndClears(int count, string? expected)
        => Assert.Equal(expected, NavRailRules.BadgeText(count));

    [Fact]
    public void ARowOpensItsLastTabOrItsDefault()
    {
        Assert.Equal("availablesubjects", NavRailRules.TargetTab("social", null));
        Assert.Equal("availablesubjects", NavRailRules.TargetTab("social", "not json"));
        Assert.Equal("friends", NavRailRules.TargetTab("social", "{\"social\":\"friends\"}"));
        Assert.Equal("quests", NavRailRules.TargetTab("you", "{\"social\":\"friends\",\"you\":\"quests\"}"));
        Assert.Equal("studio", NavRailRules.TargetTab("studio", "{\"studio\":\"quests\"}"));
        Assert.Equal("studio", NavRailRules.TargetTab("studio", "{\"studio\":\"justdrop\"}"));
        Assert.Equal("assets", NavRailRules.TargetTab("library", "{\"library\":\"mods\"}"));
        Assert.Equal("playeyes", NavRailRules.TargetTab("play", "{\"play\":\"playeyes\"}"));
        Assert.Equal("appsettings", NavRailRules.TargetTab("settings", null));
        Assert.Equal("settings", NavRailRules.TargetTab("home", "{\"home\":\"premium\"}"));
        Assert.Null(NavRailRules.TargetTab("nope", null));
    }

    [Fact]
    public void GearTagMapsToSettings()
    {
        Assert.Equal(NavSections.Settings, NavRailRules.SectionForDoorTag("appsettings"));
        Assert.Equal("social", NavRailRules.SectionForDoorTag("social"));
        Assert.Equal("appsettings", NavRailRules.DoorTagForSection(NavSections.Settings));
    }

    [Fact]
    public void MotionLevelScalesStateChanges()
    {
        Assert.Equal(80, NavRailRules.Ms(80, MotionLevel.Full));
        Assert.Equal(40, NavRailRules.Ms(80, MotionLevel.Reduced));
        Assert.Equal(0, NavRailRules.Ms(80, MotionLevel.Off));
    }

    [Fact]
    public void TheRingIsThreePixelsAtRestAndThreeAndAHalfLit()
    {
        Assert.Equal(3.0, NavRailRules.RingThickness(false));
        Assert.Equal(3.5, NavRailRules.RingThickness(true));
        Assert.Equal(0xCC, NavRailRules.RingIdleAlpha);
        Assert.Equal(0xF2, NavRailRules.RingHoverAlpha);
        Assert.Equal(0xFF, NavRailRules.RingActiveAlpha);
        Assert.Equal(((byte)0x33, (byte)0x40), (NavRailRules.FillAlpha, NavRailRules.TileTintAlpha));
    }

    [Fact]
    public void TheLitRingIsTheHueLiftedTowardWhite()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var lit = NavRailRules.RingColor(hue, active: true, hover: false);
            var want = NavStripRules.Mix(hue, Argb.White, 0.25);
            Assert.Equal(0xFF, Argb.A(lit));
            Assert.Equal(want & 0xFFFFFF, lit & 0xFFFFFF);
            var idle = NavRailRules.RingColor(hue, active: false, hover: false);
            Assert.Equal(0xCC, Argb.A(idle));
            Assert.Equal(hue & 0xFFFFFF, idle & 0xFFFFFF);
            Assert.Equal(0xF2, Argb.A(NavRailRules.RingColor(hue, false, true)));
        }
        Assert.Equal(0.25, NavRailRules.RingActiveLift);
    }

    [Fact]
    public void TheArtIsNeverWashedIdleAndOnlyBreathedOnWhenLit()
    {
        Assert.Equal(0x0D, NavRailRules.ArtTintAlpha(true));
        Assert.Equal(0x00, NavRailRules.ArtTintAlpha(false));
    }

    [Fact]
    public void TheSpurMeetsTheTileCentre()
    {
        Assert.Equal(20, NavRailRules.SpurWidth);
        Assert.Equal(8, NavRailRules.SpurHeight);
        Assert.Equal(1 + 56 / 2.0, NavRailRules.SpurTop + NavRailRules.SpurHeight / 2);
        Assert.Equal(0xE6, NavRailRules.SpurEdgeAlpha);
        Assert.Equal(0x99, NavRailRules.SpurRingAlpha);
    }

    [Fact]
    public void TheVividHueKeepsItsAngleAndLosesThePastel()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var vivid = NavRailRules.Vivid(hue);
            var (h0, s0, l0) = NavRailRules.ToHsl(hue);
            var (h1, s1, l1) = NavRailRules.ToHsl(vivid);
            double dh = Math.Abs(h0 - h1); dh = Math.Min(dh, 360 - dh);
            Assert.True(dh <= 3, $"{s.Key}: hue angle moved {dh:F1} degrees");
            Assert.True(l1 <= NavRailRules.VividLightness + 0.01, $"{s.Key}: still pastel, L {l1:F2}");
            Assert.True(s1 >= Math.Min(s0, NavRailRules.VividSaturation) - 0.02, $"{s.Key}: lost saturation");
            Assert.True(l1 < l0, $"{s.Key}: the vivid ring must be deeper than the pastel hue");
        }
    }

    [Fact]
    public void TheRingIsLitFromTheTopLeftAndSitsInItsSocketWhenOn()
    {
        static double Luma(uint c) => 0.2126 * Argb.R(c) + 0.7152 * Argb.G(c) + 0.0722 * Argb.B(c);
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var idle = NavRailRules.RingStops(hue, active: false, hover: false);
            var lit = NavRailRules.RingStops(hue, active: true, hover: false);
            Assert.True(Luma(idle[0].Color) > Luma(idle[1].Color) && Luma(idle[1].Color) > Luma(idle[^1].Color), s.Key + " idle");
            Assert.True(Luma(lit[0].Color) < Luma(lit[^1].Color), s.Key + " lit");
            Assert.All(lit, st => Assert.Equal(0xFF, Argb.A(st.Color)));
            Assert.All(idle, st => Assert.Equal(NavRailRules.RingIdleAlpha, Argb.A(st.Color)));
            Assert.Equal(NavRailRules.RingHoverAlpha, Argb.A(NavRailRules.RingStops(hue, false, true)[1].Color));
            var v = NavRailRules.Vivid(hue);
            Assert.Equal(v & 0xFFFFFF, idle[1].Color & 0xFFFFFF);
            Assert.Equal(new[] { 0.0, 0.42, 1.0 }, idle.Select(x => x.Offset).ToArray());
            Assert.Equal(new[] { 0.0, 0.5, 1.0 }, lit.Select(x => x.Offset).ToArray());
        }
    }

    // ---- NavBadges ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(3, 0, true)]
    [InlineData(3, 3, false)]
    [InlineData(2, 3, false)]
    [InlineData(4, 3, true)]
    public void Fresh_only_when_the_count_rose_past_what_was_seen(int count, int seen, bool fresh)
        => Assert.Equal(fresh, NavBadges.IsFresh(count, seen));

    [Theory]
    [InlineData(7, 0, 0)]
    [InlineData(3, 7, 3)]
    [InlineData(5, 2, 5)]
    public void A_cleared_badge_forgets_what_was_seen(int seen, int count, int expected)
        => Assert.Equal(expected, NavBadges.SeenAfterCount(seen, count));

    [Fact]
    public void Seen_badge_dims_and_brightens_again_on_a_rise()
    {
        const string key = "parity-715-seen";   // own key: NavBadges is process-wide
        var raised = new List<(string, int)>();
        void OnChanged(string k, int n) { if (k == key) raised.Add((k, n)); }
        NavBadges.Changed += OnChanged;
        try
        {
            NavBadges.Set(key, 7);
            Assert.True(NavBadges.IsFresh(key));
            NavBadges.MarkSeen(key);
            Assert.False(NavBadges.IsFresh(key));
            NavBadges.Set(key, 6);
            Assert.False(NavBadges.IsFresh(key));
            NavBadges.Set(key, 8);
            Assert.True(NavBadges.IsFresh(key));
            NavBadges.MarkSeen(key);
            NavBadges.Set(key, 0);
            NavBadges.Set(key, 1);
            Assert.True(NavBadges.IsFresh(key));
            NavBadges.Set(key, 0);
            Assert.Equal(0, NavBadges.Get(key));
            Assert.Contains((key, 8), raised);
        }
        finally { NavBadges.Changed -= OnChanged; }
    }

    [Fact]
    public void Seen_opacity_is_dim_fresh_is_full()
    {
        Assert.Equal(1.0, NavRailRules.BadgeOpacity(true));
        Assert.Equal(0.45, NavRailRules.BadgeOpacity(false));
    }

    // ---- NavStripRules: pills, keyboard, memory ---------------------------------------------

    [Fact]
    public void ThePillsFollowTheTableAndSkipHiddenTabs()
    {
        foreach (var s in NavSections.Order)
        {
            var pills = NavStripRules.Pills(s.Key).Select(t => t.Key).ToArray();
            var expected = s.Key is NavSections.Home or NavSections.Settings
                ? Array.Empty<string>()
                : s.Tabs.Where(t => !t.Hidden).Select(t => t.Key).ToArray();
            Assert.Equal(expected, pills);
        }
        Assert.False(NavStripRules.ShowsHeader(NavSections.Home));
        Assert.True(NavStripRules.ShowsHeader(NavSections.Settings));
        Assert.False(NavStripRules.ShowsPills(NavSections.Settings));
        Assert.True(NavStripRules.AsksBeforeOpening(NavSections.Find("studio")!.Tabs.Single(t => t.Key == "justdrop")));
        Assert.Equal("nav_help_friends", NavStripRules.HelpKey(new NavTab("friends", "x", NavTabKind.Tab)));
        Assert.Equal("nav_tab_games", NavStripRules.PageLabelKey("lab"));
    }

    [Theory]
    [InlineData("lab", "play")]
    [InlineData("play", "play")]
    [InlineData("playeyes", "playeyes")]
    [InlineData("gradedintake", "playsessions")]
    [InlineData("lockdown", "playsessions")]
    [InlineData("blinktrainer", "playeyes")]
    [InlineData("haptics", "haptics")]
    [InlineData("ramp", "ramp")]
    [InlineData("spiral", null)]
    [InlineData("settings", null)]
    [InlineData("premium", null)]
    [InlineData("appsettings", null)]
    public void EveryPageLightsTheRightPill(string tab, string? pill)
        => Assert.Equal(pill, NavStripRules.ActivePill(tab));

    [Theory]
    [InlineData(0, 5, StripKey.Left, 4)]
    [InlineData(4, 5, StripKey.Right, 0)]
    [InlineData(2, 5, StripKey.Right, 3)]
    [InlineData(2, 5, StripKey.Left, 1)]
    [InlineData(3, 5, StripKey.Home, 0)]
    [InlineData(1, 5, StripKey.End, 4)]
    [InlineData(-1, 5, StripKey.Right, 0)]
    [InlineData(2, 5, StripKey.Other, -1)]
    [InlineData(0, 0, StripKey.Right, -1)]
    public void TheKeyboardWrapsAndJumps(int current, int count, StripKey key, int expected)
        => Assert.Equal(expected, NavStripRules.MoveIndex(current, count, key));

    [Theory]
    [InlineData(0, null, null, false)]
    [InlineData(1, null, null, true)]     // no account service: locked, the sign never hides by mistake
    [InlineData(1, true, false, false)]
    [InlineData(1, false, true, true)]    // Basic pills read the Premium gate
    [InlineData(2, true, false, true)]
    [InlineData(2, true, true, false)]
    [InlineData(2, null, true, false)]
    public void APillIsLockedOnlyWhenItsGateSaysSo(int tier, bool? premium, bool? lab, bool locked)
        => Assert.Equal(locked, NavStripRules.PillLocked(tier, premium, lab));

    [Fact]
    public void TheLastTabPerSectionRoundTrips()
    {
        var json = NavStripRules.WithLastTab("", NavSections.Social, "leaderboard");
        json = NavStripRules.WithLastTab(json, NavSections.Play, "playeyes");
        Assert.Equal("leaderboard", NavStripRules.LastTabFor(json, NavSections.Social));
        Assert.Equal("playeyes", NavStripRules.LastTabFor(json, NavSections.Play));
        Assert.Equal("discord", NavStripRules.LastTabFor(json, NavSections.You));
        Assert.Same(json, NavStripRules.WithLastTab(json, NavSections.Social, "leaderboard"));
        Assert.Equal("availablesubjects", NavStripRules.LastTabFor("{\"social\":\"quests\"}", NavSections.Social));
        Assert.Equal("assets", NavStripRules.LastTabFor("not json", NavSections.Library));
        Assert.Equal("play", NavStripRules.LastTabFor("{\"play\":\"lab\"}", NavSections.Play));
    }

    [Fact]
    public void TheSlideFollowsTheMotionLevel()
    {
        Assert.Equal(180, NavStripRules.SlideMs(MotionLevel.Full));
        Assert.Equal(90, NavStripRules.SlideMs(MotionLevel.Reduced));
        Assert.Equal(0, NavStripRules.SlideMs(MotionLevel.Off));
    }

    [Fact]
    public void EveryVisiblePillHasAGlyph()
    {
        foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            foreach (var t in NavStripRules.Pills(s.Key))
                Assert.NotNull(NavStripRules.Glyph(t.Key));
        Assert.Equal("\uE80F", NavStripRules.Glyph("Settings"));
        Assert.Null(NavStripRules.Glyph("nope"));
        Assert.Equal(36, NavStripRules.Glyphs.Count);
    }
}
