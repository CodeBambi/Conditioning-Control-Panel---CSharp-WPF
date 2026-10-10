using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Billboard.Showcase
{
    /// <summary>
    /// The showcase card's decisions, pure: who sees which clips, the per-install order, and
    /// which clip is up next. The provider holds the state; these functions only read it.
    /// </summary>
    public static class ShowcaseRules
    {
        /// <summary>The Premium page, where every showcase button leads.</summary>
        public const string PremiumTab = "premium";

        /// <summary>Art key the showcase card wears; <see cref="ShowcaseArtRegistration"/> owns it.</summary>
        public const string ArtKey = "clip";

        public const string BasicHue = "#ffc94a";
        public const string PrimeHue = "#5fe3ff";

        /// <summary>
        /// A viewer only ever sees features above their plan: Free sees Basic and Prime, Basic sees
        /// Prime, Prime sees none (Prime gets a tip card in that slot from another provider).
        /// </summary>
        public static bool Shows(BillboardTier viewer, ShowcaseTier feature) => viewer switch
        {
            BillboardTier.Free => true,
            BillboardTier.Basic => feature == ShowcaseTier.Prime,
            _ => false,
        };

        /// <summary>
        /// The clip ids in this install's order: sorted first (so the manifest's own order never
        /// matters), then shuffled with a small LCG off the install seed. Same seed, same order,
        /// on every launch.
        /// </summary>
        public static IReadOnlyList<string> InstallOrder(IEnumerable<string> ids, int seed)
        {
            var a = ids.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            long r = seed & 0x7fffffff;
            if (r == 0) r = 1;
            for (int i = a.Length - 1; i > 0; i--)
            {
                r = (r * 1103515245L + 12345L) & 0x7fffffffL;
                int j = (int)(r % (i + 1));
                (a[i], a[j]) = (a[j], a[i]);
            }
            return a;
        }

        /// <summary>
        /// The clip whose turn it is: the first eligible id after <paramref name="lastShownId"/> in
        /// the install order, wrapping. With nothing shown yet (or the last one gone from the
        /// manifest) it is the first eligible id. Null when nothing is eligible.
        /// </summary>
        public static string? NextClip(IReadOnlyList<string> order, ISet<string> eligible, string? lastShownId)
        {
            if (order.Count == 0 || eligible.Count == 0) return null;
            int start = 0;
            if (lastShownId != null)
            {
                int at = -1;
                for (int i = 0; i < order.Count; i++)
                    if (string.Equals(order[i], lastShownId, StringComparison.Ordinal)) { at = i; break; }
                if (at >= 0) start = at + 1;
            }
            for (int k = 0; k < order.Count; k++)
            {
                var id = order[(start + k) % order.Count];
                if (eligible.Contains(id)) return id;
            }
            return null;
        }

        /// <summary>The ids the viewer may be shown, out of a manifest.</summary>
        public static HashSet<string> EligibleIds(IEnumerable<ShowcaseClip> clips, BillboardTier viewer) =>
            new(clips.Where(c => Shows(viewer, c.Tier)).Select(c => c.Id), StringComparer.Ordinal);

        public static string TitleKey(string clipId) => $"showcase_clip_{clipId}_title";

        public static string LineKey(string clipId) => $"showcase_clip_{clipId}_line";

        /// <summary>
        /// The card for a clip, or null when the app has no copy for its id (a clip the manifest
        /// added before the app knew about it is skipped, never shown with a raw key).
        /// <paramref name="text"/> answers null for a key it does not have.
        /// </summary>
        public static BillboardCardSpec? BuildCard(ShowcaseClip clip, object? artData, Func<string, string?> text)
        {
            var title = text(TitleKey(clip.Id));
            var line = text(LineKey(clip.Id));
            if (string.IsNullOrWhiteSpace(title) || line == null) return null;

            bool prime = clip.Tier == ShowcaseTier.Prime;
            var eyebrow = text(prime ? "showcase_eyebrow_prime" : "showcase_eyebrow_basic") ?? string.Empty;
            var button = text("showcase_button") ?? string.Empty;

            return new BillboardCardSpec(
                Id: "showcase:" + clip.Id,
                Kind: BillboardCardKind.Showcase,
                Priority: 0,
                Eyebrow: eyebrow,
                Title: title!,
                Line: line,
                AccentHex: prime ? PrimeHue : BasicHue,
                ArtKey: ArtKey,
                ArtData: artData,
                Action: new BillboardAction(BillboardActionKind.Tab, PremiumTab, button),
                Badge: prime ? BillboardBadge.Prime : BillboardBadge.Basic,
                Snoozable: true);
        }
    }
}
