using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-4b of the FX overhaul: the <b>Deeper</b> hub pass.
    ///
    /// One focal loop, per FX plan section 4:
    ///   • hero  - the header wave glyph drifts. Two slow, deliberately mismatched clocks (23s
    ///     vertical, 31s horizontal) on one TranslateTransform, so the path never repeats on an
    ///     obvious beat and the glyph reads as floating rather than as a pendulum.
    ///   • micro - library rows gain a hover lift on top of the border-brush reveal they already
    ///     have. Composed, not replaced: the reveal is still the DataTemplate's own trigger.
    ///
    /// The library list is a long column inside a ScrollViewer, so the rows LIFT rather than
    /// SCALE - see <see cref="DeeperRowLiftPx"/>.
    ///
    /// House rules: ambient clock gated on <see cref="DeeperFxOnScreen"/> +
    /// <see cref="MotionFx.AllowAmbientLoops"/>, capped frame rate, BeginAnimation only, every
    /// callback wrapped.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>Drift amplitudes in DIPs. Small on purpose: the glyph sits next to a title, and
        /// anything the eye can measure against that baseline reads as a wobble.</summary>
        private const double DeeperGlyphDriftY = 3.0;
        private const double DeeperGlyphDriftX = 2.0;
        private const double DeeperGlyphDriftYSeconds = 11.5;   // half-period; 23s round trip
        private const double DeeperGlyphDriftXSeconds = 15.5;   // half-period; 31s round trip

        /// <summary>Very slow motion needs very few frames. 10fps over an 11s ramp is a sub-pixel
        /// step per frame - invisible as stepping, and a third of the ambient budget.</summary>
        private const int DeeperGlyphFrameRate = 10;

        /// <summary>
        /// Rise for a full-width library row on hover. The rows stretch edge to edge inside a
        /// ScrollViewer, so the shared 1.02 scale would push several px past the viewport on each
        /// side and WPF would clip it without a word. A 2px rise reads as the same lift and cannot
        /// clip - the rows carry a 6px bottom margin.
        /// </summary>
        private const double DeeperRowLiftPx = 2.0;
        private const int DeeperRowLiftMs = 150;

        // ---- state -----------------------------------------------------------------

        private bool _deeperFxInitialized;
        private TranslateTransform? _deeperGlyphDrift;
        private AnimationClock? _deeperGlyphClockY;
        private AnimationClock? _deeperGlyphClockX;

        private bool DeeperFxOnScreen =>
            IsActive && WindowState != WindowState.Minimized && DeeperTab?.IsVisible == true;

        // ============================== lifecycle ==============================

        internal void OnDeeperTabVisibilityChanged(bool visible)
        {
            try
            {
                if (visible && !IsIncomingTab("deeper")) return;  // the outgoing tab's fade-out re-show
                InitializeDeeperFx();
                ApplyDeeperGlyphDrift();
            }
            catch (Exception ex) { App.Logger?.Debug("OnDeeperTabVisibilityChanged: {E}", ex.Message); }
        }

        private void InitializeDeeperFx()
        {
            if (_deeperFxInitialized) return;
            _deeperFxInitialized = true;
            try
            {
                Activated += OnDeeperFxWindowStateish;
                Deactivated += OnDeeperFxWindowStateish;
                StateChanged += OnDeeperFxWindowStateish;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeDeeperFx failed"); }
        }

        private void OnDeeperFxWindowStateish(object? sender, EventArgs e)
        {
            try { ApplyDeeperGlyphDrift(); }
            catch (Exception ex) { App.Logger?.Debug("OnDeeperFxWindowStateish: {E}", ex.Message); }
        }

        // ============================== header glyph drift ==============================

        private void ApplyDeeperGlyphDrift()
        {
            try
            {
                var glyph = DeeperTab?.DeeperWaveGlyph;
                if (glyph == null) return;

                if (!MotionFx.AllowAmbientLoops) { StopDeeperGlyphDrift(); return; }

                if (!DeeperFxOnScreen)
                {
                    if (_deeperGlyphClockY?.Controller != null || _deeperGlyphClockX?.Controller != null)
                    {
                        _deeperGlyphClockY?.Controller?.Pause();
                        _deeperGlyphClockX?.Controller?.Pause();
                    }
                    else StopDeeperGlyphDrift();
                    return;
                }

                if (_deeperGlyphClockY != null || _deeperGlyphClockX != null)
                {
                    _deeperGlyphClockY?.Controller?.Resume();
                    _deeperGlyphClockX?.Controller?.Resume();
                    return;
                }

                if (_deeperGlyphDrift == null)
                {
                    var current = glyph.RenderTransform;
                    if (current is TranslateTransform existing) _deeperGlyphDrift = existing;
                    else if (current == null || current == Transform.Identity || current.Value.IsIdentity)
                    {
                        _deeperGlyphDrift = new TranslateTransform();
                        glyph.RenderTransform = _deeperGlyphDrift;
                    }
                    else return;   // never clobber an authored transform
                }
                if (_deeperGlyphDrift.IsFrozen) return;

                _deeperGlyphClockY = Drift(TranslateTransform.YProperty, DeeperGlyphDriftY, DeeperGlyphDriftYSeconds);
                _deeperGlyphClockX = Drift(TranslateTransform.XProperty, DeeperGlyphDriftX, DeeperGlyphDriftXSeconds);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyDeeperGlyphDrift: {E}", ex.Message); }

            AnimationClock? Drift(DependencyProperty property, double amplitude, double seconds)
            {
                var anim = new DoubleAnimation(-amplitude, amplitude, TimeSpan.FromSeconds(seconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, DeeperGlyphFrameRate);
                anim.Freeze();
                var clock = anim.CreateClock();
                _deeperGlyphDrift!.ApplyAnimationClock(property, clock);
                return clock;
            }
        }

        private void StopDeeperGlyphDrift()
        {
            if (_deeperGlyphClockY == null && _deeperGlyphClockX == null) return;
            try
            {
                var drift = _deeperGlyphDrift;
                _deeperGlyphClockY = null;
                _deeperGlyphClockX = null;
                if (drift == null || drift.IsFrozen) return;
                drift.ApplyAnimationClock(TranslateTransform.YProperty, null);
                drift.ApplyAnimationClock(TranslateTransform.XProperty, null);
                drift.X = drift.Y = 0;
            }
            catch (Exception ex) { App.Logger?.Debug("StopDeeperGlyphDrift: {E}", ex.Message); }
        }

        // ============================== row hover ==============================

        /// <summary>Hover lift on a library row - a rise, not a scale. See
        /// <see cref="DeeperRowLiftPx"/> for why. Interaction clock, so it survives Reduced and
        /// only snaps at Off.</summary>
        internal void OnDeeperRowHover(FrameworkElement? row, bool on)
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

                double to = on ? -DeeperRowLiftPx : 0;
                if (!MotionFx.AllowTransitions)
                {
                    slide.BeginAnimation(TranslateTransform.YProperty, null);
                    slide.Y = to;
                    return;
                }
                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(DeeperRowLiftMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                slide.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("OnDeeperRowHover: {E}", ex.Message); }
        }
    }
}
