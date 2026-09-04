// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SheListening.cs (481 lines) - the
// "She's Listening" surface, sorted member by member rather than stubbed wholesale.
//
// WHAT IS REAL HERE. Everything this page needs that is settings, arithmetic or CoreSpeech:
// the loudness-gate mapping (SensToThreshold/ThresholdToSens, pure maths), MicIsArmed, the
// sensitivity slider's write, the spoken-mantras toggle with its mic-consent gate
// (Views/Dialogs/MicConsentDialog is on this head), the device chips, the status hero and the
// tab's own re-seed. CoreSpeech answers IsAvailable / HasCaptureDevice / ModelStatus, which is
// the whole of what RefreshSheListeningStatus asks a service for.
//
// SheListeningTabView loads with the GENERATED InitializeComponent, so ITS x:Name fields are
// real and are used directly. This WINDOW does not - the tab itself is reached only through
// Named<T>() (see MainShellWindow.TabNavigation.cs).
//
// _slLoading, not _isLoading. The class-wide _isLoading is listed in MainShellWindow.axaml.cs's
// dropped ledger and belongs to that file; a partial-class field can be declared exactly once,
// so this concern carries its own. It is MORE load-bearing here than on WPF: Avalonia's CheckBox
// and Slider raise IsCheckedChanged / ValueChanged on a PROGRAMMATIC set, so seeding a control
// would otherwise write settings straight back.
//
// NO CALLER YET, and both call sites are named so this is not mistaken for a live page:
//   - the seven handlers in CCP.Avalonia/Views/Tabs/SheListeningTabView.axaml.cs are still
//     `(_, _) => { }`; they are one forward each to the members below once that file is a
//     layer's to own.
//   - the on-show refresh belongs in OnTabShown (MainShellWindow.TabNavigation.cs), beside the
//     StudioTab cases already there.
//
// STILL HEAD-SIDE, each with the exact symbol and where it lives today:
//   DemandSheListeningPremium  - nothing any more: TierGate is CCP.Core/Services/TierGate.cs.
//                                Still unwired here only because its two readers below,
//                                ToggleVoiceMic's arming half and the SheListeningGate veil, are
//                                blocked on the mic services listed next.
//   ToggleVoiceMic             - App.Autonomy.RefreshVoiceInputModes / StopVoiceInput
//   DisarmVoiceMic               (ConditioningControlPanel/Services/AutonomyService.cs),
//                                App.Speech.StopListening
//                                (ConditioningControlPanel/Services/Speech/SpeechService.cs) and
//                                LockCardWindow.DisableVoiceForAll
//                                (ConditioningControlPanel/Windows/LockCardWindow.xaml.cs).
//                                The settings half of both is trivial; shipping it WITHOUT the
//                                stop calls would leave a mic open behind a switch that says off,
//                                which is the one failure this file must not have. Blocked whole.
//   SL_RevokeMicConsent_Click  - the same three, through DisarmVoiceMic. Clearing the four
//                                consent settings is pointless while the capture cannot be cut.
//   SL_Calibrate_Click         - Services.Speech.SherpaWakeService.CalibrateAsync
//   RefreshWakeEngineStatus      (ConditioningControlPanel/Services/Speech/SherpaWakeService.cs).
//                                CoreSpeech carries no wake engine, only the recognizer's status.
//   UpdateMicPill              - MainShellWindow.PrivacyPill.cs, still a stub.
//   SetSheListeningStatusPulse - MainShellWindow.SheListeningFx.cs, still a stub.
//   RefreshPremiumRail         - MainShellWindow.PremiumRail.cs / RefreshPremiumGate, neither of
//   RefreshPremiumGate           which this layer owns. TierGate itself is no longer the blocker.
// Every one of those calls is DROPPED from the restored bodies below, never faked.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The She's Listening tab. Resolved on every read - this window's generated
        /// x:Name fields are never assigned (MainShellWindow.TabNavigation.cs).</summary>
        internal Tabs.SheListeningTabView? SheListeningPage =>
            Named<Tabs.SheListeningTabView>("SheListeningTab");

        /// <summary>Seeding guard for this page. See the header for why it is not _isLoading.</summary>
        private bool _slLoading;

        // ---- the loudness gate -----------------------------------------------------------
        // Slider 0..100 maps INVERSELY to the RMS threshold: 100% = most sensitive (lowest
        // threshold, softest speech OK), 0% = strictest. Verbatim from the WPF file - pure
        // arithmetic, no dependency at all, and the numbers are the gate's useful range.

        private const double LoudThrAtMinSens = 0.045; // slider 0%
        private const double LoudThrAtMaxSens = 0.004; // slider 100%

        private static double SensToThreshold(double sens)
            => LoudThrAtMinSens - (LoudThrAtMinSens - LoudThrAtMaxSens) * (Math.Clamp(sens, 0, 100) / 100.0);

        private static double ThresholdToSens(double thr)
            => Math.Clamp((LoudThrAtMinSens - thr) / (LoudThrAtMinSens - LoudThrAtMaxSens) * 100.0, 0, 100);

        /// <summary>
        /// True when the offline mic is actually armed: consent given AND at least one input mode
        /// (wake word or push-to-talk) is on. The "She's Listening" master on/off state, fully
        /// independent of Takeover.
        /// </summary>
        internal bool MicIsArmed()
        {
            var s = CoreSettings.Current;
            return s.MicConsentGiven && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
        }

        /// <summary>
        /// Mic-sensitivity slider: tunes the loudness gate that decides whether a recognized
        /// command or mantra was "said out loud". Applies live. Does NOT touch the wake word -
        /// that is calibration. Avalonia hands the new value rather than WPF's
        /// RoutedPropertyChangedEventArgs, so the signature takes it directly.
        /// </summary>
        internal void SL_MicSensitivity_Changed(double newValue)
        {
            if (_slLoading) return;
            var tab = SheListeningPage;
            if (tab == null) return;

            CoreSettings.Current.SpeechLoudnessThreshold = SensToThreshold(newValue);
            CoreSettings.Save();
            tab.TxtSL_MicSensitivity.Text = $"{(int)Math.Round(newValue)}%";
        }

        /// <summary>
        /// On-demand spoken mantras (AppSettings.SpokenMantrasEnabled). Separate from the Takeover
        /// "surprise" auto-trigger. First enable asks for mic consent, since a mantra opens the mic
        /// to hear you repeat the phrase - and a declined dialog un-ticks the box, which is why the
        /// revert is wrapped in the seeding guard.
        ///
        /// <para>async, unlike WPF: Avalonia's ShowDialog is awaited, never blocking.</para>
        /// </summary>
        internal async void SL_Mantras_Changed()
        {
            var tab = SheListeningPage;
            if (_slLoading || tab == null) return;
            var s = CoreSettings.Current;

            var turningOn = tab.ChkSL_Mantras.IsChecked == true;
            if (turningOn && !s.MicConsentGiven)
            {
                var dlg = new Dialogs.MicConsentDialog();
                await dlg.ShowDialog(this);
                if (!dlg.ConsentGiven)
                {
                    var wasLoading = _slLoading;
                    _slLoading = true;
                    tab.ChkSL_Mantras.IsChecked = false;
                    _slLoading = wasLoading;
                    return;
                }
            }

            s.SpokenMantrasEnabled = turningOn;
            CoreSettings.Save();
            RefreshSheListeningStatus();
        }

        /// <summary>Load the She's-Listening controls from settings and repaint the status hero.
        /// Called on tab show.</summary>
        internal void RefreshSheListeningTab()
        {
            var tab = SheListeningPage;
            if (tab == null) return;
            var s = CoreSettings.Current;

            var wasLoading = _slLoading;
            _slLoading = true;
            try
            {
                tab.ChkSL_Mantras.IsChecked = s.SpokenMantrasEnabled && s.MicConsentGiven;
                double sens = ThresholdToSens(s.SpeechLoudnessThreshold);
                tab.SldSL_MicSensitivity.Value = sens;
                tab.TxtSL_MicSensitivity.Text = $"{(int)Math.Round(sens)}%";
            }
            catch (Exception ex) { Log.Debug("RefreshSheListeningTab: {E}", ex.Message); }
            finally { _slLoading = wasLoading; }

            // "Revoke consent" only means something once consent exists.
            tab.SL_PrivacyCard.IsVisible = s.MicConsentGiven;

            RefreshSheListeningDeviceChips();
            RefreshSheListeningStatus();
        }

        /// <summary>
        /// Repaint the read-only microphone chips on She's Listening AND re-seed the voice-mode
        /// rows in Settings &gt; Devices from the settings file.
        ///
        /// <para>Both halves matter. The chips are display: device, wake word, push-to-talk and
        /// headphone rows were live editors until Phase 2 and are now a summary of what
        /// Settings &gt; Devices owns. The re-seed is the other direction - the master Start/Stop
        /// button writes those settings without touching a checkbox, so the Settings page has to
        /// be told.</para>
        ///
        /// <para>The re-seed is ONE call here rather than WPF's four control writes:
        /// DevicesSettingsSection.SyncFromSettings does exactly that job and owns its own
        /// _loading guard, so poking its checkboxes from outside would only be a second, worse
        /// copy of it.</para>
        /// </summary>
        internal void RefreshSheListeningDeviceChips()
        {
            var s = CoreSettings.Current;

            AppSettingsPage?.FindControl<Controls.AppSettings.DevicesSettingsSection>("SectionDevices")
                           ?.SyncFromSettings();

            var tab = SheListeningPage;
            if (tab == null) return;

            var device = string.IsNullOrWhiteSpace(s.SpeechInputDeviceName)
                ? Loc.Get("set2_mic_system_default")
                : s.SpeechInputDeviceName;
            var off = Loc.Get("set2_chip_off");

            tab.TxtSL_MicDeviceChip.Text = device;
            tab.TxtSL_WakeWordChip.Text =
                s.SpeechWakeWordEnabled && s.MicConsentGiven
                    ? (string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords)
                    : off;
            tab.TxtSL_PttChip.Text =
                s.SpeechPushToTalkEnabled && s.MicConsentGiven
                    ? (string.IsNullOrWhiteSpace(s.SpeechPushToTalkKey) ? "F8" : s.SpeechPushToTalkKey)
                    : off;
            tab.TxtSL_HeadphonesChip.Text = s.SpeechHeadphonesMode ? Loc.Get("set2_chip_on") : off;
        }

        /// <summary>
        /// The hero: mic readiness / armed state plus the master Start/Stop button.
        ///
        /// <para>Every string is hard-coded English here BECAUSE IT IS ON WPF TOO - this page's
        /// status copy never got loc keys, and inventing one would produce a plausible key that
        /// resolves to nothing. The button's Content is a TextBlock (Avalonia parses `_` in a bare
        /// string Content as an access key), so the label is written through it.</para>
        /// </summary>
        internal void RefreshSheListeningStatus()
        {
            var tab = SheListeningPage;
            if (tab == null) return;

            var available = CoreSpeech.IsAvailable;
            var armed = available && MicIsArmed();

            tab.BtnSL_MicMaster.IsEnabled = available;
            if (tab.BtnSL_MicMaster.Content is TextBlock label)
                label.Text = armed ? "■  Stop listening" : "▶  Start listening";
            tab.BtnSL_MicMaster.Foreground = armed
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0xB0))
                : new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));

            if (!available)
            {
                tab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x5A, 0x4A, 0x6A));
                tab.SL_StatusTitle.Text = "Microphone not ready";
                tab.SL_StatusSub.Text =
                    !CoreSpeech.HasCaptureDevice
                        ? "No microphone detected — connect one to use voice."
                        : CoreSpeech.ModelStatus == CoreSpeechModelStatus.LoadFailed
                            ? "Speech model found but it would not load — remove any extra model you added under Resources\\Models\\vosk, then restart."
                            : "Offline speech model not installed yet — voice stays off until it is.";
                return;
            }

            if (armed)
            {
                tab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
                tab.SL_StatusTitle.Text = "She's listening";
                tab.SL_StatusSub.Text = "The mic is open. Call her, then say a command.";
            }
            else
            {
                tab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
                tab.SL_StatusTitle.Text = "Mic off";
                tab.SL_StatusSub.Text = "Tap Start listening so she can hear you. Works with or without Takeover.";
            }
        }
    }
}
