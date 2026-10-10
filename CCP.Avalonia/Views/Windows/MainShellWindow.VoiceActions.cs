// PORTED from ConditioningControlPanel/Services/AutonomyService.VoiceCommandHost.cs (VoiceAction's
// spiral / pink / mind wipe / quiz / bubble count / session / volume / video pause entries and
// WaitForAvatarQuietAsync) and MainWindow.Patreon.cs ApplyVoiceMute / AdjustMasterVolume.
//
// WPF's spiral and pink are OverlayService "sustained" holds that never touch the user's switch. This
// head's overlays read the switch, so a spoken "on" flips it for as long as the hold lasts and hands
// it back on "off", on panic and when the window closes (the same shape as Takeover's pink pulse).
// Not on this head yet, so left out of the grammar: keyword_on / keyword_off, shake_once, deeper.

using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>A spoken "spiral" / "pink" is on screen with the engine off (the overlays' ShouldShow).</summary>
        internal static bool VoiceSpiralHold { get; private set; }
        internal static bool VoicePinkHold { get; private set; }

        private bool _voiceSpiralFlipped;       // the hold switched the user's spiral on
        private bool _voicePinkFlipped;         // ... and the pink filter
        private int _voicePinkBase = -1;        // the user's opacity while the hold raised it (-1 = untouched)
        private int _voicePinkApplied;
        private int _preMuteMasterVolume;       // WPF _preMuteMasterVolume
        private TaskCompletionSource? _voiceClip;   // her confirmation clip in the air (echo guard)

        /// <summary>The second half of <see cref="VoiceAction"/>: the intents added with HC12.</summary>
        private Action? VoiceActionMore(string name) => name switch
        {
            "video_pause" when CoreEngine.Video != null => () => MandatoryVideoOverlay.Instance.SetExternalPause(true),
            "video_resume" when CoreEngine.Video != null => () => MandatoryVideoOverlay.Instance.SetExternalPause(false),
            // The user's OWN spiral, at the opacity they configured (#1051).
            "spiral_on" => () => VoiceSpiral(true),
            "spiral_off" => () => VoiceSpiral(false),
            "pink_on" => () => VoicePink(true),
            "pink_off" => () => VoicePink(false),
            "wipe_once" when CoreMindWipe.TriggerOnceProvider != null => CoreMindWipe.TriggerOnce,
            "quiz_once" when CoreEngine.PopQuiz != null => () => CoreEngine.PopQuiz?.Show(),
            // forceTest: true - Trigger() bails when the engine isn't running.
            "count_once" when CoreEngine.BubbleCount != null => () => CoreEngine.BubbleCount?.Trigger(forceTest: true),
            "pause" when App.Sessions != null => () => ((RemoteCommands.IRemoteHead)this).Session("pause_session", null),
            "resume" when App.Sessions != null => () => ((RemoteCommands.IRemoteHead)this).Session("resume_session", null),
            // No StopLocked on mute: silencing is not escaping (see the mute intent in Core).
            "mute" => () => ApplyVoiceMute(true),
            "unmute" => () => ApplyVoiceMute(false),
            "louder" => () => AdjustMasterVolume(+15),
            "quieter" => () => AdjustMasterVolume(-15),
            _ => null,
        };

        internal void VoiceSpiral(bool on)
        {
            var s = CoreSettings.Current;
            if (on)
            {
                if (!s.SpiralEnabled) { s.SpiralEnabled = true; _voiceSpiralFlipped = true; }
                VoiceSpiralHold = true;
            }
            else
            {
                VoiceSpiralHold = false;
                if (_voiceSpiralFlipped) { s.SpiralEnabled = false; _voiceSpiralFlipped = false; }
            }
            SpiralOverlay.Refresh(this);
        }

        /// <summary>WPF "pink_on": 40% is this command's own floor, but it never DIMS a stronger live tint (#1051).</summary>
        internal void VoicePink(bool on)
        {
            var s = CoreSettings.Current;
            if (on)
            {
                if (!s.PinkFilterEnabled) { s.PinkFilterEnabled = true; _voicePinkFlipped = true; }
                if (s.PinkFilterOpacity < 40 && _voicePinkBase < 0)
                {
                    _voicePinkBase = s.PinkFilterOpacity;
                    s.PinkFilterOpacity = 40;
                    _voicePinkApplied = s.PinkFilterOpacity;   // the setter clamps
                }
                VoicePinkHold = true;
            }
            else
            {
                VoicePinkHold = false;
                if (_voicePinkFlipped) { s.PinkFilterEnabled = false; _voicePinkFlipped = false; }
                // Hand the opacity back unless the user moved the slider meanwhile.
                if (_voicePinkBase >= 0 && s.PinkFilterOpacity == _voicePinkApplied) s.PinkFilterOpacity = _voicePinkBase;
                _voicePinkBase = -1;
            }
            PinkFilterOverlay.Refresh(this);
        }

        /// <summary>Panic and window close: a spoken spiral or tint never outlives either.</summary>
        internal void DropVoiceHolds()
        {
            try
            {
                if (VoiceSpiralHold || _voiceSpiralFlipped) VoiceSpiral(false);
                if (VoicePinkHold || _voicePinkFlipped || _voicePinkBase >= 0) VoicePink(false);
            }
            catch (Exception ex) { Log.Debug(ex, "DropVoiceHolds failed"); }
        }

        /// <summary>WPF ApplyVoiceMute: master volume, whispers and her voice, with every mirror told.</summary>
        internal void ApplyVoiceMute(bool muted)
        {
            try
            {
                var s = CoreSettings.Current;
                if (muted)
                {
                    if (s.MasterVolume > 0) _preMuteMasterVolume = s.MasterVolume;   // remember for unmute
                    s.MasterVolume = 0;
                    s.SubAudioEnabled = false;   // "mute whispers" (bark / voiceline audio)
                }
                else
                {
                    s.MasterVolume = _preMuteMasterVolume > 0 ? _preMuteMasterVolume : 70;
                    s.SubAudioEnabled = true;
                }
                s.AvatarMuted = muted;
                LayeredAudio.Instance?.SetMasterVolumeLive();
                try
                {
                    var tube = global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.Live;
                    if (muted) tube?.StopSpokenAudio();
                    tube?.UpdateQuickMenuState();
                }
                catch (Exception ex) { Log.Debug(ex, "ApplyVoiceMute: tube refresh"); }
                AudioSettingsBinder.RaiseChanged();
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Warning(ex, "ApplyVoiceMute failed"); }
        }

        /// <summary>WPF AdjustMasterVolume -> SetMasterVolume: clamp, mirror into Settings, save.</summary>
        internal void AdjustMasterVolume(int delta)
        {
            try
            {
                var s = CoreSettings.Current;
                global::ConditioningControlPanel.Services.Launcher.LauncherMediaSettings.ApplyMasterVolume(s, s.MasterVolume + delta);
                LayeredAudio.Instance?.SetMasterVolumeLive();
                AudioSettingsBinder.RaiseChanged();
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Warning(ex, "AdjustMasterVolume failed"); }
        }

        /// <summary>The tube says a confirmation: text always, her recorded clip when the pack has one.
        /// Never synthetic speech: a line with no clip is a silent bubble.</summary>
        private void VoiceSay(string text, string? audio)
        {
            TaskCompletionSource? clip = null;
            if (audio != null)
            {
                clip = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _voiceClip = clip;
            }
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_avatarTubeWindow is not { } tube) { clip?.TrySetResult(); return; }
                tube.GigglePriority(text, playSound: audio != null, aiGenerated: false, phraseAudioPath: audio,
                    barkVoice: audio != null, onSpoken: () => clip?.TrySetResult());
            });
        }

        /// <summary>WPF WaitForAvatarQuietAsync: hold until her clip ended (capped), then a short tail for
        /// the speaker echo to decay, so the command mic never hears her own voice.</summary>
        private async Task VoiceWaitQuiet(bool waitForStart)
        {
            try
            {
                if (_voiceClip is { } clip)
                {
                    await Task.WhenAny(clip.Task, Task.Delay(5000)).ConfigureAwait(false);
                    if (ReferenceEquals(_voiceClip, clip)) _voiceClip = null;
                }
                await Task.Delay(300).ConfigureAwait(false);
            }
            catch { /* never let the echo guard wedge the listen */ }
        }
    }
}
