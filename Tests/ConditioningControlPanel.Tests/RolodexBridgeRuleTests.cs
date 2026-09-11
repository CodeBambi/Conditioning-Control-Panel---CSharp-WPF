using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services.Dashboard;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE ROLODEX'S DECISIONS, WITH NO BROWSER IN THE ROOM. Everything the embed view does that is
/// not "hold a WebView2" lives in <see cref="RolodexBridgeRule"/> and
/// <see cref="RolodexInitBuilder"/>, which is what makes any of it testable: a page message is a
/// string, a tour result is three integers, and the init envelope is a JObject.
///
/// <para>The one thing a test cannot see is the airspace - whether the HWND lands on exactly the
/// grid rect under the root Viewbox's scale. What it CAN pin is that both pickers are told to go
/// to the same place, which is the last scrape at the bottom of this file.</para>
/// </summary>
public class RolodexBridgeRuleTests
{
    // ── message parsing ──────────────────────────────────────────

    [Fact]
    public void ReadyIsRecognised()
        => Assert.Equal(RolodexMessageKind.Ready, RolodexBridgeRule.Parse("{\"type\":\"ready\",\"protocol\":1}").Kind);

    [Fact]
    public void APickCarriesItsKey()
    {
        var msg = RolodexBridgeRule.Parse("{\"type\":\"pick\",\"key\":\"braindrain\"}");
        Assert.Equal(RolodexMessageKind.Pick, msg.Kind);
        Assert.Equal("braindrain", msg.Key);
    }

    [Fact]
    public void APickWithNoKeyIsNotAPick()
    {
        // It would close the picker and change nothing, which reads to the user as a picker that
        // ate their click.
        Assert.Equal(RolodexMessageKind.Unknown, RolodexBridgeRule.Parse("{\"type\":\"pick\"}").Kind);
        Assert.Equal(RolodexMessageKind.Unknown, RolodexBridgeRule.Parse("{\"type\":\"pick\",\"key\":\"  \"}").Kind);
    }

    [Fact]
    public void TourDoneCarriesItsKeysInOrder()
    {
        var msg = RolodexBridgeRule.Parse("{\"type\":\"tourDone\",\"keys\":[\"fyp\",\"dtrh\",\"spiral\"]}");
        Assert.Equal(RolodexMessageKind.TourDone, msg.Kind);
        Assert.Equal(new[] { "fyp", "dtrh", "spiral" }, msg.Keys);
    }

    [Fact]
    public void TourDoneWithNoKeysIsStillATourDone()
    {
        // Done with an empty list ends the tour; it does not leave the browser up waiting for a
        // list that is never coming.
        var msg = RolodexBridgeRule.Parse("{\"type\":\"tourDone\"}");
        Assert.Equal(RolodexMessageKind.TourDone, msg.Kind);
        Assert.Empty(msg.Keys!);
    }

    [Fact]
    public void CloseIsRecognised()
        => Assert.Equal(RolodexMessageKind.Close, RolodexBridgeRule.Parse("{\"type\":\"close\"}").Kind);

    [Theory]
    [InlineData("error", "Error")]
    [InlineData("FATAL", "Error")]
    [InlineData("warn", "Warning")]
    [InlineData("warning", "Warning")]
    [InlineData("info", "Information")]
    [InlineData("debug", "Debug")]
    [InlineData("", "Debug")]
    [InlineData("nonsense", "Debug")]
    public void ALogLineMapsOntoTheAppsLevels(string level, string expected)
    {
        var msg = RolodexBridgeRule.Parse("{\"type\":\"log\",\"level\":\"" + level + "\",\"msg\":\"boot\"}");
        Assert.Equal(RolodexMessageKind.Log, msg.Kind);
        Assert.Equal("boot", msg.Text);
        Assert.Equal(expected, RolodexBridgeRule.LogLevel(msg.Level).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"type\":")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"nope\":true}")]
    [InlineData("{\"type\":\"somethingNew\"}")]
    public void AnythingUnreadableIsIgnoredRatherThanThrown(string json)
    {
        // This runs on the browser's message loop, where a throw is a crash and a page that has
        // learned a new word is not an error.
        Assert.Equal(RolodexMessageKind.Unknown, RolodexBridgeRule.Parse(json).Kind);
    }

    // ── the tour's slot assignment ───────────────────────────────

    [Fact]
    public void ThreePicksLandInSlotsZeroTwoAndThree()
    {
        var placed = RolodexBridgeRule.TourPlacements(new[] { "fyp", "dtrh", "arcademy" });
        Assert.Equal(new[] { 0, 2, 3 }, placed.Select(p => p.Slot));
        Assert.Equal(new[] { "fyp", "dtrh", "arcademy" }, placed.Select(p => p.Key));
    }

    [Fact]
    public void SlotOneIsLeftAloneBecauseTheShippedWallSplitsIt()
    {
        // video|bubblecount out of the box. A tour that took that cell would silently drop a
        // feature the user never touched.
        Assert.DoesNotContain(1, RolodexBridgeRule.TourSlots);
        Assert.Equal(RolodexBridgeRule.TourPickCount, RolodexBridgeRule.TourSlots.Count);
    }

    [Fact]
    public void FewerThanThreePicksFillWhatTheyHave()
    {
        var placed = RolodexBridgeRule.TourPlacements(new[] { "haptics" });
        var one = Assert.Single(placed);
        Assert.Equal(0, one.Slot);
        Assert.Equal("haptics", one.Key);

        Assert.Empty(RolodexBridgeRule.TourPlacements(Array.Empty<string>()));
        Assert.Empty(RolodexBridgeRule.TourPlacements(null));
    }

    [Fact]
    public void MoreThanThreePicksStopAtThree()
        => Assert.Equal(3, RolodexBridgeRule.TourPlacements(
            new[] { "fyp", "dtrh", "arcademy", "haptics", "awareness" }).Count);

    [Fact]
    public void KeysTheCatalogDoesNotKnowAreDropped()
    {
        // Dropped here rather than refused later by Place, so a stale page cannot leave a gap in
        // the middle of the run and push everything else down a cell.
        var placed = RolodexBridgeRule.TourPlacements(new[] { "not-a-feature", "fyp", "", "dtrh" });
        Assert.Equal(new[] { "fyp", "dtrh" }, placed.Select(p => p.Key));
        Assert.Equal(new[] { 0, 2 }, placed.Select(p => p.Slot));
    }

    [Fact]
    public void OneFeaturePickedTwiceStillOnlyTakesOneCell()
    {
        var placed = RolodexBridgeRule.TourPlacements(new[] { "fyp", "FYP", "dtrh" });
        Assert.Equal(new[] { "fyp", "dtrh" }, placed.Select(p => p.Key));
    }

    [Fact]
    public void APickAlreadyOnTheWallMovesRatherThanAppearingTwice()
    {
        // flash and subliminal are slots 0 and 2 of the shipped wall; bubbles is slot 7. Running
        // the tour's placements over the default layout must leave every key exactly once.
        var layout = DashboardLayout.Default();
        foreach (var (slot, key) in RolodexBridgeRule.TourPlacements(new[] { "bubbles", "flash", "subliminal" }))
            DashboardLayoutRule.Place(layout, slot, key, split: false);

        Assert.Equal("bubbles", layout.Slots[0].Primary);
        Assert.Equal("flash", layout.Slots[2].Primary);
        Assert.Equal("subliminal", layout.Slots[3].Primary);

        // Slot 7 held bubbles and is now a hole, which is a legal layout - and the invariant that
        // matters is that nothing is on the wall twice.
        var keys = layout.Slots.SelectMany(s => new[] { s.Primary, s.Secondary })
                               .Where(k => !string.IsNullOrEmpty(k)).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── what a close owes the tour ───────────────────────────────

    [Theory]
    [InlineData(RolodexMessageKind.Close)]
    [InlineData(RolodexMessageKind.TourDone)]
    public void EveryWayOUTOfATourSpendsIt(RolodexMessageKind why)
    {
        // Done and Esc are the same answer to a once-ever offer. Leaving the Home tab and a
        // session starting underneath both arrive as Close too, which is why they are the same
        // line of code and not three.
        Assert.Equal(RolodexCloseOutcome.MarkTourShown, RolodexBridgeRule.TourOutcomeFor("tour", why));
    }

    [Fact]
    public void ATourThatNeverStartedSpendsNothing()
    {
        // An offer the app could not make is not an offer the user waved away: the browser failed,
        // the user saw nothing, and the next launch owes them the tour.
        Assert.Equal(RolodexCloseOutcome.Nothing,
                     RolodexBridgeRule.TourOutcomeFor("tour", RolodexMessageKind.Unknown));
    }

    [Theory]
    [InlineData(RolodexMessageKind.Close)]
    [InlineData(RolodexMessageKind.TourDone)]
    [InlineData(RolodexMessageKind.Pick)]
    public void AnEditCloseIsNeverTheTour(RolodexMessageKind why)
    {
        // One pencil, one slot, no offer in it. Nothing an edit does may spend the tour.
        Assert.Equal(RolodexCloseOutcome.Nothing, RolodexBridgeRule.TourOutcomeFor("edit", why));
        Assert.Equal(RolodexCloseOutcome.Nothing, RolodexBridgeRule.TourOutcomeFor(null, why));
    }

    // ── the page log budget ──────────────────────────────────────

    [Fact]
    public void ALongPageLogLineIsCutRatherThanWrittenWhole()
    {
        var clamped = RolodexBridgeRule.ClampPageLog(new string('x', 5000));
        Assert.Equal(RolodexBridgeRule.MaxLogChars + 3, clamped.Length);
        Assert.EndsWith("...", clamped);
    }

    [Fact]
    public void AShortPageLogLineIsLeftExactlyAsItCame()
    {
        Assert.Equal("rings built", RolodexBridgeRule.ClampPageLog("rings built"));
        Assert.Equal(string.Empty, RolodexBridgeRule.ClampPageLog(null));
    }

    // ── the give-up flag ─────────────────────────────────────────

    [Fact]
    public void TheGiveUpFlagLatchesAndNeverComesBack()
    {
        try
        {
            RolodexAvailability.ResetForTests();
            Assert.False(RolodexAvailability.GivenUp);

            RolodexAvailability.GiveUp("no runtime");
            Assert.True(RolodexAvailability.GivenUp);

            // Nine pencils, one probe. There is no product path back: a runtime that was missing
            // at 10:00 is missing at 10:05.
            RolodexAvailability.GiveUp("still no runtime");
            Assert.True(RolodexAvailability.GivenUp);
        }
        finally { RolodexAvailability.ResetForTests(); }
    }

    // ── the init envelope ────────────────────────────────────────

    /// <summary>An art resolver that never touches WPF imaging: the path comes back as a fake data
    /// URI, or null for the one path we want to see fail.</summary>
    private static RolodexArtResolver FakeArt(string? failFor = null)
        => path => string.Equals(path, failFor, StringComparison.OrdinalIgnoreCase)
            ? null
            : "data:image/jpeg;base64,FAKE/" + path;

    private static JObject BuildInit(Func<DashboardFeature, bool>? entitled = null, string? failArtFor = null)
        => RolodexInitBuilder.BuildInit(
            mode: "edit", slot: 4, picks: 1, reducedMotion: false, lang: "de",
            title: f => "T:" + f.Key,
            blurb: f => "B:" + f.Key,
            entitled: entitled ?? (_ => true),
            art: FakeArt(failArtFor));

    [Fact]
    public void TheEnvelopeCarriesEveryFieldThePageReads()
    {
        var init = BuildInit();
        Assert.Equal("init", (string?)init["type"]);
        Assert.Equal("edit", (string?)init["mode"]);
        Assert.Equal(4, (int?)init["slot"]);
        Assert.Equal(1, (int?)init["picks"]);
        Assert.False((bool?)init["reducedMotion"]);
        Assert.Equal("de", (string?)init["lang"]);
    }

    [Fact]
    public void TheRingsAreTheCatalogsRingsInOrder()
    {
        var rings = (JArray)BuildInit()["rings"]!;
        var expected = FeatureCatalog.All.GroupBy(f => f.Ring).OrderBy(g => g.Key).ToList();

        Assert.Equal(expected.Count, rings.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Key, (int?)rings[i]["ring"]);
            Assert.Equal(expected[i].Select(f => f.Key),
                         ((JArray)rings[i]["faces"]!).Select(f => (string?)f["key"]));
        }

        // Every catalog row reaches the page exactly once. A feature the picker cannot show is a
        // feature the wall can hold and the user can never choose.
        var faces = rings.SelectMany(r => (JArray)r["faces"]!).Select(f => (string?)f["key"]).ToList();
        Assert.Equal(FeatureCatalog.All.Count, faces.Count);
        Assert.Equal(faces.Count, faces.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TitleBlurbAndTierRideAlongAlreadyResolved()
    {
        var face = Faces(BuildInit()).First(f => (string?)f["key"] == "dtrh");
        Assert.Equal("T:dtrh", (string?)face["title"]);
        Assert.Equal("B:dtrh", (string?)face["blurb"]);
        Assert.Equal(FeatureCatalog.Find("dtrh")!.Tier, (int?)face["tier"]);
    }

    [Fact]
    public void LockedIsTheOppositeOfEntitled_AndTierIsNotLocked()
    {
        // Two separate columns on purpose: tier is the livery (a gold or diamond rim), locked is
        // whether this account is holding the key. A locked face is still pickable by design.
        var init = BuildInit(entitled: f => f.Tier == 0);
        foreach (var face in Faces(init))
        {
            var row = FeatureCatalog.Find((string?)face["key"])!;
            Assert.Equal(row.Tier != 0, (bool?)face["locked"]);
        }

        var allOpen = Faces(BuildInit(entitled: _ => true));
        Assert.All(allOpen, f => Assert.False((bool?)f["locked"]));
        Assert.Contains(allOpen, f => (int?)f["tier"] > 0);
    }

    [Fact]
    public void ArtThatWillNotResolveGoesUpAsNullRatherThanAsAHole()
    {
        var flash = FeatureCatalog.Find("flash")!;
        var faces = Faces(BuildInit(failArtFor: flash.ArtPath));

        var broken = faces.First(f => (string?)f["key"] == "flash");
        Assert.Equal(JTokenType.Null, broken["art"]!.Type);

        var fine = faces.First(f => (string?)f["key"] == "bubbles");
        Assert.StartsWith("data:image/jpeg;base64,", (string?)fine["art"]);
    }

    [Fact]
    public void ATourAsksForThreeAndAnUnknownModeIsNotTrusted()
    {
        var tour = RolodexInitBuilder.BuildInit("tour", null, RolodexBridgeRule.TourPickCount, true, "en",
            f => f.Key, f => f.Key, _ => true, FakeArt());
        Assert.Equal("tour", (string?)tour["mode"]);
        Assert.Equal(3, (int?)tour["picks"]);
        Assert.Equal(JTokenType.Null, tour["slot"]!.Type);
        Assert.True((bool?)tour["reducedMotion"]);

        // The mode reaches a page that behaves differently in each, so it is normalised, not
        // forwarded. Same for a picks count below one and a blank language.
        var junk = RolodexInitBuilder.BuildInit("TOUR-ish", 0, 0, false, "  ",
            f => f.Key, f => f.Key, _ => true, FakeArt());
        Assert.Equal("edit", (string?)junk["mode"]);
        Assert.Equal(1, (int?)junk["picks"]);
        Assert.Equal("en", (string?)junk["lang"]);
    }

    private static List<JToken> Faces(JObject init)
        => ((JArray)init["rings"]!).SelectMany(r => (JArray)r["faces"]!).ToList();

    // ── the seam ─────────────────────────────────────────────────

    [Fact]
    public void BothPickersAreHandedTheSameRectOnTheSameGrid()
    {
        // The flat overlay and the rolodex answer the same question about the same nine cells, so
        // neither is allowed to be somewhere else on screen. Nothing in the compiler can see that
        // they agree: both reach VelvetFeatureGrid by name and set their own spans.
        foreach (var source in new[] { Source("MainWindow.DashboardEdit.cs"), Source("MainWindow.DashboardRolodex.cs") })
        {
            Assert.Contains("SettingsTab?.VelvetFeatureGrid", source, StringComparison.Ordinal);
            Assert.Contains("Grid.SetRowSpan(", source, StringComparison.Ordinal);
            Assert.Contains("Grid.SetColumnSpan(", source, StringComparison.Ordinal);
            Assert.Contains(", 4)", source, StringComparison.Ordinal);
            // Over the program lock ribbon's ZIndex 30, the highest thing authored on this grid.
            Assert.Contains("Panel.SetZIndex(", source, StringComparison.Ordinal);
            Assert.Contains(", 40)", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnlyTheThingsThatCannotComeBackLatchTheGiveUp()
    {
        // A navigation that failed once and a renderer that died once are this OPEN's problem, not
        // the session's: both close the rolodex and hand the question to the flat shelf, and only
        // the runtime probe and an environment that will not build latch. Nothing in the compiler
        // can see the difference, so the two routes are pinned by name.
        var embed = File.ReadAllText(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "Controls", "Dashboard", "RolodexEmbedView.cs"));

        Assert.Contains("Abort(\"navigation failed: \"", embed, StringComparison.Ordinal);
        Assert.Contains("OpenAborted", embed, StringComparison.Ordinal);
        // Two strikes on a dead renderer, the same stand-down BrowserVideoEngine runs app-wide.
        Assert.Contains("MaxProcessFailures = 2", embed, StringComparison.Ordinal);
        // ... and the argument string is a CONSTANT: WebView2 ties a user-data folder to the
        // options it was built with, so a reduced-motion switch that flips between two opens would
        // leave this picker unable to create an environment at all.
        Assert.DoesNotContain("PrefersReducedMotionArgument", embed, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFlatPickerIsStillThere()
    {
        // The fallback is not a fallback if it has been deleted. OpenDashboardPicker must still be
        // able to reach it, and the rolodex must be the thing that gives way.
        var edit = Source("MainWindow.DashboardEdit.cs");
        Assert.Contains("OpenFlatDashboardPicker", edit, StringComparison.Ordinal);
        Assert.Contains("TryOpenRolodexPicker", edit, StringComparison.Ordinal);
        Assert.Contains("new DashboardPickerPopup", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNewLocKeyIsInEnglish()
    {
        var en = JObject.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", "en.json")));
        foreach (var key in new[] { "dash_tour_title", "dash_tour_pick_three" })
            Assert.True(en[key] != null && !string.IsNullOrWhiteSpace((string?)en[key]),
                        key + " is missing from en.json");
    }

    private static string Source(string file) => File.ReadAllText(
        Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
