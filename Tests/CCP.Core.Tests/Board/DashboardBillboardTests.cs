using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Xunit;

namespace CCP.Core.Tests.Board;

/// <summary>
/// The Tonight Board's deck (2026-10-07): ranking, the one showcase-or-tip slot, the house filler,
/// the 7-day snooze, the hold, the sounds, the action rules and the walk. All pure; the card host's
/// drawing is in <see cref="BillboardCardHostRenderTests"/>.
/// </summary>
public class DashboardBillboardTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc);

    private static BillboardCardSpec Card(string id, BillboardCardKind kind, int priority = 0,
        BillboardBadge badge = BillboardBadge.None, bool snoozable = true, BillboardAction? action = null, string title = "t") =>
        new(id, kind, priority, "e", title, "l", "#ff4fa8", "poster", null, action ?? BillboardAction.None, badge, snoozable);

    private static IReadOnlyList<string> Ids(IEnumerable<BillboardCardSpec> cards) => cards.Select(c => c.Id).ToList();

    // ---- ranking -----------------------------------------------------------------------------

    [Fact]
    public void Cards_rank_by_kind_then_priority_then_arrival()
    {
        var ranked = DashboardBillboard.Rank(new[]
        {
            Card("house", BillboardCardKind.House),
            Card("resume", BillboardCardKind.Resume),
            Card("waiting.b", BillboardCardKind.Waiting, 1),
            Card("board", BillboardCardKind.Board),
            Card("waiting.a", BillboardCardKind.Waiting, 0),
            Card("live", BillboardCardKind.Live),
            Card("event", BillboardCardKind.Event),
            Card("waiting.c", BillboardCardKind.Waiting, 1),
        });
        Assert.Equal(new[] { "live", "board", "waiting.a", "waiting.b", "waiting.c", "resume", "event", "house" }, Ids(ranked));
    }

    [Fact]
    public void A_fresh_new_post_goes_first_and_only_until_it_has_been_shown()
    {
        var cards = new[] { Card("live", BillboardCardKind.Live), Card("board:7", BillboardCardKind.Board, badge: BillboardBadge.New) };
        Assert.Equal("board:7", DashboardBillboard.Rank(cards)[0].Id);
        var shown = new HashSet<string> { "board:7" };
        Assert.Equal("live", DashboardBillboard.Rank(cards, shown)[0].Id);
    }

    // ---- the showcase-or-tip slot ------------------------------------------------------------

    private static readonly BillboardCardSpec[] Mixed =
    {
        Card("showcase.remote", BillboardCardKind.Showcase, 0, BillboardBadge.Basic),
        Card("showcase.dtrh", BillboardCardKind.Showcase, 1, BillboardBadge.Prime),
        Card("tip.a", BillboardCardKind.Tip, 0),
        Card("tip.b", BillboardCardKind.Tip, 1),
        Card("tip.c", BillboardCardKind.Tip, 2),
        Card("live", BillboardCardKind.Live),
    };

    [Theory]
    [InlineData(BillboardTier.Free)]
    [InlineData(BillboardTier.Basic)]
    [InlineData(BillboardTier.Prime)]
    public void Exactly_one_showcase_or_tip_per_cycle(BillboardTier tier)
    {
        for (int cycle = 0; cycle < 6; cycle++)
        {
            var deck = DashboardBillboard.Build(Mixed, tier, null, Now, cycle);
            Assert.Equal(1, deck.Count(c => c.Kind is BillboardCardKind.Showcase or BillboardCardKind.Tip));
        }
    }

    [Fact]
    public void Prime_never_sees_a_showcase_and_starts_on_the_first_tip()
    {
        var deck = DashboardBillboard.Build(Mixed, BillboardTier.Prime, null, Now, cycle: 0);
        Assert.DoesNotContain(deck, c => c.Kind == BillboardCardKind.Showcase);
        Assert.Equal(new[] { "tip.a" }, Ids(deck.Where(c => c.Kind == BillboardCardKind.Tip)));
    }

    [Fact]
    public void The_tip_slot_shows_the_next_tip_every_cycle_and_wraps()
    {
        // Owner, 2026-10-07: every time the deck comes round, the next tip. No wall clock.
        var seen = Enumerable.Range(0, 7)
            .Select(cycle => DashboardBillboard.Build(Mixed, BillboardTier.Prime, null, Now, cycle).Single(c => c.Kind == BillboardCardKind.Tip).Id)
            .ToList();
        Assert.Equal(new[] { "tip.a", "tip.b", "tip.c", "tip.a", "tip.b", "tip.c", "tip.a" }, seen);
        // The same cycle always picks the same tip, whatever the time.
        Assert.Equal("tip.b", DashboardBillboard.Build(Mixed, BillboardTier.Prime, null, Now.AddHours(9), 1).Single(c => c.Kind == BillboardCardKind.Tip).Id);
    }

    [Fact]
    public void The_tip_slot_skips_snoozed_tips_and_never_shows_them_all()
    {
        var snoozes = new Dictionary<string, DateTime> { ["tip.b"] = Now.AddDays(3) };
        var seen = Enumerable.Range(0, 4)
            .Select(cycle => Ids(DashboardBillboard.Build(Mixed, BillboardTier.Prime, snoozes, Now, cycle).Where(c => c.Kind == BillboardCardKind.Tip)))
            .ToList();
        Assert.All(seen, ids => Assert.Single(ids));
        Assert.Equal(new[] { "tip.a", "tip.c", "tip.a", "tip.c" }, seen.Select(ids => ids[0]));
        // Every tip snoozed: the slot stays empty rather than showing one anyway.
        var all = Mixed.Where(c => c.Kind == BillboardCardKind.Tip).ToDictionary(c => c.Id, _ => Now.AddDays(1));
        Assert.DoesNotContain(DashboardBillboard.Build(Mixed, BillboardTier.Prime, all, Now, 2), c => c.Kind == BillboardCardKind.Tip);
    }

    [Fact]
    public void A_free_player_with_no_showcase_turns_the_tips_too()
    {
        var noShow = Mixed.Where(c => c.Kind != BillboardCardKind.Showcase).ToList();
        Assert.Equal("tip.a", DashboardBillboard.PickShowcaseOrTip(Array.Empty<BillboardCardSpec>(), noShow.Where(c => c.Kind == BillboardCardKind.Tip).ToList(), BillboardTier.Free, 0)!.Id);
        Assert.Equal("tip.c", DashboardBillboard.Build(noShow, BillboardTier.Free, null, Now, 2).Single(c => c.Kind == BillboardCardKind.Tip).Id);
        Assert.Null(DashboardBillboard.PickTip(Array.Empty<BillboardCardSpec>(), 4));
    }

    [Fact]
    public void Free_and_basic_get_the_showcase_the_provider_put_first()
    {
        foreach (var tier in new[] { BillboardTier.Free, BillboardTier.Basic })
        {
            var deck = DashboardBillboard.Build(Mixed, tier, null, Now, cycle: 5);
            Assert.Equal(new[] { "showcase.remote" }, Ids(deck.Where(c => c.Kind is BillboardCardKind.Showcase or BillboardCardKind.Tip)));
        }
    }

    [Fact]
    public void With_no_showcase_left_a_free_player_may_get_a_tip()
    {
        var deck = DashboardBillboard.Build(Mixed.Where(c => c.Kind != BillboardCardKind.Showcase), BillboardTier.Free, null, Now, 0);
        Assert.Single(deck, c => c.Kind == BillboardCardKind.Tip);
    }

    // ---- the house -------------------------------------------------------------------------

    [Fact]
    public void The_house_is_the_pinned_card_and_one_turning_filler()
    {
        var houses = Enumerable.Range(0, 5).Select(i => Card("h" + i, BillboardCardKind.House, i)).ToList();
        var seen = new HashSet<string>();
        for (int cycle = 0; cycle < 8; cycle++)
        {
            var picked = DashboardBillboard.PickHouse(houses, cycle);
            Assert.Equal(DashboardBillboard.HouseSlots, picked.Count);
            Assert.Equal("h0", picked[0].Id);
            seen.Add(picked[1].Id);
        }
        Assert.Equal(new[] { "h1", "h2", "h3", "h4" }, seen.OrderBy(s => s));
        Assert.Equal(2, DashboardBillboard.PickHouse(houses.Take(2).ToList(), 3).Count);
    }

    [Fact]
    public void House_provider_pins_discord_and_keeps_support_from_prime()
    {
        var house = new HouseProvider(k => k);
        var free = house.Current(new BillboardContext(BillboardTier.Free, Now, Now)).ToList();
        var prime = house.Current(new BillboardContext(BillboardTier.Prime, Now, Now)).ToList();
        Assert.Equal("house.discord", free.OrderBy(c => c.Priority).First().Id);
        Assert.Contains(free, c => c.Id == "house.support");
        Assert.DoesNotContain(prime, c => c.Id == "house.support");
        Assert.All(free, c =>
        {
            Assert.Equal(BillboardCardKind.House, c.Kind);
            Assert.Equal(BuiltInArtKeys.Poster, c.ArtKey);
            Assert.True(DashboardBillboard.IsActionAllowed(c.Action, true), c.Id + " " + c.Action.Target);
        });
        Assert.Equal(DiscordLinks.Invite, free.Single(c => c.Id == "house.discord").Action.Target);
    }

    // ---- snooze ------------------------------------------------------------------------------

    [Fact]
    public void A_snooze_lasts_seven_days()
    {
        Assert.Equal(TimeSpan.FromDays(7), DashboardBillboard.SnoozeFor);
        var card = Card("waiting.quests", BillboardCardKind.Waiting);
        var snoozes = new Dictionary<string, DateTime> { [card.Id] = DashboardBillboard.SnoozeUntil(Now) };
        Assert.True(DashboardBillboard.IsSnoozed(card, snoozes, Now.AddDays(6.9)));
        Assert.False(DashboardBillboard.IsSnoozed(card, snoozes, Now.AddDays(7)));
    }

    [Fact]
    public void A_board_post_and_an_unsnoozable_card_never_snooze()
    {
        var board = Card("board:3", BillboardCardKind.Board, snoozable: true);
        var fixedCard = Card("live", BillboardCardKind.Live, snoozable: false);
        var snoozes = new Dictionary<string, DateTime> { [board.Id] = Now.AddDays(3), [fixedCard.Id] = Now.AddDays(3) };
        Assert.False(DashboardBillboard.CanSnooze(board));
        Assert.False(DashboardBillboard.IsSnoozed(board, snoozes, Now));
        Assert.False(DashboardBillboard.IsSnoozed(fixedCard, snoozes, Now));
        Assert.Equal(2, DashboardBillboard.Build(new[] { board, fixedCard }, BillboardTier.Free, snoozes, Now, 0).Count);
    }

    [Fact]
    public void Expired_snoozes_are_pruned()
    {
        var snoozes = new Dictionary<string, DateTime> { ["a"] = Now.AddDays(-1), ["b"] = Now.AddDays(1) };
        Assert.True(DashboardBillboard.PruneSnoozes(snoozes, Now));
        Assert.Equal(new[] { "b" }, snoozes.Keys);
        Assert.False(DashboardBillboard.PruneSnoozes(snoozes, Now));
    }

    [Fact]
    public void Malformed_and_duplicate_cards_drop_but_a_wordless_board_stays()
    {
        var deck = DashboardBillboard.Build(new[]
        {
            Card("", BillboardCardKind.Live),
            Card("live", BillboardCardKind.Live),
            Card("live", BillboardCardKind.Live, 5),
            Card("blank", BillboardCardKind.Waiting, title: ""),
            Card("board:1", BillboardCardKind.Board, title: ""),
        }, BillboardTier.Free, null, Now, 0);
        Assert.Equal(new[] { "live", "board:1" }, Ids(deck));
    }

    // ---- the clock ---------------------------------------------------------------------------

    [Fact]
    public void Timings_are_the_approved_feel()
    {
        Assert.Equal(12, DashboardBillboard.HoldSeconds);
        Assert.Equal(450, DashboardBillboard.PushMs);
        Assert.Equal(60, DashboardBillboard.TextStaggerMs);
        Assert.Equal(340, DashboardBillboard.ThudMs);
        Assert.Equal(320, DashboardBillboard.SnoozeFoldMs);
        Assert.Equal((0.2, 1.5, 0.4, 1.0), DashboardBillboard.ThudCurve);
    }

    [Theory]
    [InlineData(false, true, true, false, true)]   // on Home, pointer away, motion on: runs
    [InlineData(true, true, true, false, false)]   // hover pauses
    [InlineData(false, false, true, false, false)] // another tab
    [InlineData(false, true, false, false, false)] // Motion Off: still cards, no auto-advance
    [InlineData(false, true, true, true, false)]   // keyboard focus in the card
    public void The_hold_runs_only_when_nothing_holds_it(bool pointer, bool onScreen, bool motion, bool focus, bool expected)
        => Assert.Equal(expected, DashboardBillboard.ShouldAdvance(pointer, onScreen, motion, focus));

    [Theory]
    [InlineData(true, false, true)]   // on Home: the art plays, pointer or not
    [InlineData(false, false, false)] // another tab
    [InlineData(true, true, false)]   // folding away
    public void Hover_holds_the_deck_but_never_the_art(bool onScreen, bool folding, bool expected)
    {
        // Owner, 2026-10-07: a board frozen mid-ola under the pointer reads as stuck. Hover holds
        // the hold clock (ShouldAdvance above) and nothing else; the art rule takes no pointer.
        Assert.Equal(expected, DashboardBillboard.ArtShouldPlay(onScreen, folding));
        Assert.False(DashboardBillboard.ShouldAdvance(pointerOver: true, onScreen: true, motionOn: true));
        Assert.DoesNotContain(typeof(DashboardBillboard).GetMethod(nameof(DashboardBillboard.ArtShouldPlay))!.GetParameters(),
            p => p.Name!.Contains("pointer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Chips_name_the_card_not_the_kind()
    {
        var quests = Card(ConditioningControlPanel.Services.Billboard.Providers.WaitingCards.CardQuests, BillboardCardKind.Waiting);
        var invite = Card(ConditioningControlPanel.Services.Billboard.Providers.WaitingCards.CardInvite, BillboardCardKind.Waiting);
        Assert.Equal("billboard_chip_quests", DashboardBillboard.ChipKey(quests));
        Assert.Equal("billboard_chip_invite", DashboardBillboard.ChipKey(invite));
        // Every house card has its own short name.
        foreach (var h in HouseProvider.Cards)
            Assert.True(DashboardBillboard.ChipNameKeys.ContainsKey(h.Id), h.Id + " has no chip name");
        // A card the table does not know falls back to its kind's label.
        Assert.Equal("billboard_deck_chip_tip", DashboardBillboard.ChipKey(Card("tip.blink", BillboardCardKind.Tip)));
        Assert.Equal("billboard_deck_chip_board", DashboardBillboard.ChipKey(Card("board:9", BillboardCardKind.Board)));
        // No two cards share a chip name.
        Assert.Equal(DashboardBillboard.ChipNameKeys.Count, DashboardBillboard.ChipNameKeys.Values.Distinct().Count());
    }

    [Fact]
    public void Review_mode_keeps_every_card_the_providers_return()
    {
        var snoozes = new Dictionary<string, DateTime> { ["house.loom"] = Now.AddDays(3) };
        var cards = new[]
        {
            Card("show.a", BillboardCardKind.Showcase), Card("show.b", BillboardCardKind.Showcase),
            Card("tip.a", BillboardCardKind.Tip), Card("tip.b", BillboardCardKind.Tip),
            Card("house.discord", BillboardCardKind.House), Card("house.webapp", BillboardCardKind.House),
            Card("house.remix", BillboardCardKind.House), Card("house.loom", BillboardCardKind.House),
            Card("waiting.quests", BillboardCardKind.Waiting),
        };
        var normal = DashboardBillboard.Build(cards, BillboardTier.Free, snoozes, Now, cycle: 0);
        var all = DashboardBillboard.Build(cards, BillboardTier.Free, snoozes, Now, cycle: 0, everyCard: true);
        Assert.True(normal.Count < cards.Length);
        // Every card, snoozed or not, except that the tips collapse to one turning slot.
        var notTips = cards.Where(c => c.Kind != BillboardCardKind.Tip).Select(c => c.Id).ToList();
        Assert.Equal(notTips.Concat(new[] { "tip.a" }).OrderBy(x => x), all.Select(c => c.Id).OrderBy(x => x));
        var next = DashboardBillboard.Build(cards, BillboardTier.Free, snoozes, Now, cycle: 1, everyCard: true);
        Assert.Equal(new[] { "tip.b" }, Ids(next.Where(c => c.Kind == BillboardCardKind.Tip)));
        Assert.Equal(all.Count, next.Count);
        // Without the env switch the deck is the normal one.
        if (Environment.GetEnvironmentVariable("CCP_BOARD_DECK_ALL") != "1")
            Assert.False(BillboardDeck.DeckAllRequested());
    }

    [Fact]
    public void A_paused_hold_resumes_with_what_is_left()
    {
        Assert.Equal(12000, DashboardBillboard.RemainingMs(0));
        Assert.Equal(3000, DashboardBillboard.RemainingMs(0.75), 3);
        Assert.Equal(0, DashboardBillboard.RemainingMs(1.4));
        Assert.Equal(12000, DashboardBillboard.RemainingMs(double.NaN));
    }

    [Theory]
    [InlineData(0, -1, 6, 5)]
    [InlineData(5, 1, 6, 0)]
    [InlineData(2, 1, 6, 3)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(9, 1, 3, 1)]
    public void Manual_navigation_wraps(int current, int direction, int count, int expected)
        => Assert.Equal(expected, DashboardBillboard.SlideIndex(current, direction, count));

    // ---- sound -------------------------------------------------------------------------------

    [Fact]
    public void Card_changes_walk_the_launcher_ladder()
    {
        var rungs = Enumerable.Range(-3, 40).Select(DashboardBillboard.ChangeRung).ToList();
        Assert.All(rungs, r => Assert.InRange(r, 0, DashboardBillboard.LadderRungs - 1));
        Assert.Equal(DashboardBillboard.RootRung, DashboardBillboard.ChangeRung(0));
        Assert.True(rungs.Distinct().Count() >= 5, "the walk should move, not repeat one note");
        foreach (var (a, b, gap) in new[] { DashboardBillboard.PressChime, DashboardBillboard.RippleChime })
        {
            Assert.InRange(a, 0, DashboardBillboard.LadderRungs - 1);
            Assert.InRange(b, 0, DashboardBillboard.LadderRungs - 1);
            Assert.InRange(gap, 20, 150);
        }
        Assert.Equal(0, DashboardBillboard.SnoozeRung);
    }

    // ---- actions -----------------------------------------------------------------------------

    [Theory]
    [InlineData(BillboardActionKind.Link, "https://cclabs.app/remix/?from=panel", true)]
    [InlineData(BillboardActionKind.Link, "https://app.cclabs.app/", true)]
    [InlineData(BillboardActionKind.Link, "https://discord.gg/YxVAMt4qaZ", true)]
    [InlineData(BillboardActionKind.Link, "https://www.patreon.com/CodeBambi", true)]
    [InlineData(BillboardActionKind.Link, "http://cclabs.app/", false)]
    [InlineData(BillboardActionKind.Link, "https://cclabs.app.evil.example/", false)]
    [InlineData(BillboardActionKind.Link, "https://evilcclabs.app/", false)]
    [InlineData(BillboardActionKind.Link, "https://user@cclabs.app/", false)]
    [InlineData(BillboardActionKind.Link, "javascript:alert(1)", false)]
    [InlineData(BillboardActionKind.Tab, "availablesubjects", true)]
    [InlineData(BillboardActionKind.Tab, "", false)]
    [InlineData(BillboardActionKind.Launch, "backroom", true)]
    public void Only_safe_buttons_run(BillboardActionKind kind, string target, bool expected)
        => Assert.Equal(expected, DashboardBillboard.IsActionAllowed(new BillboardAction(kind, target, "Go"), hasProvider: true));

    [Fact]
    public void A_callback_needs_its_provider_and_every_button_a_label()
    {
        Assert.False(DashboardBillboard.IsActionAllowed(new BillboardAction(BillboardActionKind.Callback, "join", "Join"), hasProvider: false));
        Assert.True(DashboardBillboard.IsActionAllowed(new BillboardAction(BillboardActionKind.Callback, "join", "Join"), hasProvider: true));
        Assert.False(DashboardBillboard.IsActionAllowed(new BillboardAction(BillboardActionKind.Tab, "lobby", ""), true));
        Assert.True(DashboardBillboard.IsActionAllowed(BillboardAction.None, false));
    }

    [Fact]
    public void A_refused_action_becomes_no_button()
    {
        var card = new DeckCard(Card("x", BillboardCardKind.Live, action: new BillboardAction(BillboardActionKind.Link, "https://evil.example/", "Go")), null, false);
        Assert.Equal(BillboardActionKind.None, card.Action.Kind);
    }

    [Theory]
    [InlineData("#5FE3FF", "#5fe3ff")]
    [InlineData(" #ffc94a ", "#ffc94a")]
    [InlineData("pink", "#ff4fa8")]
    [InlineData("#12345", "#ff4fa8")]
    [InlineData(null, "#ff4fa8")]
    public void A_card_hue_parses_or_falls_back_to_pink(string? hex, string expected)
        => Assert.Equal(expected, DashboardBillboard.Accent(hex));

    // ---- the walk ----------------------------------------------------------------------------

    private sealed class FakeProvider : IBillboardProvider
    {
        public List<BillboardCardSpec> Cards = new();
        public bool Throw;
        public List<string> Invoked = new();
        public string Id { get; init; } = "fake";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Throw ? throw new InvalidOperationException("boom") : Cards.ToList();
        public void Invoke(string actionTarget) => Invoked.Add(actionTarget);
        public event EventHandler? Changed;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static BillboardDeck Deck(BillboardTier tier, Dictionary<string, DateTime>? snoozes, Action? saved, params IBillboardProvider[] providers) =>
        new(() => providers, () => new BillboardContext(tier, Now, Now), snoozes, saved);

    [Fact]
    public void The_walk_runs_through_the_cycle_and_starts_a_new_one()
    {
        var p = new FakeProvider { Cards = { Card("live", BillboardCardKind.Live), Card("resume", BillboardCardKind.Resume), Card("house", BillboardCardKind.House) } };
        var deck = Deck(BillboardTier.Free, null, null, p);
        Assert.Equal("live", deck.Start()!.Spec.Id);
        Assert.Equal("resume", deck.Next()!.Spec.Id);
        Assert.Equal("house", deck.Next()!.Spec.Id);
        Assert.Equal(0, deck.Cycle);
        Assert.Equal("live", deck.Next()!.Spec.Id);
        Assert.Equal(1, deck.Cycle);
    }

    [Fact]
    public void A_prime_deck_shows_one_tip_and_the_next_one_each_time_round()
    {
        var tips = new FakeProvider { Id = "tip" };
        foreach (var tip in TipCards.Table)
            tips.Cards.Add(Card(TipCards.IdPrefix + tip.Id, BillboardCardKind.Tip, tips.Cards.Count));
        var live = new FakeProvider { Cards = { Card("live", BillboardCardKind.Live) } };
        var deck = Deck(BillboardTier.Prime, null, null, live, tips);
        deck.Start();
        var shown = new List<string>();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Assert.Single(deck.Cards, c => c.Spec.Kind == BillboardCardKind.Tip);
            shown.Add(deck.Cards.Single(c => c.Spec.Kind == BillboardCardKind.Tip).Spec.Id);
            int was = deck.Cycle;
            while (deck.Cycle == was) deck.Next();
        }
        Assert.Equal(TipCards.Table.Take(3).Select(t => TipCards.IdPrefix + t.Id), shown);
    }

    [Fact]
    public void Review_mode_deck_keeps_one_tip_slot_that_turns()
    {
        var tips = new FakeProvider { Id = "tip", Cards = { Card("tip.a", BillboardCardKind.Tip, 0), Card("tip.b", BillboardCardKind.Tip, 1), Card("tip.c", BillboardCardKind.Tip, 2) } };
        var show = new FakeProvider { Id = "showcase", Cards = { Card("show.x", BillboardCardKind.Showcase, badge: BillboardBadge.Basic) } };
        var deck = Deck(BillboardTier.Free, null, null, tips, show);
        deck.EveryCard = true;
        deck.Start();
        Assert.Equal(new[] { "tip.a" }, deck.Cards.Where(c => c.Spec.Kind == BillboardCardKind.Tip).Select(c => c.Spec.Id));
        Assert.Contains(deck.Cards, c => c.Spec.Id == "show.x");
        while (deck.Cycle == 0) deck.Next();
        Assert.Equal(new[] { "tip.b" }, deck.Cards.Where(c => c.Spec.Kind == BillboardCardKind.Tip).Select(c => c.Spec.Id));
    }

    [Fact]
    public void A_provider_change_lands_at_the_next_card_change_and_keeps_the_place()
    {
        var p = new FakeProvider { Cards = { Card("a", BillboardCardKind.Live), Card("c", BillboardCardKind.Resume), Card("d", BillboardCardKind.House) } };
        var deck = Deck(BillboardTier.Free, null, null, p);
        deck.Start();
        deck.Next(); // on "c"
        p.Cards.Insert(1, Card("b", BillboardCardKind.Waiting));
        deck.MarkDirty();
        Assert.Equal(3, deck.Cards.Count); // nothing moved yet
        Assert.Equal("d", deck.Next()!.Spec.Id);
        Assert.Equal(4, deck.Cards.Count);
    }

    [Fact]
    public void A_fresh_board_post_jumps_the_queue_once_and_wears_new_once()
    {
        var p = new FakeProvider { Cards = { Card("live", BillboardCardKind.Live), Card("resume", BillboardCardKind.Resume), Card("house", BillboardCardKind.House) } };
        var deck = Deck(BillboardTier.Free, null, null, p);
        deck.Start();
        p.Cards.Add(Card("board:9", BillboardCardKind.Board, badge: BillboardBadge.New, snoozable: false));
        deck.MarkDirty();
        var landed = deck.Next()!;
        Assert.Equal("board:9", landed.Spec.Id);
        Assert.True(landed.ShowNew);
        Assert.Equal(BillboardBadge.New, landed.Badge);

        // Round the cycle: the board is back in its own place, without the tag.
        DeckCard? again = null;
        for (int i = 0; i < 8 && again == null; i++)
        {
            var c = deck.Next()!;
            if (c.Spec.Id == "board:9") again = c;
        }
        Assert.NotNull(again);
        Assert.False(again!.ShowNew);
        Assert.Equal(BillboardBadge.None, again.Badge);
        Assert.Equal("live", deck.Cards[0].Spec.Id);
    }

    [Fact]
    public void Snoozing_saves_and_lands_on_the_card_that_takes_the_place()
    {
        var p = new FakeProvider { Cards = { Card("live", BillboardCardKind.Live), Card("resume", BillboardCardKind.Resume), Card("house", BillboardCardKind.House) } };
        var snoozes = new Dictionary<string, DateTime>();
        int saves = 0;
        var deck = Deck(BillboardTier.Free, snoozes, () => saves++, p);
        deck.Start();
        deck.Next(); // resume
        var next = deck.SnoozeCurrent(Now);
        Assert.Equal("house", next!.Spec.Id);
        Assert.Equal(1, saves);
        Assert.Equal(DashboardBillboard.SnoozeUntil(Now), snoozes["resume"]);
        Assert.DoesNotContain(deck.Cards, c => c.Spec.Id == "resume");
    }

    [Fact]
    public void Snoozing_the_last_card_lands_on_the_new_last_and_a_board_refuses()
    {
        var p = new FakeProvider { Cards = { Card("live", BillboardCardKind.Live), Card("board:2", BillboardCardKind.Board, snoozable: false) } };
        var deck = Deck(BillboardTier.Free, null, null, p);
        deck.Start();
        Assert.Equal("board:2", deck.Next()!.Spec.Id);
        Assert.Null(deck.SnoozeCurrent(Now));
        Assert.Equal("board:2", deck.Current!.Spec.Id);
        deck.Select(0);
        Assert.Equal("board:2", deck.SnoozeCurrent(Now)!.Spec.Id);
    }

    [Fact]
    public void A_throwing_provider_loses_its_own_cards_not_the_deck()
    {
        var bad = new FakeProvider { Id = "bad", Throw = true };
        var good = new FakeProvider { Id = "good", Cards = { Card("house", BillboardCardKind.House) } };
        var deck = Deck(BillboardTier.Free, null, null, bad, good);
        Assert.Equal("house", deck.Start()!.Spec.Id);
        Assert.Same(good, deck.Current!.Provider);
    }

    [Fact]
    public void The_showcase_picked_at_the_cycle_start_holds_for_the_whole_cycle()
    {
        // The showcase provider moves to its next clip the moment one plays and raises nothing,
        // so a mid-cycle rebuild must not ask it again.
        var show = new FakeProvider { Id = "showcase", Cards = { Card("showcase:remote", BillboardCardKind.Showcase, badge: BillboardBadge.Basic) } };
        var rest = new FakeProvider { Id = "rest", Cards = { Card("live", BillboardCardKind.Live), Card("resume", BillboardCardKind.Resume), Card("house", BillboardCardKind.House) } };
        var deck = Deck(BillboardTier.Free, null, null, rest, show);
        deck.Start();
        show.Cards[0] = Card("showcase:lockdown", BillboardCardKind.Showcase, badge: BillboardBadge.Basic);
        deck.MarkDirty();
        deck.Next();
        Assert.Contains(deck.Cards, c => c.Spec.Id == "showcase:remote");
        Assert.DoesNotContain(deck.Cards, c => c.Spec.Id == "showcase:lockdown");
        Assert.Same(show, deck.Cards.Single(c => c.Spec.Id == "showcase:remote").Provider);

        // A new cycle asks again.
        while (deck.Cycle == 0) deck.Next();
        Assert.Contains(deck.Cards, c => c.Spec.Id == "showcase:lockdown");
    }

    [Fact]
    public void A_showcase_snoozed_mid_cycle_leaves_the_slot_empty_until_the_next_cycle()
    {
        var show = new FakeProvider { Id = "showcase", Cards = { Card("showcase:remote", BillboardCardKind.Showcase, badge: BillboardBadge.Basic) } };
        var rest = new FakeProvider { Id = "rest", Cards = { Card("live", BillboardCardKind.Live), Card("house", BillboardCardKind.House) } };
        var deck = Deck(BillboardTier.Free, new Dictionary<string, DateTime>(), null, rest, show);
        deck.Start();
        deck.Select(deck.Cards.ToList().FindIndex(c => c.Spec.Id == "showcase:remote"));
        show.Cards[0] = Card("showcase:dtrh", BillboardCardKind.Showcase, badge: BillboardBadge.Prime);
        deck.SnoozeCurrent(Now);
        Assert.DoesNotContain(deck.Cards, c => c.Spec.Kind == BillboardCardKind.Showcase);
    }

    [Fact]
    public void A_chip_selects_its_card()
    {
        var p = new FakeProvider { Cards = { Card("a", BillboardCardKind.Live), Card("b", BillboardCardKind.Resume), Card("c", BillboardCardKind.House) } };
        var deck = Deck(BillboardTier.Free, null, null, p);
        deck.Start();
        Assert.Equal("c", deck.Select(2)!.Spec.Id);
        Assert.Equal(2, deck.Index);
        Assert.Equal("c", deck.Select(99)!.Spec.Id);
    }

    [Fact]
    public void An_empty_deck_is_not_a_crash()
    {
        var deck = Deck(BillboardTier.Free, null, null);
        Assert.Null(deck.Start());
        Assert.Null(deck.Next());
        Assert.Null(deck.SnoozeCurrent(Now));
    }

    // ---- art helpers -------------------------------------------------------------------------

    // ---- the copy ----------------------------------------------------------------------------

    // The port's language files live in Core (WPF read them through CompanionLocMasters).
    private static string LanguagesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CCP.Core", "CCP.Core.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "CCP.Core", "Localization", "Languages");
    }

    private static IEnumerable<string> Languages() =>
        Directory.GetFiles(LanguagesDir(), "*.json").Select(p => Path.GetFileNameWithoutExtension(p)!).OrderBy(n => n);

    private static Dictionary<string, string> Language(string code) =>
        Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(LanguagesDir(), code + ".json")))
            .Properties().ToDictionary(p => p.Name, p => (string?)p.Value ?? "");


    private static readonly string[] DeckKeys =
    {
        "billboard_deck_chip_live", "billboard_deck_chip_board", "billboard_deck_chip_waiting", "billboard_deck_chip_resume",
        "billboard_deck_chip_basic", "billboard_deck_chip_prime", "billboard_deck_chip_tip", "billboard_deck_badge_new",
        "billboard_deck_btn_join", "billboard_deck_btn_open", "billboard_deck_btn_support",
        "billboard_deck_snooze", "billboard_deck_snoozed", "billboard_deck_paused",
    };

    [Fact]
    public void Every_deck_and_house_string_reached_all_nine_languages_in_the_house_voice()
    {
        var keys = DeckKeys.Concat(DashboardBillboard.ChipNameKeys.Values).Concat(HouseProvider.Cards.SelectMany(c => new[]
        {
            $"billboard_{c.Stem}_eyebrow", $"billboard_{c.Stem}_title", $"billboard_{c.Stem}_line", c.ButtonKey,
        })).Distinct().ToList();

        // Every key the code asks for is one of these.
        var all = new BillboardCardSpec[] { Card("a", BillboardCardKind.Live), Card("b", BillboardCardKind.Board), Card("c", BillboardCardKind.Waiting),
            Card("d", BillboardCardKind.Resume), Card("e", BillboardCardKind.Showcase, badge: BillboardBadge.Prime),
            Card("f", BillboardCardKind.Showcase, badge: BillboardBadge.Basic), Card("g", BillboardCardKind.Tip) };
        foreach (var k in all.Select(DashboardBillboard.ChipKey).Concat(new[] { BillboardBadge.New, BillboardBadge.Basic, BillboardBadge.Prime }.Select(DashboardBillboard.BadgeKey)))
            Assert.Contains(k, keys);

        foreach (var lang in Languages())
        {
            var file = Language(lang);
            foreach (var key in keys)
            {
                Assert.True(file.ContainsKey(key), key + " is missing from " + lang + ".json");
                var value = file[key];
                Assert.False(string.IsNullOrWhiteSpace(value), key + " is blank in " + lang);
                Assert.DoesNotContain("\u2014", value, StringComparison.Ordinal);
                Assert.DoesNotContain("\u2013", value, StringComparison.Ordinal);
                Assert.DoesNotContain("!", value, StringComparison.Ordinal);
            }
            Assert.Contains("{0}", file["billboard_deck_snoozed"], StringComparison.Ordinal);
        }
    }
}
