using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    // Voice modes: THE wake word and push-to-talk toggles since WPF Phase 2 (MainWindow.Autonomy.cs
    // ChkSpeechWakeWord_Changed :608, ChkSpeechPushToTalk_Changed :667, BtnSetPttKey_Click :698), and the
    // live apply for the microphone, the phrases and headphones mode (WPF DevicesSettingsSection.xaml.cs
    // :238 / :284). Recognition only: nothing here speaks. Turning a mode OFF is never gated.
    public partial class DevicesSettingsSection
    {
        /// <summary>Tests answer the two prompts without a window. Null = the real dialogs.</summary>
        internal Func<Task<bool>>? AskMicConsent;
        internal Func<Task<bool>>? AskAlwaysOn;
        /// <summary>Tests: counts the live applies (the shell is absent headless).</summary>
        internal int VoiceApplies;

        private bool _voiceReverting;
        private bool _capturingPttKey;

        private void WireVoiceModes()
        {
            ChkSpeechWakeWord.IsCheckedChanged += ChkSpeechWakeWord_Changed;
            ChkSpeechPushToTalk.IsCheckedChanged += ChkSpeechPushToTalk_Changed;
            BtnSetPttKey.Click += BtnSetPttKey_Click;
        }

        private MainShellWindow? Shell => TopLevel.GetTopLevel(this) as MainShellWindow ?? MainShellWindow.Current;

        /// <summary>WPF App.Autonomy.RefreshVoiceInputModes + Main.RefreshSheListeningDeviceChips.
        /// <paramref name="reopen"/> cuts the open capture first so the wake loop reopens on the new
        /// device or phrases (WPF App.Speech.StopListening).</summary>
        private void ApplyVoiceLive(bool reopen)
        {
            VoiceApplies++;
            try
            {
                var shell = Shell;
                if (shell == null) return;
                if (reopen) shell.ReopenVoiceInput(); else shell.RefreshVoiceInputModes();
                shell.RefreshSheListeningDeviceChips();
            }
            catch (Exception ex) { Log.Warning(ex, "Settings/Devices: voice apply failed"); }
        }

        private void RevertVoiceToggle(CheckBox box)
        {
            _voiceReverting = true;
            try { box.IsChecked = false; } finally { _voiceReverting = false; }
        }

        private async Task<bool> MicConsentAsync()
        {
            if (CoreSettings.Current.MicConsentGiven) return true;
            if (AskMicConsent != null) return await AskMicConsent();
            if (TopLevel.GetTopLevel(this) is not Window owner) return false;
            var dlg = new MicConsentDialog();
            await dlg.ShowDialogSafe(owner);
            return dlg.ConsentGiven;
        }

        internal async void ChkSpeechWakeWord_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading || _voiceReverting) return;
            try
            {
                var s = CoreSettings.Current;
                bool on = ChkSpeechWakeWord.IsChecked == true;
                if (on)
                {
                    if (!Services.TierGate.DemandPremium(Loc.Get("tab_shelistening"), "voice")) { RevertVoiceToggle(ChkSpeechWakeWord); return; }
                    if (!await MicConsentAsync()) { RevertVoiceToggle(ChkSpeechWakeWord); return; }
                    // Always-on mic is more invasive than the prompt-only path: an explicit OK (WPF copy).
                    bool yes = AskAlwaysOn != null
                        ? await AskAlwaysOn()
                        : TopLevel.GetTopLevel(this) is Window owner && await MessageDialog.ConfirmAsync(owner, "Always-on microphone",
                            "Wake word keeps the microphone open continuously while Takeover is running so she can hear you call her.\n\n" +
                            "Everything stays offline - audio is processed on your device and never recorded or sent anywhere.\n\n" +
                            "Turn on always-on listening?", okText: "Yes", cancelText: "No");
                    if (!yes) { RevertVoiceToggle(ChkSpeechWakeWord); return; }
                }
                s.SpeechWakeWordEnabled = on;
                CoreSettings.Save();
                ApplyVoiceLive(reopen: false);
            }
            catch (Exception ex) { Log.Warning(ex, "Settings/Devices: wake word toggle failed"); RevertVoiceToggle(ChkSpeechWakeWord); }
        }

        internal async void ChkSpeechPushToTalk_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading || _voiceReverting) return;
            try
            {
                var s = CoreSettings.Current;
                bool on = ChkSpeechPushToTalk.IsChecked == true;
                if (on)
                {
                    if (!Services.TierGate.DemandPremium(Loc.Get("tab_shelistening"), "voice")) { RevertVoiceToggle(ChkSpeechPushToTalk); return; }
                    if (!await MicConsentAsync()) { RevertVoiceToggle(ChkSpeechPushToTalk); return; }
                }
                s.SpeechPushToTalkEnabled = on;
                CoreSettings.Save();
                ApplyVoiceLive(reopen: false);
            }
            catch (Exception ex) { Log.Warning(ex, "Settings/Devices: push to talk toggle failed"); RevertVoiceToggle(ChkSpeechPushToTalk); }
        }

        /// <summary>WPF BtnSetPttKey_Click / CapturePttKey: the next real key is the push-to-talk key; a
        /// lone modifier keeps waiting. Losing the window cancels, as the pause-key capture here does.</summary>
        internal void BtnSetPttKey_Click(object? sender, RoutedEventArgs e)
        {
            if (_capturingPttKey) return;
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            _capturingPttKey = true;
            SetButtonLabel(BtnSetPttKey, Loc.Get("rf_btn_press_a_key"));
            top.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
            if (top is Window window) window.Deactivated += OnCancel;

            void Detach()
            {
                top.RemoveHandler(KeyDownEvent, OnKey);
                if (top is Window w) w.Deactivated -= OnCancel;
                _capturingPttKey = false;
                SetButtonLabel(BtnSetPttKey, Loc.Get("set2_btn_set_key"));
            }

            void OnCancel(object? s, EventArgs a) => Detach();

            void OnKey(object? s, KeyEventArgs k)
            {
                if (k.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None) return;
                k.Handled = true;
                Detach();
                CoreSettings.Current.SpeechPushToTalkKey = k.Key.ToString();
                CoreSettings.Save();
                TxtPttKey.Text = k.Key.ToString();
                ApplyVoiceLive(reopen: false);
            }
        }
    }
}
