using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE FUSE's tease surfaces in the main window (CONTRACT-FUSE-0816 §2.2): the header spark,
    /// its hover readout, the corner clock, and the chrome dimming's repaint trigger.
    ///
    /// <para><b>Dormant with the service.</b> Every method here is driven by
    /// <see cref="App.DescentCountdown"/>'s events, and that service raises nothing without a
    /// cached ceremony timestamp. <see cref="InitializeDescentFuse"/> subscribes and then hands the
    /// surfaces the current phase — which is <see cref="DescentFusePhase.Dark"/> on every install
    /// today, so the spark stays collapsed and no clock, tooltip or animation is ever built.</para>
    ///
    /// <para><b>The kill switch runs through here.</b> A cleared timestamp announces
    /// <see cref="DescentFusePhase.Dark"/>; <see cref="ApplyFusePhase"/> collapses both surfaces,
    /// stops the breath, drops the tooltip and repaints the chrome — live, no restart.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The gold the spark and the terminal digits burn. Not an accent resource, and
        /// deliberately not mod-tinted: the fuse is the app's own colour, not the mod's.</summary>
        private static readonly Color FuseGold = Color.FromRgb(0xE0, 0xB0, 0x52);

        private static readonly Brush FuseGoldBrush = Freeze(new SolidColorBrush(FuseGold));
        private static readonly Brush FuseNeutralDigits = Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xC4, 0xD6)));

        private TextBlock? _fuseTooltipDigits;

        /// <summary>
        /// The chrome step the theme was last painted at. Seeded in
        /// <see cref="InitializeDescentFuse"/> from the live value, so the first paint is a no-op;
        /// after that a change here is the ONLY thing that triggers a theme rewrite, which is why
        /// the expensive repaint happens four times in the feature's life instead of once a tick.
        /// </summary>
        private int _fuseLastDimStep;
        private bool _fuseBreathing;

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        /// <summary>
        /// Repaint the neutral chrome at whatever the fuse's live dim step now is. The Year One
        /// ignition calls this on close (CONTRACT-FUSE-0816 §2.4).
        ///
        /// <para><b>It exists so the show never touches a palette cache.</b>
        /// <c>RefreshThemeAwareElements</c> is the app's single writer of the neutral colour family
        /// and it re-derives everything from the ACTIVE MOD every time — which is what makes the
        /// dimming restorable without anyone having stashed an "original" that could go stale. This
        /// wrapper is here purely because that method is private and a service has no window
        /// reference to hand; it adds no behaviour of its own.</para>
        ///
        /// <para>A no-op when there is no main window (a show still fading during shutdown), which
        /// is correct — there is no chrome left to restore.</para>
        /// </summary>
        internal static void RestoreFuseChrome()
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (Application.Current?.MainWindow is not MainWindow main) return;
            main.RefreshThemeAwareElements();
        }

        /// <summary>
        /// Wire the fuse surfaces. Called once from the MainWindow constructor, beside the other
        /// header initializers. Costs two event subscriptions and one phase read.
        /// </summary>
        private void InitializeDescentFuse()
        {
            try
            {
                var fuse = App.DescentCountdown;
                if (fuse is null) return;

                fuse.PhaseChanged += OnFusePhaseChanged;
                fuse.Tick += OnFuseTick;

                // The chrome is ALREADY correct for this step: RefreshThemeAwareElements ran
                // earlier in this same constructor, and it reads the live step itself. Seeding the
                // watermark to match stops the initial paint below from firing a second, identical
                // forty-resource rewrite on every launch — including the launches with no fuse at
                // all, which must cost nothing.
                _fuseLastDimStep = DescentFuseChrome.CurrentStep;

                // Paint the state we are already in. The service announces on Start(), which runs
                // before this window exists, so its announcement had no subscribers — reading
                // LastAnnouncedPhase is how we catch up. Idempotent either way.
                ApplyFusePhase(fuse.LastAnnouncedPhase);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Fuse] Header surfaces could not be wired: {E}", ex.Message);
            }
        }

        private void OnFusePhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e)
        {
            // CLAUDE.md rule 8: the window may be closing under a queued event.
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try { ApplyFusePhase(e.Current); }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] Phase repaint failed: {E}", ex.Message); }
        }

        private void OnFuseTick(object? sender, TimeSpan remaining)
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try
            {
                var text = DescentFuseCopy.TMinus(remaining);

                if (_fuseTooltipDigits != null) _fuseTooltipDigits.Text = text;

                if (FuseCornerReadout != null && FuseCornerReadout.Visibility == Visibility.Visible)
                {
                    if (FuseCornerDigits != null) FuseCornerDigits.Text = text;
                    ApplyFusePresence();
                }

                // The chrome walks one step every six hours; this is the only thing that notices.
                // Comparing the step (not the time) means the expensive repaint happens exactly
                // four times in the feature's whole life, not once per tick.
                var step = DescentFuseChrome.CurrentStep;
                if (step != _fuseLastDimStep)
                {
                    _fuseLastDimStep = step;
                    RefreshThemeAwareElements();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] Tick repaint failed: {E}", ex.Message); }
        }

        /// <summary>
        /// The whole surface state for a phase, in one place, computed from scratch every time —
        /// so an arrival at any phase (including a jump backwards when the owner moves the date, or
        /// straight to Dark when the switch is killed) lands on a correct, complete state rather
        /// than on the accumulated result of the transitions it happened to take.
        /// </summary>
        private void ApplyFusePhase(DescentFusePhase phase)
        {
            var spark = FuseSparkHost;
            var corner = FuseCornerReadout;

            // ---- the spark: from Whisper, and nothing before it -------------------
            if (spark != null)
            {
                var show = phase >= DescentFusePhase.Whisper && phase < DescentFusePhase.Zero;
                spark.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

                if (show)
                {
                    // Reduced motion gets a SNAPPED state, never a missing one: GlowBreath parks the
                    // element at full opacity when ambient loops are off, so the spark is simply
                    // there, lit, instead of breathing.
                    if (!_fuseBreathing)
                    {
                        MotionFx.GlowBreath(FuseSparkGlyph, 0.45, 1.0, 3.8);
                        _fuseBreathing = true;
                    }

                    // A snapped size bump for the last hour. Not an animation — it is simply bigger
                    // from here on, which every motion level renders identically.
                    if (FuseSparkScale != null)
                    {
                        var s = phase >= DescentFusePhase.Vigil ? 1.25 : 1.0;
                        FuseSparkScale.ScaleX = FuseSparkScale.ScaleY = s;
                    }
                }
                else if (_fuseBreathing)
                {
                    MotionFx.Stop(FuseSparkGlyph);
                    if (FuseSparkGlyph != null) FuseSparkGlyph.Opacity = 1.0;
                    if (FuseSparkScale != null) FuseSparkScale.ScaleX = FuseSparkScale.ScaleY = 1.0;
                    _fuseBreathing = false;
                }

                // The hover readout arrives at Clock and NOT before. Pre-Clock the spark explains
                // itself to nobody — that silence is the whole tease, so there is no tooltip to
                // find, not even an empty one.
                spark.ToolTip = phase >= DescentFusePhase.Clock && phase < DescentFusePhase.Zero
                    ? BuildFuseTooltip()
                    : null;
                if (spark.ToolTip is null) _fuseTooltipDigits = null;
            }

            // ---- the corner clock: from Vigil ------------------------------------
            if (corner != null)
            {
                var show = phase >= DescentFusePhase.Vigil && phase < DescentFusePhase.Zero;
                corner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

                if (show)
                {
                    // Terminal turns the digits gold. The last ten minutes are the only time this
                    // countdown raises its voice, and it does it with a colour, not a noise.
                    if (FuseCornerDigits != null)
                    {
                        FuseCornerDigits.Foreground =
                            phase >= DescentFusePhase.Terminal ? FuseGoldBrush : FuseNeutralDigits;
                        FuseCornerDigits.Text = DescentFuseCopy.TMinus(App.DescentCountdown?.Remaining ?? TimeSpan.Zero);
                    }
                    ApplyFusePresence();
                }
            }

            // ---- the chrome ------------------------------------------------------
            var step = DescentFuseChrome.CurrentStep;
            if (step != _fuseLastDimStep)
            {
                _fuseLastDimStep = step;
                RefreshThemeAwareElements();
            }
        }

        /// <summary>
        /// "N falling with you", and only when the server actually said N. Absent count = the line
        /// is not there at all; a client-invented number would be a lie about other people, and
        /// §0.6 keeps anything that smells like social pressure off this surface entirely.
        ///
        /// <para><b>ZERO IS A READING, NOT AN ABSENCE</b> (server lane, PR #44). The server omits
        /// <c>vigil</c> entirely when it is out of window, so a 0 that arrives is a real, in-window
        /// measurement of "nobody has synced in the last fifteen minutes" — and hiding it would
        /// quietly turn an honest count into a number that only ever flatters. Only null (the
        /// server did not speak) collapses the line.</para>
        /// </summary>
        private void ApplyFusePresence()
        {
            var line = FuseCornerPresence;
            if (line is null) return;

            var count = App.DescentCountdown?.VigilCount;
            if (count is null or < 0)
            {
                line.Visibility = Visibility.Collapsed;
                return;
            }

            line.Text = DescentFuseCopy.Presence(count.Value);
            line.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// The hover readout: mono, tabular, and nothing else — no label, no explanation, no offer.
        /// Built once and kept, so <see cref="OnFuseTick"/> can retype the digits in place while the
        /// tooltip is open instead of tearing it down under the pointer.
        /// </summary>
        private object BuildFuseTooltip()
        {
            if (_fuseTooltipDigits != null && FuseSparkHost?.ToolTip is ToolTip existing) return existing;

            _fuseTooltipDigits = new TextBlock
            {
                Text = DescentFuseCopy.TMinus(App.DescentCountdown?.Remaining ?? TimeSpan.Zero),
                // Cascadia first, Consolas behind it: the digits must not reflow as they count
                // down, and a proportional fallback would jitter the whole readout every second.
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                FontSize = 14,
                Foreground = FuseNeutralDigits,
            };

            return new ToolTip
            {
                Content = _fuseTooltipDigits,
                Background = Freeze(new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x05, 0x14))),
                BorderBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0xB0, 0x52))),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 4, 9, 4),
                HasDropShadow = false,
            };
        }
    }
}
