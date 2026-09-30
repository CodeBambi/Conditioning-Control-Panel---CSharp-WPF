// PORTED from ConditioningControlPanel/Services/AutonomyService.Voice.cs (the wake loop, push-to-talk
// and the serialized prompt funnel). The command rules, table, chaining and confirmations are Core
// VoiceCommands, shared with WPF; this file is the Linux plumbing and the actions this head can run.
//
// The mic opens only under WPF's conditions: VoiceInputRules.ModesToRun (consent + an armed mode +
// premium or the "voice" free day + an available engine), re-read at every reconcile.
// ponytail: no sherpa KWS spotter (the Vosk wake grammar is WPF's fallback path), no spoken-mantra
// fallback (MantraVoiceService is not on this head), no bark voice lines (text confirmations), no
// echo wait on her clip (confirmations here are text-only; a 300 ms tail stands in). Intents with no
// seam here (spiral, pink, mind wipe, quiz, keyword triggers, bubble count, shake, deeper,
// session pause/resume, volume/mute, video pause/resume) are left out of the grammar.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Speech;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private static SpeechEngine? VoiceSpeech => Platform.PulseMicSource.Speech;

        private VoiceCommands? _voiceCommands;
        private int _voiceBusyFlag;
        private CancellationTokenSource? _wakeLoopCts;   // ends the whole wake loop
        private CancellationTokenSource? _wakeWaitCts;   // cancels only the current wake wait
        private CancellationTokenSource? _voicePromptCts; // the in-flight command chain (panic cancels it)
        private Task? _wakeLoopTask;
        private volatile bool _pttArmed;

        internal VoiceCommands VoiceCmds => _voiceCommands ??= new VoiceCommands(new VoiceCommandHost
        {
            Recognize = (grammar, opts) => VoiceSpeech?.RecognizeOneOfAsync(grammar, opts, _voicePromptCts?.Token ?? default)
                                           ?? Task.FromResult(PhraseResult.NotAvailable),
            ActionFor = VoiceAction,
            IsBlocked = g => g == VoiceGuard.Lockdown ? LockdownActive : VoiceStopLocked(),
            OnRefused = () => LockdownService.Current?.NotifyEscapeAttempt(EscapeKinds.Stop),
            ShowListening = line => _avatarTubeWindow?.ShowListeningBubble(line),
            HideListening = () => _avatarTubeWindow?.HideListeningBubble(),
            Say = (text, audio) => Dispatcher.UIThread.Post(() => _avatarTubeWindow?.GigglePriority(text,
                playSound: audio != null, aiGenerated: false, phraseAudioPath: audio, barkVoice: audio != null)),
            WaitQuiet = _ => Task.Delay(300),
            ActiveModId = () => CoreMods.ActiveModId,
            OnUi = a => Dispatcher.UIThread.InvokeAsync(a).GetTask(),
        });

        /// <summary>True while the wake-word loop owns (or is waiting to own) the mic.</summary>
        internal bool WakeLoopArmed => _wakeLoopCts != null;
        internal bool PushToTalkArmed => _pttArmed;
        /// <summary>True while a command prompt (listen, confirm, chain) holds the mic.</summary>
        internal bool VoicePromptActive => Volatile.Read(ref _voiceBusyFlag) != 0;
        internal global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow? Tube => _avatarTubeWindow;

        // WPF StopLocked (#706): Lockdown, a strict video on screen, or a strict-locked bubble count.
        private static bool VoiceStopLocked() =>
            LockdownActive
            || (CoreEngine.Video is { IsPlaying: true, IsStrict: true })
            || (BubbleCountWindow.IsAnyOpen() && CoreSettings.Current.BubbleCountStrictLock);

        /// <summary>The intents this head can run (null = not on this head, so not in the grammar).</summary>
        private Action? VoiceAction(string name) => name switch
        {
            "panic" => VoicePanic,
            "bubbles_on" when CoreBubbles.StartAction != null => CoreBubbles.Start,
            "bubbles_off" when CoreBubbles.StopAction != null => CoreBubbles.Stop,
            "video_on" when CoreEngine.Video != null => () => CoreEngine.Video?.Trigger(),
            "video_off" when CoreEngine.Video != null => () => CoreEngine.Video?.Stop(),
            "flash_once" when CoreFlash.ShowProvider != null => () => CoreFlash.ShowProvider?.Invoke(),
            "subliminals_on" when CoreSubliminal.ShowProvider != null => CoreSubliminal.Start,
            "subliminals_off" when CoreSubliminal.ShowProvider != null => CoreSubliminal.Stop,
            "bouncing_on" when CoreBouncingText.StartAction != null => CoreBouncingText.Start,
            "bouncing_off" when CoreBouncingText.StopAction != null => CoreBouncingText.Stop,
            "lock_once" when CoreLockCard.ShowHandler != null => () => CoreLockCard.Show(),
            "freeze_once" when CoreSubliminal.BambiFreezeProvider != null => CoreSubliminal.TriggerBambiFreeze,
            "takeover_on" => () => Autonomy.Start(),
            "takeover_off" => () => Autonomy.Stop(),
            "stop_listening" => StopVoiceInput,
            _ => null,
        };

        /// <summary>WPF TriggerPanicFromRemote: the spoken safe word. Deliberately NOT refused under
        /// Lockdown or a strict lock - it is the intended way out (decisions "Panic ↔ mic").</summary>
        internal void VoicePanic()
        {
            Log.Information("Panic triggered by voice");
            try { CoreHaptics.Service?.PanicStop(); } catch (Exception ex) { Log.Warning(ex, "Voice panic: haptics stop failed"); }
            StopAutonomyForPanic();
            StopEngine();
            StopLockCards();
            ShowFromTray();
        }

        /// <summary>Panic (key, tray, voice): abort the capture and the command chain in flight. The wake
        /// loop and push-to-talk stay armed (decisions "Panic ↔ mic").</summary>
        internal void CancelVoicePrompt()
        {
            try { _voicePromptCts?.Cancel(); } catch { }
            try { VoiceSpeech?.StopListening(); } catch { }
        }

        /// <summary>WPF RefreshVoiceInputModes: reconcile the wake loop and push-to-talk with settings,
        /// consent, entitlement (re-read, so a lapse closes the mic) and the engine.</summary>
        internal void RefreshVoiceInputModes()
        {
            try
            {
                var entitled = CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("voice");
                var (wake, ptt) = _windowClosed ? (false, false)
                    : VoiceInputRules.ModesToRun(CoreSettings.Current, entitled, CoreSpeech.IsAvailable);
                if (wake) StartWakeLoop(); else StopWakeLoop();
                _pttArmed = ptt;
                Platform.X11PanicKey.PushToTalkKey = () => CoreSettings.Current.SpeechPushToTalkKey is { Length: > 0 } k ? k : "F8";
                Platform.X11PanicKey.PushToTalk = OnPushToTalkKey;
            }
            catch (Exception ex) { Log.Warning(ex, "RefreshVoiceInputModes failed"); }
        }

        /// <summary>WPF StopVoiceInput (privacy pill, "stop listening", a voice lock card taking the mic):
        /// cut any capture and stand both modes down until the next reconcile.</summary>
        internal void StopVoiceInput()
        {
            try { VoiceSpeech?.StopListening(); } catch { }
            StopWakeLoop();
            _pttArmed = false;
        }

        private void OnPushToTalkKey()
        {
            if (!_pttArmed || Volatile.Read(ref _voiceBusyFlag) != 0 || !CoreSpeech.IsAvailable) return;
            Log.Information("Push-to-talk pressed");
            Dispatcher.UIThread.Post(() => OnWakeWordHeard(null));
        }

        /// <summary>WPF OnWakeWordHeard: a one-breath command runs at once, else the Tier 0 listen.</summary>
        internal void OnWakeWordHeard(string? heard)
        {
            if (VoiceCmds.TryHandleInlineCommand(heard, VoiceInputRules.WakeWords(CoreSettings.Current.SpeechWakeWords))) return;
            VoiceCmds.PrepareWake();
            _ = RequestVoiceCommandAsync();
        }

        /// <summary>WPF RequestVoiceCommand: claim the mic, free the wake loop's hold, run one prompt.</summary>
        internal async Task RequestVoiceCommandAsync()
        {
            var sp = VoiceSpeech;
            if (sp?.IsAvailable != true || _avatarTubeWindow == null) return;
            if (Interlocked.CompareExchange(ref _voiceBusyFlag, 1, 0) != 0) return;
            var cts = new CancellationTokenSource();
            _voicePromptCts = cts;
            try
            {
                _wakeWaitCts?.Cancel();
                for (int i = 0; i < 24 && sp.IsListening; i++) await Task.Delay(25).ConfigureAwait(false);
                await VoiceCmds.TryHandleVoiceCommandAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) { Log.Warning(ex, "RequestVoiceCommand failed"); }
            finally
            {
                _voicePromptCts = null;
                cts.Dispose();
                Volatile.Write(ref _voiceBusyFlag, 0);
            }
        }

        private void StartWakeLoop()
        {
            if (_wakeLoopCts != null && _wakeLoopTask is { IsCompleted: false }) return;
            var previous = _wakeLoopTask;   // a just-cancelled loop may still be draining its listen
            var cts = new CancellationTokenSource();
            _wakeLoopCts = cts;
            Log.Information("Wake-word loop starting");
            _wakeLoopTask = Task.Run(async () =>
            {
                if (previous != null) { try { await previous.ConfigureAwait(false); } catch { } }
                await WakeLoopAsync(cts.Token).ConfigureAwait(false);
            });
        }

        private void StopWakeLoop()
        {
            try { _wakeWaitCts?.Cancel(); _wakeLoopCts?.Cancel(); } catch { }
            _wakeLoopCts = null;
        }

        /// <summary>WPF WakeLoopAsync, Vosk branch: wake variants plus the command grammar, so a
        /// one-breath "hey bambi &lt;command&gt;" is transcribed by this listen.</summary>
        private async Task WakeLoopAsync(CancellationToken loopCt)
        {
            try
            {
                while (!loopCt.IsCancellationRequested)
                {
                    var sp = VoiceSpeech;
                    if (Volatile.Read(ref _voiceBusyFlag) != 0 || sp == null || sp.IsListening || !sp.IsAvailable)
                    {
                        await Task.Delay(350, loopCt).ConfigureAwait(false);
                        continue;
                    }
                    var words = VoiceInputRules.WakeWords(CoreSettings.Current.SpeechWakeWords);
                    if (words.Count == 0)
                    {
                        await Task.Delay(500, loopCt).ConfigureAwait(false);
                        continue;
                    }
                    var grammar = VoiceInputRules.ExpandWakeVariants(words);
                    foreach (var alias in VoiceCmds.Grammar())
                        if (!grammar.Contains(alias, StringComparer.OrdinalIgnoreCase)) grammar.Add(alias);

                    string? heard = null;
                    using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt))
                    {
                        _wakeWaitCts = waitCts;
                        try { heard = await sp.WaitForWakeWordAsync(grammar, waitCts.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Wake-word wait failed");
                            await Task.Delay(800, loopCt).ConfigureAwait(false);
                        }
                        finally { _wakeWaitCts = null; }
                    }
                    if (loopCt.IsCancellationRequested) break;
                    if (string.IsNullOrWhiteSpace(heard)) continue;
                    Log.Information("Wake word heard ({Chars} chars of transcript)", heard.Length);
                    Dispatcher.UIThread.Post(() => OnWakeWordHeard(heard));
                    await Task.Delay(400, loopCt).ConfigureAwait(false);   // let the prompt claim the mic
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "Wake-word loop crashed"); }
            Log.Information("Wake-word loop ended");
        }
    }
}
