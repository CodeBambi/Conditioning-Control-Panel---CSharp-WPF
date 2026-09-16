using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The dashboard billboard (owner ask 2026-09-12): the roster and the rack walk. Both are pure, so
/// the targets, the art treatment and the swap are tested without a window; the cards, the clock
/// and the click are MainWindow.DashboardBillboard.cs.
///
/// <para>The roster tests are the ones that earn their keep on release day. A card whose art is not
/// an embedded Resource renders as a black box. A card whose strings are not localisation keys
/// ships English to nine languages. And a card that lands somewhere other than where its title says
/// is the bug the first desk pass found: the Discord card opened the in-app Discord PROFILE page
/// instead of the server invite, so every target is now pinned by id, kind and exact address.</para>
/// </summary>
public class DashboardBillboardTests
{
    // Every folder a roster poster may live in is declared in ConditioningControlPanel.csproj as
    // <Resource>. There is no recursive glob in that file, so a poster under any other folder
    // silently resolves to nothing at runtime and the card renders as a black box.
    private static readonly string[] AllowedPosterRoots = { "billboard/", "features/", "nav/", "exclusives/" };

    private static BillboardCard Card(string id) =>
        DashboardBillboard.Roster.Single(c => c.Id == id);

    // ---- the roster ----

    [Fact]
    public void TheRosterIsFourToSixHouseCards()
        => Assert.InRange(DashboardBillboard.Roster.Count, 4, 6);

    [Fact]
    public void TheRosterIsBiggerThanTheRack()
    {
        // The swap brings on the card that has been off screen longest. With nothing off screen
        // there is nothing to bring on and the rack is four still cards.
        Assert.True(DashboardBillboard.Roster.Count > DashboardBillboard.RackSlots);
    }

    [Fact]
    public void EveryCardHasItsOwnId()
    {
        var ids = DashboardBillboard.Roster.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void EveryStringIsALocalisationKey()
    {
        // Keys, never sentences: a space or a capital means somebody typed the copy in here and it
        // will never be translated.
        foreach (var card in DashboardBillboard.Roster)
        {
            foreach (var key in new[] { card.EyebrowKey, card.TitleKey, card.LineKey })
            {
                Assert.StartsWith("billboard_", key, StringComparison.Ordinal);
                Assert.DoesNotContain(" ", key, StringComparison.Ordinal);
                Assert.Equal(key.ToLowerInvariant(), key);
            }
        }
    }

    [Fact]
    public void EveryPosterIsArtTheAppAlreadyShips()
    {
        foreach (var card in DashboardBillboard.Roster)
        {
            bool declared = AllowedPosterRoots.Any(r => card.Poster.StartsWith(r, StringComparison.Ordinal));
            Assert.True(declared, card.Id + " points at " + card.Poster + ", which is not a declared <Resource>");
            Assert.EndsWith(".png", card.Poster, StringComparison.Ordinal);
            Assert.DoesNotContain("\\", card.Poster, StringComparison.Ordinal);
            Assert.DoesNotContain(" ", card.Poster, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void APosterResolvesToAPackUri()
        => Assert.Equal("pack://application:,,,/Resources/billboard/loom.png",
                        DashboardBillboard.PosterUri(Card("loom")));

    [Fact]
    public void EveryCardHasItsOwnPoster()
    {
        // Six 688x384 plates in Resources/billboard/, one per card, each listed by name in the
        // csproj. Two cards sharing one file means somebody repointed a card and forgot.
        var posters = DashboardBillboard.Roster.Select(c => c.Poster).ToList();
        Assert.Equal(posters.Count, posters.Distinct(StringComparer.Ordinal).Count());
        foreach (var card in DashboardBillboard.Roster)
            Assert.Equal("billboard/" + card.Id + ".png", card.Poster);
    }

    // ---- where the cards go ----

    public static IEnumerable<object?[]> Targets() => new[]
    {
        new object?[] { "webapp",     BillboardTargetKind.Link, "https://app.cclabs.app/?from=panel" },
        new object?[] { "remix",      BillboardTargetKind.Link, "https://cclabs.app/remix/?from=panel" },
        new object?[] { "loom",       BillboardTargetKind.Link, "https://cclabs.app/loom/?from=panel" },
        new object?[] { "discord",    BillboardTargetKind.Link, "https://discord.gg/YxVAMt4qaZ" },
        new object?[] { "exclusives", BillboardTargetKind.Tab,  "exclusives" },
        new object?[] { "support",    BillboardTargetKind.Link, "https://www.patreon.com/CodeBambi" },
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void EveryCardLandsWhereItsTitleSays(string id, BillboardTargetKind kind, string target)
    {
        var card = Card(id);
        Assert.Equal(kind, card.Kind);
        Assert.Equal(target, card.Target);
    }

    [Fact]
    public void TheDiscordCardOpensTheServerInviteNotTheProfilePage()
    {
        // The bug the desk pass caught: ShowTab("discord") is the in-app Discord PROFILE page.
        var card = Card("discord");
        Assert.Equal(BillboardTargetKind.Link, card.Kind);
        Assert.Equal(DiscordLinks.Invite, card.Target);
    }

    [Fact]
    public void TheSupportCardOpensThePatreonPage()
    {
        var card = Card("support");
        Assert.Equal(BillboardTargetKind.Link, card.Kind);
        Assert.Equal(DashboardBillboard.PatreonUrl, card.Target);
        Assert.Contains("patreon.com", card.Target, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLinkIsAbsoluteHttps()
    {
        foreach (var card in DashboardBillboard.Roster.Where(c => c.Kind == BillboardTargetKind.Link))
        {
            Assert.True(Uri.TryCreate(card.Target, UriKind.Absolute, out var uri), card.Id);
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
        }
    }

    [Fact]
    public void EveryTabTargetIsALowerCaseShowTabKey()
    {
        foreach (var card in DashboardBillboard.Roster.Where(c => c.Kind == BillboardTargetKind.Tab))
        {
            Assert.Equal(card.Target.ToLowerInvariant(), card.Target);
            Assert.DoesNotContain("/", card.Target, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(card.Target));
        }
    }

    // ---- the art treatment ----

    [Fact]
    public void EveryHouseCardIsCoverFittedBecauseItHasARealPoster()
    {
        // The plate exists for a card that arrives with nothing but a square icon - cover-fitting
        // a 512px mark into a 16:9 card is what made the first cut read as a blurry giant logo.
        // All six house cards now have their own 16:9 poster, so none of them needs it.
        foreach (var card in DashboardBillboard.Roster)
            Assert.Equal(BillboardArt.Cover, card.Art);
    }

    [Fact]
    public void ThePlateIsStillReachableForACardWithIconArt()
    {
        // Guards the enum member itself: the paint path in the DataTemplate is keyed off it, and
        // dropping one without the other is how the next icon-art card becomes a blurry giant.
        Assert.NotEqual(BillboardArt.Cover, BillboardArt.Plate);
    }

    // ---- the rack walk ----

    [Fact]
    public void TheRackOpensOnTheFirstFourCards()
    {
        var rack = DashboardBillboard.InitialRack(DashboardBillboard.Roster.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, rack.Slots);
        Assert.Equal(4, rack.NextCard);
        Assert.Equal(0, rack.NextSlot);
        Assert.Equal(-1, rack.ChangedSlot);
    }

    [Fact]
    public void AStepChangesExactlyOneSlot()
    {
        int n = DashboardBillboard.Roster.Count;
        var rack = DashboardBillboard.InitialRack(n);
        var next = DashboardBillboard.NextRack(rack, n);

        Assert.InRange(next.ChangedSlot, 0, rack.Slots.Count - 1);
        int changed = 0;
        for (int i = 0; i < rack.Slots.Count; i++) if (rack.Slots[i] != next.Slots[i]) changed++;
        Assert.Equal(1, changed);
        Assert.Equal(4, next.Slots[next.ChangedSlot]);
    }

    [Fact]
    public void NoCardIsEverOnScreenTwice()
    {
        int n = DashboardBillboard.Roster.Count;
        var rack = DashboardBillboard.InitialRack(n);
        for (int step = 0; step < 200; step++)
        {
            Assert.Equal(rack.Slots.Count, rack.Slots.Distinct().Count());
            Assert.All(rack.Slots, i => Assert.InRange(i, 0, n - 1));
            rack = DashboardBillboard.NextRack(rack, n);
        }
    }

    [Fact]
    public void EveryCardGetsItsTurn()
    {
        int n = DashboardBillboard.Roster.Count;
        var rack = DashboardBillboard.InitialRack(n);
        var seen = new HashSet<int>(rack.Slots);
        for (int step = 0; step < n * 2; step++)
        {
            rack = DashboardBillboard.NextRack(rack, n);
            foreach (var i in rack.Slots) seen.Add(i);
        }
        Assert.Equal(n, seen.Count);
    }

    [Fact]
    public void TheWalkComesBackToWhereItStarted()
    {
        int n = DashboardBillboard.Roster.Count;
        var start = DashboardBillboard.InitialRack(n);
        var rack = start;
        // Cards and slots advance in lockstep, so the cycle closes at lcm(roster, slots).
        int steps = Lcm(n, start.Slots.Count);
        for (int i = 0; i < steps; i++) rack = DashboardBillboard.NextRack(rack, n);
        Assert.Equal(start.Slots, rack.Slots);
        Assert.Equal(start.NextCard, rack.NextCard);
        Assert.Equal(start.NextSlot, rack.NextSlot);
    }

    [Fact]
    public void ARosterNoBiggerThanTheRackNeverSwaps()
    {
        var rack = DashboardBillboard.InitialRack(4);
        var next = DashboardBillboard.NextRack(rack, 4);
        Assert.Equal(-1, next.ChangedSlot);
        Assert.Equal(rack.Slots, next.Slots);
    }

    [Fact]
    public void AnEmptyRosterIsNotACrash()
    {
        var rack = DashboardBillboard.InitialRack(0);
        Assert.Empty(rack.Slots);
        Assert.Equal(-1, DashboardBillboard.NextRack(rack, 0).ChangedSlot);
    }

    [Fact]
    public void ADriftedRackComesHome()
    {
        int n = DashboardBillboard.Roster.Count;
        var drifted = new BillboardRack(new[] { 0, 1, 2, 3 }, NextCard: 99, NextSlot: -7, ChangedSlot: -1);
        var next = DashboardBillboard.NextRack(drifted, n);
        Assert.InRange(next.ChangedSlot, 0, 3);
        Assert.All(next.Slots, i => Assert.InRange(i, 0, n - 1));
    }

    [Fact]
    public void CardAtNeverThrows()
    {
        Assert.Equal(DashboardBillboard.Roster[0], DashboardBillboard.CardAt(-4));
        Assert.Equal(DashboardBillboard.Roster[0], DashboardBillboard.CardAt(DashboardBillboard.Roster.Count));
        Assert.Equal(DashboardBillboard.Roster[1], DashboardBillboard.CardAt(1));
    }

    [Theory]
    [InlineData(false, true, true)]   // on screen, pointer away: the clock runs
    [InlineData(true, true, false)]   // the pointer on the rack holds it
    [InlineData(false, false, false)] // another tab is up: nothing to advance
    [InlineData(true, false, false)]
    public void ThePointerAndAHiddenTabBothStopTheClock(bool pointerOver, bool onScreen, bool expected)
        => Assert.Equal(expected, DashboardBillboard.ShouldAdvance(pointerOver, onScreen));

    [Fact]
    public void TheCardHoldsLongEnoughToRead()
    {
        // An ambient loop by MotionFx's own definition, so it belongs in the 8-60s band.
        Assert.InRange(DashboardBillboard.RotateSeconds, 8, 60);
    }

    // ---- the copy ----

    [Fact]
    public void EveryCardStringReachedAllNineLanguages()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var card in DashboardBillboard.Roster)
            {
                foreach (var key in new[] { card.EyebrowKey, card.TitleKey, card.LineKey })
                {
                    Assert.True(file.ContainsKey(key), key + " is missing from " + lang + ".json");
                    Assert.False(string.IsNullOrWhiteSpace(file[key]), key + " is blank in " + lang + ".json");
                }
            }
        }
    }

    [Fact]
    public void TheCopyKeepsTheHouseVoice()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var card in DashboardBillboard.Roster)
            {
                foreach (var key in new[] { card.EyebrowKey, card.TitleKey, card.LineKey })
                {
                    var value = file[key];
                    Assert.DoesNotContain("—", value, StringComparison.Ordinal);
                    Assert.DoesNotContain("–", value, StringComparison.Ordinal);
                    Assert.DoesNotContain("!", value, StringComparison.Ordinal);
                    Assert.DoesNotContain("\n", value, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void TheFoldTooltipsReachedAllNineLanguages()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            Assert.True(file.ContainsKey("tooltip_browser_fold"), lang);
            Assert.True(file.ContainsKey("tooltip_browser_unfold"), lang);
        }
    }

    private static int Lcm(int a, int b)
    {
        int x = a, y = b;
        while (y != 0) { (x, y) = (y, x % y); }
        return a / x * b;
    }
}
