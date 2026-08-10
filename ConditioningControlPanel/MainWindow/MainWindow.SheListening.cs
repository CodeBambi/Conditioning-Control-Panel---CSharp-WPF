using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "She's Listening" Exclusive — the voice-control surface.
    ///
    /// <para>Until Phase 2 of the UX restructure this file was a mirror: its toggles copied their
    /// value onto a Collapsed twin on the Takeover tab, called that tab's handler, and copied the
    /// result back, so the visible control and the logic that owned it lived on different tabs.
    /// The microphone now has one owner — Settings → Devices — which holds the device picker, the
    /// wake word, push-to-talk and headphone barge-in with their original x:Names and their
    /// original MainWindow.Autonomy.cs handlers.</para>
    ///
    /// <para>What is left here is what belongs to this page: the master arm/disarm switch, the
    /// loudness gate, spoken mantras, wake calibration, the test and mic consent — plus
    /// <see cref="RefreshSheListeningDeviceChips"/>, which paints the read-only summary rows and,
    /// in the other direction, re-seeds the Settings toggles after the master switch writes the
    /// settings behind their back.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// The premium bar for the offline mic, spelled once. SheListeningGate is only a Border
        /// over the tab, so every control under it stays reachable by keyboard focus and by
        /// automation - the bar has to sit on the handlers that actually hand the mic to the wake
        /// loop. Turning the mic OFF is never gated: a lapsed patron must always be able to stop
        /// a mic that is already open.
        /// </summary>
        private static bool DemandSheListeningPremium()
            => Services.TierGate.DemandPremium(Localization.Loc.Get("tab_shelistening"));

        // RevertCheck lived here: it un-ticked a She's-Listening toggle the premium bar had just
        // refused. The two toggles it guarded (wake word, push-to-talk) are read-only chips on this
        // tab since Phase 2, and the bar moved with them into MainWindow.Autonomy.cs, which reverts
        // through its own RevertToggle.

        // MirrorCheck lived here: it copied a She's-Listening toggle onto the Collapsed twin on the
        // Takeover tab, called that tab's handler, then copied the result back. Phase 2 of the UX
        // restructure gave the mic one editor (Settings > Devices) and the hidden twin is gone, so
        // the mirror - and the four handlers that used it - went with it.

        /// <summary>
        /// On-demand spoken mantras (the She's Listening capability = AppSettings.SpokenMantrasEnabled).
        /// Separate from the Takeover "surprise" auto-trigger (AutonomyCanTriggerVoiceCommand). First
        /// enable asks for mic consent, since a mantra opens the mic to hear you repeat the phrase.
        /// </summary>
        internal void SL_Mantras_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var turningOn = SheListeningTab.ChkSL_Mantras.IsChecked == true;
            if (turningOn && !s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = this };
                var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                if (!ok)
                {
                    var wasLoading = _isLoading;
                    _isLoading = true;
                    SheListeningTab.ChkSL_Mantras.IsChecked = false;
                    _isLoading = wasLoading;
                    return;
                }
            }

            s.SpokenMantrasEnabled = turningOn;
            App.Settings?.Save();
            RefreshSheListeningStatus();
        }

        private bool _calibratingWake;

        /// <summary>
        /// Tune the wake word to the user's own voice + mic. Frees the mic (stops the wake loop), records a
        /// few spoken "Hey Bambi"s via <see cref="Services.Speech.SherpaWakeService.CalibrateAsync"/>, stores
        /// the chosen sensitivity, then re-arms. Live progress + the result land in the status line.
        /// </summary>
        internal async void SL_Calibrate_Click(object sender, RoutedEventArgs e)
        {
            if (_calibratingWake || SheListeningTab == null) return;
            var wake = App.WakeWord;
            if (wake == null || !wake.IsConfigured)
            {
                MessageBox.Show(this, "The offline wake-word model isn't installed yet, so there's nothing to calibrate.",
                    "Calibrate wake word", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var s = App.Settings?.Current;
            if (s == null) return;
            if (!s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = this };
                if (!(dlg.ShowDialog() == true && dlg.ConsentGiven)) return;
            }

            _calibratingWake = true;
            var btn = SheListeningTab.BtnSL_Calibrate;
            var lbl = SheListeningTab.TxtSL_WakeEngineStatus;
            if (btn != null) btn.IsEnabled = false;

            // Free the single capture session: stop the wake loop / PTT so calibration owns the mic.
            try { App.Autonomy?.StopVoiceInput(); } catch { }
            for (int i = 0; i < 20 && App.WakeWord?.IsListening == true; i++) await Task.Delay(25);

            if (lbl != null)
            {
                lbl.Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xA0, 0xFF));
                lbl.Text = "Listening… say “Hey Bambi” 5 times, pausing between each.";
            }

            var progress = new Progress<Services.Speech.SherpaWakeService.CalibrationProgress>(p =>
            {
                if (lbl == null) return;
                lbl.Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xA0, 0xFF));
                lbl.Text = p.Phase == "analyze"
                    ? "Got it — finding your best sensitivity…"
                    : $"Listening… say “Hey Bambi” clearly  ({p.Captured}/{p.Target})";
            });

            Services.Speech.SherpaWakeService.CalibrationResult result;
            try { result = await wake.CalibrateAsync(5, progress); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Wake calibration failed");
                result = new Services.Speech.SherpaWakeService.CalibrationResult { Message = "Calibration failed — see logs." };
            }

            if (lbl != null)
            {
                lbl.Foreground = result.Success
                    ? new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0xB0));
                lbl.Text = result.Message;
            }

            // Re-arm from settings (the wake loop reconciles + the engine rebuilds at the new sensitivity).
            try { App.Autonomy?.RefreshVoiceInputModes(); } catch { }
            UpdateMicPill();

            if (btn != null) btn.IsEnabled = true;
            _calibratingWake = false;
        }

        /// <summary>Repaint the reliable-wake (sherpa-onnx KWS) status line: model present + active, or what's missing.</summary>
        private void RefreshWakeEngineStatus()
        {
            var lbl = SheListeningTab?.TxtSL_WakeEngineStatus;
            if (lbl == null) return;
            // NOTE: do NOT call ResetInitState() here. IsAvailable already lazily inits, and a model
            // dropped in while running is auto-detected (its files change the fingerprint). Forcing a
            // reset on every tab paint used to dispose the engine mid-wake-session and crash the native
            // decode. ResetInitState now also no-ops during an active session as a backstop.
            if (App.WakeWord?.IsAvailable == true)
            {
                lbl.Text = "✓ Active — open-source 'Hey Bambi' wake engine installed.";
                lbl.Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
                return;
            }
            lbl.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            lbl.Text = App.WakeWord?.IsConfigured == true
                ? "Model found but the engine didn't start — see logs. Using the built-in recognizer."
                : "Drop the sherpa-onnx KWS model into Resources\\Models\\sherpa-kws\\ to enable (see the README there).";
        }

        /// <summary>
        /// True when the offline mic is actually armed: consent given AND at least one input mode
        /// (wake word or push-to-talk) is on. This is the "She's Listening" master on/off state —
        /// fully independent of Takeover.
        /// </summary>
        internal bool MicIsArmed()
        {
            var s = App.Settings?.Current;
            return s != null && s.MicConsentGiven
                   && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
        }

        /// <summary>
        /// Master mic switch for She's Listening (and the dashboard Voice chip). Off→On: consent,
        /// then enable the wake word by default (so she actually listens) and arm. On→Off: disable
        /// both input modes and cut any in-flight capture. Independent of Takeover.
        /// </summary>
        internal void ToggleVoiceMic()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            if (MicIsArmed()) { DisarmVoiceMic(); return; }

            // Arming is the premium half - and this is the shared entry point for the master
            // Start button and the dashboard Voice chip, so the bar covers both.
            if (!DemandSheListeningPremium()) return;

            // Arm: consent, then default to the wake word so "she's listening" means something.
            if (App.Speech?.IsAvailable != true)
            {
                MessageBox.Show(this,
                    Services.Speech.SpeechService.HasCaptureDevice
                        ? "The offline speech model isn't installed yet, so the mic can't start."
                        : "No microphone detected — connect one to use voice control.",
                    "She's Listening", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = this };
                if (!(dlg.ShowDialog() == true && dlg.ConsentGiven)) return;
            }
            if (!s.SpeechWakeWordEnabled && !s.SpeechPushToTalkEnabled)
                s.SpeechWakeWordEnabled = true;
            App.Settings?.Save();
            App.Autonomy?.RefreshVoiceInputModes();

            RefreshSheListeningTab();          // reload the sub-toggles + status
            RefreshSheListeningDeviceChips();  // arming writes the setting directly - tell Settings > Devices
            RefreshPremiumRail();              // keep the dashboard Voice dot honest
            UpdateMicPill();                   // privacy pill: wake word is now armed → mic is open
        }

        /// <summary>
        /// Fully turn the offline mic OFF and keep it off: clear the continuous input modes (wake word
        /// + push-to-talk) so nothing re-arms it, cut any in-flight capture, then repaint the dashboard
        /// dot + She's Listening. Shared by the master Stop button and the title-bar privacy pill so
        /// both genuinely disarm (not just pause) and the UI stays honest.
        /// </summary>
        internal void DisarmVoiceMic()
        {
            var s = App.Settings?.Current;
            if (s != null)
            {
                s.SpeechWakeWordEnabled = false;
                s.SpeechPushToTalkEnabled = false;
                App.Settings?.Save();
            }
            try { App.Speech?.StopListening(); } catch { }
            try { App.Autonomy?.StopVoiceInput(); } catch { }

            // Any open Voice Lock Card would otherwise keep re-opening the mic (its solve loop only
            // checks Speech.IsAvailable, which ignores this master switch) with the typed input still
            // hidden — leaving the card unsolvable. Drop it to typed solve so the lock still holds.
            try { LockCardWindow.DisableVoiceForAll(); } catch { }

            if (SheListeningTab != null) RefreshSheListeningTab();
            RefreshSheListeningDeviceChips();  // disarming writes the setting directly - re-seed Settings > Devices
            RefreshPremiumRail();
            UpdateMicPill();          // privacy pill: mic fully disarmed → pill off
        }

        /// <summary>
        /// Revoke microphone consent — the mic counterpart of the webcam "Revoke consent" button.
        /// Disarms the mic via <see cref="DisarmVoiceMic"/> (wake word + push-to-talk off, in-flight
        /// capture cut, lock cards dropped to typed solve), then clears the mic capabilities that
        /// DisarmVoiceMic leaves alone (spoken mantras, Takeover voice prompts, voice lock cards)
        /// and the consent record itself, so the next enable re-runs the consent dialog. The mic
        /// stores nothing on disk, so clearing settings is the whole job.
        /// </summary>
        internal void SL_RevokeMicConsent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    this,
                    "This turns off every voice feature (wake word, push-to-talk, spoken mantras, voice lock cards) and clears your mic consent. You'll be asked again next time you enable one.",
                    "Revoke microphone consent",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);
                if (result != MessageBoxResult.OK) return;

                DisarmVoiceMic();

                var s = App.Settings?.Current;
                if (s != null)
                {
                    s.SpokenMantrasEnabled = false;
                    s.AutonomyCanTriggerVoiceCommand = false;
                    s.LockCardVoiceMode = false;
                    s.MicConsentGiven = false;
                    App.Settings?.Save();
                }
                App.Logger?.Information("Microphone consent revoked");

                // DisarmVoiceMic repainted before consent was cleared — repaint again so the
                // privacy card hides and the Takeover-tab mirrors untick.
                var wasLoading = _isLoading;
                _isLoading = true;
                if (BambiTakeoverTab?.ChkAutonomyVoice != null) BambiTakeoverTab.ChkAutonomyVoice.IsChecked = false;
                if (AppSettingsTab?.ChkSpeechWakeWord != null) AppSettingsTab.ChkSpeechWakeWord.IsChecked = false;
                if (AppSettingsTab?.ChkSpeechPushToTalk != null) AppSettingsTab.ChkSpeechPushToTalk.IsChecked = false;
                _isLoading = wasLoading;
                RefreshSheListeningTab();
                RefreshAutonomyVoiceHint();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SL_RevokeMicConsent_Click failed");
            }
        }

        /// <summary>Load the She's-Listening controls from settings + refresh status/gate. Called on tab show.</summary>
        internal void RefreshSheListeningTab()
        {
            if (SheListeningTab == null) return;
            var s = App.Settings?.Current;
            var wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                if (s != null)
                {
                    SheListeningTab.ChkSL_Mantras.IsChecked = s.SpokenMantrasEnabled && s.MicConsentGiven;
                    if (SheListeningTab.SldSL_MicSensitivity != null)
                    {
                        double sens = ThresholdToSens(s.SpeechLoudnessThreshold);
                        SheListeningTab.SldSL_MicSensitivity.Value = sens;
                        if (SheListeningTab.TxtSL_MicSensitivity != null)
                            SheListeningTab.TxtSL_MicSensitivity.Text = $"{(int)Math.Round(sens)}%";
                    }
                }
            }
            finally { _isLoading = wasLoading; }

            // "Revoke consent" only means something once consent exists.
            if (SheListeningTab.SL_PrivacyCard != null)
                SheListeningTab.SL_PrivacyCard.Visibility =
                    s?.MicConsentGiven == true ? Visibility.Visible : Visibility.Collapsed;

            RefreshWakeEngineStatus();
            RefreshSheListeningDeviceChips();
            RefreshSheListeningStatus();
            RefreshPremiumGate(SheListeningTab.SheListeningGate);
        }

        /// <summary>
        /// Repaint the read-only microphone chips on She's Listening AND re-seed the voice-mode
        /// toggles in Settings → Devices from the settings file.
        ///
        /// <para>Both halves matter. The chips are display: the device, wake word, push-to-talk and
        /// headphone rows on this tab were live editors until Phase 2 and are now a summary of what
        /// Settings → Devices owns. The re-seed is the other direction: the master Start/Stop button
        /// (<see cref="ToggleVoiceMic"/> / <see cref="DisarmVoiceMic"/>) and the title-bar privacy
        /// pill write SpeechWakeWordEnabled and SpeechPushToTalkEnabled straight to settings without
        /// touching a checkbox, so the Settings page has to be told, or arming the mic here would
        /// leave the toggle over there stale until the next full LoadSettings.</para>
        ///
        /// Everything is null-guarded — either view may not be realized yet.
        /// </summary>
        internal void RefreshSheListeningDeviceChips()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            // Settings → Devices: seed, never fire the change handlers.
            if (AppSettingsTab != null)
            {
                var wasLoading = _isLoading;
                _isLoading = true;
                try
                {
                    if (AppSettingsTab.ChkSpeechWakeWord != null)
                        AppSettingsTab.ChkSpeechWakeWord.IsChecked = s.SpeechWakeWordEnabled && s.MicConsentGiven;
                    if (AppSettingsTab.ChkSpeechPushToTalk != null)
                        AppSettingsTab.ChkSpeechPushToTalk.IsChecked = s.SpeechPushToTalkEnabled && s.MicConsentGiven;
                    if (AppSettingsTab.TxtSpeechWakeWords != null)
                        AppSettingsTab.TxtSpeechWakeWords.Text =
                            string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords;
                    if (AppSettingsTab.TxtPttKey != null)
                        AppSettingsTab.TxtPttKey.Text =
                            string.IsNullOrWhiteSpace(s.SpeechPushToTalkKey) ? "F8" : s.SpeechPushToTalkKey;
                }
                finally { _isLoading = wasLoading; }
            }

            if (SheListeningTab == null) return;

            var device = string.IsNullOrWhiteSpace(s.SpeechInputDeviceName)
                ? Localization.Loc.Get("set2_mic_system_default")
                : s.SpeechInputDeviceName;
            var off = Localization.Loc.Get("set2_chip_off");

            if (SheListeningTab.TxtSL_MicDeviceChip != null)
                SheListeningTab.TxtSL_MicDeviceChip.Text = device;
            if (SheListeningTab.TxtSL_WakeWordChip != null)
                SheListeningTab.TxtSL_WakeWordChip.Text =
                    s.SpeechWakeWordEnabled && s.MicConsentGiven
                        ? (string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords)
                        : off;
            if (SheListeningTab.TxtSL_PttChip != null)
                SheListeningTab.TxtSL_PttChip.Text =
                    s.SpeechPushToTalkEnabled && s.MicConsentGiven
                        ? (string.IsNullOrWhiteSpace(s.SpeechPushToTalkKey) ? "F8" : s.SpeechPushToTalkKey)
                        : off;
            if (SheListeningTab.TxtSL_HeadphonesChip != null)
                SheListeningTab.TxtSL_HeadphonesChip.Text =
                    s.SpeechHeadphonesMode ? Localization.Loc.Get("set2_chip_on") : off;
        }

        // "Mic sensitivity" slider <-> loudness gate. Slider 0..100 maps INVERSELY to the RMS threshold:
        // 100% = most sensitive (lowest threshold, softest speech OK), 0% = strictest (must speak up).
        // Useful gate range only — far below this is room noise, far above rejects normal speech.
        private const double LoudThrAtMinSens = 0.045; // slider 0%
        private const double LoudThrAtMaxSens = 0.004; // slider 100%

        private static double SensToThreshold(double sens)
            => LoudThrAtMinSens - (LoudThrAtMinSens - LoudThrAtMaxSens) * (Math.Clamp(sens, 0, 100) / 100.0);
        private static double ThresholdToSens(double thr)
            => Math.Clamp((LoudThrAtMinSens - thr) / (LoudThrAtMinSens - LoudThrAtMaxSens) * 100.0, 0, 100);

        /// <summary>
        /// Mic-sensitivity slider: tunes the loudness gate (<see cref="Models.AppSettings.SpeechLoudnessThreshold"/>)
        /// that decides whether a recognized command/mantra was "said out loud". Applies live — the next
        /// listen reads the new value. Does NOT touch the wake word (that's calibration).
        /// </summary>
        internal void SL_MicSensitivity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SheListeningTab == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SpeechLoudnessThreshold = SensToThreshold(e.NewValue);
            App.Settings?.Save();
            if (SheListeningTab.TxtSL_MicSensitivity != null)
                SheListeningTab.TxtSL_MicSensitivity.Text = $"{(int)Math.Round(e.NewValue)}%";
        }

        /// <summary>Update the hero: mic readiness / armed state + the master Start/Stop button.</summary>
        private void RefreshSheListeningStatus()
        {
            UpdateMicPill(); // arm/disarm via the wake/PTT toggles flows through here — keep the pill honest

            if (SheListeningTab?.SL_StatusTitle == null) return;

            var available = App.Speech?.IsAvailable == true;
            var armed = available && MicIsArmed();

            if (SheListeningTab.BtnSL_MicMaster != null)
            {
                SheListeningTab.BtnSL_MicMaster.IsEnabled = available;
                SheListeningTab.BtnSL_MicMaster.Content = armed ? "■  Stop listening" : "▶  Start listening";
                SheListeningTab.BtnSL_MicMaster.Foreground = armed
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0xB0))
                    : new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
            }

            // FX (PR-4a): the mic disc breathes only while the mic is genuinely armed. Not "the tab
            // is open", not "a device exists" - armed. Everything else leaves it a still disc.
            SetSheListeningStatusPulse(armed);

            if (!available)
            {
                SheListeningTab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x5A, 0x4A, 0x6A));
                SheListeningTab.SL_StatusTitle.Text = "Microphone not ready";
                SheListeningTab.SL_StatusSub.Text =
                    App.Speech == null || !Services.Speech.SpeechService.HasCaptureDevice
                        ? "No microphone detected — connect one to use voice."
                        : "Offline speech model not installed yet — voice stays off until it is.";
                return;
            }

            if (armed)
            {
                SheListeningTab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
                SheListeningTab.SL_StatusTitle.Text = "She's listening";
                SheListeningTab.SL_StatusSub.Text = "The mic is open. Call her, then say a command.";
            }
            else
            {
                SheListeningTab.SL_StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
                SheListeningTab.SL_StatusTitle.Text = "Mic off";
                SheListeningTab.SL_StatusSub.Text = "Tap Start listening so she can hear you. Works with or without Takeover.";
            }
        }
    }
}
