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
    /// its page. The provider hands the whole table back, rotated, so the deck can take the first
    /// one the player has not snoozed; the rotation moves on every <see cref="Rotation"/>.
    /// </summary>
    public static class TipCards
    {
        public const string IdPrefix = "tip.";

        public static readonly TimeSpan Rotation = TimeSpan.FromMinutes(2);

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

        /// <summary>The table, starting at the tip whose turn it is. Empty unless the viewer is Prime.</summary>
        public static IReadOnlyList<BillboardCardSpec> Decide(BillboardTier tier, DateTime nowUtc, Func<string, string> loc,
            IReadOnlyList<BillboardTip>? table = null)
        {
            table ??= Table;
            if (tier != BillboardTier.Prime || table.Count == 0) return Array.Empty<BillboardCardSpec>();
            int start = StartIndex(nowUtc, table.Count);
            var cards = new List<BillboardCardSpec>(table.Count);
            for (int i = 0; i < table.Count; i++)
            {
                var tip = table[(start + i) % table.Count];
                cards.Add(new BillboardCardSpec(IdPrefix + tip.Id, BillboardCardKind.Tip, i,
                    loc("billboard_card_tip_eyebrow"),
                    loc("billboard_card_tip_" + tip.Id + "_title"),
                    loc("billboard_card_tip_" + tip.Id + "_line"),
                    CardHues.Tip, CardArt.Tip, tip.Id,
                    new BillboardAction(BillboardActionKind.Tab, tip.Tab, loc("billboard_card_tip_button"))));
            }
            return cards;
        }

        /// <summary>Which tip leads now: one step per <see cref="Rotation"/> of wall-clock time.</summary>
        public static int StartIndex(DateTime nowUtc, int count)
        {
            if (count <= 0) return 0;
            long step = nowUtc.Ticks / Rotation.Ticks;
            return (int)(step % count);
        }
    }

    /// <summary>TIP, the adapter: nothing to read but the tier and the clock.</summary>
    public sealed class TipProvider : BillboardProviderBase
    {
        public override string Id => "tip";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) =>
            Safe(() => TipCards.Decide(context.Tier, context.NowUtc, Loc));
    }
}
