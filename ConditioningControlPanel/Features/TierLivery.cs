using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// The tier livery lookup, in one place: gold = Tier 1, diamond = Tier 2.
    ///
    /// <para>The brushes themselves live in <c>Resources\Theme\Brushes.xaml</c> under the
    /// TIER LIVERY block and are deliberately CONSTANT across mods - a tier mark is commerce,
    /// not decor, so it never takes colour from the mod chain. This helper only resolves them
    /// and carries the same flat literal fallbacks the rack uses, because a missing brush must
    /// degrade to a duller rim and never to a throw inside card construction.</para>
    ///
    /// <para><b>Tier numbers here are the livery's tiers, not an entitlement check.</b> Nothing
    /// in this file gates anything; <see cref="Services.TierGate"/> is still the only thing that
    /// refuses. (<c>StudioTabView.TierLiveryBrush</c> predates this helper and keeps its own
    /// identical private copy for the rack rows - same keys, same fallbacks.)</para>
    /// </summary>
    internal static class TierLivery
    {
        /// <summary>Gold #F0C24B - the Tier 1 base tone, for accents that need a colour rather
        /// than a gradient brush (glows, shadows, seams).</summary>
        private static readonly Color GoldAccent = Color.FromRgb(0xF0, 0xC2, 0x4B);

        /// <summary>Diamond #8FD4EF - the Tier 2 base tone. Ice-white cyan; the violet flask
        /// stays on the lockband glyphs only.</summary>
        private static readonly Color DiamondAccent = Color.FromRgb(0x8F, 0xD4, 0xEF);

        /// <summary>The persistent border gradient for <paramref name="tier"/>. Tier 2 and up
        /// wear diamond; everything else wears gold.</summary>
        internal static Brush BorderBrush(int tier)
        {
            var key = tier >= 2 ? "Tier2DiamondBorderBrush" : "Tier1GoldBorderBrush";
            if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
            var fallback = new SolidColorBrush(AccentColor(tier));
            fallback.Freeze();
            return fallback;
        }

        /// <summary>The hover/lit variant of <see cref="BorderBrush"/>.</summary>
        internal static Brush HoverBrush(int tier)
        {
            var key = tier >= 2 ? "Tier2DiamondHoverBrush" : "Tier1GoldHoverBrush";
            if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
            return BorderBrush(tier);
        }

        /// <summary>The single flat tone behind the livery, for drop shadows and washes that
        /// need a <see cref="Color"/> rather than a gradient.</summary>
        internal static Color AccentColor(int tier) => tier >= 2 ? DiamondAccent : GoldAccent;
    }
}
