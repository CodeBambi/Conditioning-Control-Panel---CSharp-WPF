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

        // The wake-ack line picked once in OnWakeWordHeard. Tier 0 listens BEFORE speaking, so this is
        // NOT spoken on wake — it is stashed so (a) the primary listen's dots bubble can read the same
        // words, and (b) the command driver can speak it aloud as the "you called?" re-prompt if you
        // stay silent. Audio is the matching clip for the active mod (null = text-only).
        private string? _pendingWakeAckText;
        private string? _pendingWakeAckAudio;

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
                bool baseOk = !_disposed
                              && s?.MicConsentGiven == true
                              && App.Speech?.IsAvailable == true;

                // Wake-word loop
                if (baseOk && s!.SpeechWakeWordEnabled && WakeWords().Count > 0)
                    StartWakeLoop();
                else
                    StopWakeLoop();

                // Push-to-talk hook
                if (baseOk && s!.SpeechPushToTalkEnabled)
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
                    if (await TryHandleVoiceCommandAsync().ConfigureAwait(false))
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

        private List<string> WakeWords()
        {
            var raw = App.Settings?.Current?.SpeechWakeWords ?? "";
            return raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(w => w.Trim())
                      .Where(w => w.Length > 0)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();
        }

        // "bambi" (and friends) aren't English dictionary words, so the offline model can't spell them
        // reliably — it returns close-but-not-exact tokens or dumps the name into [unk]. Feeding the
        // decoder these acoustically-plausible spellings as extra grammar targets lets it return a full
        // phrase that fuzzy-matches the canonical wake word. Only the trailing name token is varied.
        private static readonly Dictionary<string, string[]> WakeNameVariants =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["bambi"]  = new[] { "bambi", "bamby", "bambie", "bambee", "bombi", "bambit" },
                ["bimbo"]  = new[] { "bimbo", "bimba", "bimbow", "bimboh" },
                ["bambis"] = new[] { "bambis", "bambies" },
            };

        /// <summary>
        /// Expand the user's wake phrases with phonetic spellings of any OOV name token (e.g. "hey bambi"
        /// -> "hey bambi/bamby/bambie/..."). Canonical phrases stay FIRST so the recognizer's match target
        /// (its words[0]) remains the real wake word; the variants only widen what the decoder can return.
        /// </summary>
        private static List<string> ExpandWakeVariants(IReadOnlyList<string> phrases)
        {
            var outp = new List<string>(phrases);
            foreach (var phrase in phrases)
            {
                var toks = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length == 0) continue;
                if (!WakeNameVariants.TryGetValue(toks[^1], out var variants)) continue;
                var prefix = toks.Length > 1 ? string.Join(' ', toks[..^1]) + " " : "";
                foreach (var v in variants)
                {
                    var cand = prefix + v;
                    if (!outp.Contains(cand, StringComparer.OrdinalIgnoreCase)) outp.Add(cand);
                }
            }
            return outp;
        }

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
                            foreach (var alias in VoiceCommandIntents.SelectMany(i => i.Aliases))
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
                        App.Logger?.Information("AutonomyService: wake word heard ('{Heard}')", heard);
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
            // separate listen pass would miss it — this is the only place we can catch it. When there's no
            // trailing command (bare "hey bambi", or push-to-talk which passes null) we fall through to the
            // Tier 0 listen flow below.
            if (TryHandleInlineCommand(heard)) return;

            // Tier 0 — listen BEFORE speaking, the way Alexa/Google do: on wake they flash a "listening"
            // cue and open the mic in the same instant; a spoken re-prompt ("you called?") only comes
            // AFTER you stay silent. So here we DON'T speak the ack — we stash it, pop the dots bubble for
            // instant "I'm listening" feedback, and open the command mic right away. Because nothing is
            // speaking, there's no avatar voice to bleed into the open mic, so:
            //   • "hey bambi, show me bubbles" (one breath) and "hey bambi" … <short pause> … "command"
            //     both land in the primary listen window and run with no wait.
            //   • only if you say nothing does the command driver speak the stashed ack and listen again.
            //
            // Pick once here (PickVoiceLine locks internally, so off-UI is fine): voiced manifest variant
            // when available (rotates, avoiding immediate repeats), else a plain per-mod line, text-only.
            var voiced = App.Bark?.PickVoiceLine("voicecmd_wake");
            string ack;
            string? audio = null;
            if (voiced is { } line && !string.IsNullOrWhiteSpace(line.Text))
            {
                ack = line.Text;
                audio = line.Audio;
            }
            else
            {
                ack = ModKey() switch
                {
                    "bambi" => "mmm? you called for me~",
                    "circe" => "you called. i'm listening.",
                    _       => "yes, lovely? i'm right here~",
                };
            }

            // Stash for the listen window (dots bubble text) and the on-silence re-prompt (spoken there).
            _pendingWakeAckText = ack;
            _pendingWakeAckAudio = audio;

            // Instant visual "I'm listening" — dots, no speech. The listen window re-shows this too, but
            // popping it now covers the brief funnel hand-off so the cue appears the moment she's woken.
            DispatcherHelper.RunOnUI(() =>
            {
                try { App.AvatarWindow?.ShowListeningBubble(ack); } catch { }
            });
            RequestVoiceCommand(allowCommands: true);
        }

        /// <summary>
        /// Parse a command that rode in on the wake utterance ("hey bambi show me bubbles") and, if one
        /// fuzzy-matches an intent, run it immediately — no second listen window. Returns true when a
        /// command was executed. Strips the leading wake phrase (any phonetic variant) first; bare wake
        /// (no remainder) and unmatched tails return false so the caller runs the normal listen flow.
        /// </summary>
        private bool TryHandleInlineCommand(string? heard)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(heard)) return false;
                var tokens = SpeechService.Normalize(heard).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) return false;

                // Drop the wake prefix: pick the longest wake variant whose leading tokens fuzzy-match.
                int drop = 0;
                foreach (var v in ExpandWakeVariants(WakeWords()))
                {
                    var vt = SpeechService.Normalize(v).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (vt.Length == 0 || vt.Length > tokens.Length || vt.Length <= drop) continue;
                    var lead = string.Join(' ', tokens.Take(vt.Length));
                    if (SpeechService.Similarity(string.Join(' ', vt), lead) >= 0.6) drop = vt.Length;
                }
                if (drop == 0) return false;                       // wake prefix not found — let the flow handle it
                var remainder = string.Join(' ', tokens.Skip(drop)).Trim();
                if (remainder.Length == 0) return false;           // bare wake, no chained command

                // Fuzzy-match the remainder to an intent (same scoring + guards as the listen path).
                var (best, bestScore) = MatchVoiceIntent(remainder);
                if (best == null) return false;
                // Mantra / "again" need the listen-flow context — defer those to the normal path.
                if (best.IsMantra || best.IsReplay) return false;

                App.Logger?.Information(
                    "AutonomyService: inline voice command '{Name}' from wake utterance (remainder '{Rem}', score {Score:0.00})",
                    best.Name, remainder, bestScore);

                var toRun = best;
                if (Application.Current?.Dispatcher != null)
                    _ = Application.Current.Dispatcher.InvokeAsync(() => ExecuteIntentAndConfirm(toRun));
                if (best.Repeatable) _lastVoiceIntent = best;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AutonomyService: TryHandleInlineCommand failed");
                return false;
            }
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
