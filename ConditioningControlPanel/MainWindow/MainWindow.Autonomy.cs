using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Autonomy mode: autonomous companion behavior controls.
    public partial class MainWindow
    {
        #region Autonomy Mode

        internal void ChkAutonomyEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = BambiTakeoverTab.ChkAutonomyEnabled.IsChecked ?? false;

            // Lockdown mode: a RUNNING Takeover cannot be disabled while locked in (#514) —
            // same rule as the engine stop / exit / panic-key guards. Keyed on the service
            // actually running (not the persisted setting) so a stale setting can be cleared.
            if (!isEnabled && App.Autonomy?.IsEnabled == true && App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_stop_dur"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                _isLoading = true;
                try { BambiTakeoverTab.ChkAutonomyEnabled.IsChecked = true; }
                finally { _isLoading = false; }
                return;
            }

            // If enabling for the first time, show consent dialog
            if (isEnabled && !App.Settings.Current.AutonomyConsentGiven)
            {
                var result = MessageBox.Show(
                    "AUTONOMY MODE\n\n" +
                    "This feature allows the companion to autonomously trigger effects:\n" +
                    "• Flash images\n" +
                    "• Videos (without strict mode)\n" +
                    "• Subliminal messages\n" +
                    "• Make comments\n\n" +
                    "She will act on her own within your configured intensity settings.\n\n" +
                    "You can disable this at any time. Videos triggered autonomously will NEVER use strict mode.\n\n" +
                    "Do you consent to enable Autonomy Mode?",
                    "Enable Autonomy Mode",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    App.Settings.Current.AutonomyConsentGiven = true;
                }
                else
                {
                    BambiTakeoverTab.ChkAutonomyEnabled.IsChecked = false;
                    return;
                }
            }

            App.Settings.Current.AutonomyModeEnabled = isEnabled;

            // Start/stop autonomy service (works independently of engine!)
            // Requires Patreon + Consent. Canonical gate: Settings.PatreonTier is only written
            // at login and goes stale for live-validated subscribers (#465).
            var hasPatreon = App.Patreon?.HasPremiumAccess == true;

            if (isEnabled)
            {
                if (!hasPatreon)
                {
                    App.Logger?.Warning("Autonomy Mode enabled but Patreon access missing - service will not start");
                    MessageBox.Show(
                        "Autonomy Mode requires Patreon access.\n\n" +
                        "The setting has been saved, but the feature will not activate until you have Patreon access.",
                        "Patreon Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    App.Autonomy?.Stop();
                }
                else if (App.Settings.Current.AutonomyConsentGiven)
                {
                    App.Autonomy?.Start();
                }
            }
            else
            {
                App.Autonomy?.Stop();
            }
            // Keep the Takeover tab's Start/Stop button in sync. This handler fires when
            // the feature is toggled from anywhere that flips ChkAutonomyEnabled (the premium
            // rail chip, etc.); without this the tab button stays stuck on "Stop". Treat the
            // patreon-missing case as off, since the service was just stopped above.
            UpdateAutonomyButtonState(isEnabled && hasPatreon);

            App.Logger?.Information("Autonomy Mode toggled: {Enabled} (Engine running: {EngineRunning}, Patreon: {Patreon})",
                isEnabled, _isRunning, hasPatreon);

            App.Settings.Save();

            // Sync avatar menu state
            Dispatcher.BeginInvoke(() => _avatarTubeWindow?.UpdateQuickMenuState());
        }

        internal void BtnAutonomyStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            var isCurrentlyEnabled = settings.AutonomyModeEnabled;

            // Lockdown mode: a RUNNING Takeover cannot be disabled while locked in (#514).
            if (isCurrentlyEnabled && App.Autonomy?.IsEnabled == true && App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_stop_dur"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            // If starting for the first time, show consent dialog
            if (!isCurrentlyEnabled && !settings.AutonomyConsentGiven)
            {
                var result = MessageBox.Show(
                    "AUTONOMY MODE\n\n" +
                    "This feature allows the companion to autonomously trigger effects:\n" +
                    "• Flash images\n" +
                    "• Videos (without strict mode)\n" +
                    "• Subliminal messages\n" +
                    "• Make comments\n\n" +
                    "She will act on her own schedule based on your intensity setting.\n" +
                    "You can stop her at any time by clicking the Stop button.\n\n" +
                    "Do you consent to enabling Autonomy Mode?",
                    "Autonomy Mode Consent",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                settings.AutonomyConsentGiven = true;
            }

            // Toggle the mode
            settings.AutonomyModeEnabled = !isCurrentlyEnabled;
            App.Settings.Save();

            // Update button appearance
            UpdateAutonomyButtonState(!isCurrentlyEnabled);

            // Start/stop autonomy service
            if (!isCurrentlyEnabled)
            {
                App.Autonomy?.Start();
            }
            else
            {
                App.Autonomy?.Stop();
            }

            App.Logger?.Information("Autonomy Mode button toggled: {Enabled}", !isCurrentlyEnabled);

            // Sync avatar menu state
            Dispatcher.BeginInvoke(() => _avatarTubeWindow?.UpdateQuickMenuState());
        }

        private void UpdateAutonomyButtonState(bool isEnabled)
        {
            if (BambiTakeoverTab.BtnAutonomyStartStop == null) return;

            if (isEnabled)
            {
                BambiTakeoverTab.BtnAutonomyStartStop.Content = Loc.Get("btn_stop_2");
                BambiTakeoverTab.BtnAutonomyStartStop.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")); // Pink
            }
            else
            {
                BambiTakeoverTab.BtnAutonomyStartStop.Content = Loc.Get("btn_start_2");
                BambiTakeoverTab.BtnAutonomyStartStop.Foreground = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // Light green
            }
        }

        /// <summary>
        /// Called from AvatarTubeWindow to sync the button/checkbox state when toggled from avatar menu
        /// </summary>
        public void SyncAutonomyCheckbox(bool isEnabled)
        {
            // Read the actual setting value to ensure consistency
            var actualValue = App.Settings?.Current?.AutonomyModeEnabled ?? false;
            App.Logger?.Information("MainWindow.SyncAutonomyCheckbox called with isEnabled={IsEnabled}, actualSetting={Actual}", isEnabled, actualValue);

            // Use BeginInvoke to ensure UI update happens after current operation completes
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    // Re-read the setting inside dispatcher to get the latest value
                    var settingValue = App.Settings?.Current?.AutonomyModeEnabled ?? false;

                    // Update button state
                    UpdateAutonomyButtonState(settingValue);

                    // Also update hidden checkbox for backwards compatibility
                    if (BambiTakeoverTab.ChkAutonomyEnabled != null)
                    {
                        var wasLoading = _isLoading;
                        _isLoading = true;
                        BambiTakeoverTab.ChkAutonomyEnabled.IsChecked = settingValue;
                        _isLoading = wasLoading;
                    }

                    App.Logger?.Information("MainWindow.SyncAutonomyCheckbox synced to {Value}", settingValue);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "MainWindow.SyncAutonomyCheckbox failed");
                }
            }));
        }

        internal void SliderAutonomyIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || BambiTakeoverTab.TxtAutonomyIntensity == null) return;
            BambiTakeoverTab.TxtAutonomyIntensity.Text = $"{(int)e.NewValue}";
            App.Settings.Current.AutonomyIntensity = (int)e.NewValue;
            App.Settings.Save();
        }

        internal void SliderAutonomyCooldown_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || BambiTakeoverTab.TxtAutonomyCooldown == null) return;
            BambiTakeoverTab.TxtAutonomyCooldown.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.AutonomyCooldownSeconds = (int)e.NewValue;
            App.Settings.Save();
        }

        internal void SliderAutonomyInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || BambiTakeoverTab.TxtAutonomyInterval == null) return;
            BambiTakeoverTab.TxtAutonomyInterval.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.AutonomyRandomIntervalSeconds = (int)e.NewValue;
            App.Autonomy?.RefreshRandomTimer();
            App.Settings.Save();
        }

        internal void ChkAutonomyIdle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyIdleTriggerEnabled = BambiTakeoverTab.ChkAutonomyIdle.IsChecked ?? false;
            App.Autonomy?.RefreshIdleTimer();
            App.Settings.Save();
        }

        internal void ChkAutonomyRandom_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyRandomTriggerEnabled = BambiTakeoverTab.ChkAutonomyRandom.IsChecked ?? false;
            App.Autonomy?.RefreshRandomTimer();
            App.Settings.Save();
        }

        internal void ChkAutonomyTimeAware_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyTimeAwareEnabled = BambiTakeoverTab.ChkAutonomyTimeAware.IsChecked ?? false;
            App.Settings.Save();
        }

        internal void ChkAutonomyBehavior_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyCanTriggerFlash = BambiTakeoverTab.ChkAutonomyFlash.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerVideo = BambiTakeoverTab.ChkAutonomyVideo.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerWebVideo = BambiTakeoverTab.ChkAutonomyWebVideo.IsChecked ?? false;
            App.Settings.Current.TakeoverVideosStrict = BambiTakeoverTab.ChkTakeoverVideosStrict.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerSubliminal = BambiTakeoverTab.ChkAutonomySubliminal.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBubbles = BambiTakeoverTab.ChkAutonomyBubbles.IsChecked ?? false;
            App.Settings.Current.AutonomyCanComment = BambiTakeoverTab.ChkAutonomyComment.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerMindWipe = BambiTakeoverTab.ChkAutonomyMindWipe.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerLockCard = BambiTakeoverTab.ChkAutonomyLockCard.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerSpiral = BambiTakeoverTab.ChkAutonomySpiral.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerPinkFilter = BambiTakeoverTab.ChkAutonomyPinkFilter.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBouncingText = BambiTakeoverTab.ChkAutonomyBouncingText.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBubbleCount = BambiTakeoverTab.ChkAutonomyBubbleCount.IsChecked ?? false;
            App.Settings.Save();
        }

        internal void SliderAutonomyAnnounce_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || BambiTakeoverTab.TxtAutonomyAnnounce == null) return;
            BambiTakeoverTab.TxtAutonomyAnnounce.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.AutonomyAnnouncementChance = (int)e.NewValue;
            App.Settings.Save();
        }

        internal void BtnTestAutonomy_Click(object sender, RoutedEventArgs e)
        {
            App.Autonomy?.TestTrigger();
        }

        internal void BtnTestVoice_Click(object sender, RoutedEventArgs e)
        {
            App.Autonomy?.TestVoiceCommand();
        }

        internal void ChkAutonomyVoice_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var turningOn = BambiTakeoverTab.ChkAutonomyVoice.IsChecked == true;

            // First time enabling: require explicit mic consent. If declined, revert the toggle.
            if (turningOn && !s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = this };
                var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                if (!ok)
                {
                    var wasLoading = _isLoading;
                    _isLoading = true;                       // suppress the re-fired Changed
                    BambiTakeoverTab.ChkAutonomyVoice.IsChecked = false;
                    _isLoading = wasLoading;
                    return;
                }
            }

            s.AutonomyCanTriggerVoiceCommand = turningOn;
            App.Settings?.Save();

            RefreshAutonomyVoiceHint();
        }

        /// <summary>
        /// Keep the "Surprise me with mantras" hint honest. The Takeover auto-trigger is suppressed
        /// whenever the user is driving the mic themselves (wake word / push-to-talk on) — see
        /// AutonomyService candidate gating — so when it's enabled-but-dormant, say so in amber instead
        /// of leaving the user wondering why surprise mantras never fire. Also surfaces an unavailable
        /// engine. Safe to call any time; no-ops if the control isn't built yet.
        /// </summary>
        internal void RefreshAutonomyVoiceHint()
        {
            var hint = BambiTakeoverTab?.TxtAutonomyVoiceHint;
            if (hint == null) return;
            var s = App.Settings?.Current;
            bool on = s?.AutonomyCanTriggerVoiceCommand == true && s.MicConsentGiven;

            var amber = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
            var grey = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

            if (on && App.Speech?.IsAvailable != true)
            {
                hint.Foreground = amber;
                hint.Text = App.Speech == null || !Services.Speech.SpeechService.HasCaptureDevice
                    ? "No microphone detected — connect one to use this."
                    : "Speech model not installed yet — voice prompts stay off until it is.";
                return;
            }
            if (on && (s!.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled))
            {
                hint.Foreground = amber;
                hint.Text = "Paused — you're driving the mic with “Hey Bambi” / push-to-talk, so she only does mantras when you ask. Turn those off in She's Listening for surprise mantras.";
                return;
            }
            hint.Foreground = grey;
            hint.Text = on
                ? "During Takeover she'll now and then ask you to repeat a phrase. Offline mic — opens only when she prompts you."
                : "During Takeover she'll now and then ask you to repeat a phrase. Suppressed while you're driving the mic yourself.";
        }

        internal void ChkAutonomyResume_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AutonomyResumeOnStartup = BambiTakeoverTab.ChkAutonomyResumeOnStartup.IsChecked == true;
            App.Settings?.Save();
        }

        internal void ChkShowTakeoverCountdown_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.ShowTakeoverCountdownBar = BambiTakeoverTab.ChkShowTakeoverCountdown.IsChecked == true;
            App.Settings?.Save();
        }

        internal void ChkSpeechWakeWord_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var turningOn = BambiTakeoverTab.ChkSpeechWakeWord.IsChecked == true;

            if (turningOn)
            {
                // Mic consent first (shared with the prompt-only path).
                if (!s.MicConsentGiven)
                {
                    var dlg = new MicConsentDialog { Owner = this };
                    var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                    if (!ok) { RevertToggle(BambiTakeoverTab.ChkSpeechWakeWord); return; }
                }

                // Always-on mic is more invasive than the prompt-only path — get an explicit OK.
                var confirm = MessageBox.Show(this,
                    "Wake word keeps the microphone open continuously while Takeover is running so she can hear you call her.\n\n" +
                    "Everything stays offline — audio is processed on your device and never recorded or sent anywhere.\n\n" +
                    "Turn on always-on listening?",
                    "Always-on microphone",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) { RevertToggle(BambiTakeoverTab.ChkSpeechWakeWord); return; }
            }

            s.SpeechWakeWordEnabled = turningOn;
            App.Settings?.Save();
            App.Autonomy?.RefreshVoiceInputModes();
            UpdateVoiceModeHints();
        }

        internal void TxtSpeechWakeWords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var text = BambiTakeoverTab.TxtSpeechWakeWords.Text?.Trim();
            s.SpeechWakeWords = string.IsNullOrWhiteSpace(text) ? "hey bambi" : text;
            if (string.IsNullOrWhiteSpace(text)) BambiTakeoverTab.TxtSpeechWakeWords.Text = "hey bambi";
            App.Settings?.Save();
            // Restart the loop so new phrases take effect immediately.
            App.Autonomy?.RefreshVoiceInputModes();
        }

        internal void ChkSpeechPushToTalk_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var turningOn = BambiTakeoverTab.ChkSpeechPushToTalk.IsChecked == true;
            if (turningOn && !s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = this };
                var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                if (!ok) { RevertToggle(BambiTakeoverTab.ChkSpeechPushToTalk); return; }
            }

            s.SpeechPushToTalkEnabled = turningOn;
            App.Settings?.Save();
            App.Autonomy?.RefreshVoiceInputModes();
            UpdateVoiceModeHints();
        }

        private bool _capturingPttKey;

        internal void BtnSetPttKey_Click(object sender, RoutedEventArgs e)
        {
            if (_capturingPttKey) return;
            _capturingPttKey = true;
            BambiTakeoverTab.BtnSetPttKey.Content = "Press a key…";
            PreviewKeyDown += CapturePttKey;
        }

        private void CapturePttKey(object sender, KeyEventArgs e)
        {
            if (!_capturingPttKey) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            // Ignore lone modifier presses — wait for a real key.
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
                return;

            e.Handled = true;
            _capturingPttKey = false;
            PreviewKeyDown -= CapturePttKey;

            var s = App.Settings?.Current;
            if (s != null)
            {
                s.SpeechPushToTalkKey = key.ToString();
                App.Settings?.Save();
                App.Autonomy?.RefreshVoiceInputModes();
            }
            BambiTakeoverTab.TxtPttKey.Text = key.ToString();
            BambiTakeoverTab.BtnSetPttKey.Content = "Set key…";
        }

        /// <summary>Refresh the small grey availability hints under the voice toggles.</summary>
        private void UpdateVoiceModeHints()
        {
            if (BambiTakeoverTab?.TxtAutonomyVoiceHint == null) return;
            var available = App.Speech?.IsAvailable == true;
            if (!available)
            {
                BambiTakeoverTab.TxtAutonomyVoiceHint.Text =
                    App.Speech == null || !Services.Speech.SpeechService.HasCaptureDevice
                        ? "No microphone detected — connect one to use this."
                        : "Speech model not installed yet — voice prompts stay off until it is.";
            }
            else
            {
                BambiTakeoverTab.TxtAutonomyVoiceHint.Text = "Offline mic. Opens only when she prompts you.";
            }
        }

        private void RevertToggle(System.Windows.Controls.CheckBox box)
        {
            var wasLoading = _isLoading;
            _isLoading = true;                 // suppress the re-fired Changed
            box.IsChecked = false;
            _isLoading = wasLoading;
        }

        internal void BtnForceStartAutonomy_Click(object sender, RoutedEventArgs e)
        {
            App.Autonomy?.ForceStart();
        }

        #endregion
    }
}
