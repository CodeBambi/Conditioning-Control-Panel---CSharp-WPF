using System;
using System.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE TEASE CARD - the dashboard mosaic's one nameless tile.
    ///
    /// <para>It is always on the wall, for every account, and it names nothing. Its art is blurred
    /// past recognition, its title is "???", it wears the teased feature's TIER LIVERY as a
    /// persistent rim (gold = Tier 1, diamond = Tier 2), and clicking it opens a cryptic teaser
    /// (<see cref="TeaseRevealPopup"/>) rather than a door. It is an advertisement for a thing
    /// that does not exist yet, and the whole design brief is that it must not leak WHICH thing.</para>
    ///
    /// <para><b>Swapping the teased feature is the block below and nothing else.</b> The card is
    /// generic (<c>FeatureCard.TeaseTier</c>), the popup is generic (it takes a tier), and the copy
    /// is in the language files under <c>tease_*</c> - so a different feature, or a different tier,
    /// or "nothing is being teased right now" are all one-line edits here.</para>
    ///
    /// <para>Deliberately NOT gated on <c>JustDropService.DoorAvailable</c> for visibility (owner,
    /// 2026-08-13): the tease is the point, so it shows to everyone. The service, its per-launch
    /// server check and the nav rail door it drives are untouched - that flag still owns whether
    /// the real door exists, and this file only reads it to know when to take the costume OFF.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ==================== THE TEASE SWITCH - EDIT HERE ONLY ====================

        /// <summary>Which livery the tease wears: 1 = gold (a Tier 1 feature), 2 = diamond
        /// (Tier 2). Just Drop is a Tier 2 perk, so it ships diamond.</summary>
        private const int TeaseCardTier = 2;

        /// <summary>The tile's own label while teased. A literal, not a loc key: nine language
        /// files do not need nine rows to say "???", and every language already does.</summary>
        private const string TeaseCardTitle = "???";

        /// <summary>The badge worn instead of the old "SOON" price tag - a small livery diamond,
        /// painted in the tease's metal by <c>FeatureCard.ApplyTeaseState</c>. U+25C6 is a plain
        /// text glyph with no emoji presentation, so it renders in the 9px pill; a colour emoji
        /// would need the EmojiToImageSource bitmap workaround and would not fit.</summary>
        private const string TeaseCardBadge = "◆";

        /// <summary>Has the teased feature actually landed? While this is false the card wears the
        /// costume; the moment it is true the card becomes an ordinary named tile pointing at the
        /// real door, so the tease can never outlive the reveal.</summary>
        private static bool TeaseCardRevealed => Services.JustDrop.JustDropService.DoorAvailable;

        /// <summary>Where a revealed card navigates, and what it calls itself once revealed. The
        /// title stays an English literal exactly as it was before the tease - see the note in
        /// SettingsTabView.xaml about placeholder names and translation cost.</summary>
        private const string TeaseCardRevealTab = "justdrop";
        private const string TeaseCardRevealedTitle = "Just Drop";

        // ==========================================================================

        /// <summary>The tile itself. Null while the dashboard view has not been built.</summary>
        private Features.FeatureCard? TeaseCard => SettingsTab?.CardJustDrop;

        /// <summary>
        /// Paints the tease (or takes it off). Called from <c>RefreshMosaicTierBadges</c>, which
        /// already carries every trigger this needs, and again from
        /// <c>ApplyJustDropDoorVisibility</c> so a mid-session reveal lands on the tile at the same
        /// moment it lands on the rail - the tile and the rail can never disagree about whether
        /// the feature exists, which was the original contract here and still is.
        /// </summary>
        internal void ApplyTeaseCard()
        {
            var card = TeaseCard;
            if (card == null) return;

            try
            {
                var revealed = TeaseCardRevealed;

                card.TeaseTier = revealed ? 0 : TeaseCardTier;
                card.Title = revealed ? TeaseCardRevealedTitle : TeaseCardTitle;
                // Hover copy is the only other place the tile speaks, so it is teased too. Null
                // once revealed, which restores the no-tooltip default the tile shipped with.
                card.ToolTip = revealed ? null : Loc.Get("tease_card_tooltip");

                // Same helper every other price tag on the wall goes through: "allowed" clears the
                // badge, refused wears it. A revealed feature is a plain tile with no tag.
                SetTierBadge(card, revealed, TeaseCardBadge);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyTeaseCard: {E}", ex.Message); }
        }

        /// <summary>
        /// The tile's click. While teased it opens the teaser and goes nowhere - a tile that
        /// silently does nothing when clicked reads as a bug, and this app has shipped that bug
        /// before. Once the feature is revealed it navigates to the one existing entry for it,
        /// never launches anything (the wall's rule, PlayTabView.xaml:1165).
        /// </summary>
        internal void TeaseCardClicked()
        {
            try
            {
                if (TeaseCardRevealed)
                {
                    ShowTab(TeaseCardRevealTab);
                    return;
                }

                TeaseRevealPopup.ShowFor(TeaseCardTier, this);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "TeaseCardClicked failed"); }
        }
    }
}
