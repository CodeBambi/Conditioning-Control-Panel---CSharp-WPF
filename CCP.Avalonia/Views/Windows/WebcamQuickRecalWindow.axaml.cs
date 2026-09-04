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
    ///
    /// <para><b>NO OPENER, DELIBERATELY.</b> All three WPF call sites
    /// (MainWindow.LabTab.cs:1116, MainWindow.BlinkTrainer.cs:1427, MainWindow.xaml.cs:1462) first
    /// require the tracking service to be RUNNING — each starts it itself and refuses if the start
    /// fails — and the Lab one additionally refuses when <c>svc.Calibration == null</c>, because
    /// Quick Recal only nudges an existing calibration. Two of the three then READ THE RESULT: the
    /// Lab handler reports the applied offset and the hotkey path logs applied-vs-cancelled. On this head
    /// the sampling sequence is gone with WebcamTrackingService, so the window would show its dot,
    /// count nothing, and close having "recalibrated" a calibration that does not exist. A control
    /// that reports a recal it never performed is worse than a door that is shut, so the door stays
    /// shut; the Avalonia call sites (MainShellWindow.LabTab.cs's dropped
    /// BtnWebcamDebugQuickRecal_Click, DeeperTabView.BtnDeeperWebcamQuickRecal_Click) stay stubs.
    /// </para>
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
            // The tip is HIDDEN on this head rather than worded, and the old note named the wrong
            // blocker. MainWindow.QuickRecalHotkeyHint (MainWindow/MainWindow.xaml.cs:1263) is just
            // Loc.GetF("webcam_quick_recal_hotkey_hint", QuickRecalHotkeyChord, CameraShortcutChord),
            // the key is in CCP.Core/Localization/Languages/*.json, and both chords are derivable
            // here (the quick-recal one is a constant, the camera one reads
            // CoreSettings.Current.CompanionPrompt.CameraShortcut*). So it is writable today.
            // What stops it is that BOTH chords are Win32 RegisterHotKey registrations in
            // ConditioningControlPanel/Services/Input/GlobalHotkeyService.cs, and this head installs no
            // global hotkey of any kind - "runs from anywhere with Ctrl+Alt+G" would be teaching a
            // key that does nothing. An empty hint is a gap; a taught dead key is a lie.
            _txtHotkeyHint.IsVisible = false;

            // WPF closed with `DialogResult = _completedOk`, but the error panel is only ever
            // shown on a failure path, so that flag was false every time this button was
            // reachable. It comes back with the sampling sequence.
            this.FindControl<Button>("BtnErrorClose")!.Click += (_, _) => Close(false);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);

            // ponytail: needs ConditioningControlPanel/Services/Webcam/WebcamTrackingService.cs
            // (IsRunning / Calibration / OnGazeMove / SetRuntimeOffset) plus
            // ConditioningControlPanel/Services/CalibrationSoundService.cs. Neither is in Core and
            // no Core seam names a tracker. With those back this shows the dot,
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
