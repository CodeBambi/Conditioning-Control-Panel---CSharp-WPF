using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-5 of the FX overhaul: the <b>event moments</b> (FX plan section 4, phase 4). Six things
    /// in the app are worth a one-shot particle burst - a level-up, an achievement unlock, a quest
    /// complete, a program day complete, an enhancement purchase, and a prestige rank-up - and
    /// nothing else is. They cost nothing when they are not happening, which is the whole appeal:
    /// high perceived polish, zero idle clock.
    ///
    /// <para><b>The shared burst layer.</b> One <see cref="AmbientFxCanvas"/> lives in
    /// <c>EventFxHost</c>, a child of RootGrid <i>outside</i> the 1489x901 Viewbox. Inside the
    /// Viewbox everything is bitmap-scaled, which is right for a fog wash and wrong for crisp
    /// sparks. The canvas is:
    /// <list type="bullet">
    ///   <item>created lazily, on the first event moment a user actually reaches;</item>
    ///   <item>a fixed <see cref="BurstBoxPx"/> square, not a full-window surface - a burst
    ///   travels ~40% of its box, so a box centred on the anchor holds the whole thing while
    ///   keeping the per-frame Skia raster to about a megapixel instead of the whole window;</item>
    ///   <item>moved by Margin only. Its size never changes after creation, so no burst after the
    ///   first one needs a layout pass to know its own dimensions.</item>
    /// </list>
    /// Between bursts it holds no clock at all: <c>AmbientFxCanvas.ShouldRun</c> only returns true
    /// while sparks are alive, because this canvas runs no ambient layers.</para>
    ///
    /// <para><b>Anchoring.</b> Never by layout arithmetic - the anchor is almost always inside the
    /// Viewbox and its layout coordinates are in design-canvas units, not window pixels.
    /// <see cref="FireBurstAt"/> maps the anchor with <c>TransformToVisual(EventFxHost)</c>, which
    /// carries the Viewbox scale, and hands the resulting rectangle to the pure
    /// <see cref="FxBurstAnchor"/> decision (tested headlessly) for the off-screen check and the
    /// origin.</para>
    ///
    /// <para><b>Rails.</b> Every hook is fire-and-forget safe: wrapped, null-tolerant, and it
    /// changes nothing about the feature it decorates. Bursts are skipped outright when particles
    /// are not allowed (MotionLevel Reduced/Off, or a performance tier with no particle budget -
    /// <see cref="MotionFx.AllowParticles"/>) and when nobody is looking (window inactive or
    /// minimised). Colour comes from <see cref="FxTheme"/>; there is not a literal hex in here.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>
        /// Edge of the square burst surface, in window pixels. Sized from the sim: a spark's
        /// fastest opening speed is ~0.73 element-widths/sec and drag kills it inside ~1.2s, so it
        /// covers at most ~40% of the box - a 560px box keeps a burst centred on its anchor whole.
        /// </summary>
        private const double BurstBoxPx = 560;

        private const int LevelUpBurstCount = 110;
        private const int AchievementBurstCount = 95;
        private const int QuestBurstCount = 85;
        private const int ProgramDayBurstCount = 90;
        private const int EnhancementBurstCount = 100;

        /// <summary>Prestige is the rarest moment in the app and gets the top of the range.</summary>
        private const int PrestigeBurstCount = 150;

        private const double PrestigeSheenSeconds = 1.15;
        private const double PrestigeSheenPeak = 0.16;

        /// <summary>Achievement tile reveal: blur out and settle. Interaction clock, not ambient.</summary>
        private const int AchievementRevealMs = 320;
        private const double AchievementRevealBlurRadius = 15;
        private const double AchievementRevealScale = 1.08;

        // ---- state -----------------------------------------------------------------

        private AmbientFxCanvas? _eventBurstLayer;

        /// <summary>Set to false once the layer has failed to build, so we stop retrying per event.</summary>
        private bool _eventBurstLayerFailed;

        /// <summary>
        /// The Prestige row on the Enhancements tab, remembered by RefreshEnhancementsUI so a
        /// rank-up can burst on the number that changed. Rebuilt on every refresh.
        /// </summary>
        private Border? _prestigeRowBorder;

        /// <summary>
        /// Lifetime sparkle points spent, sampled just before a purchase. A prestige rank is
        /// <c>1 + spent/100</c> (MainWindow.Enhancements.cs), so a purchase that pushes the total
        /// across a hundred is the rank-up - there is no separate prestige action to hook.
        /// </summary>
        private static long PrestigeRankNow()
        {
            try { return 1 + ((App.Achievements?.Progress?.LifetimeSkillPointsSpent ?? 0) / 100); }
            catch { return 1; }
        }

        // ============================== the shared layer ==============================

        /// <summary>
        /// Whether an event burst may fire at all right now. Deliberately stricter than the ambient
        /// gate on one point: an event that lands while the app is in the background is simply not
        /// celebrated, rather than celebrated to an empty screen.
        /// </summary>
        private bool EventFxAllowed
        {
            get
            {
                try
                {
                    if (!MotionFx.AllowParticles) return false;
                    if (!IsVisible || WindowState == WindowState.Minimized) return false;
                    return IsActive;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Builds the burst surface on first use. Returns null if the host is not in the tree yet
        /// (very early startup) or if construction throws - either way the caller just skips.
        /// </summary>
        private AmbientFxCanvas? EnsureEventBurstLayer()
        {
            if (_eventBurstLayer != null) return _eventBurstLayer;
            if (_eventBurstLayerFailed) return null;
            try
            {
                var host = EventFxHost;
                if (host == null) { _eventBurstLayerFailed = true; return null; }

                var layer = new AmbientFxCanvas
                {
                    Width = BurstBoxPx,
                    Height = BurstBoxPx,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false,
                };
                host.Children.Add(layer);

                // The one and only forced layout pass this file ever does. Burst() reads
                // ActualWidth, which is 0 until the new child has been arranged; from here on the
                // size is fixed and only the Margin moves, so no later burst pays this again.
                layer.UpdateLayout();

                _eventBurstLayer = layer;
                return layer;
            }
            catch (Exception ex)
            {
                _eventBurstLayerFailed = true;
                App.Logger?.Warning(ex, "EnsureEventBurstLayer failed - event bursts disabled");
                return null;
            }
        }

        /// <summary>
        /// Fires a one-shot burst centred on <paramref name="anchor"/>. The single entry point for
        /// every event moment; safe to call from anywhere, on any state, at any time.
        /// </summary>
        /// <param name="anchor">Any laid-out element in this window's tree.</param>
        /// <param name="spot">Which part of the anchor the sparks come from.</param>
        /// <param name="color">Overrides the mod's particle colour. Null = FxTheme's.</param>
        /// <param name="count">Spark count, clamped by the canvas to the tier's budget.</param>
        /// <returns>True if a burst was actually emitted - only the tests and the callers that
        /// want a fallback anchor care.</returns>
        internal bool FireBurstAt(FrameworkElement? anchor, FxBurstSpot spot = FxBurstSpot.Center,
                                  Color? color = null, int count = 90)
        {
            try
            {
                if (anchor == null || !EventFxAllowed) return false;
                var host = EventFxHost;
                if (host == null) return false;
                if (!anchor.IsVisible || anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0) return false;

                // TransformToVisual is the only correct mapping here: the anchor is almost always
                // inside the Viewbox, so its own coordinates are design-canvas units. This throws
                // when the two are not connected (an anchor on a detached tab) - hence the wrap.
                var bounds = anchor.TransformToVisual(host)
                                   .TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));

                if (!FxBurstAnchor.TryResolve(bounds, new Size(host.ActualWidth, host.ActualHeight),
                                              spot, out var origin))
                    return false;

                var layer = EnsureEventBurstLayer();
                if (layer == null) return false;

                // Move the box so its centre is the origin. Negative margins are legal and are
                // exactly what we want near a window edge: the box simply hangs off and clips.
                layer.Margin = new Thickness(origin.X - BurstBoxPx / 2, origin.Y - BurstBoxPx / 2, 0, 0);
                layer.Burst(BurstBoxPx / 2, BurstBoxPx / 2, color, count);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FireBurstAt: {E}", ex.Message);
                return false;
            }
        }

        /// <summary>Bursts on the first anchor that is actually on screen. Used where an event can
        /// land while its own tab is hidden - the nav button that leads there is the fallback, so
        /// the celebration still points somewhere real.</summary>
        private void FireBurstAtFirstVisible(FxBurstSpot spot, Color? color, int count,
                                             params FrameworkElement?[] anchors)
        {
            foreach (var anchor in anchors)
            {
                if (FireBurstAt(anchor, spot, color, count)) return;
                // Only the primary anchor gets its authored spot; a fallback is a button, and the
                // centre of a button is the only sensible place on it.
                spot = FxBurstSpot.Center;
            }
        }

        // ============================== 1. level up ==============================

        /// <summary>
        /// Level-up: bloom the XP bar's cap and burst at the end of the track.
        ///
        /// Called from OnLevelUp BEFORE UpdateLevelDisplay, on purpose - at that instant the bar
        /// is still standing at the cap that was just reached, so the bloom lands on a full bar
        /// and the reset reads as the consequence rather than the event. The bloom reuses
        /// XPBarFlashOverlay (the pre-existing pink-glow overlay sized to the fill) and the
        /// existing FlashOverlay animation, so the two never fight over the same property.
        /// </summary>
        internal void CelebrateLevelUp()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                FlashOverlay(XPBarFlashOverlay);
                // The track, not the fill: the cap is where the bar ENDS, and after a level-up the
                // fill has wrapped back to a sliver near the left.
                FireBurstAt(XPBarTrack, FxBurstSpot.RightEdge, count: LevelUpBurstCount);
            }
            catch (Exception ex) { App.Logger?.Debug("CelebrateLevelUp: {E}", ex.Message); }
        }

        // ============================== 2. achievement unlock ==============================

        /// <summary>
        /// Achievement unlock: burst on the tile if the grid is on screen, otherwise on the
        /// Achievements nav button (the unlock toast is its own top-level window - AchievementPopup
        /// - and this window's FX layer cannot reach across into it).
        /// When the tile IS on screen it also gets the reveal: the locked blur dissolves off and
        /// the tile settles back from a small overshoot.
        /// </summary>
        internal void CelebrateAchievementUnlock(string? achievementId)
        {
            try
            {
                // The card is looked up directly rather than walked up from the badge Image: on the
                // redesigned cards the Image's parent is a layout Grid inside the ToggleButton's
                // content, not the card itself.
                FrameworkElement? tile = null;
                if (!string.IsNullOrEmpty(achievementId)
                    && _achievementCards.TryGetValue(achievementId!, out var card))
                {
                    tile = card;
                    _achievementImages.TryGetValue(achievementId!, out var image);
                    if (image != null && AchievementsTab?.IsVisible == true) RevealAchievementTile(tile, image);
                }

                // Rail fallback goes through NavAnchorForTab, never the entry button directly: the
                // You door is shut on most launches, and its entries then all map onto the same
                // zero-height strip, so a raw BtnAchievements burst erupts from an unrelated row.
                FireBurstAtFirstVisible(FxBurstSpot.Center, FxTheme.GlowColor, AchievementBurstCount,
                                        AchievementsTab?.IsVisible == true ? tile : null,
                                        NavAnchorForTab("achievements"));
            }
            catch (Exception ex) { App.Logger?.Debug("CelebrateAchievementUnlock: {E}", ex.Message); }
        }

        /// <summary>
        /// The unlock reveal. A real blur-dissolve where the tier can afford it: the tile was
        /// already wearing a BlurEffect while it was locked, so animating one back down to 0 is
        /// the same cost the grid paid a moment ago and it reads as the fog clearing off the art.
        /// Where glow/blur is not allowed (Performance tier) it degrades to the opacity+scale
        /// settle alone, which is the same beat without the raster.
        /// </summary>
        private void RevealAchievementTile(FrameworkElement tile, Image image)
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;

                if (PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier))
                {
                    var blur = new BlurEffect { Radius = AchievementRevealBlurRadius };
                    image.Effect = blur;
                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(AchievementRevealMs))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    };
                    fade.Completed += (_, __) =>
                    {
                        // Drop the effect entirely rather than leaving a 0-radius blur on the
                        // tile: a live Effect still puts the tile on its own render pass.
                        try { if (ReferenceEquals(image.Effect, blur)) image.Effect = null; }
                        catch (Exception ex) { App.Logger?.Debug("RevealAchievementTile completion: {E}", ex.Message); }
                    };
                    blur.BeginAnimation(BlurEffect.RadiusProperty, fade);
                }

                // The settle. Every tile already carries the TransformGroup the holo-foil tilt
                // needs (PrepareAchievementTileFx -> EnsureCardTransforms), so this reuses that
                // group's ScaleTransform rather than clobbering the tile's transform.
                var scale = FindTransform<ScaleTransform>(tile);
                if (scale == null || scale.IsFrozen) return;

                var settle = new DoubleAnimation(AchievementRevealScale, 1.0,
                                                 TimeSpan.FromMilliseconds(AchievementRevealMs))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 },
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
            }
            catch (Exception ex) { App.Logger?.Debug("RevealAchievementTile: {E}", ex.Message); }
        }

        // ============================== 3. quest complete ==============================

        /// <summary>
        /// Quest complete: burst at the cap of the bar that just filled (daily or weekly). When the
        /// Quests tab is not the one on screen the completion is announced by QuestCompletePopup,
        /// its own window - so the fallback is the Quests nav button.
        /// </summary>
        internal void CelebrateQuestComplete(QuestType type)
        {
            try
            {
                bool onTab = QuestsTab?.IsVisible == true;
                FrameworkElement? fill = !onTab ? null
                    : type == QuestType.Weekly ? QuestsTab?.WeeklyProgressFill
                                               : QuestsTab?.DailyProgressFill;
                FrameworkElement? track = !onTab ? null
                    : type == QuestType.Weekly ? QuestsTab?.WeeklyProgressTrack
                                               : QuestsTab?.DailyProgressTrack;

                // The fill is the honest anchor when it has a width; a completed-but-unmeasured
                // bar falls back to its track, which occupies the same strip of screen.
                FireBurstAtFirstVisible(FxBurstSpot.RightEdge, null, QuestBurstCount,
                                        fill?.ActualWidth > 1 ? fill : null, track,
                                        NavAnchorForTab("quests"));
            }
            catch (Exception ex) { App.Logger?.Debug("CelebrateQuestComplete: {E}", ex.Message); }
        }

        // ============================== 4. program day complete ==============================

        /// <summary>
        /// Program day complete: burst on the "day complete" banner of the Today card, alongside
        /// the scale pop it already does. Called from the same once-per-day guarded branch, so a
        /// day that completed while the tab was hidden shows the settled banner and no burst -
        /// which is the right answer for a celebration nobody was there to see.
        /// </summary>
        internal void CelebrateProgramDayComplete(FrameworkElement? banner)
        {
            try { FireBurstAt(banner, FxBurstSpot.Center, count: ProgramDayBurstCount); }
            catch (Exception ex) { App.Logger?.Debug("CelebrateProgramDayComplete: {E}", ex.Message); }
        }

        // ============================== 5/6. enhancement purchase + prestige ==============================

        /// <summary>
        /// A successful enhancement purchase. Bursts on the node the user just bought (the caller
        /// still holds it - this runs before RefreshEnhancementsUI rebuilds the tree and detaches
        /// it), and if the spend pushed the lifetime total across a hundred, chains the prestige
        /// moment on top.
        /// </summary>
        /// <param name="node">The clicked node/card border.</param>
        /// <param name="rankBefore">Prestige rank sampled before the purchase.</param>
        internal void CelebrateEnhancementPurchase(FrameworkElement? node, long rankBefore)
        {
            try
            {
                FireBurstAtFirstVisible(FxBurstSpot.Center, null, EnhancementBurstCount,
                                        node, NavAnchorForTab("enhancements"));
                if (PrestigeRankNow() > rankBefore) CelebratePrestige();
            }
            catch (Exception ex) { App.Logger?.Debug("CelebrateEnhancementPurchase: {E}", ex.Message); }
        }

        /// <summary>
        /// Prestige rank-up - the rarest moment in the app, and the only one that gets the whole
        /// chrome: the top-of-range burst on the Prestige row plus one sheen pass across the entire
        /// window. The sheen is a single interaction-clock band on an already-parked element; it
        /// starts no loop and leaves nothing running.
        /// </summary>
        internal void CelebratePrestige()
        {
            try
            {
                if (!EventFxAllowed) return;
                FireBurstAtFirstVisible(FxBurstSpot.Center, FxTheme.GlowColor, PrestigeBurstCount,
                                        _prestigeRowBorder, NavAnchorForTab("enhancements"));
                SweepSheen(EventFxHost, PrestigeSheen, PrestigeSheenSlide,
                           PrestigeSheenSeconds, PrestigeSheenPeak);
                App.Logger?.Information("[FX] prestige rank-up celebrated");
            }
            catch (Exception ex) { App.Logger?.Debug("CelebratePrestige: {E}", ex.Message); }
        }
    }
}
