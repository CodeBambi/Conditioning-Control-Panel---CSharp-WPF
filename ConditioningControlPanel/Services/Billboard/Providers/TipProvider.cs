using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
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

    /// <summary>TIP, the adapter: nothing to read but the tier.</summary>
    public sealed class TipProvider : BillboardProviderBase
    {
        public override string Id => "tip";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) =>
            Safe(() => TipCards.Decide(context.Tier, context.NowUtc, Loc));
    }
}
