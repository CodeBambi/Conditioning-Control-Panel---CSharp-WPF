// Port of ConditioningControlPanel/MainWindow/MainWindow.DescentFuse.cs: the header spark, its
// hover readout and the corner clock, driven by Core DescentCountdownService (App.DescentCountdown).
// Plus the live zero opener of WPF Services/Descent/DescentShowDirector.cs:130-170.
//
// ponytail: the chrome dimming is not ported - DescentFuseChrome (WPF head) and this head's
// RefreshThemeAwareElements do not share a dim step, so the neutral chrome never darkens here.
// ponytail: zero opens only the Live show; the catch-up crack, the post-zero retry and the
// ceremony focus/hand-off (DescentShowDirector, DescentMigrationService) remain WPF-only.
using System;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private static readonly IBrush FuseGoldBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x52));
        private static readonly IBrush FuseNeutralDigits = new SolidColorBrush(Color.FromRgb(0xC9, 0xC4, 0xD6));

        private TextBlock? _fuseTooltipDigits;
        private System.Threading.CancellationTokenSource? _fuseBreath;

        /// <summary>WPF InitializeDescentFuse: two subscriptions and one catch-up paint.</summary>
        internal void InitializeDescentFuse()
        {
            try
            {
                var fuse = App.DescentCountdown;
                if (fuse is null) return;
                fuse.PhaseChanged += (_, e) => ApplyFusePhase(e.Current);
                fuse.Tick += (_, remaining) => OnFuseTick(remaining);
                fuse.ZeroReached += (_, _) => _ = OpenLiveFuseShow();
                ApplyFusePhase(fuse.LastAnnouncedPhase);
            }
            catch (Exception ex) { Log.Debug("[Fuse] Header surfaces could not be wired: {E}", ex.Message); }
        }

        private void OnFuseTick(TimeSpan remaining)
        {
            var text = DescentFuseCopy.TMinus(remaining);
            if (_fuseTooltipDigits != null) _fuseTooltipDigits.Text = text;
            if (Named<Border>("FuseCornerReadout") is { IsVisible: true })
            {
                if (Named<TextBlock>("FuseCornerDigits") is { } digits) digits.Text = text;
                ApplyFusePresence();
            }
        }

        /// <summary>WPF ApplyFusePhase: the whole surface state for a phase, from scratch.</summary>
        internal void ApplyFusePhase(DescentFusePhase phase)
        {
            if (Named<Grid>("FuseSparkHost") is { } spark)
            {
                var show = phase >= DescentFusePhase.Whisper && phase < DescentFusePhase.Zero;
                spark.IsVisible = show;
                var glyph = Named<Control>("FuseSparkGlyph");
                if (show && _fuseBreath is null && glyph != null)
                {
                    // WPF MotionFx.GlowBreath(glyph, 0.45, 1.0, 3.8): reduced motion parks it lit.
                    _fuseBreath = new System.Threading.CancellationTokenSource();
                    if (ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowAmbientLoops)
                    {
                        // X7: the header fuse is up for hours, so its breath rides the shared 30 fps beat
                        // (BreathClock), never an infinite Animation. Same curve: 0.45 to 1.0 over 3.8 s, back again.
                        var breath = new Features.BreathClock(glyph, 3.8);
                        _fuseBreath.Token.Register(breath.Stop);
                        breath.Start((glyph, 0.45, 1.0));
                    }
                }
                else if (!show && _fuseBreath != null)
                {
                    _fuseBreath.Cancel();
                    _fuseBreath = null;
                    if (glyph != null) glyph.Opacity = 1.0;
                }
                // The last-hour size bump, snapped (WPF FuseSparkScale).
                if (spark.RenderTransform is ScaleTransform scale)
                    scale.ScaleX = scale.ScaleY = show && phase >= DescentFusePhase.Vigil ? 1.25 : 1.0;

                // The hover readout arrives at Clock and not before.
                if (phase >= DescentFusePhase.Clock && phase < DescentFusePhase.Zero)
                {
                    var remaining = DescentFuseCopy.TMinus(App.DescentCountdown?.Remaining ?? TimeSpan.Zero);
                    // Built once and kept (WPF BuildFuseTooltip), so ticks retype it in place.
                    if (_fuseTooltipDigits != null) _fuseTooltipDigits.Text = remaining;
                    else ToolTip.SetTip(spark, NewFuseTip(_fuseTooltipDigits = new TextBlock
                    {
                        Text = remaining,
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        FontSize = 14,
                        Foreground = FuseNeutralDigits,
                    }));
                }
                else
                {
                    ToolTip.SetTip(spark, null);
                    _fuseTooltipDigits = null;
                }
            }

            if (Named<Border>("FuseCornerReadout") is { } corner)
            {
                var show = phase >= DescentFusePhase.Vigil && phase < DescentFusePhase.Zero;
                corner.IsVisible = show;
                if (show && Named<TextBlock>("FuseCornerDigits") is { } digits)
                {
                    // Terminal turns the digits gold.
                    digits.Foreground = phase >= DescentFusePhase.Terminal ? FuseGoldBrush : FuseNeutralDigits;
                    digits.Text = DescentFuseCopy.TMinus(App.DescentCountdown?.Remaining ?? TimeSpan.Zero);
                    ApplyFusePresence();
                }
            }
        }

        private static Border NewFuseTip(TextBlock digits) => new()
        {
            Child = digits,
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x05, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0xB0, 0x52)),
            BorderThickness = new global::Avalonia.Thickness(1),
            Padding = new global::Avalonia.Thickness(9, 4, 9, 4),
        };

        /// <summary>WPF ApplyFusePresence: only when the server actually said N (0 is a reading).</summary>
        private void ApplyFusePresence()
        {
            if (Named<TextBlock>("FuseCornerPresence") is not { } line) return;
            var count = App.DescentCountdown?.VigilCount;
            line.IsVisible = count is >= 0;
            if (count is >= 0) line.Text = DescentFuseCopy.Presence(count.Value);
        }

        /// <summary>WPF DescentShowDirector live zero: nothing to reveal to an answered account.</summary>
        internal static DescentFuseWindow? OpenLiveFuseShow()
        {
            var s = CoreSettings.Current;
            if (s.DescentMigrationCompleted || DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice))
            {
                Log.Information("[Fuse] Zero, but the question is already answered - no live show.");
                return null;
            }
            return DescentFuseWindow.Open(DescentShowKind.Live);
        }
    }
}
