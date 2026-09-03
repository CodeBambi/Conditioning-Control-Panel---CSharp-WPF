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
// Two of WPF's gates have no equivalent here and are NOT faked:
//   * MotionFx.AllowAmbientLoops (reduced motion + the performance tier). The only copy of that
//     gate on this head is AmbientFxCanvas's private nested Env, which a non-canvas loop cannot
//     reach. So this drift currently runs whenever the tab is showing. Restore the gate when
//     MotionFx moves to Core - it is one `if` at the top of ApplyDeeperGlyphDrift.
//   * DeeperGlyphFrameRate = 10. That is Timeline.SetDesiredFrameRate, a per-storyboard frame cap.
//     Avalonia has no per-animation clock rate, so it drops for the same reason AmbientFrameRate
//     did (see MainShellWindow.AmbientFx.cs). A 3px sine over 11.5s is sub-pixel per frame either
//     way; what is lost is the CPU saving, not the look.
// Window activation/minimise parking (WPF's Activated/Deactivated/StateChanged funnel) is also
// gone: the drift is two interpolators on one transform, and a tab the user is not looking at
// already cancels it.
//
// Still a note: OnDeeperRowHover, the 2px library-row hover lift. It needs MotionFx.AllowTransitions
// AND a hover handler on the row template inside DeeperTabView, which this layer does not own.
// The rows keep the border-brush reveal their template already carries, so hover is not dead -
// only the lift is missing.
//
// Members of the WPF file still dropped (6):
//   private const double DeeperRowLiftPx / private const int DeeperRowLiftMs
//   private const int DeeperGlyphFrameRate                 - no Avalonia equivalent, see above
//   private bool DeeperFxOnScreen                          - the tab key SwitchTabFx passes IS this
//   private void OnDeeperFxWindowStateish(…)
//   internal void OnDeeperRowHover(…)

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
                bool wanted = string.Equals(tab, "deeper", StringComparison.OrdinalIgnoreCase)
                              && _deeperGlyphDrift != null;
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
