// PORTED from ConditioningControlPanel/MainWindow/MainWindow.TeaseCard.cs (111 lines).
//
// THE TEASE CARD - the dashboard mosaic's one nameless tile. It is always on the wall, it names
// nothing, its art is blurred past recognition, its title is "???", it wears the teased feature's
// tier livery as a rim, and clicking it opens a cryptic teaser rather than a door. Both members are
// real here: Views/Features/FeatureCard.axaml.cs carries the same TeaseTier / Title / TierBadge
// surface, and Views/Windows/TeaseRevealPopup.axaml.cs is ported with the same ShowFor(tier, owner).
//
// THE ONE DELIBERATE DEVIATION: TeaseCardRevealed is a constant `false` on this head, and that is
// the TRUTH here rather than a stub. WPF reads Services/JustDrop/JustDropService.DoorAvailable, a
// per-launch server GET that says whether the real Just Drop door exists for this account. Neither
// the service nor the shop window is on this head - "justdrop" is in ShowTab's WindowKeys and is a
// documented no-op (MainShellWindow.TabNavigation.cs) - so there is no door for a revealed tile to
// point at. Revealing the tile would make it navigate nowhere, which is precisely the silent-click
// bug the WPF file's own comment says this app has shipped before. When JustDropService lands,
// this is one line: `=> JustDropService.DoorAvailable`.
//
// SetTierBadge (MainShellWindow.Presets.cs, still a stub) is not needed for the teased state: it
// exists to CLEAR the pill when a feature is allowed, and a tease is never allowed, so the write is
// unconditional here. When the reveal path arrives it should go back through that helper.
//
// NO CALLER YET. WPF paints from RefreshMosaicTierBadges (MainWindow.Presets.cs) and again from
// ApplyJustDropDoorVisibility (MainShellWindow.JustDrop.cs, still head-side); the click arrives
// from Views/Tabs/SettingsTabView.axaml.cs:296's CardJustDrop_Click, which is an empty forwarding
// stub. Each is one line, in a file this layer does not own.

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ==================== THE TEASE SWITCH - EDIT HERE ONLY ====================

        /// <summary>Which livery the tease wears: 1 = gold (a Tier 1 feature), 2 = diamond
        /// (Tier 2). Just Drop is a Tier 2 perk, so it ships diamond.</summary>
        private const int TeaseCardTier = 2;

        /// <summary>The tile's own label while teased. A literal, not a loc key: nine language
        /// files do not need nine rows to say "???", and every language already does.</summary>
        private const string TeaseCardTitle = "???";

        /// <summary>The badge worn instead of a price tag - a small livery diamond, painted in the
        /// tease's metal by FeatureCard's tease state. U+25C6 is a plain text glyph with no emoji
        /// presentation, so it renders inside the 9px pill.</summary>
        private const string TeaseCardBadge = "◆";

        /// <summary>Has the teased feature actually landed? See the header: on this head the
        /// answer is a constant no, because the door it would point at does not exist.</summary>
        private static bool TeaseCardRevealed => false;

        // ==========================================================================

        /// <summary>The tile itself. Null while the dashboard view has not been built.</summary>
        private FeatureCard? TeaseCard =>
            Named<Control>("SettingsTab")?.FindControl<FeatureCard>("CardJustDrop");

        /// <summary>
        /// Paints the tease (or takes it off). Idempotent, and computed from scratch every time, so
        /// a repaint from any trigger lands on a complete state.
        /// </summary>
        internal void ApplyTeaseCard()
        {
            var card = TeaseCard;
            if (card == null) return;

            try
            {
                var revealed = TeaseCardRevealed;

                card.TeaseTier = revealed ? 0 : TeaseCardTier;
                card.Title = revealed ? "Just Drop" : TeaseCardTitle;
                // Hover copy is the only other place the tile speaks, so it is teased too. Null
                // once revealed, which restores the no-tooltip default the tile shipped with.
                ToolTip.SetTip(card, revealed ? null : Loc.Get("tease_card_tooltip"));
                card.TierBadge = revealed ? null : TeaseCardBadge;
            }
            catch (Exception ex) { Log.Debug("ApplyTeaseCard: {E}", ex.Message); }
        }

        /// <summary>
        /// The tile's click. While teased it opens the teaser and goes nowhere - a tile that
        /// silently does nothing when clicked reads as a bug. Once the feature is revealed it
        /// navigates to the one existing entry for it, and never launches anything.
        /// </summary>
        internal void TeaseCardClicked()
        {
            try
            {
                if (TeaseCardRevealed) { ShowTab("justdrop"); return; }
                TeaseRevealPopup.ShowFor(TeaseCardTier, this);
            }
            catch (Exception ex) { Log.Warning(ex, "TeaseCardClicked failed"); }
        }
    }
}
