// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.DeeperFx.cs (207 lines).
//
// The hero loop is live: the Deeper header's wave glyph drifts. WPF ran two DoubleAnimations with
// AutoReverse + RepeatBehavior.Forever on one TranslateTransform; Avalonia's twin is two
// Animations with PlaybackDirection.Alternate + IterationCount.Infinite over the same transform,
// which is the same motion rather than an approximation of it. The amplitudes and the deliberately
// mismatched half-periods are verbatim (3.0dip over 11.5s, 2.0dip over 15.5s), so the path still
// never repeats on a beat the eye can find and the glyph reads as floating, not as a pendulum.
//
// Start and stop are driven by SwitchTabFx: arriving at "deeper" runs it, leaving cancels it and
// puts the glyph back at 0,0. That replaces WPF's OnDeeperTabVisibilityChanged.
//
// WPF's gates are kept: MotionFx.AllowAmbientLoops (AmbientFxCanvas.Env) and DeeperFxOnScreen
// (active, not minimised; Activated/Deactivated/WindowState re-evaluate it). Dropped: only
// DeeperGlyphFrameRate = 10 - Avalonia has no per-animation clock rate (see
// MainShellWindow.AmbientFx.cs); a 3px sine over 11.5s is sub-pixel per frame either way.
// OnDeeperRowHover (the 2px library-row lift) is driven by DeeperTabView's row enter/leave.

using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Drift amplitudes in DIPs, verbatim from WPF. Small on purpose: the glyph sits
        /// next to a title, and anything the eye can measure against that baseline reads as a
        /// wobble.</summary>
        private const double DeeperGlyphDriftY = 3.0;
        private const double DeeperGlyphDriftX = 2.0;

        /// <summary>Half-periods; 23s and 31s round trips, mismatched so the path does not repeat
        /// on an obvious beat.</summary>
        private const double DeeperRowLiftPx = 2.0;
        private const int DeeperRowLiftMs = 150;
        private const double DeeperGlyphDriftYSeconds = 11.5;
        private const double DeeperGlyphDriftXSeconds = 15.5;

        private bool _deeperFxInitialized;
        private TranslateTransform? _deeperGlyphDrift;
        private CancellationTokenSource? _deeperGlyphClock;

        /// <summary>
        /// Resolves the glyph and gives it the transform the drift animates. Once, on the first
        /// arrival at the tab - called from EnsureTabFx.
        /// </summary>
        private void EnsureDeeperFx()
        {
            if (_deeperFxInitialized) return;
            _deeperFxInitialized = true;
            try
            {
                // WPF InitializeDeeperFx: park the drift while the window is inactive or minimised.
                Activated += OnDeeperFxWindowStateish;
                Deactivated += OnDeeperFxWindowStateish;
                PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) OnDeeperFxWindowStateish(null, EventArgs.Empty); };

                // FindControl, not the generated field: DeeperTabView loads with
                // AvaloniaXamlLoader.Load, so DeeperWaveGlyph is null on it despite compiling.
                var glyph = Named<Tabs.DeeperTabView>("DeeperTab")
                    ?.FindControl<TextBlock>("DeeperWaveGlyph");
                if (glyph == null) return;

                if (glyph.RenderTransform is TranslateTransform existing) _deeperGlyphDrift = existing;
                else if (glyph.RenderTransform == null)
                {
                    _deeperGlyphDrift = new TranslateTransform();
                    glyph.RenderTransform = _deeperGlyphDrift;
                }
                // else: an authored transform. Never clobbered - the glyph simply does not drift.
            }
            catch (Exception ex) { Log.Warning(ex, "EnsureDeeperFx failed"); }
        }

        /// <summary>
        /// Runs the drift while the Deeper tab is the one showing, and parks it flat otherwise.
        /// Called from SwitchTabFx with the incoming tab key, so leaving the tab stops it.
        /// </summary>
        private void ApplyDeeperGlyphDrift(string tab)
        {
            try
            {
                // WPF DeeperFxOnScreen + MotionFx.AllowAmbientLoops.
                bool wanted = string.Equals(tab, "deeper", StringComparison.OrdinalIgnoreCase)
                              && _deeperGlyphDrift != null
                              && IsActive && WindowState != WindowState.Minimized
                              && global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowAmbientLoops;
                if (!wanted) { StopDeeperGlyphDrift(); return; }
                if (_deeperGlyphClock != null) return;    // already drifting

                _deeperGlyphClock = new CancellationTokenSource();
                Drift(TranslateTransform.YProperty, DeeperGlyphDriftY, DeeperGlyphDriftYSeconds);
                Drift(TranslateTransform.XProperty, DeeperGlyphDriftX, DeeperGlyphDriftXSeconds);
            }
            catch (Exception ex) { Log.Debug("ApplyDeeperGlyphDrift: {E}", ex.Message); }

            void Drift(AvaloniaProperty property, double amplitude, double seconds)
            {
                var anim = new Animation
                {
                    Duration = TimeSpan.FromSeconds(seconds),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(property, -amplitude) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(property, amplitude) } },
                    },
                };
                _ = anim.RunAsync(_deeperGlyphDrift!, _deeperGlyphClock!.Token);
            }
        }

        private void OnDeeperFxWindowStateish(object? sender, EventArgs e) => ApplyDeeperGlyphDrift(CurrentTab ?? "");

        /// <summary>WPF row hover lift: a 2px rise, not a scale (full-width rows in a ScrollViewer
        /// would clip a scale). Interaction motion: eased at every level but Off, which snaps.</summary>
        internal void OnDeeperRowHover(Control? row, bool on)
        {
            if (row == null) return;
            try
            {
                if (row.RenderTransform is not TranslateTransform slide)
                {
                    if (row.RenderTransform != null) return;   // an authored transform is never clobbered
                    row.RenderTransform = slide = new TranslateTransform();
                }
                slide.Transitions = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowTransitions
                    ? new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty,
                        Duration = TimeSpan.FromMilliseconds(DeeperRowLiftMs), Easing = new QuadraticEaseOut() } }
                    : null;
                slide.Y = on ? -DeeperRowLiftPx : 0;
            }
            catch (Exception ex) { Log.Debug("OnDeeperRowHover: {E}", ex.Message); }
        }

        private void StopDeeperGlyphDrift()
        {
            var clock = _deeperGlyphClock;
            if (clock == null) return;
            _deeperGlyphClock = null;
            try
            {
                clock.Cancel();
                clock.Dispose();
                // A cancelled Avalonia animation leaves the property wherever it stopped, so the
                // glyph is put back on its baseline explicitly.
                if (_deeperGlyphDrift != null) { _deeperGlyphDrift.X = 0; _deeperGlyphDrift.Y = 0; }
            }
            catch (Exception ex) { Log.Debug("StopDeeperGlyphDrift: {E}", ex.Message); }
        }
    }
}
