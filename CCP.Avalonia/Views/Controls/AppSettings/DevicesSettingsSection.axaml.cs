using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
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

            SyncFromSettings();
            PopulateMicDevices();
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
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
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

        // ponytail: the webcam engine bar's device/monitor combos, the calibrate / quick-recal /
        // start / tracker-test / privacy / revoke buttons and the debug-cursor toggle all need
        // WebcamTrackingService + App.GazeCursor (ConditioningControlPanel/Services/Webcam/,
        // MainWindow.LabTab.cs), still in the WPF head. Their controls render inert.
        //
        // Re-checked against Core: CCP.Core/Services/Webcam/WebcamConsent.cs is there, but it is a
        // READ predicate (IsCurrent) only. BtnWebcamRevokeConsent needs App.Webcam.RevokeConsent,
        // which stops tracking, deletes the calibration file and disables the webcam features -
        // four promises the dialog makes. Clearing the consent flag alone would keep three of them
        // and still tell the user everything was undone, so the button stays inert until the
        // service crosses.

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

        // ponytail: ChkSpeechWakeWord / ChkSpeechPushToTalk need TierGate.DemandPremium
        // (ConditioningControlPanel/Services/TierGate.cs) and App.Autonomy.RefreshVoiceInputModes
        // (ConditioningControlPanel/Services/Autonomy/), both still in the WPF head. They are
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
            // ponytail: WPF also stops/starts the low-level keyboard hook here (MainWindow's
            // _keyboardHook, ConditioningControlPanel/Services/Input/), a Win32 WH_KEYBOARD_LL hook
            // with no equivalent on this head - so there is no hook to leave running either.
        }

        /// <summary>Posted, not assigned inline: the revert has to run after the dialog's event
        /// stack unwinds or the toggle animation sticks in the ON position, exactly as on WPF.</summary>
        private void RevertNoPanic() => Dispatcher.UIThread.Post(() =>
        {
            _loading = true;
            ChkNoPanic.IsChecked = false;
            _loading = false;
        });

        // ponytail: BtnPanicKey / BtnPauseKey capture the next key through MainWindow's global
        // keyboard hook (MainWindow.xaml.cs UpdatePanicKeyButton / _isCapturingPanicKey); the
        // buttons show the stored binding but cannot rebind it on this head.

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
                if (!await dlg.ShowDialog<bool>(owner)) return;

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
        // toggles WebcamTrackingService. No webcam engine exists on this head at all (see the
        // webcam note above), so a rebind here would let the user configure a key for a feature
        // that cannot fire - and the row's own label would then report a binding that does nothing.
        // The label is left at its XAML literal for the same reason. Unblocks with the tracker.
    }
}
