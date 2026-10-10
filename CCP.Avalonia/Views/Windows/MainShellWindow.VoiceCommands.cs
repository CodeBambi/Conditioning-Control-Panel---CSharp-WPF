// PORTED from ConditioningControlPanel/Services/AutonomyService.Voice.cs (the wake loop, push-to-talk
// and the serialized prompt funnel). The command rules, table, chaining and confirmations are Core
// VoiceCommands, shared with WPF; this file is the Linux plumbing and the actions this head can run.
//
// The mic opens only under WPF's conditions: VoiceInputRules.ModesToRun (consent + an armed mode +
// premium or the "voice" free day + an available engine), re-read at every reconcile.
// ponytail: no sherpa KWS spotter (the Vosk wake grammar is WPF's fallback path). Confirmations are her
// recorded bark clips when the active pack has one, else a text bubble (never synthetic speech).
// The second half of the actions is MainShellWindow.VoiceActions.cs; keyword triggers, shake and
// deeper have no seam here and stay out of the grammar.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
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
        private volatile bool _userVoiceTurn;   // the prompt in flight is a wake / push-to-talk turn

        internal VoiceCommands VoiceCmds => _voiceCommands ??= new VoiceCommands(new VoiceCommandHost
        {
            Recognize = (grammar, opts) => VoiceSpeech?.RecognizeOneOfAsync(grammar, opts, _voicePromptCts?.Token ?? default)
                                           ?? Task.FromResult(PhraseResult.NotAvailable),
            ActionFor = VoiceAction,
            IsBlocked = g => g == VoiceGuard.Lockdown ? LockdownActive : VoiceStopLocked(),
            OnRefused = () => LockdownService.Current?.NotifyEscapeAttempt(EscapeKinds.Stop),
            ShowListening = line => _avatarTubeWindow?.ShowListeningBubble(line),
            HideListening = () => _avatarTubeWindow?.HideListeningBubble(),
            Say = VoiceSay,
            PickVoiceLine = id => Platform.BarkHead.Engine?.PickVoiceLine(id),
            WaitQuiet = VoiceWaitQuiet,
            ActiveModId = () => CoreMods.ActiveModId,
            OnUi = a => Dispatcher.UIThread.InvokeAsync(a).GetTask(),
            HelpFromAvailable = true,
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
        internal Action? VoiceAction(string name) => name switch
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
            _ => VoiceActionMore(name),
        };

        /// <summary>WPF TriggerPanicFromRemote: the spoken safe word. Owner, 2026-10-10 (hard rule 6): it is a
        /// panic press by another name, so it answers to the same rule as the key, the tray stop and the
        /// 6-blink stop (Core BlinkStopGate): refused under Lockdown, with the panic key switched off and
        /// under Strict Lock. It used to stop everything regardless, which made the spoken word a wider way
        /// out than the key. Refused = what a refused key press does: a log line, nothing stopped, no
        /// Chaster safety hold, and (as the key) a leash task still parks, because panic always works on a
        /// leash. Cutting the leash is a different door and is never gated or priced.</summary>
        internal void VoicePanic()
        {
            var block = VoiceStopBlock();
            if (block != ConditioningControlPanel.Services.Safety.BlinkStopGate.Block.None)
            {
                Log.Information("Voice safe word refused ({Reason})", block);
                // WPF LeashPanicKeyWhilePanicOff, as Win32Input.OnPanicPress does for the key.
                if (block is ConditioningControlPanel.Services.Safety.BlinkStopGate.Block.Lockdown
                        or ConditioningControlPanel.Services.Safety.BlinkStopGate.Block.NoEscape
                    && Platform.LeashHead.IsLeashed)
                    Platform.LeashTaskHost.OnPanicPress(panicRuns: false);
                return;
            }
            Log.Information("Panic triggered by voice");
            PanicSurfaces.StopAll("voice", this);
            ShowFromTray();
        }

        /// <summary>Why the spoken safe word is refused right now; None when the panic key would run too.
        /// The same inputs as <see cref="TrayStopBlock"/> and the 6-blink stop.</summary>
        internal static ConditioningControlPanel.Services.Safety.BlinkStopGate.Block VoiceStopBlock() => TrayStopBlock();

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
                // A lapse / disarm mid-command ends a wake/PTT turn; Takeover's own mantra is not that
                // session and keeps running unless consent itself is gone.
                if ((!wake && !ptt && _userVoiceTurn) || !CoreSettings.Current.MicConsentGiven) CancelVoicePrompt();
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
            // First: a command prompt in flight must end too, or its "you called?" re-prompt / retry
            // would reopen the mic after the user closed it.
            CancelVoicePrompt();
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
            _ = RequestVoiceCommandAsync(allowCommands: true);
        }

        /// <summary>WPF RequestVoiceCommand: claim the mic, free the wake loop's hold, run one prompt.
        /// Wake / push-to-talk (<paramref name="allowCommands"/>) try a command first and fall back to a
        /// mantra only with on-demand mantras on; Takeover's surprise mantra always asks one.</summary>
        internal async Task RequestVoiceCommandAsync(bool allowCommands)
        {
            var sp = VoiceSpeech;
            if (sp?.IsAvailable != true || _avatarTubeWindow == null) return;
            if (Interlocked.CompareExchange(ref _voiceBusyFlag, 1, 0) != 0) return;
            var cts = new CancellationTokenSource();
            _voicePromptCts = cts;
            _userVoiceTurn = allowCommands;
            try
            {
                _wakeWaitCts?.Cancel();
                for (int i = 0; i < 24 && sp.IsListening; i++) await Task.Delay(25).ConfigureAwait(false);
                if (allowCommands)
                {
                    if (await VoiceCmds.TryHandleVoiceCommandAsync(cts.Token).ConfigureAwait(false)) return;
                    if (!CoreSettings.Current.SpokenMantrasEnabled) return;
                }
                if (cts.IsCancellationRequested) return;
                // WPF OnSpeechPartial / OnSpeechLevel, for this prompt only.
                EventHandler<string> partial = (_, t) => Dispatcher.UIThread.Post(() => ShowVoiceHeard(t));
                EventHandler<double> level = (_, l) => Dispatcher.UIThread.Post(() => SetVoiceLevel(l));
                sp.PartialTranscript += partial;
                sp.LevelChanged += level;
                try { await SpokenMantra.RunAsync(App.MantraVoice, MantraHost(sp, cts.Token), cts.Token).ConfigureAwait(false); }
                finally { sp.PartialTranscript -= partial; sp.LevelChanged -= level; }
            }
            catch (Exception ex) { Log.Warning(ex, "RequestVoiceCommand failed"); }
            finally
            {
                _voicePromptCts = null;
                _userVoiceTurn = false;
                cts.Dispose();
                Volatile.Write(ref _voiceBusyFlag, 0);
            }
        }

        /// <summary>WPF AutonomyService.TestVoiceCommand (She's Listening and Takeover "Test"): say why
        /// it can't run, ask for mic consent if missing, then ask one spoken mantra.</summary>
        internal async void TestSpokenMantra()
        {
            try
            {
                string? why = null, title = null;
                if (!CoreSpeech.IsAvailable)
                {
                    title = Loc.Get("voice_test_unavailable_title");
                    why = Loc.Get("voice_test_unavailable_intro") + "\n\n" + (!CoreSpeech.HasCaptureDevice
                        ? Loc.Get("voice_test_unavailable_no_mic")
                        : CoreSpeech.ModelStatus == CoreSpeechModelStatus.LoadFailed
                            ? Loc.Get("voice_test_unavailable_load_failed")
                            : Loc.Get("voice_test_unavailable_no_model"));
                }
                if (why != null) { await Dialogs.MessageDialog.ShowAsync(this, title!, why); return; }
                // Usually the companion was switched off (Dismiss sticks across restarts). Offer to
                // turn it on right here, then carry on (WPF AutonomyService.TestVoiceCommand, 9dfccda39).
                if (_avatarTubeWindow == null && !CoreSettings.Current.AvatarEnabled
                    && await Dialogs.MessageDialog.ConfirmAsync(this, Loc.Get("voice_test_companion_off_title"),
                                                               Loc.Get("voice_test_companion_off_body")))
                    SetAvatarEnabled(true);
                if (_avatarTubeWindow == null)
                {
                    // Declined: nothing to say. Switched on and still no tube: the old note.
                    if (CoreSettings.Current.AvatarEnabled)
                        await Dialogs.MessageDialog.ShowAsync(this, Loc.Get("voice_test_no_avatar_title"),
                            Loc.Get("voice_test_no_avatar_body"));
                    return;
                }
                if (!App.MantraVoice.HasMantras())
                    (title, why) = (Loc.Get("voice_test_no_mantras_title"), Loc.Get("voice_test_no_mantras_body"));
                if (why != null) { await Dialogs.MessageDialog.ShowAsync(this, title!, why); return; }
                // Privacy gate: the mic never opens until the consent dialog was accepted.
                if (!CoreSettings.Current.MicConsentGiven)
                {
                    var dlg = new Dialogs.MicConsentDialog();
                    if (await dlg.ShowDialogSafe<bool?>(this) != true || !dlg.ConsentGiven || !CoreSettings.Current.MicConsentGiven)
                    {
                        Log.Information("TestVoiceCommand: mic consent declined, not opening mic");
                        return;
                    }
                }
                Log.Information("TestVoiceCommand invoked manually");
                await RequestVoiceCommandAsync(allowCommands: false);
            }
            catch (Exception ex) { Log.Warning(ex, "TestSpokenMantra failed"); }
        }

        /// <summary>The Avalonia host for Core SpokenMantra. WaitSpoken completes from the tube's clip
        /// onFinished (CoreAudio.PlayOneShot), so the mic opens only after her voiced prompt ended.</summary>
        private SpokenMantraHost MantraHost(SpeechEngine sp, CancellationToken ct)
        {
            Task spoken = Task.CompletedTask;
            return new SpokenMantraHost
            {
                Recognize = (phrase, opts) => sp.RecognizePhraseAsync(phrase, opts, ct),
                Say = (text, audio) =>
                {
                    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    spoken = done.Task;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_avatarTubeWindow is not { } tube) { done.TrySetResult(); return; }
                        tube.GigglePriority(text, playSound: audio != null, aiGenerated: false, phraseAudioPath: audio,
                            barkVoice: audio != null, onSpoken: () => done.TrySetResult());
                    });
                },
                WaitSpoken = c => spoken.WaitAsync(c),
                IsSpeaking = () => _avatarTubeWindow?.IsSpeaking == true,
                Credit = () => { if (!App.Mantra.TryCompleteMantra()) App.Mantra.CreditExternalMantra(); },
                PromptStarted = phrase => Dispatcher.UIThread.Post(() => ShowVoicePrompt(phrase)),
                PromptFinished = r => Dispatcher.UIThread.Post(() => ShowVoiceVerdict(r)),
                ShowListening = phrase => _avatarTubeWindow?.ShowListeningBubble(phrase),   // RunOnAvatar marshals
                HideListening = () => _avatarTubeWindow?.HideListeningBubble(),
            };
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
