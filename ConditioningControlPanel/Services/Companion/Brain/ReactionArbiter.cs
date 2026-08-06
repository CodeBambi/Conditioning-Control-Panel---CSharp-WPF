using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// THE arbiter (MASTER-SCOPE §3 reconciliation 1: doc 01's Voice Arbiter and doc 02's
    /// ReactionArbiter are one component, and it lives with the brain — hence this file's folder and
    /// its <c>Services.Awareness</c> namespace, which the observer, prompt and privacy packages all
    /// code against).
    ///
    /// <para><b>One character, one mouth.</b> Every ambient line the companion speaks — an
    /// awareness-gated bark, a model-written awareness quip, a keyword trigger's avatar comment —
    /// passes through <see cref="ReactionCooldownLedger"/>. Today <c>BarkService</c> and the AvatarTube
    /// reaction path keep independent cooldowns and can both fire on a single tab switch (doc 02 §1.5);
    /// under <c>UseAwarenessV2</c> both of those stop self-firing and this drives delivery instead.</para>
    ///
    /// <para><b>Exactly one reaction per frame.</b> <see cref="SubmitAsync"/> has one delivery per call
    /// by construction: every branch returns immediately after speaking, the LLM path falls back to a
    /// bark at most once, and a second frame arriving while one is in flight is refused rather than
    /// queued. This is the same "Fork D" shape the lock-card path already uses.</para>
    ///
    /// <para><b>Cooldowns burn on delivery, never on attempt.</b> A refusal, a timeout, an empty reply,
    /// a stale drop and a <c>[PASS]</c> all leave the budget exactly as they found it — that is the
    /// literal meaning of "the budget slot is refunded", and it is why nothing here records anything
    /// before the speaker has said it did.</para>
    ///
    /// <para><b>Moderation is untouched.</b> The LLM path goes through <c>App.Brain.ReactAsync</c> →
    /// <c>IAiService.SendAsync</c>, which is where <c>ModerationGuard.CheckInput</c>/<c>CheckOutput</c>,
    /// the <c>ModerationLog</c> write and the refusal sentinels live. The arbiter deliberately does not
    /// re-check: a second check would double the compliance log and halve the hits needed to trip the
    /// user-facing cooldown. A refused reply arrives as "nothing usable" and costs a bark.</para>
    /// </summary>
    public sealed class ReactionArbiter : IReactionArbiter
    {
        /// <summary>
        /// How long an LLM line may take before the frame is written off and the bark fires instead
        /// (doc 02 §5.1). Past this the joke is stale anyway.
        /// </summary>
        public static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(8);

        private readonly ReactionCooldownLedger _cooldowns;
        private readonly WorthinessScorer? _scorer;
        private readonly Func<DateTime> _clock;
        private readonly IAwarenessSpeaker? _speaker;
        private readonly IAwarenessLineSource? _lineSource;
        private readonly ICompanionMemory? _memory;
        private readonly TimeSpan _llmTimeout;

        // 0 = idle, 1 = a frame owns the mouth. The LLM leg is seconds long; without this a burst of
        // frames would race to speak and "one reaction per moment" would hold per frame but not per
        // moment, which is the same bug wearing a different hat.
        private int _inFlight;

        public ReactionArbiter(
            ReactionCooldownLedger? cooldowns = null,
            WorthinessScorer? scorer = null,
            Func<DateTime>? localClock = null,
            IAwarenessSpeaker? speaker = null,
            IAwarenessLineSource? lineSource = null,
            ICompanionMemory? memory = null,
            TimeSpan? llmTimeout = null)
        {
            _cooldowns = cooldowns ?? new ReactionCooldownLedger();
            _scorer = scorer;
            _clock = localClock ?? (() => DateTime.Now);
            _speaker = speaker;
            _lineSource = lineSource;
            _memory = memory;
            _llmTimeout = llmTimeout is { } t && t > TimeSpan.Zero ? t : LlmTimeout;
        }

        /// <summary>The shared cooldown state. Exposed so nothing invents a second dialect of it.</summary>
        public ReactionCooldownLedger Cooldowns => _cooldowns;

        /// <summary>True when an LLM line could actually be produced right now.</summary>
        private bool LlmReady => _lineSource != null && _lineSource.IsAvailable;

        /// <inheritdoc />
        public async Task<ArbiterDecision> SubmitAsync(ContextFrame frame, CancellationToken cancellationToken = default)
        {
            if (frame == null) return Log(null, ArbiterDecision.Silent("null-frame"));

            // The adult cluster has its own reaction toggle, and an unreadable settings object is not
            // permission. Fails closed: no toggle, no joke about that cluster (doc 02 §6.1).
            if (frame.IsAdultCluster && !AdultReactionsAllowed())
                return Log(frame, ArbiterDecision.Silent("adult-off"));

            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
                return Log(frame, ArbiterDecision.Silent("busy"));

            try
            {
                var now = _clock();
                bool wantsLlm = frame.Tier >= RarityTier.Uncommon;

                if (wantsLlm && LlmReady)
                {
                    if (_cooldowns.CanSpeak(ReactionSource.AwarenessLlm, frame.AppId, now, out var llmGate))
                        return Log(frame, await RunLlmAsync(frame, cancellationToken).ConfigureAwait(false));

                    // Doc 02 §5.1: over-budget or floored LLM degrades to the free, instant tier. The
                    // bark gate is a different question (no 90s LLM floor), so ask it rather than
                    // assuming the answer.
                    return Log(frame, TryBark(frame, llmGate));
                }

                var reason = wantsLlm ? "llm-unavailable" : "common-tier";
                return Log(frame, TryBark(frame, reason));
            }
            catch (Exception ex)
            {
                // A background awareness failure is a missed joke, never a crash log.
                App.Logger?.Warning(ex, "ReactionArbiter: submit failed");
                return Log(frame, ArbiterDecision.Silent("error"));
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
            }
        }

        /// <inheritdoc />
        public void RecordExternalLine(ReactionSource source, string? appId = null)
            => NoteDelivered(source, appId, _clock());

        /// <inheritdoc />
        public bool CanSpeak(ReactionSource source, string? appId = null) =>
            _cooldowns.CanSpeak(source, appId, _clock(), out _);

        // ===================== the LLM leg =====================

        private async Task<ArbiterDecision> RunLlmAsync(ContextFrame frame, CancellationToken cancellationToken)
        {
            AwarenessReply reply;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var call = _lineSource!.RequestAsync(frame, cts.Token);
                var timeout = Task.Delay(_llmTimeout, cts.Token);
                var finished = await Task.WhenAny(call, timeout).ConfigureAwait(false);

                // Whichever lost, stop it: an abandoned request must not keep a provider slot warm,
                // and an abandoned delay must not sit on the timer queue.
                try { cts.Cancel(); } catch { /* already disposed race */ }

                if (!ReferenceEquals(finished, call))
                {
                    // The abandoned call is never awaited, so observe its fault explicitly — an
                    // unobserved task exception on a background path is a crash log waiting to happen.
                    _ = call.ContinueWith(static t => _ = t.Exception,
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

                    App.Logger?.Debug("[AWARE] llm timed out after {Seconds}s", (int)_llmTimeout.TotalSeconds);
                    return FallbackBark(frame, "llm-timeout");
                }

                reply = await call.ConfigureAwait(false) ?? AwarenessReply.Empty;
            }
            catch (OperationCanceledException)
            {
                return FallbackBark(frame, "llm-cancelled");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ReactionArbiter: awareness line request failed");
                return FallbackBark(frame, "llm-error");
            }

            // [PASS]: she had nothing good. Deliver nothing, spend nothing, say so in the log. The
            // "refund" is structural — no cooldown was burned to get here.
            if (reply.IsPass)
            {
                App.Logger?.Debug("[AWARE] llm passed on {App}", AwarenessText.SanitizeId(frame.AppId));
                return ArbiterDecision.Silent("pass");
            }

            // Delivery-time staleness (doc 02 §4.3). A present-tense line about a window the user left
            // is the loudest way this feature reads as broken, so it is never delivered: either the
            // model offered a past-tense callback variant, or the line is dropped.
            bool stale = IsStale(frame);
            var text = stale ? reply.Alternate : reply.Line;

            if (string.IsNullOrWhiteSpace(text))
            {
                if (stale)
                {
                    // Not a bark either: a canned line about the same app is exactly as stale.
                    App.Logger?.Debug("[AWARE] dropped a stale line for {App} (no alternate offered)",
                        AwarenessText.SanitizeId(frame.AppId));
                    return ArbiterDecision.Silent("stale");
                }
                return FallbackBark(frame, "llm-empty");
            }

            if (_speaker == null || !_speaker.TrySpeakLine(text!, frame.Tier))
                return FallbackBark(frame, "no-mouth");

            var now = _clock();
            NoteDelivered(ReactionSource.AwarenessLlm, frame.AppId, now);
            RememberLine(text!, frame, now);
            return new ArbiterDecision(AwarenessVerdict.Llm, frame.Tier, stale ? "delivered-alt" : "delivered");
        }

        /// <summary>
        /// The one and only fallback. Reached from the timeout, failure, empty and refusal paths, each
        /// of which returns straight afterwards — so a frame can produce a bark instead of a line, but
        /// never as well as one.
        /// </summary>
        private ArbiterDecision FallbackBark(ContextFrame frame, string reason) => TryBark(frame, reason);

        // ===================== the bark leg =====================

        private ArbiterDecision TryBark(ContextFrame frame, string reason)
        {
            var now = _clock();

            if (!_cooldowns.CanSpeak(ReactionSource.Bark, frame.AppId, now, out var gate))
                return ArbiterDecision.Silent(Gate(reason, gate));

            if (_speaker == null || !_speaker.TrySpeakBark(frame))
                return ArbiterDecision.Silent(Gate(reason, "no-bark"));

            NoteDelivered(ReactionSource.Bark, frame.AppId, now);
            return new ArbiterDecision(AwarenessVerdict.Bark, RarityTier.Common, reason);
        }

        // ===================== bookkeeping =====================

        /// <summary>
        /// The single delivery hook: cooldown ledger first, pacing state second. Both, always, or the
        /// arbiter and the scorer end up disagreeing about how much has already been said and the
        /// silence budget leaks.
        ///
        /// <para>Keyword lines are excluded from the scorer: the user configured them by hand, so they
        /// must not push the awareness threshold up as a punishment for using the feature. They still
        /// take the global gap, which is what stops two voices inside a second.</para>
        /// </summary>
        private void NoteDelivered(ReactionSource source, string? appId, DateTime at)
        {
            _cooldowns.RecordDelivery(source, appId, at);
            if (source != ReactionSource.Keyword) _scorer?.RegisterDelivery(appId, at);
        }

        /// <summary>
        /// Adds a delivered model line to the recent-reaction ban list, so the next prompt can be told
        /// not to reuse its structure or punchline (doc 02 §3.1 item 4).
        ///
        /// <para>Barks are deliberately absent: their text is authored, finite and meant to recur, and
        /// banning her own voicelines would mute the free tier a line at a time.</para>
        /// </summary>
        private void RememberLine(string text, ContextFrame frame, DateTime at)
        {
            if (_memory == null) return;
            try
            {
                _ = _memory.RecordReactionAsync(new ReactionSummary(text, frame.AppId, frame.Tier, at));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ReactionArbiter: ban-list write failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Whether the user has moved on since the frame was cut. Unknown reads as "not stale": the
        /// foreground is unclassifiable for plenty of ordinary windows, and treating that as stale
        /// would drop every line on those machines.
        /// </summary>
        private bool IsStale(ContextFrame frame)
        {
            string? live;
            try { live = _speaker?.CurrentAppId; }
            catch (Exception ex)
            {
                App.Logger?.Debug("ReactionArbiter: foreground read failed: {Error}", ex.Message);
                return false;
            }

            if (string.IsNullOrWhiteSpace(live) || string.IsNullOrWhiteSpace(frame.AppId)) return false;
            return !string.Equals(
                AwarenessText.SanitizeId(live),
                AwarenessText.SanitizeId(frame.AppId),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The adult-cluster reaction toggle, read the way every awareness gate reads settings: a null
        /// or throwing settings object is a "no", never a "sure".
        /// </summary>
        private static bool AdultReactionsAllowed()
        {
            try { return App.Settings?.Current?.AwarenessAdultReactionsEnabled == true; }
            catch { return false; }
        }

        private static string Gate(string reason, string gate) =>
            string.Equals(reason, gate, StringComparison.Ordinal) ? reason : reason + "/" + gate;

        /// <summary>
        /// The <c>[AWARE]</c> decision record — one per submitted frame, no exceptions (invariant 8,
        /// same philosophy as BarkService's <c>[BARK]</c> log). Every string in it is either an enum
        /// name or a gate name from our own code, and the app id goes through
        /// <see cref="AwarenessText.SanitizeId"/>: a mod-supplied cluster file must not be able to
        /// write newlines into the log.
        /// </summary>
        private ArbiterDecision Log(ContextFrame? frame, ArbiterDecision decision)
        {
            try
            {
                var now = _clock();
                App.Logger?.Information(
                    "[AWARE] app={App} score={Score} tier={Tier} verdict={Verdict} gate={Gate} lines_hr={Lines}",
                    AwarenessText.SanitizeId(frame?.AppId),
                    AwarenessText.Num(frame?.Worthiness ?? 0),
                    decision.Tier,
                    decision.Verdict,
                    AwarenessText.SanitizeDisplayName(decision.Reason, 48),
                    _cooldowns.LinesLastHour(now));
            }
            catch { /* a log line must never be the reason a reaction fails */ }

            return decision;
        }
    }
}
