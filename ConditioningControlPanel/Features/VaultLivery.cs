using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// The Velvet Vault's edge vocabulary, and the one place that decides what a vault surface's
    /// rim and tier sign look like.
    ///
    /// <para><b>Why this is not a method on MainWindow.</b> The shelf is built by
    /// <c>MainWindow.BuildExclusiveCard</c>, which needs a live window - so nothing a render test
    /// can reach. Pulling the chrome decision out here leaves the builder doing assembly and this
    /// doing judgement, and lets ExclusivesRenderTests realize a bare Border plus a
    /// <see cref="TierBadge"/> and check the SAME code the shelf runs, rather than a lookalike.</para>
    ///
    /// <para>Everything here is presentation. <c>ExclusiveFeature.GateState</c> and, at the
    /// destination, TierGate are the only things that refuse; a rim has never stopped anybody.</para>
    /// </summary>
    internal static class VaultLivery
    {
        /// <summary>Rim weight for a tiered shelf card, and for the spotlight band (hero weight).
        /// Both are a step up from the 1px the shelf shipped with - the whole point of the
        /// 2026-08-13 pass is that thin lines do not read as metal.</summary>
        internal const double CardRim = 3.0;
        internal const double SpotlightRim = 4.0;

        /// <summary>
        /// Where the entitlement chip sits once a badge owns the top-right corner. Derived from the
        /// shipped art: a 336px card takes a 151px badge (TierBadge's 45% rule), the taller of the
        /// two cuts (900x440 gold) is then ~74px, and the badge is TILTED - the lean plus its
        /// wobble pushes the low corner about 10px further down than the upright box, which is the
        /// part that is easy to forget and impossible to see in a diff.
        /// ExclusivesRenderTests measures a real badge, rotates the result, and checks it against
        /// this number - so a re-cut of the art fails the build instead of quietly landing on the
        /// chip.
        /// </summary>
        internal const double ChipTopWhenTiered = 84;

        /// <summary>The badge's own offset on a shelf card: tucked up and out over the corner so it
        /// reads as pinned ON the card. Shared with the chip clearance check above.</summary>
        internal const double CardBadgeTopMargin = -6;

        /// <summary>A live card's resting edge (violet, 1px) - what an untiered card wears.</summary>
        internal static readonly SolidColorBrush EdgeDefault = Frozen(Color.FromArgb(0x4D, 0xB4, 0x78, 0xFF));

        /// <summary>An UNTIERED free-today card's edge: gold, and thicker. A tiered card never
        /// reaches this - it keeps its livery and says the free day with the re-stamp instead.</summary>
        internal static readonly SolidColorBrush EdgeFree = Frozen(Color.FromArgb(0xE6, 0xFF, 0xD2, 0x7A));

        /// <summary>The spotlight band's resting edge (pink), as authored in the view.</summary>
        internal static readonly SolidColorBrush SpotlightEdgeDefault = Frozen(Color.FromArgb(0x66, 0xFF, 0x69, 0xB4));

        /// <summary>
        /// Dresses one vault surface: the rim it wears, and the tier sign stamped on its art.
        ///
        /// <para><b>A tiered card keeps its livery on a free day.</b> The gold FREE TODAY edge and
        /// the gold pill are for surfaces with no tier sign; where there IS one, the free day is
        /// said by the re-stamp (the sign dims and the pink stamp lands over it), because a livery
        /// that changed colour for a day would stop being a livery.</para>
        /// </summary>
        /// <param name="card">The surface's own Border - it keeps painting the resting rim.</param>
        /// <param name="badge">Its tier sign, or null on an untiered surface.</param>
        /// <param name="tier">The livery tier. NOT an entitlement check.</param>
        /// <param name="freeToday">True when this feature is today's daily free pick.</param>
        /// <param name="rim">Rim weight; hero surfaces pass <see cref="SpotlightRim"/>.</param>
        /// <param name="untieredEdge">Resting edge for an untiered surface.</param>
        /// <returns>True when the caller should fall back to the gold FREE TODAY pill, i.e. this
        /// surface has no sign to re-stamp.</returns>
        internal static bool Apply(Border card, TierBadge? badge, int tier, bool freeToday,
                                   double rim = CardRim, Brush? untieredEdge = null)
        {
            if (tier > 0)
            {
                card.BorderBrush = TierLivery.BorderBrush(tier);
                card.BorderThickness = new Thickness(rim);
                // RimThickness must match the stroke or the traveling band misses the metal.
                TierFxBorder.SetRimThickness(card, rim);
                TierFxBorder.SetTier(card, tier);
            }
            else
            {
                TierFxBorder.SetTier(card, 0);
                card.BorderBrush = freeToday ? EdgeFree : (untieredEdge ?? EdgeDefault);
                card.BorderThickness = new Thickness(freeToday ? 2 : 1);
            }

            if (badge != null)
            {
                badge.Tier = tier;
                badge.FreeToday = tier > 0 && freeToday;
            }

            return freeToday && (tier <= 0 || badge == null);
        }

        private static SolidColorBrush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }
}
