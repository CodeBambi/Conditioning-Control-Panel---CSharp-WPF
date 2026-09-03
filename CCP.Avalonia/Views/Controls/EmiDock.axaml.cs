using System;
using System.Threading;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// THE DOCK CHIP: a 40 px pink ring at the bottom of the nav rail with EMI's live face in it.
    /// Click summons her, click again sends her away.
    ///
    /// PORTED from ConditioningControlPanel/Controls/EmiDock.xaml.cs. Deviations:
    ///  - <c>App.EmiDesk</c> (OutChanged, KnockRequested, Toggle, the live face binding and the
    ///    muted flag) is a WPF-head service. <see cref="Refresh"/> and <see cref="StartKnock"/> are
    ///    public so a host can drive the chip until the service moves to Core.
    ///  - The four WPF keyframe timelines become one Avalonia <see cref="Animation"/> on the ring
    ///    (stroke colour, thickness) plus one on its glow. Both run three times and stop.
    ///  - The frozen-brush guard is gone: Avalonia brushes do not freeze.
    /// </summary>
    public partial class EmiDock : UserControl
    {
        private readonly Button _btnChip;
        private readonly Ellipse _ring;
        private readonly TextBlock _txtMuted;

        /// <summary>True only while the six seconds of pulses are running.</summary>
        private bool _knocking;
        private CancellationTokenSource? _knock;

        public EmiDock()
        {
            AvaloniaXamlLoader.Load(this);
            _btnChip = this.FindControl<Button>("BtnChip")!;
            _ring = this.FindControl<Ellipse>("Ring")!;
            _txtMuted = this.FindControl<TextBlock>("TxtMuted")!;

            _btnChip.Click += OnChipClick;
            Unloaded += (_, _) => StopKnock();
        }

        /// <summary>Show or hide the muted pill. The pill states a FACT about right now, so the
        /// host asks the same gate the tube asks: it is never shown just because the setting is on.
        /// ANY route out answers the knock, so the pulses stop here too.</summary>
        public void Refresh(bool isOut, bool avatarMuted = false)
        {
            if (isOut) StopKnock();
            // ponytail: needs EmiDeskService for the live face binding; the stub face rests.
            _txtMuted.IsVisible = avatarMuted;
        }

        private void OnChipClick(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            StopKnock();
            // ponytail: needs EmiDeskService.Toggle(), wired when it moves to Core
            Log.Debug("[EmiDesk] dock chip clicked with no service");
        }

        // ============================================================================================
        //  THE KNOCK
        // ============================================================================================

        /// <summary>One pulse: a fast swell and a slow fall. Three of these is the whole knock.</summary>
        private const int PulseMs = 2000;

        /// <summary>Pulses. Three reads as deliberate; more reads as a notification badge.</summary>
        private const int PulseCount = 3;

        /// <summary>Her pink at rest.</summary>
        private static readonly Color RestPink = Color.FromRgb(0xFF, 0x69, 0xB4);

        /// <summary>...and the brighter pink each swell reaches.</summary>
        private static readonly Color HotPink = Color.FromRgb(0xFF, 0xC4, 0xE8);

        /// <summary>The ring's resting stroke, restored by hand when the pulses are taken off.</summary>
        private const double RestThickness = 2.0;

        /// <summary>
        /// Three pink pulses over about six seconds, and then quiet forever. The ring, never the
        /// face. Nothing starts unless the chip is really loaded into a window.
        /// </summary>
        public void StartKnock()
        {
            try
            {
                if (_knocking) return;
                if (!IsLoaded) return;
                if (_ring.Effect is not DropShadowEffect glow) return;

                _knocking = true;
                _knock = new CancellationTokenSource();
                var span = TimeSpan.FromMilliseconds(PulseMs);
                var repeat = new IterationCount(PulseCount);

                // The swell is fast and the fall is slow: a pulse that decays reads as a knock, a
                // pulse that is symmetrical reads as a warning light.
                var ring = new Animation { Duration = span, IterationCount = repeat };
                ring.Children.Add(Frame(0.00, new Setter(Shape.StrokeProperty, new SolidColorBrush(RestPink)), new Setter(Shape.StrokeThicknessProperty, RestThickness)));
                ring.Children.Add(Frame(0.18, new Setter(Shape.StrokeProperty, new SolidColorBrush(HotPink)), new Setter(Shape.StrokeThicknessProperty, 3.2)));
                ring.Children.Add(Frame(0.55, new Setter(Shape.StrokeProperty, new SolidColorBrush(RestPink)), new Setter(Shape.StrokeThicknessProperty, RestThickness)));
                ring.Children.Add(Frame(1.00, new Setter(Shape.StrokeProperty, new SolidColorBrush(RestPink)), new Setter(Shape.StrokeThicknessProperty, RestThickness)));

                var glowAnim = new Animation { Duration = span, IterationCount = repeat };
                glowAnim.Children.Add(Frame(0.00, new Setter(DropShadowEffect.OpacityProperty, 0.0), new Setter(DropShadowEffect.BlurRadiusProperty, 0.0)));
                glowAnim.Children.Add(Frame(0.18, new Setter(DropShadowEffect.OpacityProperty, 0.95), new Setter(DropShadowEffect.BlurRadiusProperty, 16.0)));
                glowAnim.Children.Add(Frame(0.55, new Setter(DropShadowEffect.OpacityProperty, 0.0), new Setter(DropShadowEffect.BlurRadiusProperty, 0.0)));
                glowAnim.Children.Add(Frame(1.00, new Setter(DropShadowEffect.OpacityProperty, 0.0), new Setter(DropShadowEffect.BlurRadiusProperty, 0.0)));

                // ONE of the two carries the tidy-up: its task completes once the whole repeat
                // count is spent, and StopKnock is idempotent, so a click landing mid-pulse and
                // the natural end both arrive at the same place.
                var token = _knock.Token;
                _ = glowAnim.RunAsync(glow, token);
                ring.RunAsync(_ring, token).ContinueWith(_ => global::Avalonia.Threading.Dispatcher.UIThread.Post(StopKnock));

                Log.Information("[EmiDesk] the dock chip is knocking");
            }
            catch (Exception ex)
            {
                _knocking = false;
                Log.Warning(ex, "[EmiDesk] dock chip knock failed to start");
            }
        }

        private static KeyFrame Frame(double cue, params Setter[] setters)
        {
            var f = new KeyFrame { Cue = new Cue(cue) };
            foreach (var s in setters) f.Setters.Add(s);
            return f;
        }

        /// <summary>Put the ring back exactly as it was, and never knock again. Idempotent.</summary>
        private void StopKnock()
        {
            try
            {
                if (!_knocking) return;
                _knocking = false;

                _knock?.Cancel();
                _knock?.Dispose();
                _knock = null;

                _ring.Stroke = new SolidColorBrush(RestPink);
                _ring.StrokeThickness = RestThickness;
                if (_ring.Effect is DropShadowEffect glow)
                {
                    glow.Opacity = 0;
                    glow.BlurRadius = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] dock chip knock stop failed");
            }
        }
    }
}
