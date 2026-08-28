using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Services.Quiz
{
    /// <summary>
    /// SPEECH BRIDGE for the Graded Intake's say-it (Mantra) beat.
    ///
    /// The page used to ask the browser for <c>window.SpeechRecognition</c>. Inside WebView2 that
    /// API is either absent or errors (<c>network</c> / <c>not-allowed</c>) because the Edge runtime
    /// has no cloud recognizer to lean on, so a T2 tester (Ashley, 2026-08-28) could never advance
    /// a "repeat after me" item by speaking. Regular mantras and She's Listening work because they
    /// go through the app's OFFLINE Vosk engine (<see cref="SpeechService"/>) — this bridge routes
    /// the intake through the same engine, with the same match leniency
    /// (<c>SpeechMatchThreshold</c> + the loudness gate), so a phrase that satisfies a spoken
    /// mantra satisfies the intake too.
    ///
    /// Wire (additive to the protocol in web-shim.js; the page feature-detects
    /// <c>init.config.speech.bridge</c> and keeps its browser path when absent, which is what the
    /// public website still uses):
    ///
    ///   Page -&gt; Host:  speech-start { id, phrase }   open the mic for this phrase (auto on beat mount)
    ///                  speech-stop  { id }           beat unmounted / typed instead / skipped
    ///   Host -&gt; Page:  speech-event { id, kind, ... }
    ///       kind = listening                        mic is open
    ///              partial  { transcript }          streaming hypothesis ("I'm hearing…")
    ///              final    { matched, transcript, score, loudEnough }
    ///              silence                          a whole window with nothing said; host re-opens
    ///              idle                             host stopped re-opening (silence/miss caps); re-tap
    ///              unavailable { reason }           see <see cref="IntakeSpeechPolicy"/> reasons
    ///              stopped                          ack of speech-stop / window closing
    ///
    /// ONE RULE: this never fights another mic owner. If the wake-word loop, push-to-talk, a lock
    /// card or a spoken mantra already holds the mic, the page is told <c>busy</c> and drops the
    /// item to typed input. (LockCardWindow and SpeakPromptSession evict the wake loop instead;
    /// the intake is a 20-minute windowed tool and standing She's Listening down for its whole
    /// length would be the bigger surprise.)
    ///
    /// Threading: page messages arrive on the UI thread; the listen loop runs on the pool and
    /// every post back is marshalled onto the dispatcher because <c>CoreWebView2</c> is UI-affine.
    /// </summary>
    internal static partial class IntakeHostService
    {
        private static readonly object _speechGate = new();
        private static CancellationTokenSource? _speechCts;
        private static int _speechId;            // the page's id for the session we are running (0 = none)

        /// <summary>The <c>speech</c> block that rides <c>init.config</c>. <c>bridge:true</c> is
        /// the feature-detect; <c>available</c>/<c>reason</c> are a snapshot so the page can paint
        /// the right note before its first start (availability is re-checked on every start).</summary>
        private static object BuildSpeechCaps()
        {
            var reason = SpeechUnavailability();
            return new { bridge = true, available = reason == null, reason };
        }

        /// <summary>Live availability check; null = the mic can open now.</summary>
        private static string? SpeechUnavailability()
        {
            try
            {
                var speech = App.Speech;
                var consent = App.Settings?.Current?.MicConsentGiven == true;
                var hasMic = SpeechService.HasCaptureDevice;
                // IsAvailable probes (and caches) the model; ask it first so ModelStatus is real.
                var model = speech == null ? SpeechModelStatus.NoModelFound
                          : (speech.IsAvailable ? SpeechModelStatus.Ok : speech.ModelStatus);
                var held = speech?.IsListening == true
                        || App.WakeWord?.IsListening == true;
                return IntakeSpeechPolicy.Unavailability(consent, hasMic, model, held);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("IntakeHostService.speech: availability probe failed: {E}", ex.Message);
                return IntakeSpeechPolicy.ReasonError;
            }
        }

        // ============================ page -> host ============================

        private static void OnSpeechStart(JObject o)
        {
            int id = (int?)o["id"] ?? 0;
            var phrase = ((string?)o["phrase"] ?? "").Trim();
            if (id <= 0 || phrase.Length == 0)
            {
                PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonError });
                return;
            }

            // A new start supersedes whatever was running (the page moved on to another beat, or
            // re-tapped after an idle). Cancel it first so at most one loop ever touches the mic.
            StopSpeechBridge("superseded", notifyPage: false);

            var reason = SpeechUnavailability();
            if (reason != null)
            {
                App.Logger?.Information("IntakeHostService: speech-start #{Id} refused ({Reason})", id, reason);
                PostSpeechEvent(id, "unavailable", new { reason });
                return;
            }

            var cts = new CancellationTokenSource();
            lock (_speechGate)
            {
                _speechCts = cts;
                _speechId = id;
            }
            App.Logger?.Information("IntakeHostService: speech-start #{Id} target='{Phrase}'", id, phrase);
            _ = Task.Run(() => RunSpeechLoopAsync(id, phrase, cts.Token));
        }

        private static void OnSpeechStop(JObject o)
        {
            int id = (int?)o["id"] ?? 0;
            lock (_speechGate)
            {
                // A stop for a session we already left behind is a no-op (the page's cleanup for the
                // previous beat can race the start of the next one).
                if (id != 0 && id != _speechId) return;
            }
            StopSpeechBridge("page", notifyPage: true);
        }

        /// <summary>Cancel the running listen loop, if any. Safe from any thread and idempotent.
        /// Called from <see cref="DisposeAll"/> so a closed window never leaves the mic open.</summary>
        private static void StopSpeechBridge(string why, bool notifyPage)
        {
            CancellationTokenSource? cts;
            int id;
            lock (_speechGate)
            {
                cts = _speechCts;
                id = _speechId;
                _speechCts = null;
                _speechId = 0;
            }
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            // The loop's own finally disposes the cts; cancelling also cuts the in-flight
            // RecognizePhraseAsync through the token it was handed, so the mic closes at once.
            App.Logger?.Debug("IntakeHostService: speech #{Id} stopped ({Why})", id, why);
            if (notifyPage) PostSpeechEvent(id, "stopped", null);
        }

        // ============================ the listen loop ============================

        private static async Task RunSpeechLoopAsync(int id, string phrase, CancellationToken ct)
        {
            var speech = App.Speech;
            if (speech == null) { PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonNoModel }); return; }

            string lastPartial = "";
            void OnPartial(object? _, string text)
            {
                // Vosk re-emits the partial on every 50 ms buffer; only forward changes.
                if (string.IsNullOrWhiteSpace(text) || text == lastPartial) return;
                lastPartial = text;
                PostSpeechEvent(id, "partial", new { transcript = text });
            }

            int silent = 0, misses = 0;
            speech.PartialTranscript += OnPartial;
            try
            {
                // The same window the spoken-mantra and lock-card paths use — long enough to get a
                // whole sentence out without racing the recognizer.
                var options = new RecognizeOptions { Timeout = TimeSpan.FromSeconds(10) };
                bool announced = false;

                while (!ct.IsCancellationRequested)
                {
                    // Re-check between windows: the engine can vanish (device unplugged) and another
                    // feature can claim the mic mid-run. Neither is ours to argue with.
                    var reason = SpeechUnavailability();
                    if (reason != null)
                    {
                        PostSpeechEvent(id, "unavailable", new { reason });
                        return;
                    }
                    if (!announced) { announced = true; PostSpeechEvent(id, "listening", null); }
                    lastPartial = "";

                    PhraseResult res;
                    try { res = await speech.RecognizePhraseAsync(phrase, options, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("IntakeHostService: speech #{Id} recognize threw: {E}", id, ex.Message);
                        PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonError });
                        return;
                    }
                    if (ct.IsCancellationRequested) return;

                    if (res.Unavailable)
                    {
                        // The single-session guard refused us: someone else grabbed the mic between
                        // our probe and the open. Report busy rather than spin.
                        PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonBusy });
                        return;
                    }

                    if (res.Matched)
                    {
                        App.Logger?.Information("IntakeHostService: speech #{Id} matched '{Phrase}' (score={S:0.00}, conf={C:0.00})",
                            id, phrase, res.Score, res.Confidence);
                        PostSpeechEvent(id, "final", new { matched = true, transcript = res.Transcript, score = res.Score, loudEnough = res.LoudEnough });
                        return;
                    }

                    if (res.TimedOut && string.IsNullOrWhiteSpace(res.Transcript))
                    {
                        silent++;
                        misses = 0;
                        PostSpeechEvent(id, "silence", null);
                    }
                    else
                    {
                        misses++;
                        silent = 0;
                        App.Logger?.Information("IntakeHostService: speech #{Id} miss — heard '{Heard}' (score={S:0.00}, loud={L})",
                            id, res.Transcript, res.Score, res.LoudEnough);
                        PostSpeechEvent(id, "final", new { matched = false, transcript = res.Transcript, score = res.Score, loudEnough = res.LoudEnough });
                        // A beat between windows so the page's "almost" can land before the mic reopens.
                        try { await Task.Delay(350, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                    }

                    if (IntakeSpeechPolicy.ShouldGoIdle(silent, misses))
                    {
                        PostSpeechEvent(id, "idle", null);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("IntakeHostService: speech #{Id} loop failed: {E}", id, ex.Message);
                PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonError });
            }
            finally
            {
                speech.PartialTranscript -= OnPartial;
                CancellationTokenSource? mine = null;
                lock (_speechGate)
                {
                    if (_speechId == id) { mine = _speechCts; _speechCts = null; _speechId = 0; }
                }
                try { mine?.Dispose(); } catch { }
            }
        }

        // ============================ host -> page ============================

        /// <summary>Post one <c>speech-event</c>. Marshals to the UI thread (CoreWebView2 is
        /// UI-affine) and drops the frame silently once the window is gone.</summary>
        private static void PostSpeechEvent(int id, string kind, object? extra)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            void Send()
            {
                try
                {
                    if (_host == null) return;
                    var frame = new JObject { ["type"] = "speech-event", ["id"] = id, ["kind"] = kind };
                    if (extra != null) frame.Merge(JObject.FromObject(extra));
                    _host.Post(frame);
                }
                catch (Exception ex) { App.Logger?.Debug("IntakeHostService.speech post: {E}", ex.Message); }
            }
            if (disp.CheckAccess()) Send();
            else disp.BeginInvoke((Action)Send);
        }
    }
}
