// PORTED from ConditioningControlPanel/MainWindow/MainWindow.LabTab.cs (WPF 7.1.5): UpdateMicPill,
// MicActivePill_Click (DisarmVoiceMic), WireWebcamActivePill, WebcamActivePill_Click (:108-150, :477).
// Ledger row platform#17: the header "Mic active" / "Camera active" pills are privacy stops.
// The camera pill stops Focus Gaze first, then the Blink Trainer, then the tracker (WPF order).

using System;
using System.ComponentModel;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private void InitializeSafetyPills()
        {
            var tracker = WebcamTracker.Instance;
            Action onCam = () => Dispatcher.UIThread.Post(UpdateWebcamPill);
            EventHandler<bool> onMic = (_, _) => Dispatcher.UIThread.Post(UpdateMicPill);
            PropertyChangedEventHandler onSetting = (_, e) =>
            {
                if (e.PropertyName is nameof(CoreSettings.Current.MicConsentGiven) or nameof(CoreSettings.Current.SpeechWakeWordEnabled))
                    Dispatcher.UIThread.Post(UpdateMicPill);
            };
            ConditioningControlPanel.Services.Speech.SpeechEngine? hookedSpeech = null;
            Opened += (_, _) =>
            {
                tracker.StateChanged += onCam;
                hookedSpeech = VoiceSpeech;
                if (hookedSpeech != null) hookedSpeech.ListeningChanged += onMic;
                CoreSettings.Current.PropertyChanged += onSetting;
                UpdateMicPill();
                UpdateWebcamPill();
            };
            Closed += (_, _) =>
            {
                tracker.StateChanged -= onCam;
                if (hookedSpeech != null) hookedSpeech.ListeningChanged -= onMic;
                CoreSettings.Current.PropertyChanged -= onSetting;
            };
        }

        /// <summary>WPF UpdateMicPill: lit whenever the mic is reachable - any live capture, or the whole
        /// time the wake word is armed (consent + wake enabled), since it holds the mic open.</summary>
        internal void UpdateMicPill()
        {
            var s = CoreSettings.Current;
            bool wakeContinuous = s.MicConsentGiven && s.SpeechWakeWordEnabled;
            bool capturing = VoiceSpeech?.IsListening == true;
            if (Named<global::Avalonia.Controls.Border>("MicActivePill") is { } pill) pill.IsVisible = wakeContinuous || capturing;
        }

        /// <summary>WPF WireWebcamActivePill: lit while the tracker runs.</summary>
        internal void UpdateWebcamPill()
        {
            if (Named<global::Avalonia.Controls.Border>("WebcamActivePill") is { } pill) pill.IsVisible = WebcamTracker.Instance.IsRunning;
        }

        // The privacy stop: fully disarm the offline mic so it stays off (wake word + push-to-talk
        // cleared, live capture cut, open Voice Lock Cards dropped to typed solve so the lock holds).
        private void MicActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            try { DisarmVoiceMic(); } catch (Exception ex) { Log.Debug("MicActivePill_Click failed: {Error}", ex.Message); }
            UpdateMicPill();
        }

        // The camera's stop: every consumer that shares the tracker releases together. StopAsync, never
        // inline: the teardown joins the capture thread (up to 5 s) on a wedged driver.
        private async void WebcamActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            await StopCameraConsumersAsync();
            UpdateWebcamPill();
        }

        /// <summary>WPF WebcamActivePill_Click (MainWindow.LabTab.cs:477-493): Focus Gaze stands down FIRST, so
        /// nothing is still reading the tracker while it tears down, then the Blink Trainer, then the camera.</summary>
        internal static async System.Threading.Tasks.Task StopCameraConsumersAsync()
        {
            // IA10: the master and its saved intent go off too, or the next feature to start the tracker re-arms it.
            try { GazeFocusHead.Instance.StandDownForGood(); } catch (Exception ex) { Log.Debug("Camera pill: Focus Gaze stop failed: {Error}", ex.Message); }
            try { Overlays.BlinkTrainerSession.Stop(); } catch { }
            try { await WebcamTracker.Instance.StopAsync(); }
            catch (Exception ex) { Log.Debug("WebcamActivePill_Click stop failed: {Error}", ex.Message); }
        }
    }
}
