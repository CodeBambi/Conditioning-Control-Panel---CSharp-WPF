// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SheListening.cs - the "She's
// Listening" surface. The shared rules (MicIsArmed, the loudness-gate dial, wake phrases, which
// modes may run) are Core VoiceInputRules, which WPF delegates to as well.
//
// Every tab handler is wired: master Start/Stop (premium bar, speech check, consent, default wake
// word), revoke consent, spoken mantras with the consent gate, calibrate, the premium veil.
//
// The mic opens through MainShellWindow.VoiceCommands.cs (the wake loop / push-to-talk feeding
// Core VoiceCommands) exactly under WPF's conditions; every repaint here reconciles it, so arm,
// Stop, revoke and an entitlement lapse all open or close it. The title-bar mic pill (WPF
// MainWindow.LabTab.cs UpdateMicPill) lives here too. ponytail: not here - sherpa wake engine
// (calibration shows WPF's "not installed" notice).
//
// _slLoading, not _isLoading: a partial-class field is declared once, and Avalonia's CheckBox
// raises IsCheckedChanged on a programmatic set, so seeding needs its own guard.

using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Speech;
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

        /// <summary>
        /// True when the offline mic is actually armed: consent given AND at least one input mode
        /// (wake word or push-to-talk) is on. The "She's Listening" master on/off state, fully
        /// independent of Takeover.
        /// </summary>
        internal bool MicIsArmed()
            => VoiceInputRules.MicIsArmed(CoreSettings.Current);

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
                await dlg.ShowDialogSafe(this);
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
            // First, tab or no tab: WPF pairs every arm/Stop/revoke/lapse with RefreshVoiceInputModes.
            RefreshVoiceInputModes();
            Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab")?.RefreshAutonomyVoiceHint();   // WPF SheListening.cs:294
            var tab = SheListeningPage;
            if (tab == null) return;
            var s = CoreSettings.Current;

            var wasLoading = _slLoading;
            _slLoading = true;
            try
            {
                tab.ChkSL_Mantras.IsChecked = s.SpokenMantrasEnabled && s.MicConsentGiven;
                double sens = VoiceInputRules.ThresholdToSens(s.SpeechLoudnessThreshold);
                tab.SldSL_MicSensitivity.Value = sens;
                tab.TxtSL_MicSensitivity.Text = $"{(int)Math.Round(sens)}%";
            }
            catch (Exception ex) { Log.Debug("RefreshSheListeningTab: {E}", ex.Message); }
            finally { _slLoading = wasLoading; }

            tab.ShowOnlyVoiceCommands(VoiceCmds.Available.Select(i => i.Name).ToHashSet());

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
            UpdateMicPill(); // WPF SheListening.cs:423: every arm/disarm flows through here
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

            // WPF SheListening.cs:436-448: the disc breathes only while armed; the folder button is up
            // exactly while the status line is about a model, decided on every path.
            SetSheListeningStatusPulse(armed);
            tab.BtnSL_OpenModels.IsVisible = !available && SpeechModelIsTheProblem();

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

        /// <summary>WPF ToggleVoiceMic: the master Start/Stop. Arming is premium-barred (the veil is
        /// only a Border); disarming never is.</summary>
        internal async void ToggleVoiceMic()
        {
            var s = CoreSettings.Current;
            if (MicIsArmed()) { DisarmVoiceMic(); return; }
            if (!Services.TierGate.DemandPremium(Loc.Get("tab_shelistening"), "voice")) return;

            if (!CoreSpeech.IsAvailable)
            {
                await Dialogs.MessageDialog.ShowAsync(this, "She's Listening",
                    CoreSpeech.HasCaptureDevice
                        ? "The offline speech model isn't installed yet, so the mic can't start."
                        : "No microphone detected — connect one to use voice control.");
                return;
            }
            if (!s.MicConsentGiven)
            {
                var dlg = new Dialogs.MicConsentDialog();
                await dlg.ShowDialogSafe(this);
                if (!dlg.ConsentGiven) return;
            }
            if (!s.SpeechWakeWordEnabled && !s.SpeechPushToTalkEnabled)
                s.SpeechWakeWordEnabled = true;
            CoreSettings.Save();
            RefreshSheListeningTab();
        }

        /// <summary>WPF DisarmVoiceMic: clear both input modes, cut any in-flight capture (a voice
        /// lock card's), drop open lock cards to typed solve, repaint.</summary>
        internal void DisarmVoiceMic()
        {
            var s = CoreSettings.Current;
            s.SpeechWakeWordEnabled = false;
            s.SpeechPushToTalkEnabled = false;
            CoreSettings.Save();
            StopVoiceInput();   // WPF App.Autonomy.StopVoiceInput: cut the capture, stand the loop down
            try { LockCardWindow.DisableVoiceForAll(); } catch { }
            RefreshSheListeningTab();
        }

        /// <summary>WPF SL_RevokeMicConsent_Click: disarm, then clear every mic capability and the
        /// consent record so the next enable asks again.</summary>
        internal async void SL_RevokeMicConsent_Click()
        {
            try
            {
                if (!await Dialogs.MessageDialog.ConfirmAsync(this, "Revoke microphone consent",
                        "This turns off every voice feature (wake word, push-to-talk, spoken mantras, voice lock cards) and clears your mic consent. You'll be asked again next time you enable one.",
                        defaultToCancel: true))
                    return;
                RevokeMicConsent();
            }
            catch (Exception ex) { Log.Warning(ex, "SL_RevokeMicConsent_Click failed"); }
        }

        /// <summary>The revoke itself, once confirmed.</summary>
        internal void RevokeMicConsent()
        {
            DisarmVoiceMic();
            var s = CoreSettings.Current;
            s.SpokenMantrasEnabled = false;
            s.AutonomyCanTriggerVoiceCommand = false;
            s.LockCardVoiceMode = false;
            s.MicConsentGiven = false;
            CoreSettings.Save();
            Log.Information("Microphone consent revoked");
            RefreshSheListeningTab();
        }

        /// <summary>WPF MainWindow.LabTab.cs UpdateMicPill: the title-bar pill is lit whenever the mic is
        /// reachable - any live capture, or the whole time the wake word is armed (it holds the mic open).
        /// Push-to-talk alone lights it only during its capture.</summary>
        internal void UpdateMicPill()
        {
            if (Named<Border>("MicActivePill") is not { } pill) return;
            var s = CoreSettings.Current;
            pill.IsVisible = (s.MicConsentGiven && s.SpeechWakeWordEnabled) || VoiceSpeech?.IsListening == true;
        }

        private Services.Speech.SpeechEngine? _micPillEngine;

        /// <summary>WPF WireMicActivePill (MainWindow.xaml.cs:3594): repaint the pill on every capture
        /// change. The event fires off the capture thread, so it is posted.</summary>
        private void InitializeMicActivePill()
        {
            _micPillEngine = VoiceSpeech;
            if (_micPillEngine != null) _micPillEngine.ListeningChanged += OnMicListeningChanged;
            Closed += (_, _) => { if (_micPillEngine != null) _micPillEngine.ListeningChanged -= OnMicListeningChanged; };
            if (Named<Border>("MicActivePill") is { } pill)
                pill.KeyDown += (_, e) =>   // P17: the privacy stop is keyboard-reachable
                {
                    if (e.Key is not (global::Avalonia.Input.Key.Enter or global::Avalonia.Input.Key.Space)) return;
                    e.Handled = true;
                    DisarmVoiceMic();
                };
            UpdateMicPill();
        }

        private void OnMicListeningChanged(object? sender, bool listening) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(UpdateMicPill);

        /// <summary>WPF SL_Calibrate_Click's first branch: this head has no sherpa wake engine
        /// (App.WakeWord), so it is never configured and WPF's notice is the whole answer.</summary>
        internal void SL_Calibrate_Click()
            => _ = Dialogs.MessageDialog.ShowAsync(this, "Calibrate wake word",
                "The offline wake-word model isn't installed yet, so there's nothing to calibrate.");
    }
}
