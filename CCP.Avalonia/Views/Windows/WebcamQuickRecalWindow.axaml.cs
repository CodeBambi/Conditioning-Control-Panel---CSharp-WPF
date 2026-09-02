using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// One-dot quick recal: shows a center pink dot, samples ~2 s of gaze projections while the
    /// user stares at it, computes the mean drift from screen center and persists it as the
    /// webcam calibration's runtime offset. At runtime the offset is added after the polynomial
    /// projection so the cursor lands where the user is actually looking - no full 16-point
    /// recalibration needed.
    ///
    /// PORTED from ConditioningControlPanel/Windows/WebcamQuickRecalWindow.xaml.cs. Deviations:
    ///  - The whole sampling sequence is gone. It only exists to talk to WebcamTrackingService
    ///    (OnGazeMove / SetRuntimeOffset / Calibration) and CalibrationSoundService, all still in
    ///    the WPF head, so there is nothing left for the median math or the offset write to act
    ///    on. <see cref="OnOpened"/> shows the dot and the opening status line instead, which is
    ///    the state the sequence starts in.
    ///  - WPF's <c>DialogResult = x; Close();</c> becomes <c>Close(x)</c>: Avalonia carries the
    ///    result through <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - Loaded -> Opened, and the KeyDown / Click handlers are wired in the constructor rather
    ///    than in markup, per the porting convention.
    /// </summary>
    public partial class WebcamQuickRecalWindow : Window
    {
        private readonly Ellipse _dot;
        private readonly TextBlock _txtStatus, _txtHotkeyHint, _txtErrorDetail;
        private readonly Border _errorPanel;

        public WebcamQuickRecalWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _dot = this.FindControl<Ellipse>("Dot")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _txtHotkeyHint = this.FindControl<TextBlock>("TxtHotkeyHint")!;
            _txtErrorDetail = this.FindControl<TextBlock>("TxtErrorDetail")!;
            _errorPanel = this.FindControl<Border>("ErrorPanel")!;

            // Discoverability: this window is the one place every user of Quick Recal provably
            // reaches, so it is where the global chord gets taught. The real line quotes the
            // user's own (rebindable) camera shortcut alongside it.
            // ponytail: needs MainWindow.QuickRecalHotkeyHint, wired when the hotkey map moves to Core
            _txtHotkeyHint.Text = "Tip: the same quick recal runs from anywhere with the global quick-recal chord.";

            // WPF closed with `DialogResult = _completedOk`, but the error panel is only ever
            // shown on a failure path, so that flag was false every time this button was
            // reachable. It comes back with the sampling sequence.
            this.FindControl<Button>("BtnErrorClose")!.Click += (_, _) => Close(false);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);

            // ponytail: needs WebcamTrackingService (IsRunning / Calibration / OnGazeMove /
            // SetRuntimeOffset) + CalibrationSoundService. With those back this shows the dot,
            // samples for 2 s, takes the per-axis median after dropping the saccade onto the
            // dot, and writes (window centre - median) as the runtime offset. Without them
            // there is nothing to sample, so the window just parks in its opening state.
            _dot.IsVisible = true;
            _txtStatus.Text = "Get comfortable, then look at the pink dot.";
        }

        /// <summary>The WPF original's error path: no tracking, or no calibration to nudge.</summary>
        private void ShowError(string detail)
        {
            _dot.IsVisible = false;
            _txtStatus.IsVisible = false;
            _txtErrorDetail.Text = detail;
            _errorPanel.IsVisible = true;
        }
    }
}
