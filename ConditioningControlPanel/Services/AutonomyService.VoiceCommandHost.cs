using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The WPF half of the "Hey Bambi" voice commands. The intent table, matching, chaining and
    /// confirmations are Core <see cref="VoiceCommands"/>; this file hands it the mic, the tube and
    /// the App services each intent drives.
    /// </summary>
    public partial class AutonomyService
    {
        private VoiceCommands? _voiceCommands;

        private VoiceCommands VoiceCmds => _voiceCommands ??= new VoiceCommands(new VoiceCommandHost
        {
            Recognize = (grammar, opts) => App.Speech?.RecognizeOneOfAsync(grammar, opts) ?? Task.FromResult(PhraseResult.NotAvailable),
            ActionFor = VoiceAction,
            IsBlocked = g => g == VoiceGuard.Lockdown ? App.Lockdown?.IsActive == true : StopLocked(),
            OnRefused = () => App.Lockdown?.NotifyEscapeAttempt(Possession.EscapeKinds.Stop),
            ShowListening = line => OnUi(() => App.AvatarWindow?.ShowListeningBubble(line)),
            HideListening = () => OnUi(() => App.AvatarWindow?.HideListeningBubble()),
            Say = (text, audio) => OnUi(() => App.AvatarWindow?.GigglePriority(text, playSound: audio != null,
                aiGenerated: false, phraseAudioPath: audio, barkVoice: audio != null)),
            PickVoiceLine = id => App.Bark?.PickVoiceLine(id),
            WaitQuiet = waitForStart => WaitForAvatarQuietAsync(waitForStart),
            ActiveModId = () => App.Mods?.ActiveModId,
            OnUi = a => Application.Current?.Dispatcher is { } d ? d.InvokeAsync(a).Task : Task.CompletedTask,
        });

        private static void OnUi(Action a)
        {
            if (Application.Current?.Dispatcher == null) return;
            _ = Application.Current.Dispatcher.InvokeAsync(() => { try { a(); } catch { } });
        }

        /// <summary>Pure mod id -> confirmation pack key (Core <see cref="VoiceCommands.ModKeyFor"/>).</summary>
        internal static string ModKeyFor(string? activeModId) => VoiceCommands.ModKeyFor(activeModId);

        /// <summary>Test seam: every confirmation table (VoiceCommandNeutralPackTests).</summary>
        internal static List<(string Intent, string Table, IReadOnlyDictionary<string, string> Lines)> VoiceConfirmTablesForTests()
            => VoiceCommands.ConfirmTables();

        // VoiceGuard.StopLocked on this head. Mirrors the contract AvatarTubeWindow.IsEngineStopLocked()
        // already enforces for the tube's Stop button, so the two entry points can't disagree (#706).
        private static bool StopLocked() =>
            App.Lockdown?.IsActive == true ||
            App.Video?.IsStrictActive == true ||
            (App.BubbleCount?.IsBusy == true && App.Settings?.Current?.BubbleCountStrictLock == true);

        /// <summary>What each intent does on this head (the old Execute closures, by intent name).</summary>
        private static Action? VoiceAction(string name) => name switch
        {
            "panic" => () => App.MainWindowRef?.TriggerPanicFromRemote(),
            "bubbles_on" => () => App.Bubbles?.Start(bypassLevelCheck: true),
            "bubbles_off" => () => App.Bubbles?.Stop(),
            "video_on" => () => App.Video?.TriggerVideo(),
            "video_off" => () => App.Video?.Stop(),
            "video_pause" => () => App.Video?.PausePrimary(),
            "video_resume" => () => App.Video?.PlayPrimary(),
            // TriggerFlash() bails when the service isn't running; TriggerFlashOnce() is the
            // standalone one-shot (sets its own images path) so "flash me" fires without a session.
            "flash_once" => () => App.Flash?.TriggerFlashOnce(),
            "subliminals_on" => () => App.Subliminal?.Start(),
            "subliminals_off" => () => App.Subliminal?.Stop(),
            "bouncing_on" => () => App.BouncingText?.Start(),
            "bouncing_off" => () => App.BouncingText?.Stop(),
            // The user's OWN spiral, at the opacity they configured (#1051).
            "spiral_on" => () => App.Overlay?.ShowOverlaySustained("spiral",
                (App.Settings?.Current?.SpiralOpacity ?? 10) / 100.0),
            "spiral_off" => () => App.Overlay?.HideOverlaySustained("spiral"),
            // OverlayService keys this overlay "pink_filter". 0.4 is this command's own floor, but it
            // must never DIM a stronger live tint (#1051).
            "pink_on" => () => App.Overlay?.ShowOverlaySustained("pink_filter",
                Math.Max(0.4, (App.Settings?.Current?.PinkFilterOpacity ?? 10) / 100.0)),
            "pink_off" => () => App.Overlay?.HideOverlaySustained("pink_filter"),
            "wipe_once" => () => App.MindWipe?.TriggerOnce(),
            "lock_once" => () => App.LockCard?.ShowLockCard(),
            "quiz_once" => () => App.PopQuiz?.ShowPopQuiz(),
            "keyword_on" => () => App.KeywordTriggers?.Start(),
            "keyword_off" => () => App.KeywordTriggers?.Stop(),
            // forceTest: true — TriggerGame() bails when the engine isn't running.
            "count_once" => () => App.BubbleCount?.TriggerGame(forceTest: true),
            "freeze_once" => () => App.Subliminal?.TriggerBambiFreeze(),
            "shake_once" => () => App.ScreenShake?.Shake(60, 1200),
            "deeper" => () => App.BrainDrain?.Start(),
            "takeover_on" => () => App.Autonomy?.Start(),
            "takeover_off" => () => App.Autonomy?.Stop(),
            "pause" => () => App.MainWindowRef?.PauseSessionFromRemote(),
            "resume" => () => App.MainWindowRef?.ResumeSessionFromRemote(),
            // No StopLocked on mute: silencing is not escaping (see the mute intent in Core).
            "mute" => () => App.MainWindowRef?.ApplyVoiceMute(true),
            "unmute" => () => App.MainWindowRef?.ApplyVoiceMute(false),
            "louder" => () => App.MainWindowRef?.AdjustMasterVolume(+15),
            "quieter" => () => App.MainWindowRef?.AdjustMasterVolume(-15),
            "stop_listening" => () => App.Autonomy?.StopVoiceInput(),
            "mantra" => () => { },   // never run: the listen flow hands a mantra request to the funnel
            _ => null,
        };

        /// <summary>
        /// Holds until the avatar has stopped speaking (capped by <paramref name="maxWaitMs"/>), then
        /// waits a short <paramref name="tailMs"/> for speaker echo to decay — so the command mic never
        /// hears her own voice. When <paramref name="waitForStart"/> is set (the wake/PTT turn, where the
        /// ack clip starts after a brief bubble lead-in), it first gives her clip up to
        /// <paramref name="graceMs"/> to actually begin so we don't sail past the wait before she speaks.
        /// </summary>
        private static async Task WaitForAvatarQuietAsync(bool waitForStart, int graceMs = 800, int maxWaitMs = 5000, int tailMs = 300)
        {
            try
            {
                if (waitForStart)
                {
                    int g = 0;
                    while (App.AvatarWindow?.IsSpeakingAudio != true && g < graceMs)
                    {
                        await Task.Delay(50).ConfigureAwait(false);
                        g += 50;
                    }
                }

                int waited = 0;
                while (App.AvatarWindow?.IsSpeakingAudio == true && waited < maxWaitMs)
                {
                    await Task.Delay(60).ConfigureAwait(false);
                    waited += 60;
                }

                if (tailMs > 0) await Task.Delay(tailMs).ConfigureAwait(false);
            }
            catch { /* never let the echo guard wedge the listen */ }
        }
    }
}
