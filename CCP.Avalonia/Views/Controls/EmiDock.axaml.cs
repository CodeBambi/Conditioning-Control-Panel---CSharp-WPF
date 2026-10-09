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
    ///  - <c>App.EmiDesk</c> is the head's <c>EmiDeskService.Instance</c> (Toggle, OutChanged,
    ///    AvatarMuted). The mini face mirrors the widget's <c>FaceChanged</c> feed while she is out
    ///    (WPF binds EmiFace.Face), as text: EmiFace's pixel glyphs are not ported on either surface.
    ///    KnockRequested is not ported yet (ponytail: needs EmiKnockMachine/EmiKnockWorld from
    ///    ConditioningControlPanel/Services/EmiDesk/EmiKnock.cs + TryKnock), so <see cref="StartKnock"/>
    ///    stays public and nothing calls it.
    ///  - The four WPF keyframe timelines become one Avalonia <see cref="Animation"/> on the ring
    ///    (stroke colour, thickness) plus one on its glow. Both run three times and stop.
    ///  - The frozen-brush guard is gone: Avalonia brushes do not freeze.
    /// </summary>
    public partial class EmiDock : UserControl
    {
        private readonly Button _btnChip;
        private readonly Ellipse _ring;
        private readonly TextBlock _txtMuted;
        private readonly TextBlock _miniFace;

        /// <summary>The widget whose face the mini face is mirroring, or null while it rests.</summary>
        private Windows.EmiDesk.EmiDeskWindow? _faceSource;

        /// <summary>True only while the six seconds of pulses are running.</summary>
        private bool _knocking;
        private CancellationTokenSource? _knock;

        public EmiDock()
        {
            AvaloniaXamlLoader.Load(this);
            _btnChip = this.FindControl<Button>("BtnChip")!;
            _ring = this.FindControl<Ellipse>("Ring")!;
            _txtMuted = this.FindControl<TextBlock>("TxtMuted")!;
            _miniFace = this.FindControl<TextBlock>("MiniFace")!;

            _btnChip.Click += OnChipClick;
            // WPF EmiDock.xaml.cs:57-107: follow the service while loaded, let go when unloaded.
            var svc = Windows.EmiDesk.EmiDeskService.Instance;
            Loaded += (_, _) => { svc.OutChanged += OnOutChanged; Refresh(svc.IsOut, svc.AvatarMuted); };
            Unloaded += (_, _) => { svc.OutChanged -= OnOutChanged; MirrorFace(null); StopKnock(); };
        }

        private void OnOutChanged(object? sender, bool isOut) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Refresh(isOut, Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted));

        /// <summary>Show or hide the muted pill. The pill states a FACT about right now, so the
        /// host asks the same gate the tube asks: it is never shown just because the setting is on.
        /// The pulses SURVIVE her arrival (WPF :141-147): the knock summons her itself, so stopping
        /// here would kill them in the frame they start. A click still cuts them short.</summary>
        public void Refresh(bool isOut, bool avatarMuted = false)
        {
            // WPF :150-163: point the mini face at the live widget, or let it rest.
            MirrorFace(isOut ? Windows.EmiDesk.EmiDeskService.Instance.Window : null);
            _txtMuted.IsVisible = avatarMuted;
        }

        /// <summary>Follow <paramref name="desk"/>'s face (null: rest). Pushed, never polled, and
        /// let go when she leaves or the rail drops the chip, so a torn-down widget is not kept alive.</summary>
        private void MirrorFace(Windows.EmiDesk.EmiDeskWindow? desk)
        {
            if (!ReferenceEquals(_faceSource, desk))
            {
                if (_faceSource != null) _faceSource.FaceChanged -= OnFaceChanged;
                _faceSource = desk;
                if (desk != null) desk.FaceChanged += OnFaceChanged;
            }
            _miniFace.Text = desk?.Face ?? RestFace;
        }

        private void OnFaceChanged(object? sender, string face) => _miniFace.Text = face;

        /// <summary>EmiChains.RestFace.</summary>
        private const string RestFace = "0_0";

        private void OnChipClick(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            StopKnock();
            Windows.EmiDesk.EmiDeskService.Instance.Toggle();
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
