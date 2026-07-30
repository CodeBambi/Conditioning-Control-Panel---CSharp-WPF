using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "She's Listening" Exclusive — the voice-control surface. Its toggles drive the SAME
    /// AppSettings as the Takeover tab, so rather than duplicate the consent/settings logic, these
    /// handlers mirror the value onto the Takeover control, call the existing handler (which runs
    /// consent + saves + RefreshVoiceInputModes), then reflect the result back. Both tabs stay in
    /// sync, and there is one source of truth for the logic.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Copy a checkbox's value to another without firing its Changed handler.</summary>
        private void MirrorCheck(CheckBox from, CheckBox to)
        {
            var wasLoading = _isLoading;
            _isLoading = true;
            to.IsChecked = from.IsChecked;
            _isLoading = wasLoading;
        }

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

        internal void SL_WakeWord_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            MirrorCheck(SheListeningTab.ChkSL_WakeWord, BambiTakeoverTab.ChkSpeechWakeWord);
            ChkSpeechWakeWord_Changed(sender, e);
            MirrorCheck(BambiTakeoverTab.ChkSpeechWakeWord, SheListeningTab.ChkSL_WakeWord);
            RefreshSheListeningStatus();
            RefreshPremiumRail();
            RefreshAutonomyVoiceHint(); // arming/disarming wake suppresses/restores surprise mantras
        }

        internal void SL_WakeWords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            var wasLoading = _isLoading;
            _isLoading = true;
            BambiTakeoverTab.TxtSpeechWakeWords.Text = SheListeningTab.TxtSL_WakeWords.Text;
            _isLoading = wasLoading;
            TxtSpeechWakeWords_LostFocus(sender, e);
            // The handler may normalize an empty box back to "hey bambi" — reflect that.
            _isLoading = true;
            SheListeningTab.TxtSL_WakeWords.Text = BambiTakeoverTab.TxtSpeechWakeWords.Text;
            _isLoading = wasLoading;
        }

        internal void SL_PushToTalk_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            MirrorCheck(SheListeningTab.ChkSL_PushToTalk, BambiTakeoverTab.ChkSpeechPushToTalk);
            ChkSpeechPushToTalk_Changed(sender, e);
            MirrorCheck(BambiTakeoverTab.ChkSpeechPushToTalk, SheListeningTab.ChkSL_PushToTalk);
            RefreshSheListeningStatus();
            RefreshPremiumRail();
            RefreshAutonomyVoiceHint(); // arming/disarming PTT suppresses/restores surprise mantras
        }

        /// <summary>
        /// Headphones / barge-in preference (AppSettings.SpeechHeadphonesMode). When on, the command
        /// listener skips the wait-until-she's-quiet echo guard so you can talk over her. Pure preference —
        /// no consent prompt, no mic re-arm needed; the listener reads it on the next turn.
        /// </summary>
        internal void SL_Headphones_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || SheListeningTab == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SpeechHeadphonesMode = SheListeningTab.ChkSL_Headphones.IsChecked == true;
            App.Settings?.Save();
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

            RefreshSheListeningTab(); // reload the sub-toggles + status
            RefreshPremiumRail();     // keep the dashboard Voice dot honest
            UpdateMicPill();          // privacy pill: wake word is now armed → mic is open
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
                if (BambiTakeoverTab?.ChkSpeechWakeWord != null) BambiTakeoverTab.ChkSpeechWakeWord.IsChecked = false;
                if (BambiTakeoverTab?.ChkSpeechPushToTalk != null) BambiTakeoverTab.ChkSpeechPushToTalk.IsChecked = false;
                _isLoading = wasLoading;
                RefreshSheListeningTab();
                RefreshAutonomyVoiceHint();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SL_RevokeMicConsent_Click failed");
            }
        }

        internal void SL_SetPttKey_Click(object sender, RoutedEventArgs e)
        {
            if (_capturingPttKey || SheListeningTab == null) return;
            _capturingPttKey = true;
            SheListeningTab.BtnSL_SetPttKey.Content = "Press a key…";
            PreviewKeyDown += CaptureSlPttKey;
        }

        private void CaptureSlPttKey(object sender, KeyEventArgs e)
        {
            if (!_capturingPttKey) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
                return;

            e.Handled = true;
            _capturingPttKey = false;
            PreviewKeyDown -= CaptureSlPttKey;

            var s = App.Settings?.Current;
            if (s != null)
            {
                s.SpeechPushToTalkKey = key.ToString();
                App.Settings?.Save();
                App.Autonomy?.RefreshVoiceInputModes();
            }
            var keyStr = key.ToString();
            if (SheListeningTab != null)
            {
                SheListeningTab.TxtSL_PttKey.Text = keyStr;
                SheListeningTab.BtnSL_SetPttKey.Content = "Set key…";
            }
            // Keep the Takeover tab's label in sync too.
            if (BambiTakeoverTab?.TxtPttKey != null) BambiTakeoverTab.TxtPttKey.Text = keyStr;
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
                    SheListeningTab.ChkSL_WakeWord.IsChecked = s.SpeechWakeWordEnabled && s.MicConsentGiven;
                    SheListeningTab.TxtSL_WakeWords.Text = string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords;
                    SheListeningTab.ChkSL_PushToTalk.IsChecked = s.SpeechPushToTalkEnabled && s.MicConsentGiven;
                    SheListeningTab.TxtSL_PttKey.Text = string.IsNullOrWhiteSpace(s.SpeechPushToTalkKey) ? "F8" : s.SpeechPushToTalkKey;
                    SheListeningTab.ChkSL_Headphones.IsChecked = s.SpeechHeadphonesMode;
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
            PopulateSlMicDevices();
            RefreshSheListeningStatus();
            RefreshPremiumGate(SheListeningTab.SheListeningGate);
        }

        /// <summary>Guards the mic-device combo while we rebuild it, so re-populating doesn't fire the handler.</summary>
        private bool _slMicPopulating;

        /// <summary>
        /// Fill the microphone picker with the available capture devices (index -1 = system default)
        /// and select the one stored in <see cref="Models.AppSettings.SpeechInputDeviceIndex"/>. Mirrors
        /// the webcam picker on the Lab tab. Safe to call repeatedly (e.g. on tab show / Refresh).
        /// </summary>
        private void PopulateSlMicDevices()
        {
            var combo = SheListeningTab?.CmbSL_MicDevice;
            if (combo == null) return;

            int saved = App.Settings?.Current?.SpeechInputDeviceIndex ?? -1;
            _slMicPopulating = true;
            try
            {
                combo.Items.Clear();
                ComboBoxItem? toSelect = null;
                foreach (var dev in Services.Speech.SpeechService.EnumerateInputDevices())
                {
                    var item = new ComboBoxItem { Content = dev.Name, Tag = dev.Index };
                    combo.Items.Add(item);
                    if (dev.Index == saved) toSelect = item;
                }
                // Fall back to "System default" if the saved device is gone (unplugged / reordered).
                combo.SelectedItem = toSelect ?? (combo.Items.Count > 0 ? combo.Items[0] : null);
            }
            finally { _slMicPopulating = false; }
        }

        /// <summary>
        /// User picked a microphone. Persist the index; <see cref="Services.Speech.SpeechService.ResolveDeviceNumber"/>
        /// reads it the next time the mic opens. If we're listening right now, cut the in-flight session and
        /// re-arm so the new device takes effect immediately.
        /// </summary>
        internal void SL_MicDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_slMicPopulating || _isLoading || SheListeningTab == null) return;
            if (SheListeningTab.CmbSL_MicDevice?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not int idx) return;

            var s = App.Settings?.Current;
            if (s == null) return;
            var name = idx < 0 ? "" : (item.Content?.ToString() ?? "");
            if (s.SpeechInputDeviceIndex == idx && s.SpeechInputDeviceName == name) return;
            s.SpeechInputDeviceIndex = idx;
            s.SpeechInputDeviceName = name; // matched by name on reopen — robust to ordinal reshuffle (#441b)
            App.Settings?.Save();

            // Apply live: stop the current capture so the wake loop reopens on the new device, then reconcile.
            if (MicIsArmed())
            {
                try { App.Speech?.StopListening(); } catch { }
                try { App.Autonomy?.RefreshVoiceInputModes(); } catch { }
            }
        }

        /// <summary>Re-scan connected microphones (devices may have been plugged in since the tab opened).</summary>
        internal void SL_MicRefresh_Click(object sender, RoutedEventArgs e)
        {
            PopulateSlMicDevices();
            RefreshSheListeningStatus();
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
