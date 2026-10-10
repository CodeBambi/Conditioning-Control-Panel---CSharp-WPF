using System;
using System.Collections.Generic;

// Moved from the WPF head (BillboardWiring.cs, Providers/BillboardProviders.cs, Providers/TipProvider.cs)
// unchanged, so both heads build the house and tip cards from one table.

namespace ConditioningControlPanel.Services.Billboard
{
    /// <summary>The art keys the deck lane registers itself. Providers pick one of these (or a
    /// key another lane registers: "board", "clip").</summary>
    public static class BuiltInArtKeys
    {
        /// <summary>A 16:9 poster. ArtData = a resource path under Resources/, e.g. "billboard/loom.png".</summary>
        public const string Poster = "poster";

        /// <summary>Open tables sliding in. ArtData = optional {count} (IReadOnlyDictionary of string to int, 1..3 rows drawn) or names.</summary>
        public const string Tables = "tables";

        /// <summary>A prize wheel turning. ArtData = optional {done, total}: a pip per slot under it.</summary>
        public const string Wheel = "wheel";

        /// <summary>A four-arm spiral in the card hue. ArtData = optional second hue "#rrggbb", or null.</summary>
        public const string Spiral = "spiral";

        /// <summary>A month of days. ArtData = {counted, need, days, today} (Locktober), {day, days} (a program), or null.</summary>
        public const string Calendar = "calendar";

        /// <summary>Four tiles, one switching on and off under a click ring. ArtData = the tip id (unused).</summary>
        public const string Tip = "tip";

        /// <summary>A paper invite pass: a code row with one glyph flipping up, seven day pips on the stub. ArtData unused.</summary>
        public const string Invite = "invite";

        /// <summary>Today's quests as slips on a clipboard, the done ones checked. ArtData = {done, total}.</summary>
        public const string Quests = "quests";

        public static readonly IReadOnlyList<string> All = new[] { Poster, Tables, Wheel, Spiral, Calendar, Tip, Invite, Quests };
    }
}

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>The card hues, one per section (the mockup's).</summary>
    public static class CardHues
    {
        public const string Live = "#5fe3ff";
        public const string Waiting = "#ffc94a";
        public const string Resume = "#ff4fa8";
        public const string Event = "#ff4fa8";
        public const string Tip = "#9b7bff";
    }

    /// <summary>The art keys a provider may name (registered by the deck lane).</summary>
    public static class CardArt
    {
        public const string Tables = "tables";
        public const string Wheel = "wheel";
        public const string Spiral = "spiral";
        public const string Calendar = "calendar";
        public const string Tip = "tip";
        public const string Poster = "poster";
        public const string Invite = "invite";
        public const string Quests = "quests";

        public static readonly IReadOnlyList<string> All = new[] { Tables, Wheel, Spiral, Calendar, Tip, Poster, Invite, Quests };
    }

    /// <summary>One "did you know": loc keys <c>billboard_card_tip_{Id}_title</c> / <c>_line</c>, and the page it shows.</summary>
    public sealed record BillboardTip(string Id, string Tab);

    /// <summary>
    /// TIP, the pure half. Prime players never see a showcase (they own everything it would sell);
    /// they get one of these in that slot instead. Every tip is about a real feature and opens
    /// its page. The provider hands the whole table back in table order (priority = place in the
    /// table); the DECK turns it, one tip per cycle, the next one every time it comes round
    /// (<see cref="DashboardBillboard.PickShowcaseOrTip"/>). Owner, 2026-10-07: no wall clock.
    /// </summary>
    public static class TipCards
    {
        public const string IdPrefix = "tip.";

        public static readonly IReadOnlyList<BillboardTip> Table = new[]
        {
            new BillboardTip("quests", "quests"),
            new BillboardTip("awareness", "awareness"),
            new BillboardTip("programs", "programs"),
            new BillboardTip("deeper", "deeper"),
            new BillboardTip("blink", "blinktrainer"),
            new BillboardTip("haptics", "haptics"),
            new BillboardTip("folders", "assets"),
            new BillboardTip("lockdown", "lockdown"),
            new BillboardTip("remote", "remotecontrol"),
        };

        /// <summary>The whole table, in table order. Empty unless the viewer is Prime.</summary>
        public static IReadOnlyList<BillboardCardSpec> Decide(BillboardTier tier, DateTime nowUtc, Func<string, string> loc,
            IReadOnlyList<BillboardTip>? table = null)
        {
            table ??= Table;
            if (tier != BillboardTier.Prime || table.Count == 0) return Array.Empty<BillboardCardSpec>();
            var cards = new List<BillboardCardSpec>(table.Count);
            for (int i = 0; i < table.Count; i++)
            {
                var tip = table[i];
                cards.Add(new BillboardCardSpec(IdPrefix + tip.Id, BillboardCardKind.Tip, i,
                    loc("billboard_card_tip_eyebrow"),
                    loc("billboard_card_tip_" + tip.Id + "_title"),
                    loc("billboard_card_tip_" + tip.Id + "_line"),
                    CardHues.Tip, CardArt.Tip, tip.Id,
                    new BillboardAction(BillboardActionKind.Tab, tip.Tab, loc("billboard_card_tip_button"))));
            }
            return cards;
        }
    }
}
