using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-4b of the FX overhaul: the <b>Assets</b> tab pass.
    ///
    /// This tab gets NO hero loop - that is the plan's decision (FX plan section 4), not an
    /// omission. Assets is a working surface: a file tree, a thumbnail wall and a pack strip. Air
    /// drifting behind a file browser is noise, and a thumbnail wall is already the busiest grid in
    /// the app. Everything here is therefore hover- or event-driven and costs exactly nothing while
    /// the tab sits open:
    ///   • tree rows nudge on hover, on top of the background wash the TreeViewItem style already
    ///     paints;
    ///   • a pack card catches one sheen while the pointer is on it, and the adorner comes off the
    ///     moment the pointer leaves;
    ///   • the Media Log button pulses (a fixed three beats, never a loop) when media has been
    ///     logged that the user has not looked at.
    ///
    /// House rules: BeginAnimation only, colour from <see cref="FxTheme"/> via the shared sheen,
    /// every callback wrapped.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>Tree rows are left-aligned text in a 280px column; a 3px slide to the right is
        /// the whole gesture. A scale would blur the glyphs and fight the checkbox hit box.</summary>
        private const double AssetTreeRowNudgePx = 3.0;
        private const int AssetTreeRowNudgeMs = 130;

        private const double PackCardCornerRadius = 8;

        /// <summary>Three beats, then done. A standing pulse on a nav button is a nag; three is a
        /// notification.</summary>
        private const int MediaLogPulseBeats = 3;
        private const double MediaLogPulseSeconds = 0.45;
        private const double MediaLogPulseFloor = 0.45;

        // ---- state -----------------------------------------------------------------

        private bool _assetsFxInitialized;

        private CardSheenAdorner? _packCardSheen;
        private AdornerLayer? _packCardSheenLayer;
        private FrameworkElement? _packCardSheenHost;

        /// <summary>
        /// How many media entries had been logged the last time the user opened the Media Log.
        /// Starts at 0, so the first visit of a session with any history at all announces itself
        /// once and then goes quiet. Session-scoped by design: this is a "look at this" nudge, not
        /// a read-receipt worth a settings key and a migration.
        /// </summary>
        private int _mediaLogSeenCount;

        // ============================== lifecycle ==============================

        internal void OnAssetsTabVisibilityChanged(bool visible)
        {
            try
            {
                if (!visible)
                {
                    DetachPackCardSheen();
                    return;
                }
                if (!IsIncomingTab("assets")) return;  // the outgoing tab's fade-out re-show
                InitializeAssetsFx();
                PulseMediaLogIfUnseen();
            }
            catch (Exception ex) { App.Logger?.Debug("OnAssetsTabVisibilityChanged: {E}", ex.Message); }
        }

        private void InitializeAssetsFx()
        {
            if (_assetsFxInitialized) return;
            _assetsFxInitialized = true;
            try
            {
                if (AssetsTab?.BtnMediaLog != null)
                    AssetsTab.BtnMediaLog.Click += MediaLogButton_Clicked;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeAssetsFx failed"); }
        }

        // ============================== tree row hover ==============================

        internal void OnAssetTreeRowHover(FrameworkElement? row, bool on)
        {
            if (row == null) return;
            try
            {
                if (row.RenderTransform is not TranslateTransform slide)
                {
                    var current = row.RenderTransform;
                    if (current != null && current != Transform.Identity && !current.Value.IsIdentity) return;
                    slide = new TranslateTransform();
                    row.RenderTransform = slide;
                }
                if (slide.IsFrozen) return;

                double to = on ? AssetTreeRowNudgePx : 0;
                if (!MotionFx.AllowTransitions)
                {
                    slide.BeginAnimation(TranslateTransform.XProperty, null);
                    slide.X = to;
                    return;
                }
                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(AssetTreeRowNudgeMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                slide.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("OnAssetTreeRowHover: {E}", ex.Message); }
        }

        // ============================== pack card sheen ==============================

        /// <summary>
        /// Moves the shared sheen onto the hovered pack card and takes it off on the way out.
        ///
        /// Note for screenshot review: the packs strip ships hidden (PacksSectionEnabled is a const
        /// false in MainWindow.xaml.cs - most packs live outside the app now), so this is wired and
        /// dormant rather than visible. It costs nothing while the strip is collapsed and lights up
        /// the day that flag flips. Only
        /// ever one card at a time, and no clock exists at all while the pointer is elsewhere -
        /// which is why this is gated on the INTERACTION clock: a sheen you have to hold the mouse
        /// still for is a hover response, not ambient motion.
        /// </summary>
        internal void OnPackCardHover(FrameworkElement? card, bool on)
        {
            try
            {
                if (!on)
                {
                    if (card == null || ReferenceEquals(card, _packCardSheenHost)) DetachPackCardSheen();
                    return;
                }
                if (card == null || !card.IsVisible) return;
                if (!MotionFx.AllowTransitions) return;
                if (ReferenceEquals(card, _packCardSheenHost) && _packCardSheen != null) return;

                DetachPackCardSheen();
                var layer = AdornerLayer.GetAdornerLayer(card);
                if (layer == null) return;

                var sheen = new CardSheenAdorner(card, PackCardCornerRadius);
                layer.Add(sheen);
                sheen.Start();
                _packCardSheen = sheen;
                _packCardSheenLayer = layer;
                _packCardSheenHost = card;
            }
            catch (Exception ex) { App.Logger?.Debug("OnPackCardHover: {E}", ex.Message); }
        }

        private void DetachPackCardSheen()
        {
            try
            {
                var sheen = _packCardSheen;
                var layer = _packCardSheenLayer;
                _packCardSheen = null;
                _packCardSheenLayer = null;
                _packCardSheenHost = null;
                if (sheen == null) return;
                sheen.Stop();
                layer?.Remove(sheen);
            }
            catch (Exception ex) { App.Logger?.Debug("DetachPackCardSheen: {E}", ex.Message); }
        }

        // ============================== Media Log pulse ==============================

        /// <summary>
        /// Three beats on the Media Log button when something has been logged since the log was
        /// last opened. <see cref="MediaHistoryService.Count"/> is the whole signal - no new state,
        /// no polling, and nothing at all happens when the count has not moved.
        /// </summary>
        private void PulseMediaLogIfUnseen()
        {
            try
            {
                var button = AssetsTab?.BtnMediaLog;
                if (button == null) return;

                int count = App.MediaHistory?.Count ?? 0;
                if (count <= _mediaLogSeenCount) return;
                if (!MotionFx.AllowTransitions) return;

                var pulse = new DoubleAnimation(1.0, MediaLogPulseFloor,
                                                TimeSpan.FromSeconds(MediaLogPulseSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(MediaLogPulseBeats),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, AmbientFrameRate);
                button.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            catch (Exception ex) { App.Logger?.Debug("PulseMediaLogIfUnseen: {E}", ex.Message); }
        }

        private void MediaLogButton_Clicked(object sender, RoutedEventArgs e)
        {
            try
            {
                _mediaLogSeenCount = App.MediaHistory?.Count ?? 0;
                if (sender is UIElement button)
                {
                    button.BeginAnimation(UIElement.OpacityProperty, null);
                    button.Opacity = 1.0;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("MediaLogButton_Clicked: {E}", ex.Message); }
        }
    }
}
