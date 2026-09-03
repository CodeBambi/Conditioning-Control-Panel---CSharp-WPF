// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Animations.cs (199 lines).
//
// The two ambient loops are LIVE. WPF drove them with Storyboards; the Avalonia twin of each is a
// keyframe Animation with PlaybackDirection.Alternate + IterationCount.Infinite, run against the
// same object WPF targeted, with a CancellationTokenSource for the Stop half (the pattern
// MainShellWindow.DeeperFx.cs established). The durations, the amplitudes and the two colours are
// verbatim, so this is the same motion rather than a lookalike:
//
//   * the season banner's title shimmer - the gradient sweeps across the glyphs (StartPoint
//     -1..1, EndPoint 0..2 over 3s) while its drop shadow breathes 0.3 <-> 0.9 over 1.5s;
//   * the Lockdown plate's pink breath - #FF1493 <-> #FF69B4 on the border, blur 12 <-> 22 and
//     glow opacity 0.7 <-> 1.0, all on the same 1.5s clock.
//
// WPF reached both through the tab view's generated field, which is exactly the namescope trap its
// own comments describe; here every element is resolved by name off the view (FindControl), so a
// view this head has not ported is a no-op rather than a crash.
//
// TWO AVALONIA FACTS SHAPE THE LOCKDOWN LOOP. Avalonia cannot put an x:Name on a brush or an
// effect (AVLN2000), so LockdownImageBorderBrush and LockdownImageGlow do not exist as names -
// they are reached as LockdownImageBorder's own BorderBrush and Effect. And a brush written as a
// XAML attribute (BorderBrush="#FF1493") parses to an IMMUTABLE brush, which cannot be animated,
// so the start swaps in a mutable SolidColorBrush of the same colour and the stop puts the
// original object back.
//
// NOTHING CALLS THESE YET. On WPF the four Start/Stop methods are called from the tab transition
// in MainWindow.TabNavigation.cs; on this head that funnel is SwitchTabFx in
// MainShellWindow.AmbientFx.cs, which this layer does not own. Each is one line there, keyed on
// "quests" and "lockdown".
//
// Dropped, with the reason (2 of the file's 6 members):
//   private void StopSkillTreeAnimations(…) / RestartSkillTreeAnimations(…)
//     SUPERSEDED, not missing. Both drive a hand-rolled gradient brush plus per-Ellipse opacity
//     animations that MainWindow.Enhancements.cs's DrawSkillTree creates - and the tree on this
//     head is a SAMPLE drawn by Views/Tabs/EnhancementsTabView.axaml.cs with no animated brush and
//     no particles, while the tab's ambient air is an AmbientFxCanvas that parks and resumes
//     itself (MainShellWindow.EnhancementsFx.cs). There is nothing here for a stop/restart pair to
//     own; re-adding them would be a window-side clock over a canvas that already has one.
//
// Also not carried over: MotionFx.AllowAmbientLoops (ConditioningControlPanel/Services/MotionFx.cs)
// and Timeline.SetDesiredFrameRate/AmbientFrameRate. The first is not in Core, so both loops run
// whenever they are started - restore it as one `if` at the top of each Start when MotionFx moves.
// The second has no Avalonia equivalent at all (see MainShellWindow.AmbientFx.cs).

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
        private CancellationTokenSource? _seasonTitleClock;
        private CancellationTokenSource? _lockdownPulseClock;

        /// <summary>The brush the Lockdown plate shipped with, kept so the stop puts back the
        /// exact object rather than a colour that happens to match.</summary>
        private IBrush? _lockdownRestingBrush;

        // ---- season banner ---------------------------------------------------------

        private void StartSeasonTitleShimmer()
        {
            if (_seasonTitleClock != null) return;                   // already running
            try
            {
                var title = Named<Control>("QuestsTab")?.FindControl<TextBlock>("TxtSeasonTitle");
                if (title?.Foreground is not LinearGradientBrush brush) return;

                _seasonTitleClock = new CancellationTokenSource();
                var token = _seasonTitleClock.Token;

                var sweep = new Animation
                {
                    Duration = TimeSpan.FromSeconds(3),
                    IterationCount = IterationCount.Infinite,
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0d),
                            Setters =
                            {
                                new Setter(LinearGradientBrush.StartPointProperty, Relative(-1)),
                                new Setter(LinearGradientBrush.EndPointProperty, Relative(0)),
                            },
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1d),
                            Setters =
                            {
                                new Setter(LinearGradientBrush.StartPointProperty, Relative(1)),
                                new Setter(LinearGradientBrush.EndPointProperty, Relative(2)),
                            },
                        },
                    },
                };
                _ = sweep.RunAsync(brush, token);

                if (title.Effect is DropShadowEffect glow)
                {
                    var breath = new Animation
                    {
                        Duration = TimeSpan.FromSeconds(1.5),
                        IterationCount = IterationCount.Infinite,
                        PlaybackDirection = PlaybackDirection.Alternate,
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(DropShadowEffect.OpacityProperty, 0.3) } },
                            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(DropShadowEffect.OpacityProperty, 0.9) } },
                        },
                    };
                    _ = breath.RunAsync(glow, token);
                }
            }
            catch (Exception ex) { Log.Warning("Failed to start season title shimmer: {Error}", ex.Message); }

            static RelativePoint Relative(double x) => new(x, 0.5, RelativeUnit.Relative);
        }

        private void StopSeasonTitleShimmer()
        {
            var clock = _seasonTitleClock;
            if (clock == null) return;
            _seasonTitleClock = null;
            try
            {
                clock.Cancel();
                clock.Dispose();

                // A cancelled Avalonia animation leaves the property wherever it stopped, so the
                // banner is put back on the static gradient its XAML authored.
                var title = Named<Control>("QuestsTab")?.FindControl<TextBlock>("TxtSeasonTitle");
                if (title?.Foreground is LinearGradientBrush brush)
                {
                    brush.StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative);
                    brush.EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative);
                }
                if (title?.Effect is DropShadowEffect glow) glow.Opacity = 0.6;
            }
            catch (Exception ex) { Log.Debug("StopSeasonTitleShimmer: {E}", ex.Message); }
        }

        // ---- lockdown plate --------------------------------------------------------

        private void StartLockdownPulse()
        {
            if (_lockdownPulseClock != null) return;                 // already running
            try
            {
                var plate = Named<Control>("LockdownTab")?.FindControl<Border>("LockdownImageBorder");
                if (plate == null) return;

                // See the header: the XAML brush is immutable, so the loop needs one of its own.
                _lockdownRestingBrush ??= plate.BorderBrush;
                var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x14, 0x93));
                plate.BorderBrush = brush;

                _lockdownPulseClock = new CancellationTokenSource();
                var token = _lockdownPulseClock.Token;

                var colour = new Animation
                {
                    Duration = TimeSpan.FromSeconds(1.5),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(SolidColorBrush.ColorProperty, Color.FromRgb(0xFF, 0x14, 0x93)) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(SolidColorBrush.ColorProperty, Color.FromRgb(0xFF, 0x69, 0xB4)) } },
                    },
                };
                _ = colour.RunAsync(brush, token);

                if (plate.Effect is DropShadowEffect glow)
                {
                    var breath = new Animation
                    {
                        Duration = TimeSpan.FromSeconds(1.5),
                        IterationCount = IterationCount.Infinite,
                        PlaybackDirection = PlaybackDirection.Alternate,
                        Children =
                        {
                            new KeyFrame
                            {
                                Cue = new Cue(0d),
                                Setters =
                                {
                                    new Setter(DropShadowEffect.BlurRadiusProperty, 12d),
                                    new Setter(DropShadowEffect.OpacityProperty, 0.7),
                                },
                            },
                            new KeyFrame
                            {
                                Cue = new Cue(1d),
                                Setters =
                                {
                                    new Setter(DropShadowEffect.BlurRadiusProperty, 22d),
                                    new Setter(DropShadowEffect.OpacityProperty, 1.0),
                                },
                            },
                        },
                    };
                    _ = breath.RunAsync(glow, token);
                }
            }
            catch (Exception ex) { Log.Warning("Failed to start lockdown pulse: {Error}", ex.Message); }
        }

        private void StopLockdownPulse()
        {
            var clock = _lockdownPulseClock;
            if (clock == null) return;
            _lockdownPulseClock = null;
            try
            {
                clock.Cancel();
                clock.Dispose();

                var plate = Named<Control>("LockdownTab")?.FindControl<Border>("LockdownImageBorder");
                if (plate == null) return;
                if (_lockdownRestingBrush != null) plate.BorderBrush = _lockdownRestingBrush;
                if (plate.Effect is DropShadowEffect glow) { glow.BlurRadius = 14; glow.Opacity = 0.7; }
            }
            catch (Exception ex) { Log.Debug("StopLockdownPulse: {E}", ex.Message); }
        }
    }
}
