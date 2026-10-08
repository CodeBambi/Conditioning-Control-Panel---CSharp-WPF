using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// Settings door · DEVICES, ported from the WPF head with its settings logic restored against
    /// <see cref="CoreSettings"/>.
    ///
    /// <para>The WPF file is two kinds of handler: hops into a MainWindow partial, and the mic
    /// block ported wholesale from the retired WebcamFeatureControl popup. Every hop whose
    /// MainWindow body is a pure settings write happens here directly (the two precision sliders,
    /// headphones mode, the blink-recal shortcut, drift correction, gaze restriction, the panic
    /// override master and the panic-key disable). Every hop that needs a device, a global keyboard
    /// hook or a Win32 window is named in a <c>ponytail:</c> note where it sits.</para>
    ///
    /// <para><b>_loading starts true on purpose.</b> A Slider raises ValueChanged during
    /// InitializeComponent - <c>Minimum="0.3"</c> coerces the default 0 up to 0.3 - before settings
    /// are read. Without the guard, opening the app would silently write 0.3 over the user's wake
    /// threshold. Same trap the popup had, and Avalonia raises the event exactly as WPF did.</para>
    /// </summary>
    public partial class DevicesSettingsSection : UserControl
    {
        private bool _loading = true;
        private bool _micPopulating;   // Items.Clear()/SelectedItem raise SelectionChanged

        public DevicesSettingsSection()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and the seed below reads them.
            InitializeComponent();

            SliderWakePrecision.ValueChanged += SliderWakePrecision_ValueChanged;
            SliderCmdPrecision.ValueChanged += SliderCmdPrecision_ValueChanged;
            ChkHeadphones.IsCheckedChanged += ChkHeadphones_Changed;
            ChkBlinkRecalWebcamBar.IsCheckedChanged += ChkBlinkRecalShortcut_Changed;
            ChkWebcamDriftCorrection.IsCheckedChanged += ChkWebcamDriftCorrection_Changed;
            ChkRestrictGazeToCalScreen.IsCheckedChanged += ChkRestrictGazeToCalScreen_Changed;
            ChkPanicOverridesAll.IsCheckedChanged += ChkPanicOverridesAll_Changed;
            ChkNoPanic.IsCheckedChanged += ChkNoPanic_Changed;
            TxtSpeechWakeWords.LostFocus += TxtSpeechWakeWords_LostFocus;
            CmbMicDevice.SelectionChanged += CmbMicDevice_SelectionChanged;
            BtnMicRefresh.Click += BtnMicRefresh_Click;
            BtnChatShortcutDevices.Click += BtnChatShortcut_Click;
            BtnPanicKey.Click += BtnPanicKey_Click;
            BtnWebcamRevokeConsent.Click += BtnWebcamRevokeConsent_Click;
            BtnWebcamReviewPrivacy.Click += BtnWebcamReviewPrivacy_Click;
            BtnWebcamDebugStart.Click += BtnWebcamDebugStart_Click;
            BtnWebcamDebugQuickRecal.Click += BtnWebcamDebugQuickRecal_Click;
            BtnWebcamDebugCalibrate.Click += BtnWebcamDebugCalibrate_Click;
            BtnWebcamDebugTrackerTest.Click += BtnWebcamDebugTrackerTest_Click;
            CmbWebcamDevice.SelectionChanged += CmbWebcamDevice_SelectionChanged;
            BtnWebcamDeviceRefresh.Click += BtnWebcamDeviceRefresh_Click;

            SyncFromSettings();
            PopulateMicDevices();
            RefreshWebcamAvailability();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            // The WPF section implements IAppSettingsSection.OnSectionShown because device lists go
            // stale the moment someone plugs a headset in. Attach is this head's equivalent reveal
            // hook, so the mic list is re-enumerated here too.
            SyncFromSettings();
            PopulateMicDevices();
            PopulateWebcamDevices();   // WPF OnSectionShown -> RefreshDeviceSettingsLists
            _lockdown = LockdownService.Current;
            if (_lockdown != null) { _lockdown.LockdownActivated += OnLockdownChanged; _lockdown.LockdownDeactivated += OnLockdownChanged; }
            ApplyLockdownHold();
        }

        private LockdownService? _lockdown;
        private void OnLockdownChanged() => Dispatcher.UIThread.Post(ApplyLockdownHold);

        private static bool NoPanicHeld => Windows.MainShellWindow.LockdownActive && CoreSettings.Current.LockdownDisablePanicKey;

        /// <summary>WPF MainWindow.Lab.cs:659-663 (activate) / 741-745 (deactivate).</summary>
        internal void ApplyLockdownHold() => Windows.MainShellWindow.HoldUnderLockdown(ChkNoPanic, NoPanicHeld);

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_lockdown != null) { _lockdown.LockdownActivated -= OnLockdownChanged; _lockdown.LockdownDeactivated -= OnLockdownChanged; _lockdown = null; }
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        // A cloud restore or a factory reset swaps the instance; repaint from it, on the UI thread.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        // =====================================================================================
        //  seed
        // =====================================================================================

        internal void SyncFromSettings()
        {
            _loading = true;
            try
            {
                var s = CoreSettings.Current;

                // The sliders' labels are set explicitly rather than left to ValueChanged, which
                // _loading blocks.
                SliderWakePrecision.Value = s.SpeechWakeMatchThreshold;
                SliderCmdPrecision.Value = s.SpeechMatchThreshold;
                TxtWakeVal.Text = s.SpeechWakeMatchThreshold.ToString("0.00");
                TxtCmdVal.Text = s.SpeechMatchThreshold.ToString("0.00");
                ChkHeadphones.IsChecked = s.SpeechHeadphonesMode;

                // Voice modes: seeded here because this head has no MainWindow.LoadSettings sweep.
                // Both read the consent as well as the enable - an enable without mic consent is not
                // a mic that is open (MainWindow.Settings.cs:194/196).
                ChkSpeechWakeWord.IsChecked = s.SpeechWakeWordEnabled && s.MicConsentGiven;
                ChkSpeechPushToTalk.IsChecked = s.SpeechPushToTalkEnabled && s.MicConsentGiven;
                TxtSpeechWakeWords.Text = string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords;
                TxtPttKey.Text = string.IsNullOrWhiteSpace(s.SpeechPushToTalkKey) ? "F8" : s.SpeechPushToTalkKey;

                // Webcam rows that are pure settings (MainWindow.LabTab.cs:412/414 and the
                // blink-recal seed at :296).
                ChkBlinkRecalWebcamBar.IsChecked = s.BlinkRecalibrateShortcutEnabled;
                ChkWebcamDriftCorrection.IsChecked = s.WebcamAutoDriftCorrection;
                ChkRestrictGazeToCalScreen.IsChecked = s.RestrictGazeContentToCalibratedScreen;

                // Safety (MainWindow.Settings.cs:77/78). ChkNoPanic is the INVERSE of the enable.
                ChkNoPanic.IsChecked = !s.PanicKeyEnabled;
                ChkPanicOverridesAll.IsChecked = s.PanicOverridesAll;
                SetButtonLabel(BtnPanicKey, $"🔑 {s.PanicKey}");
                SetButtonLabel(BtnPauseKey, string.IsNullOrEmpty(s.PauseKey)
                    ? Loc.Get("btn_pause_key_unbound")
                    : $"⏸ {s.PauseKey}");

                RefreshChatShortcutLabel();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Settings/Devices: section load failed");
            }
            finally { _loading = false; }
        }

        /// <summary>Buttons here carry a TextBlock child so Avalonia does not eat the underscore in
        /// a snake_case string; replacing that child keeps the opt-out.</summary>
        private static void SetButtonLabel(Button button, string text) =>
            button.Content = new TextBlock { Text = text };

        // =====================================================================================
        //  microphone - ported wholesale from the retired WebcamFeatureControl popup
        // =====================================================================================

        private void SliderWakePrecision_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            var s = CoreSettings.Current;
            s.SpeechWakeMatchThreshold = e.NewValue;        // clamped in the setter
            TxtWakeVal.Text = s.SpeechWakeMatchThreshold.ToString("0.00");
            CoreSettings.Save();
        }

        private void SliderCmdPrecision_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            var s = CoreSettings.Current;
            s.SpeechMatchThreshold = e.NewValue;            // clamped in the setter
            TxtCmdVal.Text = s.SpeechMatchThreshold.ToString("0.00");
            CoreSettings.Save();
        }

        private void ChkHeadphones_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CoreSettings.Current.SpeechHeadphonesMode = ChkHeadphones.IsChecked == true;
            CoreSettings.Save();
            // ponytail: WPF also re-quotes the device on the She's Listening chip
            // (MainWindow.RefreshSheListeningDeviceChips); no such host on this head.
        }

        /// <summary>
        /// The WPF PopulateMicDevices, against <see cref="CoreSpeech"/> instead of SpeechService.
        /// An empty enumeration means no head has seeded the seam, and clearing on that would
        /// leave a blank ComboBox where the XAML's "System default" placeholder sits - so the
        /// list is only replaced when there is a real one to replace it with.
        /// </summary>
        private void PopulateMicDevices()
        {
            var devices = CoreSpeech.EnumerateInputDevices();
            if (devices.Count == 0) return;   // no speech head attached; keep the placeholder

            int saved = CoreSettings.Current.SpeechInputDeviceIndex;
            _micPopulating = true;   // Clear() raises SelectionChanged here as it does on WPF
            try
            {
                CmbMicDevice.Items.Clear();
                ComboBoxItem? toSelect = null;
                foreach (var dev in devices)
                {
                    var item = new ComboBoxItem { Content = dev.Name, Tag = dev.Index };
                    CmbMicDevice.Items.Add(item);
                    if (dev.Index == saved) toSelect = item;
                }
                // Fall back to the first entry (the OS default) if the saved device is gone.
                CmbMicDevice.SelectedItem = toSelect ?? (CmbMicDevice.Items.Count > 0 ? CmbMicDevice.Items[0] : null);
            }
            finally { _micPopulating = false; }
        }

        private void CmbMicDevice_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_micPopulating || _loading) return;
            if (CmbMicDevice.SelectedItem is not ComboBoxItem item || item.Tag is not int idx) return;

            var s = CoreSettings.Current;
            var name = idx < 0 ? "" : (item.Content?.ToString() ?? "");
            if (s.SpeechInputDeviceIndex == idx && s.SpeechInputDeviceName == name) return;
            s.SpeechInputDeviceIndex = idx;
            s.SpeechInputDeviceName = name; // matched by name on reopen - robust to ordinal reshuffle (#441b)
            CoreSettings.Save();
            // ponytail: WPF also cuts the open capture so the wake loop reopens on the new device
            // (App.Speech.StopListening + App.Autonomy.RefreshVoiceInputModes) and re-quotes the
            // device on the She's Listening chip. The seam carries capability only, and neither
            // the autonomy service nor that chip exists on this head.
        }

        private void BtnMicRefresh_Click(object? sender, RoutedEventArgs e) => PopulateMicDevices();

        // =====================================================================================
        //  webcam
        // =====================================================================================

        /// <summary>
        /// The engine bar, gated on <see cref="CoreWebcam.IsAvailable"/> (seeded true on this head by
        /// Platform/WebcamTracker). Privacy info, Start tracking and Revoke are live.
        ///
        /// Quick Recal and Tracker Test are live over the tracker's gaze feed.
        /// The camera picker is live over <see cref="Platform.V4l2Cameras"/>.
        /// <para>ponytail: the monitor combo and the debug cursor stay DISABLED with a
        /// stated reason: calibration follows its own window's screen and the cursor overlay is not ported. The status pill stays
        /// at its <c>rf_webcam_stopped</c> literal for the same reason (no OnTrackingStateChanged).</para>
        /// </summary>
        private void RefreshWebcamAvailability()
        {
            bool has = CoreWebcam.IsAvailable;
            BtnWebcamReviewPrivacy.IsEnabled = has;
            BtnWebcamDebugStart.IsEnabled = has;
            BtnWebcamRevokeConsent.IsEnabled = has;
            BtnWebcamDebugQuickRecal.IsEnabled = has;
            BtnWebcamDebugTrackerTest.IsEnabled = has;
            BtnWebcamDebugCalibrate.IsEnabled = has;
            CmbWebcamDevice.IsEnabled = has;
            BtnWebcamDeviceRefresh.IsEnabled = has;
            foreach (var c in new Control[] { CmbWebcamMonitor, ChkWebcamDebugCursor })
            {
                c.IsEnabled = false;   // IsEnabled only - never IsChecked, which would fire the handler
                ToolTip.SetShowOnDisabled(c, true);
                ToolTip.SetTip(c, "Not available on this build yet (needs monitor selection or the gaze cursor overlay).");
            }
            if (!has)
                AppendWebcamDebugLog("No webcam tracking engine on this build — camera controls are unavailable.");
            RefreshWebcamStartLabel();
        }

        private bool _webcamDevicePopulating;   // Items.Clear()/SelectedItem raise SelectionChanged

        /// <summary>WPF PopulateWebcamCombo (MainWindow.LabTab.cs:1372), over sysfs instead of DirectShow.
        /// Selection matches the saved /dev/videoN number, since V4L2 numbers are not contiguous.</summary>
        private int PopulateWebcamDevices()
        {
            var devices = Platform.V4l2Cameras.Enumerate();
            _webcamDevicePopulating = true;
            try
            {
                CmbWebcamDevice.Items.Clear();
                if (devices.Count == 0)
                {
                    CmbWebcamDevice.Items.Add(new ComboBoxItem { Content = "(no cameras detected)", Tag = -1, IsEnabled = false });
                    CmbWebcamDevice.SelectedIndex = 0;
                    return 0;
                }
                int saved = CoreSettings.Current.WebcamDeviceIndex, target = 0;
                for (int i = 0; i < devices.Count; i++)
                {
                    CmbWebcamDevice.Items.Add(new ComboBoxItem { Content = $"[{devices[i].Index}] {devices[i].Name}", Tag = devices[i].Index });
                    if (devices[i].Index == saved) target = i;
                }
                CmbWebcamDevice.SelectedIndex = target;
                return devices.Count;
            }
            finally { _webcamDevicePopulating = false; }
        }

        /// <summary>WPF CmbWebcamDevice_SelectionChanged (MainWindow.LabTab.cs:1410), log line verbatim.</summary>
        private void CmbWebcamDevice_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_webcamDevicePopulating) return;
            if (CmbWebcamDevice.SelectedItem is not ComboBoxItem item || item.Tag is not int idx || idx < 0) return;
            var s = CoreSettings.Current;
            if (s.WebcamDeviceIndex == idx) return;
            s.WebcamDeviceIndex = idx;
            s.WebcamDeviceName = item.Content?.ToString() ?? "";
            CoreSettings.Save();
            AppendWebcamDebugLog($"Camera set to {item.Content}. {(Platform.WebcamTracker.Instance.IsRunning ? "Stop and Start tracking to apply." : "Will be used on next Start.")}");
        }

        /// <summary>WPF BtnWebcamDeviceRefresh_Click (MainWindow.LabTab.cs:1427): counts real devices, not items (#291).</summary>
        private void BtnWebcamDeviceRefresh_Click(object? sender, RoutedEventArgs e)
        {
            int found = PopulateWebcamDevices();
            AppendWebcamDebugLog(found == 0 ? "Re-scanned cameras: none detected." : $"Re-scanned cameras: {found} found.");
        }

        private void RefreshWebcamStartLabel()
            => BtnWebcamDebugStart.Content = Platform.WebcamTracker.Instance.IsRunning ? "Stop tracking" : "Start tracking";

        /// <summary>WPF BtnWebcamReviewPrivacy_Click: the consent dialog as review; Cancel changes nothing.</summary>
        private async void BtnWebcamReviewPrivacy_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                await new WebcamConsentDialog().ShowDialogSafe(owner);
                AppendWebcamDebugLog("Privacy info reviewed.");
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam review privacy dialog failed"); }
        }

        /// <summary>WPF BtnWebcamDebugStart_Click (MainWindow.LabTab.cs:495): stop if running; else
        /// consent when stale, then start off the UI thread and log the outcome.</summary>
        private async void BtnWebcamDebugStart_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var tracker = Platform.WebcamTracker.Instance;
                if (tracker.IsRunning)
                {
                    AppendWebcamDebugLog("Stop requested.");
                    await tracker.StopAsync();
                    RefreshWebcamStartLabel();
                    return;
                }
                if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    if (TopLevel.GetTopLevel(this) is not Window owner) return;
                    AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                    await new WebcamConsentDialog().ShowDialogSafe(owner);
                    if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current)) { AppendWebcamDebugLog("Consent declined or dialog cancelled."); return; }
                    AppendWebcamDebugLog("Consent granted.");
                }
                AppendWebcamDebugLog("Starting webcam (camera open + model load can take a few seconds)…");
                BtnWebcamDebugStart.IsEnabled = false;
                bool started = await tracker.StartAsync();
                BtnWebcamDebugStart.IsEnabled = true;
                RefreshWebcamStartLabel();
                AppendWebcamDebugLog(started ? "Start() returned true — capture thread launching." : $"Start() returned false. {tracker.LastError}");
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam debug start failed"); }
        }

        /// <summary>WPF BtnWebcamDebugTrackerTest_Click (MainWindow.LabTab.cs:1094).</summary>
        private async void BtnWebcamDebugTrackerTest_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await RunGazeWindowAsync(new WebcamGazeTrackerWindow(), "No calibration loaded — run Calibrate (16-point) first.",
                    "Opening tracker test window…", _ => "Tracker test closed.");
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam tracker test failed"); }
        }

        /// <summary>WPF BtnWebcamDebugCalibrate_Click (MainWindow.LabTab.cs:754): consent when stale,
        /// start tracking if off (left running, as WPF), then the 16-point window.</summary>
        private async void BtnWebcamDebugCalibrate_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                var tracker = Platform.WebcamTracker.Instance;
                if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                    await new WebcamConsentDialog().ShowDialogSafe(owner);
                    if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current)) { AppendWebcamDebugLog("Consent declined."); return; }
                }
                if (!tracker.IsRunning)
                {
                    if (!await tracker.StartAsync()) { AppendWebcamDebugLog($"Couldn't start tracking. {tracker.LastError}"); return; }
                    RefreshWebcamStartLabel();
                }
                AppendWebcamDebugLog("Opening calibration window…");
                var result = await WebcamCalibrationWindow.ShowDialogWithRecalibrate(owner);
                AppendWebcamDebugLog(result == true ? "Calibration applied. Gaze classification should now be much more accurate." : "Calibration cancelled or failed.");
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam calibrate failed"); }
        }

        /// <summary>WPF BtnWebcamDebugQuickRecal_Click (MainWindow.LabTab.cs:1153).</summary>
        private async void BtnWebcamDebugQuickRecal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await RunGazeWindowAsync(new WebcamQuickRecalWindow(),
                    "No calibration loaded — run Calibrate (16-point) first. Quick Recal only nudges an existing calibration.",
                    "Opening quick-recal window…", ok =>
                    {
                        var off = Platform.WebcamTracker.Instance.Calibration?.RuntimeOffset;
                        return ok == true ? $"Quick recal applied (offset {off?.Dx:F0}, {off?.Dy:F0} px)." : "Quick recal cancelled.";
                    });
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam quick recal failed"); }
        }

        /// <summary>The shared WPF shape of both handlers: consent when stale, start tracking if it is
        /// off (and stop it again afterwards only then), refuse without a calibration, run the dialog.</summary>
        private async System.Threading.Tasks.Task RunGazeWindowAsync(Window dialog, string noCalibration, string opening, Func<bool?, string> closed)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var tracker = Platform.WebcamTracker.Instance;
            if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current))
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                await new WebcamConsentDialog().ShowDialogSafe(owner);
                if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current)) { AppendWebcamDebugLog("Consent declined."); return; }
            }
            bool startedHere = false;
            if (!tracker.IsRunning)
            {
                if (!await tracker.StartAsync()) { AppendWebcamDebugLog($"Couldn't start tracking. {tracker.LastError}"); return; }
                startedHere = true;
                RefreshWebcamStartLabel();
            }
            if (tracker.Calibration == null)
            {
                AppendWebcamDebugLog(noCalibration);
                if (startedHere) { await tracker.StopAsync(); RefreshWebcamStartLabel(); }
                return;
            }
            AppendWebcamDebugLog(opening);
            var result = await dialog.ShowDialogSafe<bool?>(owner);
            AppendWebcamDebugLog(closed(result));
            if (startedHere) { await tracker.StopAsync(); RefreshWebcamStartLabel(); }
        }

        /// <summary>
        /// MainWindow.LabTab.cs:1159, with the WPF <c>MessageBox.Show(..., OKCancel, Warning,
        /// MessageBoxResult.Cancel)</c> as <see cref="MessageDialog.ConfirmAsync"/> with
        /// <c>defaultToCancel</c> — same wording, same safe default.
        ///
        /// <para><b>Awaited, and the revoke happens strictly after the answer.</b> Avalonia's
        /// ShowDialog is async; revoking first and asking second would make the confirmation
        /// decorative on the one control in this section that deletes user data.</para>
        ///
        /// <para>Gated on <see cref="CoreWebcam.IsAvailable"/> a second time even though the button
        /// is disabled without it: <see cref="CoreWebcam.RevokeConsent"/> is a no-op unseeded, so
        /// running the rest would print "consent revoked" over a consent record still on disk.
        /// Clearing <c>WebcamConsentGiven</c> here instead is exactly the half-measure the old note
        /// refused — it would keep three of the dialog's four promises and claim all four.</para>
        ///
        /// <para>WPF also refreshes three blink-trainer rows; here the Blink Trainer page re-reads
        /// consent whenever it is shown, and the debug cursor does not exist on this head.</para>
        /// </summary>
        private async void BtnWebcamRevokeConsent_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!CoreWebcam.IsAvailable) return;
                if (TopLevel.GetTopLevel(this) is not Window owner || !owner.IsVisible) return;

                bool ok = await MessageDialog.ConfirmAsync(owner, "Revoke webcam consent",
                    "Revoke webcam consent?\n\n" +
                    "This will:\n" +
                    "  • Stop webcam tracking immediately\n" +
                    "  • Delete your calibration data\n" +
                    "  • Disable Focus Gaze and any webcam triggers\n" +
                    "  • Clear your consent record\n\n" +
                    "You'll be re-prompted to consent and recalibrate the next time you enable a webcam feature.",
                    defaultToCancel: true);
                if (!ok) return;

                CoreWebcam.RevokeConsent();
                RefreshWebcamStartLabel();
                AppendWebcamDebugLog("Consent revoked. Calibration deleted; webcam features disabled.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Webcam revoke consent failed");
            }
        }

        private void ChkBlinkRecalShortcut_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CoreSettings.Current.BlinkRecalibrateShortcutEnabled = ChkBlinkRecalWebcamBar.IsChecked == true;
            CoreSettings.Save();
        }

        private void ChkWebcamDriftCorrection_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool v = ChkWebcamDriftCorrection.IsChecked == true;
            CoreSettings.Current.WebcamAutoDriftCorrection = v;
            // WPF's handler has no Save because MainWindow's settings sweep picks the flag up
            // later; this head has no sweep, so the write would sit in memory. Same call the
            // Performance section makes for the same reason.
            CoreSettings.Save();
            AppendWebcamDebugLog(v
                ? "Auto drift correction enabled — clicks near your gaze will fine-tune calibration."
                : "Auto drift correction disabled.");
        }

        private void ChkRestrictGazeToCalScreen_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CoreSettings.Current.RestrictGazeContentToCalibratedScreen = ChkRestrictGazeToCalScreen.IsChecked == true;
            CoreSettings.Save();   // no sweep on this head - see ChkWebcamDriftCorrection_Changed
        }

        /// <summary>MainWindow.LabTab.cs:1399, moved to the control that owns the TextBlock.</summary>
        private void AppendWebcamDebugLog(string line)
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            var existing = TxtWebcamDebugLog.Text ?? "";
            if (existing == "(events will appear here)") existing = "";
            var lines = (existing + (existing.Length > 0 ? "\n" : "") + $"[{stamp}] {line}").Split('\n');
            if (lines.Length > 12) lines = lines[(lines.Length - 12)..];
            TxtWebcamDebugLog.Text = string.Join("\n", lines);
        }

        // =====================================================================================
        //  voice modes
        // =====================================================================================

        // ponytail: ChkSpeechWakeWord / ChkSpeechPushToTalk need App.Autonomy.RefreshVoiceInputModes
        // (ConditioningControlPanel/Services/Autonomy/), still in the WPF head. TierGate is not the
        // blocker any more - CCP.Core/Services/TierGate.cs - but the mic half is. They are
        // seeded above and left without a write handler: a toggle that saved the flag but could
        // neither charge the premium bar nor open the mic would be a lie in both directions.
        // BtnSetPttKey likewise needs MainWindow's global-hook key capture.

        private void TxtSpeechWakeWords_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var text = TxtSpeechWakeWords.Text?.Trim();
            CoreSettings.Current.SpeechWakeWords = string.IsNullOrWhiteSpace(text) ? "hey bambi" : text;
            if (string.IsNullOrWhiteSpace(text)) TxtSpeechWakeWords.Text = "hey bambi";
            CoreSettings.Save();
            // ponytail: WPF also restarts the wake loop so new phrases take effect immediately
            // (App.Autonomy.RefreshVoiceInputModes); no speech engine on this head.
        }

        // =====================================================================================
        //  safety
        // =====================================================================================

        /// <summary>
        /// v6.8.5 master: ON (default) = one panic press stops every surface at once; OFF = the
        /// pre-6.8.5 hand-off ladder. No confirmation dialog either way - both settings are safe,
        /// they only differ in how many presses an emergency stop costs.
        /// </summary>
        private void ChkPanicOverridesAll_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = CoreSettings.Current;
            s.PanicOverridesAll = ChkPanicOverridesAll.IsChecked ?? true;
            CoreSettings.Save();
            Log.Information("Panic override mode: {State}", s.PanicOverridesAll ? "stop everything" : "legacy ladder");
        }

        /// <summary>
        /// Disabling the panic key is gated behind the double warning, exactly as on WPF. Async
        /// because Avalonia's ShowDialog has no blocking form; the revert on a decline is posted so
        /// it runs after the dialog's event stack unwinds.
        /// </summary>
        private async void ChkNoPanic_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;

            var isNoPanic = ChkNoPanic.IsChecked ?? false;

            // Greyed under Lockdown (ApplyLockdownHold); belt and braces if a click still lands.
            if (!isNoPanic && NoPanicHeld)
            {
                Dispatcher.UIThread.Post(() => { _loading = true; ChkNoPanic.IsChecked = true; _loading = false; });
                return;
            }

            if (isNoPanic)
            {
                // No window to parent the warning to: the gate cannot be shown, so the answer is
                // no. Revert rather than return - a checked box over PanicKeyEnabled=true would
                // tell the user the escape hatch is off when it is still armed.
                var owner = TopLevel.GetTopLevel(this) as Window;
                if (owner == null) { RevertNoPanic(); return; }

                var confirmed = await WarningDialog.ShowDoubleWarningAsync(owner,
                    "Disable Panic Key",
                    "• You will have NO emergency escape option\n" +
                    "• The ONLY way to exit will be the Exit button\n" +
                    "• Combined with Strict Lock, this is VERY restrictive\n" +
                    "• Make sure you know what you're doing!");

                if (!confirmed) { RevertNoPanic(); return; }

                CoreSettings.Current.PanicKeyEnabled = false;
                CoreSettings.Save();
                Log.Information("Panic key disabled");
            }
            else
            {
                CoreSettings.Current.PanicKeyEnabled = true;
                CoreSettings.Save();
                Log.Information("Panic key enabled");
            }
            // WPF also stops/starts its keyboard hook here; the X11 listener reads PanicKeyEnabled on
            // every press instead (MainShellWindow.PanicKey.cs), so there is nothing to toggle.
        }

        /// <summary>Posted, not assigned inline: the revert has to run after the dialog's event
        /// stack unwinds or the toggle animation sticks in the ON position, exactly as on WPF.</summary>
        private void RevertNoPanic() => Dispatcher.UIThread.Post(() =>
        {
            _loading = true;
            ChkNoPanic.IsChecked = false;
            _loading = false;
        });

        /// <summary>
        /// WPF BtnPanicKey_Click (MainWindow.UiUpdates.cs:2440) + the capture branch of
        /// OnGlobalKeyPressed (MainWindow.xaml.cs:916): no dialog, the button reads "Press any
        /// key..." and the NEXT key, any key, becomes the panic key. WPF takes it from its global
        /// hook; here the window has focus after the click, so a tunnelling KeyDown gets it first.
        /// Capture stays set a beat after the key so the X11 listener's copy of that same press
        /// (queued on the UI thread in either order) is not also a panic.
        /// </summary>
        private void BtnPanicKey_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            MainShellWindow.CapturingPanicKey = true;
            SetButtonLabel(BtnPanicKey, "Press any key...");
            top.AddHandler(KeyDownEvent, OnCaptureKey, RoutingStrategies.Tunnel);
            // WPF's global hook always got the next key; an in-window capture can be abandoned by
            // clicking away, which would leave the panic key disabled for good. Losing the window cancels.
            if (top is Window window) window.Deactivated += OnCancel;

            void Detach()
            {
                top.RemoveHandler(KeyDownEvent, OnCaptureKey);
                if (top is Window w) w.Deactivated -= OnCancel;
            }

            void OnCancel(object? s, EventArgs a)
            {
                Detach();
                MainShellWindow.CapturingPanicKey = false;
                SetButtonLabel(BtnPanicKey, $"🔑 {CoreSettings.Current.PanicKey}");
            }

            void OnCaptureKey(object? s, KeyEventArgs k)
            {
                Detach();
                k.Handled = true;
                CoreSettings.Current.PanicKey = k.Key.ToString();
                CoreSettings.Save();
                SetButtonLabel(BtnPanicKey, $"🔑 {CoreSettings.Current.PanicKey}");
                Log.Information("Panic key changed to: {Key}", k.Key);
                DispatcherTimer.RunOnce(() => MainShellWindow.CapturingPanicKey = false, TimeSpan.FromMilliseconds(300));
            }
        }

        // ponytail: BtnPauseKey still only shows its binding - the pause key parks a video (the #735
        // grace pause), and this head has no grace pause for it to reach yet.

        // =====================================================================================
        //  the chat shortcut (MainWindow.SessionIO.cs BtnChatShortcut_Click / RefreshChatShortcutLabel)
        // =====================================================================================

        /// <summary>
        /// Paints the row's pill with the combo actually stored. The label is a bare literal in the
        /// XAML, not a <c>{loc:Str}</c>, so assigning .Text here is safe - and it has to be code
        /// rather than a binding because the value is composed from two settings strings.
        /// </summary>
        private void RefreshChatShortcutLabel() =>
            TxtChatShortcutLabelDevices.Text = AvatarTubeWindow.FormatChatShortcut();

        /// <summary>
        /// Opens the capture dialog, stores the captured combo and re-applies the binding without a
        /// restart - WPF's BtnChatShortcut_Click (MainWindow.SessionIO.cs:1202) with its two
        /// re-applications kept: the shell window (WPF passes <c>this</c>, i.e. MainWindow) and the
        /// open tube, which is <see cref="AvatarTubeWindow.Live"/> here rather than App.AvatarWindow.
        /// Awaited, not fire-and-forget: Avalonia's ShowDialog is async, and writing the setting
        /// before the answer lands would store whatever the previous combo was.
        ///
        /// <para><b>ChatShortcutGlobal is stored but not registered.</b> The checkbox's "activate
        /// from any app" half is GlobalHotkeyService, a Win32 RegisterHotKey that stays in the WPF
        /// head. Storing the user's choice is still right - it is one settings file across both
        /// heads, and WPF honours it - and the IN-WINDOW binding this method applies works on this
        /// head either way, which is exactly what WPF falls back to when the flag is off.</para>
        /// </summary>
        private async void BtnChatShortcut_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // No owner means no modal dialog to show, so there is nothing to capture. WPF could
                // not reach this state; here it is the headless/detached case.
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                var prompt = CoreSettings.Current.CompanionPrompt;
                if (prompt == null) return;

                var dlg = new ChatShortcutCaptureDialog { GlobalHotkey = prompt.ChatShortcutGlobal };
                if (!await dlg.ShowDialogSafe<bool>(owner)) return;

                if (dlg.ResetToDefault)
                {
                    prompt.ChatShortcutKey = "T";
                    prompt.ChatShortcutModifiers = "Control";
                }
                else
                {
                    prompt.ChatShortcutKey = dlg.CapturedKey.ToString();
                    // Serialises "Windows", not Avalonia's "Meta", so a file written here still
                    // parses on the WPF head.
                    prompt.ChatShortcutModifiers = AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
                }
                prompt.ChatShortcutGlobal = dlg.GlobalHotkey;
                CoreSettings.Save();

                AvatarTubeWindow.ApplyChatShortcutTo(owner);
                AvatarTubeWindow.ApplyChatShortcutTo(AvatarTubeWindow.Live);
                RefreshChatShortcutLabel();
                Log.Information("Chat shortcut rebound to {Combo}", AvatarTubeWindow.FormatChatShortcut());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Settings/Devices: chat shortcut rebind failed");
            }
        }

        // ponytail: BtnCameraShortcutDevices stays inert, and NOT for the reason the old note gave.
        // SerializeModifiers shipped with the tube, so the capture half would work - but the combo
        // it stores drives MainWindow.ToggleWebcamFromHotkey (MainWindow.SessionIO.cs:1485), which
        // toggles WebcamTrackingService. This head now has a tracker (Platform/WebcamTracker, and
        // CoreWebcam.IsAvailable is seeded true), but no global hotkey listener calls it, so a rebind
        // here would let the user configure a key that cannot fire - and the row's own label would then
        // report a binding that does nothing. The label is left at its XAML literal for the same reason.
        // Unblocks with a global-hotkey route to WebcamTracker (ToggleWebcamFromHotkey).
    }
}
