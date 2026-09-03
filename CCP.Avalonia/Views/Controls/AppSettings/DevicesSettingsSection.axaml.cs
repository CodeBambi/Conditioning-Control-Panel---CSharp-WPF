using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            // The WPF section implements IAppSettingsSection.OnSectionShown because device lists go
            // stale the moment someone plugs a headset in. Attach is this head's equivalent reveal
            // hook; there is nothing to re-enumerate yet (see the mic note below).
            SyncFromSettings();
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

        // ponytail: CmbMicDevice / BtnMicRefresh need SpeechService.EnumerateInputDevices
        // (ConditioningControlPanel/Services/Speech/SpeechService.cs, NAudio/WASAPI), still in the
        // WPF head. Without an enumeration there is no device to select and nothing honest to
        // write to SpeechInputDeviceIndex / SpeechInputDeviceName, so both stay unwired.

        // =====================================================================================
        //  webcam
        // =====================================================================================

        // ponytail: the webcam engine bar's device/monitor combos, the calibrate / quick-recal /
        // start / tracker-test / privacy / revoke buttons and the debug-cursor toggle all need
        // WebcamTrackingService + App.GazeCursor (ConditioningControlPanel/Services/Webcam/,
        // MainWindow.LabTab.cs), still in the WPF head. Their controls render inert.

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
            // No Save: WPF leaves this to the settings sweep, so the flag is written the same way.
            CoreSettings.Current.WebcamAutoDriftCorrection = v;
            AppendWebcamDebugLog(v
                ? "Auto drift correction enabled — clicks near your gaze will fine-tune calibration."
                : "Auto drift correction disabled.");
        }

        private void ChkRestrictGazeToCalScreen_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            // No Save here either, matching MainWindow.LabTab.cs:1220.
            CoreSettings.Current.RestrictGazeContentToCalibratedScreen = ChkRestrictGazeToCalScreen.IsChecked == true;
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
                var owner = TopLevel.GetTopLevel(this) as Window;
                if (owner == null) return;   // no window to parent the warning to: refuse, do not silently disable

                var confirmed = await WarningDialog.ShowDoubleWarningAsync(owner,
                    "Disable Panic Key",
                    "• You will have NO emergency escape option\n" +
                    "• The ONLY way to exit will be the Exit button\n" +
                    "• Combined with Strict Lock, this is VERY restrictive\n" +
                    "• Make sure you know what you're doing!");

                if (!confirmed)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _loading = true;
                        ChkNoPanic.IsChecked = false;
                        _loading = false;
                    });
                    return;
                }

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

        // ponytail: BtnPanicKey / BtnPauseKey capture the next key through MainWindow's global
        // keyboard hook (MainWindow.xaml.cs UpdatePanicKeyButton / _isCapturingPanicKey); the
        // buttons show the stored binding but cannot rebind it on this head.

        // ponytail: BtnChatShortcutDevices / BtnCameraShortcutDevices need AvatarTubeWindow
        // .FormatChatShortcut / SerializeModifiers / ApplyChatShortcutTo and GlobalHotkeyService
        // (ConditioningControlPanel/Views/Windows/, /Services/), all Win32 and still in the WPF
        // head - the dialog itself (Views/Dialogs/ChatShortcutCaptureDialog) is already ported, so
        // this unblocks as soon as the modifier serialiser and the hotkey registrar move.
    }
}
