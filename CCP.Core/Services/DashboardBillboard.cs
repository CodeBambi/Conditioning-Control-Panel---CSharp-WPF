using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The Tonight Board's rules (2026-10-07): which cards make the deck, in what order, how long
    /// each holds, what a snooze means and which note a change plays. Pure, so every rule is pinned
    /// by a test without a window. <see cref="BillboardDeck"/> is the walk built on these rules;
    /// <c>Controls/Billboard/BillboardCardHost</c> draws it and <c>MainWindow.DashboardBillboard.cs</c>
    /// runs the buttons. Design page: https://claude.ai/artifact/HjF7nno2cxiG6Bopjg8KWr
    /// </summary>
    public static class DashboardBillboard
    {
        // ---- timings (owner decisions, mockup numbers) -------------------------------------------

        /// <summary>How long a card holds before the deck moves on. Hover and focus pause it.</summary>
        public const int HoldSeconds = 12;

        /// <summary>The new card pushes in over the old one.</summary>
        public const int PushMs = 450;

        /// <summary>How far the old card slides left while the new one pushes in (share of width).</summary>
        public const double PushOldShift = 0.18;

        /// <summary>Where the new card's left edge starts, as a share of the width.</summary>
        public const double PushNewStart = 0.40;

        /// <summary>The new card's scale at the start of the push; it settles to 1.</summary>
        public const double PushNewScale = 1.05;

        /// <summary>Eyebrow, title and line land this far apart.</summary>
        public const int TextStaggerMs = 60;

        /// <summary>One line of text rising into place.</summary>
        public const int TextRiseMs = 420;

        /// <summary>How far a line of text rises, in px.</summary>
        public const double TextRisePx = 14;

        /// <summary>The button landing (cubic-bezier(.2,1.5,.4,1)).</summary>
        public const int ThudMs = 340;

        /// <summary>When the button lands, after the card starts.</summary>
        public const int ThudDelayMs = 280;

        /// <summary>When the badge lands, after the card starts.</summary>
        public const int BadgeDelayMs = 200;

        /// <summary>Snooze: the words fold away and the chip pops off before the next card.</summary>
        public const int SnoozeFoldMs = 320;

        /// <summary>Arrival motes start this long after a card lands.</summary>
        public const int MotesDelayMs = 220;

        /// <summary>The button's action waits this long so the press and the sparks read.</summary>
        public const int PressActionDelayMs = 120;

        /// <summary>A snoozed card stays away this long.</summary>
        public static readonly TimeSpan SnoozeFor = TimeSpan.FromDays(7);

        /// <summary>The control points of the house "thud": cubic-bezier(.2,1.5,.4,1).</summary>
        public static readonly (double X1, double Y1, double X2, double Y2) ThudCurve = (0.2, 1.5, 0.4, 1.0);

        // ---- the deck's shape --------------------------------------------------------------------

        /// <summary>House cards in one cycle: the pinned one (lowest priority, Discord) and one
        /// rotating filler. The house is filler, never the show.</summary>
        public const int HouseSlots = 2;

        // ---- sound -------------------------------------------------------------------------------

        /// <summary>The launcher's pluck ladder: wood_05.wav is C5, wood_00 C4, wood_17 E7.</summary>
        public const int RootRung = 5;

        /// <summary>Rungs on the launcher's ladder (Scripts/render-launcher-notes.mjs).</summary>
        public const int LadderRungs = 18;

        /// <summary>The walk one card change after another takes over the pentatonic ladder
        /// (the mockup's WALK), as steps above <see cref="RootRung"/>.</summary>
        private static readonly int[] Walk = { 0, 2, 1, 3, 2, 4, 3, 5, 4, 2 };

        /// <summary>The rung the n-th card change plays. Always inside the ladder.</summary>
        public static int ChangeRung(int step) => RootRung + Walk[Turn(step, Walk.Length)];

        /// <summary>The two notes of a button press (A5 then D6), the second this many ms later.</summary>
        public static readonly (int First, int Second, int GapMs) PressChime = (RootRung + 4, RootRung + 6, 70);

        /// <summary>The soft low note of a snooze: the bottom of the ladder.</summary>
        public const int SnoozeRung = 0;

        /// <summary>A touch on the board: E5 then C6.</summary>
        public static readonly (int First, int Second, int GapMs) RippleChime = (RootRung + 2, RootRung + 5, 50);

        // ---- ranking -----------------------------------------------------------------------------

        /// <summary>
        /// Deck order: an unseen NEW card first (a fresh board post shows first, once), then by
        /// kind (Live, Board, Waiting, Resume, Event, Showcase, Tip, House), then by priority, then
        /// in the order the providers gave them. Stable.
        /// </summary>
        public static IReadOnlyList<BillboardCardSpec> Rank(IEnumerable<BillboardCardSpec> cards, ISet<string>? newShown = null) =>
            cards
                .Select((c, i) => (c, i))
                .OrderBy(p => IsFreshNew(p.c, newShown) ? 0 : 1)
                .ThenBy(p => (int)p.c.Kind)
                .ThenBy(p => p.c.Priority)
                .ThenBy(p => p.i)
                .Select(p => p.c)
                .ToList();

        /// <summary>A card wearing NEW that this run has not shown yet.</summary>
        public static bool IsFreshNew(BillboardCardSpec card, ISet<string>? newShown) =>
            card.Badge == BillboardBadge.New && (newShown == null || !newShown.Contains(card.Id));

        /// <summary>
        /// The whole deck for one cycle. Drops malformed, duplicate and snoozed cards, keeps
        /// exactly one showcase-or-tip and the house filler, then ranks.
        ///
        /// <para>Prime never sees a showcase: the slot is a tip. Everyone else gets a showcase when
        /// one exists, and a tip only when no showcase does. Only ever one of them.</para>
        /// </summary>
        public static IReadOnlyList<BillboardCardSpec> Build(
            IEnumerable<BillboardCardSpec> cards,
            BillboardTier tier,
            IReadOnlyDictionary<string, DateTime>? snoozes,
            DateTime nowUtc,
            int cycle,
            ISet<string>? newShown = null,
            bool everyCard = false)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var clean = new List<BillboardCardSpec>();
            foreach (var c in cards)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.Id)) continue;
                // A board post may carry no words; every other card needs a title to be a card.
                if (string.IsNullOrWhiteSpace(c.Title) && c.Kind != BillboardCardKind.Board) continue;
                if (!seen.Add(c.Id)) continue;
                if (!everyCard && IsSnoozed(c, snoozes, nowUtc)) continue;
                clean.Add(c);
            }

            var tips = Ordered(clean.Where(c => c.Kind == BillboardCardKind.Tip));

            // Review mode (DEBUG, CCP_BOARD_DECK_ALL=1): every card, snoozed or not, no slot or house
            // cut, except the tips: they share ONE slot that turns with the cycle, as in play.
            if (everyCard)
            {
                var review = clean.Where(c => c.Kind != BillboardCardKind.Tip).ToList();
                var tip = PickTip(tips, cycle);
                if (tip != null) review.Add(tip);
                return Rank(review, newShown);
            }

            var showcases = Ordered(clean.Where(c => c.Kind == BillboardCardKind.Showcase));
            var houses = Ordered(clean.Where(c => c.Kind == BillboardCardKind.House));

            var deck = clean.Where(c => c.Kind is not (BillboardCardKind.Showcase or BillboardCardKind.Tip or BillboardCardKind.House)).ToList();
            var slot = PickShowcaseOrTip(showcases, tips, tier, cycle);
            if (slot != null) deck.Add(slot);
            deck.AddRange(PickHouse(houses, cycle));

            return Rank(deck, newShown);
        }

        /// <summary>
        /// The one showcase-or-tip card of a cycle, or null. A showcase is the FIRST of its pool
        /// (lowest priority) that survived the snooze filter: the showcase provider already hands
        /// over one clip per cycle, so a second turn here would skip clips. A tip is the next one
        /// each time the deck comes round (<see cref="PickTip"/>).
        /// </summary>
        public static BillboardCardSpec? PickShowcaseOrTip(
            IReadOnlyList<BillboardCardSpec> showcases, IReadOnlyList<BillboardCardSpec> tips, BillboardTier tier, int cycle)
        {
            if (tier != BillboardTier.Prime && showcases.Count > 0) return showcases[0];
            return PickTip(tips, cycle);
        }

        /// <summary>
        /// The tip of a cycle: the tip provider hands over the whole table in order, snoozed tips
        /// are already gone, and the deck turns what is left one step per cycle, so every time the
        /// deck comes round the slot shows the NEXT tip (owner, 2026-10-07: never a chip per tip,
        /// never the wall clock).
        /// </summary>
        public static BillboardCardSpec? PickTip(IReadOnlyList<BillboardCardSpec> tips, int cycle) =>
            tips.Count == 0 ? null : tips[Turn(cycle, tips.Count)];

        /// <summary>The house filler: the pinned card (lowest priority) and one rotating card.</summary>
        public static IReadOnlyList<BillboardCardSpec> PickHouse(IReadOnlyList<BillboardCardSpec> houses, int cycle)
        {
            if (houses.Count <= HouseSlots) return houses;
            var picked = new List<BillboardCardSpec> { houses[0] };
            int rest = houses.Count - 1;
            for (int k = 0; k < HouseSlots - 1; k++)
                picked.Add(houses[1 + Turn(cycle * (HouseSlots - 1) + k, rest)]);
            return picked;
        }

        private static IReadOnlyList<BillboardCardSpec> Ordered(IEnumerable<BillboardCardSpec> cards) =>
            cards.Select((c, i) => (c, i)).OrderBy(p => p.c.Priority).ThenBy(p => p.i).Select(p => p.c).ToList();

        /// <summary>A non-negative turn of a counter over a pool.</summary>
        public static int Turn(int counter, int count) =>
            count <= 0 ? 0 : (int)(((long)counter % count + count) % count);

        // ---- snooze ------------------------------------------------------------------------------

        /// <summary>Whether a card may be snoozed at all. A board post never can.</summary>
        public static bool CanSnooze(BillboardCardSpec card) =>
            card.Snoozable && card.Kind != BillboardCardKind.Board;

        /// <summary>A snoozable card whose snooze has not run out yet.</summary>
        public static bool IsSnoozed(BillboardCardSpec card, IReadOnlyDictionary<string, DateTime>? snoozes, DateTime nowUtc) =>
            CanSnooze(card) && snoozes != null && snoozes.TryGetValue(card.Id, out var until) && until > nowUtc;

        /// <summary>When a snooze taken now runs out.</summary>
        public static DateTime SnoozeUntil(DateTime nowUtc) => nowUtc + SnoozeFor;

        /// <summary>Drops every snooze that has run out. Returns whether anything changed.</summary>
        public static bool PruneSnoozes(IDictionary<string, DateTime> snoozes, DateTime nowUtc)
        {
            var gone = snoozes.Where(p => p.Value <= nowUtc).Select(p => p.Key).ToList();
            foreach (var k in gone) snoozes.Remove(k);
            return gone.Count > 0;
        }

        // ---- the clock ---------------------------------------------------------------------------

        /// <summary>
        /// Whether the hold runs on. It holds while the pointer is on the card (a card that walks
        /// away mid-read is a card nobody finishes), while a key has focus in it, while Home is not
        /// on screen, and always at Motion Off (still cards, no auto-advance).
        /// </summary>
        public static bool ShouldAdvance(bool pointerOver, bool onScreen, bool motionOn, bool focusWithin = false) =>
            onScreen && motionOn && !pointerOver && !focusWithin;

        /// <summary>
        /// Whether the card's ART plays. Owner, 2026-10-07: hover holds only the deck (the hold and
        /// the "paused while you read" pill); the art keeps moving under the pointer, or a board
        /// frozen mid-ola reads as stuck. It stops only off screen and while the card folds away.
        /// Motion Off is each view's own gate (Play() under Motion Off holds the still frame).
        /// There is deliberately no pointer argument.
        /// </summary>
        public static bool ArtShouldPlay(bool onScreen, bool folding) => onScreen && !folding;

        /// <summary>Milliseconds left in a hold that has run to <paramref name="progress"/> (0..1).</summary>
        public static double RemainingMs(double progress) =>
            HoldSeconds * 1000.0 * (1 - Math.Clamp(double.IsNaN(progress) ? 0 : progress, 0, 1));

        /// <summary>Manual navigation wraps both ways.</summary>
        public static int SlideIndex(int current, int direction, int count)
        {
            if (count <= 0) return 0;
            int c = current < 0 || current >= count ? 0 : current;
            return Turn(c + direction % count, count);
        }

        // ---- actions -----------------------------------------------------------------------------

        /// <summary>Hosts a Link action may open: the CC Labs sites, the Discord invite and the
        /// project's own Patreon page (the house Support card). Sub domains count.</summary>
        private static readonly string[] LinkHosts = { "cclabs.app", "discord.gg", "discord.com", "patreon.com" };

        /// <summary>
        /// Whether a card's button may run. Links are absolute https on an allowed host; a callback
        /// needs the provider that issued it; every button needs a label.
        /// </summary>
        public static bool IsActionAllowed(BillboardAction? action, bool hasProvider)
        {
            if (action == null) return false;
            if (action.Kind == BillboardActionKind.None) return true;
            if (string.IsNullOrWhiteSpace(action.Label) || string.IsNullOrWhiteSpace(action.Target)) return false;
            switch (action.Kind)
            {
                case BillboardActionKind.Tab:
                case BillboardActionKind.Launch:
                    return true;
                case BillboardActionKind.Callback:
                    return hasProvider;
                case BillboardActionKind.Link:
                    if (!Uri.TryCreate(action.Target, UriKind.Absolute, out var uri)) return false;
                    if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return false;
                    var host = uri.Host.ToLowerInvariant();
                    return LinkHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
                default:
                    return false;
            }
        }

        // ---- look --------------------------------------------------------------------------------

        /// <summary>The house pink, for a card whose hue does not parse.</summary>
        public const string FallbackAccent = "#ff4fa8";

        /// <summary>The card's hue as "#rrggbb", or the house pink.</summary>
        public static string Accent(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return FallbackAccent;
            var h = hex.Trim();
            if (h.Length == 7 && h[0] == '#' && h.Skip(1).All(Uri.IsHexDigit)) return h.ToLowerInvariant();
            return FallbackAccent;
        }

        /// <summary>
        /// Chips name the CARD, not its kind (owner, 2026-10-07: two chips both read "Waiting").
        /// One short word per known card id; a card not in the table falls back to its kind's label.
        /// </summary>
        // Card ids spelled out: the WPF providers that own them (Services/Billboard/Providers,
        // LiveCards/WaitingCards/ResumeCards/EventCards.Card*) read App.* and stay in the head.
        public static readonly IReadOnlyDictionary<string, string> ChipNameKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["live.friend"] = "billboard_chip_friend",
            ["live.tables"] = "billboard_chip_tables",
            ["live.playing"] = "billboard_chip_playing",
            ["waiting.quests"] = "billboard_chip_quests",
            ["waiting.program"] = "billboard_chip_program",
            ["waiting.invite"] = "billboard_chip_invite",
            ["resume.session"] = "billboard_chip_session",
            ["resume.deeper"] = "billboard_chip_deeper",
            ["event.locktober"] = "billboard_chip_locktober",
            ["house.discord"] = "billboard_chip_discord",
            ["house.webapp"] = "billboard_chip_webapp",
            ["house.remix"] = "billboard_chip_remix",
            ["house.loom"] = "billboard_chip_loom",
            ["house.support"] = "billboard_chip_support",
            ["house.backroom"] = "billboard_chip_backroom",
        };

        /// <summary>The loc key of a card's chip label: its own name when the table knows the card,
        /// else its kind's label, or null for a card that names its chip with its own title.</summary>
        public static string? ChipKey(BillboardCardSpec card) =>
            card.Id != null && ChipNameKeys.TryGetValue(card.Id, out var named) ? named : KindChipKey(card);

        /// <summary>The kind's own chip label (Live, Board, Tip, Basic, Prime...), or null.</summary>
        public static string? KindChipKey(BillboardCardSpec card) => card.Kind switch
        {
            BillboardCardKind.Live => "billboard_deck_chip_live",
            BillboardCardKind.Board => "billboard_deck_chip_board",
            BillboardCardKind.Waiting => "billboard_deck_chip_waiting",
            BillboardCardKind.Resume => "billboard_deck_chip_resume",
            BillboardCardKind.Showcase => card.Badge == BillboardBadge.Prime ? "billboard_deck_chip_prime" : "billboard_deck_chip_basic",
            BillboardCardKind.Tip => "billboard_deck_chip_tip",
            _ => null,
        };

        /// <summary>The loc key of a corner badge, or null for none.</summary>
        public static string? BadgeKey(BillboardBadge badge) => badge switch
        {
            BillboardBadge.New => "billboard_deck_badge_new",
            BillboardBadge.Basic => "billboard_deck_chip_basic",
            BillboardBadge.Prime => "billboard_deck_chip_prime",
            _ => null,
        };

        /// <summary>Whether the art fills the whole card with no shade or words (the pixel board).</summary>
        public static bool IsFullBleed(BillboardCardSpec card) => card.Kind == BillboardCardKind.Board;
    }

    /// <summary>One card in the deck: the spec, who made it, and whether NEW shows this time.</summary>
    public sealed record DeckCard(BillboardCardSpec Spec, IBillboardProvider? Provider, bool ShowNew)
    {
        /// <summary>The badge to draw: NEW only on its first show, the others always.</summary>
        public BillboardBadge Badge => Spec.Badge == BillboardBadge.New && !ShowNew ? BillboardBadge.None : Spec.Badge;

        /// <summary>The button, or None when the action did not pass the deck's rules.</summary>
        public BillboardAction Action =>
            DashboardBillboard.IsActionAllowed(Spec.Action, Provider != null) ? Spec.Action : BillboardAction.None;

        /// <summary>The x shows.</summary>
        public bool CanSnooze => DashboardBillboard.CanSnooze(Spec);
    }

    /// <summary>
    /// The deck as a walk: one cycle at a time, built from the providers when a cycle starts and
    /// rebuilt at the next card change when a provider says its state moved. No WPF and no clock:
    /// the host says when to move on.
    /// </summary>
    public sealed class BillboardDeck
    {
        private readonly Func<IEnumerable<IBillboardProvider>> _providers;
        private readonly Func<BillboardContext> _context;
        private readonly IDictionary<string, DateTime> _snoozes;
        private readonly Action? _snoozesChanged;
        private readonly HashSet<string> _newShown = new(StringComparer.Ordinal);
        private List<DeckCard> _cards = new();
        private bool _dirty;
        private DeckCard? _cycleSlot;

        public BillboardDeck(
            Func<IEnumerable<IBillboardProvider>> providers,
            Func<BillboardContext> context,
            IDictionary<string, DateTime>? snoozes = null,
            Action? snoozesChanged = null)
        {
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _snoozes = snoozes ?? new Dictionary<string, DateTime>(StringComparer.Ordinal);
            _snoozesChanged = snoozesChanged;
            EveryCard = DeckAllRequested();
        }

        /// <summary>
        /// Review mode: the deck holds every card any provider returns (asked as Free and as Prime
        /// too, so tips, showcases and the Support card all come), snoozed ones included, with no
        /// one-per-cycle cut, except that the tips still share one slot that turns each cycle.
        /// Only a DEBUG build turns it on, from <c>CCP_BOARD_DECK_ALL=1</c>.
        /// </summary>
        public bool EveryCard { get; set; }

        /// <summary>DEBUG only: <c>CCP_BOARD_DECK_ALL=1</c>. Always false in Release.</summary>
        public static bool DeckAllRequested()
        {
#if DEBUG
            try { return Environment.GetEnvironmentVariable("CCP_BOARD_DECK_ALL") == "1"; }
            catch { return false; }
#else
            return false;
#endif
        }

        /// <summary>The cards of this cycle, in order.</summary>
        public IReadOnlyList<DeckCard> Cards => _cards;

        /// <summary>The card on screen.</summary>
        public int Index { get; private set; }

        /// <summary>Cycles completed since the deck started. Turns the tip and the house filler.</summary>
        public int Cycle { get; private set; }

        public DeckCard? Current => Index >= 0 && Index < _cards.Count ? _cards[Index] : null;

        /// <summary>A provider's state moved: rebuild at the next card change.</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>Whether a rebuild is waiting for the next card change.</summary>
        public bool IsDirty => _dirty;

        /// <summary>Builds the first cycle and lands on its first card.</summary>
        public DeckCard? Start()
        {
            Cycle = 0;
            Rebuild(newCycle: true);
            return Land(0);
        }

        /// <summary>The next card; past the last one a new cycle starts.</summary>
        public DeckCard? Next()
        {
            if (_cards.Count == 0) return Start();

            int next = Index + 1;
            if (_dirty && next < _cards.Count)
            {
                string? currentId = Current?.Spec.Id;
                int was = Index;
                Rebuild();
                int fresh = FirstFresh();
                if (fresh >= 0) return Land(fresh);
                int at = currentId == null ? -1 : _cards.FindIndex(c => c.Spec.Id == currentId);
                // The card on screen left the deck: its old place now holds the card after it.
                next = at >= 0 ? at + 1 : was;
            }

            if (next >= _cards.Count) return NewCycle();
            return Land(next);
        }

        /// <summary>A chip pressed: that card, rebuilding first if a provider moved.</summary>
        public DeckCard? Select(int index)
        {
            if (index < 0 || index >= _cards.Count) return Current;
            if (_dirty)
            {
                var id = _cards[index].Spec.Id;
                Rebuild();
                int at = _cards.FindIndex(c => c.Spec.Id == id);
                return Land(at >= 0 ? at : Math.Min(index, Math.Max(0, _cards.Count - 1)));
            }
            return Land(index);
        }

        /// <summary>Snoozes the card on screen for a week and lands on the card that takes its place.
        /// Returns null when the card cannot be snoozed (the caller keeps it on screen).</summary>
        public DeckCard? SnoozeCurrent(DateTime nowUtc)
        {
            var card = Current;
            if (card == null || !DashboardBillboard.CanSnooze(card.Spec)) return null;
            _snoozes[card.Spec.Id] = DashboardBillboard.SnoozeUntil(nowUtc);
            DashboardBillboard.PruneSnoozes(_snoozes, nowUtc);
            try { _snoozesChanged?.Invoke(); } catch { }

            int at = Index;
            Rebuild();
            if (_cards.Count == 0) { Index = 0; return null; }
            return Land(Math.Min(at, _cards.Count - 1));
        }

        private DeckCard? NewCycle()
        {
            Cycle++;
            Rebuild(newCycle: true);
            return Land(0);
        }

        private static bool IsSlotKind(BillboardCardSpec c) => c.Kind is BillboardCardKind.Showcase or BillboardCardKind.Tip;

        private int FirstFresh() => _cards.FindIndex(c => DashboardBillboard.IsFreshNew(c.Spec, _newShown));

        private DeckCard? Land(int index)
        {
            if (_cards.Count == 0) { Index = 0; return null; }
            Index = Math.Clamp(index, 0, _cards.Count - 1);
            var card = _cards[Index];
            bool showNew = card.Spec.Badge == BillboardBadge.New && _newShown.Add(card.Spec.Id);
            // Later visits in this run draw it without the tag.
            _cards[Index] = card with { ShowNew = false };
            return card with { ShowNew = showNew };
        }

        /// <summary>
        /// Asks every provider again. The showcase-or-tip slot is picked when a CYCLE starts and
        /// kept for the whole cycle: the showcase provider moves to its next clip as soon as a clip
        /// starts playing (without raising Changed), so asking it again mid-cycle would hand over
        /// the next clip under a new id. A slot card snoozed mid-cycle just leaves the slot empty.
        /// </summary>
        private void Rebuild(bool newCycle = false)
        {
            _dirty = false;
            BillboardContext ctx;
            try { ctx = _context(); }
            catch { ctx = new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now); }

            var byId = new Dictionary<string, IBillboardProvider>(StringComparer.Ordinal);
            var specs = new List<BillboardCardSpec>();
            List<IBillboardProvider> providers;
            try { providers = _providers()?.Where(p => p != null).ToList() ?? new List<IBillboardProvider>(); }
            catch { providers = new List<IBillboardProvider>(); }

            // Review mode asks as the viewer, then as Free and Prime, so tier-bound cards come too.
            var asks = EveryCard
                ? new[] { ctx, ctx with { Tier = BillboardTier.Free }, ctx with { Tier = BillboardTier.Prime } }
                : new[] { ctx };
            foreach (var ask in asks)
            foreach (var p in providers)
            {
                List<BillboardCardSpec> mine;
                // A provider that throws loses its own cards, never the deck.
                try { mine = p.Current(ask)?.Where(c => c != null).ToList() ?? new List<BillboardCardSpec>(); }
                catch { continue; }
                foreach (var c in mine)
                {
                    if (string.IsNullOrWhiteSpace(c.Id) || byId.ContainsKey(c.Id)) continue;
                    byId[c.Id] = p;
                    specs.Add(c);
                }
            }

            if (!newCycle && !EveryCard)
            {
                specs.RemoveAll(IsSlotKind);
                if (_cycleSlot != null)
                {
                    specs.Add(_cycleSlot.Spec);
                    if (_cycleSlot.Provider != null) byId[_cycleSlot.Spec.Id] = _cycleSlot.Provider;
                }
            }

            var deck = DashboardBillboard.Build(specs, ctx.Tier, new Dictionary<string, DateTime>(_snoozes), ctx.NowUtc, Cycle, _newShown, EveryCard);
            _cards = deck.Select(s => new DeckCard(s, byId.TryGetValue(s.Id, out var p) ? p : null, false)).ToList();
            if (newCycle) _cycleSlot = _cards.FirstOrDefault(c => IsSlotKind(c.Spec));
        }
    }
}
