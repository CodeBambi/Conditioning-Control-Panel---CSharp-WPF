using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Opt-in user-driven mic input for the Takeover "say it for me" mechanic:
    ///  - <b>Wake-word</b> ("Hey Bambi"): an always-on listen loop that fires a voice prompt
    ///    when she hears her name. Runs only while Takeover is active.
    ///  - <b>Push-to-talk</b>: a system-wide key that summons a voice prompt on demand.
    ///
    /// Both are additive and self-protecting: nothing arms unless the user gave mic consent, the
    /// Vosk engine is actually available, and Takeover is running. Everything funnels through the
    /// single serialized <see cref="RequestVoiceCommand"/> so the recognizer's one-session-at-a-time
    /// guard is never violated. When either mode is on, the surprise auto-trigger is suppressed
    /// (the mic only opens on the user's initiative).
    /// </summary>
    public partial class AutonomyService
    {
        // Set for the whole life of a voice prompt (announce beat + listen + verdict) so the wake
        // loop, the PTT key, and the auto-scheduler all stand off the mic while one is in flight.
        // Claimed atomically (0/1) in case wake-word and push-to-talk ever race for it.
        private int _voiceBusyFlag;
        private bool _voiceBusy => Volatile.Read(ref _voiceBusyFlag) != 0;

        private CancellationTokenSource? _wakeLoopCts; // ends the whole wake loop
        private CancellationTokenSource? _wakeWaitCts; // cancels only the current WaitForWakeWord
        private Task? _wakeLoopTask;

        private GlobalKeyboardHook? _pttHook;

        /// <summary>Whether the user has armed a self-initiated mic mode (and so the mic only opens on demand).</summary>
        public bool UserDrivenVoiceArmed
        {
            get
            {
                var s = App.Settings?.Current;
                return s != null && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
            }
        }

        /// <summary>
        /// Reconcile the wake-word loop and push-to-talk hook with current settings. Safe to call
        /// any time (lifecycle start, or when the user flips a toggle). No-op unless Takeover is
        /// running, mic consent is given, and the speech engine is available.
        /// </summary>
        public void RefreshVoiceInputModes()
        {
            try
            {
                // NOTE: deliberately NOT gated on _isEnabled (Takeover running). The mic features
                // (wake word / push-to-talk / voice commands) are decoupled from Takeover — they
                // arm from their own toggles + consent + an available engine, so "Hey Bambi" works
                // even with Takeover off. Their home UI is the "She's Listening" Exclusive.
                var s = App.Settings?.Current;

                // Entitlement, in the base condition so BOTH mic modes inherit it. She's Listening is
                // a premium feature whose tab wears the veil, but these two toggles persist and this
                // method re-arms them from settings on EVERY launch — so before this a lapsed account
                // came back from a restart with a wake-word loop holding the microphone open, behind a
                // padlock covering the only controls that disarm it. Exactly the shape PR #267 was
                // written to delete; it just never reached this file.
                //
                // Re-read here rather than cached: this method is the reconcile point, so a lapse that
                // happens while the loop is up is corrected the next time anything calls it, and
                // EnforceEntitlementLapse calls it on the day-roll / tier-change / logout funnel.
                bool entitled = App.Patreon?.HasPremiumAccess == true
                                || App.DailyFree?.IsFreeToday("voice") == true;

                var (wake, ptt) = s == null || _disposed
                    ? (false, false)
                    : VoiceInputRules.ModesToRun(s, entitled, App.Speech?.IsAvailable == true);

                // Wake-word loop
                if (wake)
                    StartWakeLoop();
                else
                    StopWakeLoop();

                // Push-to-talk hook
                if (ptt)
                    StartPushToTalk();
                else
                    StopPushToTalk();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: RefreshVoiceInputModes failed"); }
        }

        /// <summary>Tear down both mic modes. Called from Stop()/Dispose().</summary>
        private void StopVoiceInputModes()
        {
            StopWakeLoop();
            StopPushToTalk();
        }

        /// <summary>
        /// User-initiated "stop the mic" (the privacy pill). Cuts any in-flight capture and tears
        /// down the wake-word loop and push-to-talk hook so the mic won't reopen until re-armed.
        /// Leaves the rest of Takeover running — this is about the microphone, not the takeover.
        /// </summary>
        public void StopVoiceInput()
        {
            try
            {
                App.Speech?.StopListening();
                StopVoiceInputModes();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: StopVoiceInput failed"); }
        }

        // ── Serialized voice-prompt entry point ───────────────────────────────

        /// <summary>
        /// The single funnel every voice initiator uses. Claims the mic, frees the wake loop if it
        /// was holding it, waits for the capture session to release, then runs one prompt. Re-entrant
        /// calls are dropped while one is already in flight.
        ///
        /// <paramref name="allowCommands"/>: user-initiated paths (wake-word / push-to-talk) first
        /// listen for a "Hey Bambi" voice command and only fall back to a mantra if none is heard;
        /// the auto-scheduler and dev test pass false so they always deliver a mantra.
        /// </summary>
        private async void RequestVoiceCommand(bool allowCommands = false)
        {
            if (App.Speech?.IsAvailable != true || App.AvatarWindow == null) return;
            // Atomically claim the mic; bail if a prompt is already in flight.
            if (Interlocked.CompareExchange(ref _voiceBusyFlag, 1, 0) != 0) return;
            try
            {
                // If the wake loop currently owns the mic, cancel its wait so the session releases.
                _wakeWaitCts?.Cancel();
                for (int i = 0; i < 24 && App.Speech?.IsListening == true; i++)
                    await Task.Delay(25).ConfigureAwait(false);

                // Voice-command layer first (only on user-initiated paths). On a match we're done; on
                // no-match the wake/PTT turn falls back to a mantra ONLY if on-demand mantras are on.
                if (allowCommands)
                {
                    if (await VoiceCmds.TryHandleVoiceCommandAsync().ConfigureAwait(false))
                        return;
                    if (App.Settings?.Current?.SpokenMantrasEnabled != true)
                        return; // commands only; no mantra fallback when on-demand mantras are off
                }

                await RunSpokenMantraAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: RequestVoiceCommand failed"); }
            finally { Volatile.Write(ref _voiceBusyFlag, 0); }
        }

        // ── Wake-word loop ────────────────────────────────────────────────────

        // Wake phrases + phonetic variants live in Core (VoiceInputRules), shared with the Avalonia head.
        private List<string> WakeWords() => VoiceInputRules.WakeWords(App.Settings?.Current?.SpeechWakeWords);

        private static List<string> ExpandWakeVariants(IReadOnlyList<string> phrases)
            => VoiceInputRules.ExpandWakeVariants(phrases);

        private void StartWakeLoop()
        {
            // A live (not-stopped) loop already running? Leave it. StopWakeLoop nulls _wakeLoopCts, so a
            // non-null cts alongside an unfinished task means a loop that is still actively listening.
            if (_wakeLoopCts != null && _wakeLoopTask is { IsCompleted: false }) return;

            var previous = _wakeLoopTask; // may be a just-cancelled loop still draining its native listen
            var cts = new CancellationTokenSource();
            _wakeLoopCts = cts;
            var ct = cts.Token;
            App.Logger?.Information("AutonomyService: wake-word loop starting (words: {Words})", string.Join(" / ", WakeWords()));

            // Chain off any draining predecessor so two wake loops never overlap on the single-session
            // recognizer. A fast disarm→re-arm used to spin up a SECOND loop (it called the recognizer
            // directly, bypassing the funnel) and violate the one-session guarantee; awaiting the old
            // loop first also avoids the "stuck off after a quick toggle" case.
            _wakeLoopTask = Task.Run(async () =>
            {
                if (previous != null) { try { await previous.ConfigureAwait(false); } catch { } }
                await WakeLoopAsync(ct).ConfigureAwait(false);
            }, ct);
        }

        private void StopWakeLoop()
        {
            try
            {
                _wakeWaitCts?.Cancel();
                _wakeLoopCts?.Cancel();
            }
            catch { }
            _wakeLoopCts = null;
            // Do NOT null _wakeLoopTask: StartWakeLoop chains the next loop off it so a draining loop and
            // a freshly-armed one can't overlap on the mic. It clears itself once WakeLoopAsync returns.
        }

        private async Task WakeLoopAsync(CancellationToken loopCt)
        {
            try
            {
                while (!loopCt.IsCancellationRequested)
                {
                    // Stand off the mic while a prompt is in flight or another session holds it.
                    if (_voiceBusy || App.Speech?.IsListening == true || App.Speech?.IsAvailable != true)
                    {
                        await Task.Delay(350, loopCt).ConfigureAwait(false);
                        continue;
                    }

                    var words = WakeWords();
                    if (words.Count == 0)
                    {
                        await Task.Delay(500, loopCt).ConfigureAwait(false);
                        continue;
                    }
                    // Prefer the dedicated sherpa-onnx KWS spotter when the model is installed — it nails
                    // the OOV name "Bambi" that the Vosk free recognizer only catches ~half the time. It
                    // spots the keyword ONLY (no transcript), so a one-breath "hey bambi <command>" isn't
                    // read here; the Tier 0 listen that OnWakeWordHeard opens catches the command instead.
                    bool useKws = App.WakeWord?.IsAvailable == true;

                    // Vosk fallback grammar: phonetic spellings of the OOV name (canonical first so the
                    // match scores against the real wake word) plus the command vocabulary, so a chained
                    // "hey bambi <command>" is transcribed in one breath (that audio is consumed by THIS
                    // recognizer — a later listen would miss it). Only built when the KWS engine isn't used.
                    List<string>? grammar = null;
                    if (!useKws)
                    {
                        grammar = ExpandWakeVariants(words);
                        try
                        {
                            foreach (var alias in VoiceCmds.Grammar())
                                if (!grammar.Contains(alias, StringComparer.OrdinalIgnoreCase)) grammar.Add(alias);
                        }
                        catch { }
                    }

                    string? heard = null; // Vosk transcript (may carry an inline command)
                    bool kwsHit = false;  // sherpa-onnx KWS fired (keyword only)
                    using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt))
                    {
                        _wakeWaitCts = waitCts;
                        try
                        {
                            if (useKws)
                                kwsHit = await App.WakeWord!.WaitForWakeAsync(waitCts.Token).ConfigureAwait(false);
                            else
                                heard = await App.Speech!.WaitForWakeWordAsync(grammar!, waitCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { /* normal on stop/interrupt */ }
                        catch (Exception ex)
                        {
                            App.Logger?.Warning(ex, "AutonomyService: wake-word wait failed");
                            await Task.Delay(800, loopCt).ConfigureAwait(false);
                        }
                        finally { _wakeWaitCts = null; }
                    }

                    if (loopCt.IsCancellationRequested) break;
                    if (kwsHit)
                    {
                        App.Logger?.Information("AutonomyService: wake word heard (sherpa-onnx KWS)");
                        OnWakeWordHeard();   // no transcript -> Tier 0 listen flow, like push-to-talk
                        await Task.Delay(400, loopCt).ConfigureAwait(false);
                    }
                    else if (!string.IsNullOrWhiteSpace(heard))
                    {
                        App.Logger?.Information("AutonomyService: wake word heard ({Chars} chars of transcript)", heard.Length);
                        OnWakeWordHeard(heard);
                        // Let the prompt claim the mic before we loop back and re-grab it.
                        await Task.Delay(400, loopCt).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { /* clean shutdown */ }
            catch (Exception ex) { App.Logger?.Warning(ex, "AutonomyService: wake-word loop crashed"); }
            App.Logger?.Information("AutonomyService: wake-word loop ended");
        }

        private void OnWakeWordHeard(string? heard = null)
        {
            // One-breath chaining: if the wake utterance already carried a command ("hey bambi show me
            // bubbles"), run it directly. That command audio was consumed by the wake recognizer, so a
            // separate listen pass would miss it. Otherwise the Tier 0 listen flow (Core VoiceCommands):
            // stash the wake ack, pop the dots, open the command mic at once.
            if (VoiceCmds.TryHandleInlineCommand(heard, WakeWords())) return;
            VoiceCmds.PrepareWake();
            RequestVoiceCommand(allowCommands: true);
        }

        // ── Push-to-talk ──────────────────────────────────────────────────────

        /// <summary>The configured push-to-talk key, or F8 if the saved value won't parse.</summary>
        public Key PushToTalkKey()
        {
            var raw = App.Settings?.Current?.SpeechPushToTalkKey;
            if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse<Key>(raw, ignoreCase: true, out var k) && k != Key.None)
                return k;
            return Key.F8;
        }

        private void StartPushToTalk()
        {
            if (_pttHook != null) return;
            try
            {
                _pttHook = new GlobalKeyboardHook();
                _pttHook.KeyPressed += OnPushToTalkKey;
                _pttHook.Start();
                App.Logger?.Information("AutonomyService: push-to-talk armed on key {Key}", PushToTalkKey());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AutonomyService: failed to arm push-to-talk hook");
                _pttHook = null;
            }
        }

        private void StopPushToTalk()
        {
            if (_pttHook == null) return;
            try
            {
                _pttHook.KeyPressed -= OnPushToTalkKey;
                _pttHook.Dispose();
            }
            catch { }
            _pttHook = null;
        }

        private void OnPushToTalkKey(Key key)
        {
            if (key != PushToTalkKey()) return;
            if (_voiceBusy) return;
            // Decoupled from Takeover (_isEnabled), exactly like the wake-word loop — the button works
            // whenever PTT is armed + the engine is available, not only while a takeover is running.
            if (App.Speech?.IsAvailable != true) return;
            App.Logger?.Information("AutonomyService: push-to-talk pressed");
            // Behave EXACTLY like a "Hey Bambi" wake: pop the listening dots, open the mic immediately,
            // and only speak the ack if you stay silent (Tier 0, all in OnWakeWordHeard). OnWakeWordHeard
            // marshals its own UI work, but we're on the low-level hook thread (must return fast), so hop off it.
            DispatcherHelper.RunOnUI(() => OnWakeWordHeard());
        }
    }
}
