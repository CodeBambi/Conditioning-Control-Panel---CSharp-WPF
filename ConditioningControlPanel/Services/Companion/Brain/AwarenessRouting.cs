using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The switch that takes the legacy mouths offline and hands the moment to the arbiter.
    ///
    /// <para><b>Why a holder and not just a settings read.</b> Two legacy paths self-fire on window
    /// changes today: <c>BarkService</c>'s <c>ActivityChanged</c>/<c>StillOnActivity</c> subscriptions
    /// and <c>AvatarTubeWindow</c>'s reaction handlers. v2 replaces both, and while both are live at
    /// once the companion has the two mouths this whole train exists to fix. But suppressing them on a
    /// settings flag alone would mute her outright on any machine where v2 is configured and the
    /// observer failed to come up. So the flag is not enough: something must have actually
    /// <see cref="Attach"/>ed a live arbiter. Unwired = legacy behaviour, unchanged, every time.</para>
    ///
    /// <para>Wiring is one line for whoever owns startup:
    /// <c>AwarenessV2Routing.Attach(arbiter)</c> after the observer is constructed, and
    /// <c>Detach()</c> on shutdown.</para>
    /// </summary>
    public static class AwarenessV2Routing
    {
        private static IReactionArbiter? _arbiter;

        /// <summary>The live arbiter, or null when v2 was never wired up.</summary>
        public static IReactionArbiter? Arbiter => Volatile.Read(ref _arbiter);

        /// <summary>
        /// True when the arbiter owns ambient speech: an arbiter is wired AND awareness v2 is enabled,
        /// consented and switched on (<see cref="AwarenessObserver.IsEnabled"/>).
        ///
        /// <para>Every legacy self-firing path checks this and returns. It is deliberately cheap and
        /// exception-free — it runs on the 1.5s poll's event path.</para>
        /// </summary>
        public static bool IsActive
        {
            get
            {
                try { return Arbiter != null && AwarenessObserver.IsEnabled; }
                catch { return false; }
            }
        }

        /// <summary>Installs the arbiter as the owner of ambient speech. Idempotent.</summary>
        public static void Attach(IReactionArbiter? arbiter)
        {
            Volatile.Write(ref _arbiter, arbiter);
            App.Logger?.Information("AwarenessV2Routing: arbiter {State}", arbiter == null ? "detached" : "attached");
        }

        /// <summary>Hands ambient speech back to the legacy paths. Used on shutdown and by tests.</summary>
        public static void Detach() => Attach(null);
    }

    /// <summary>
    /// The production mouth: barks through <c>BarkService</c>, lines through the avatar's existing
    /// speech-bubble entry point, and a live foreground read for the staleness check.
    ///
    /// <para>Nothing here decides anything. It is the thinnest possible adapter precisely because the
    /// deciding is what the tests have to be able to reach.</para>
    /// </summary>
    public sealed class AvatarAwarenessSpeaker : IAwarenessSpeaker
    {
        private readonly Func<string?>? _currentAppId;

        /// <param name="currentAppId">
        /// Optional override for the live foreground app id. The observer package resolves the
        /// foreground window every poll and should point this at that value once it does; the default
        /// re-reads the foreground window here, which is correct but does the classification twice.
        /// </param>
        public AvatarAwarenessSpeaker(Func<string?>? currentAppId = null)
        {
            _currentAppId = currentAppId;
        }

        /// <inheritdoc />
        public string? CurrentAppId
        {
            get
            {
                try { return _currentAppId != null ? _currentAppId() : ResolveForegroundAppId(); }
                catch { return null; }
            }
        }

        /// <inheritdoc />
        public bool TrySpeakBark(ContextFrame frame)
        {
            if (!StillWatching()) return false;

            try { return App.Bark?.RaiseAwarenessBark(frame) ?? false; }
            catch (Exception ex)
            {
                App.Logger?.Debug("AvatarAwarenessSpeaker: bark failed: {Error}", ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public bool TrySpeakLine(string line, RarityTier tier)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (!StillWatching()) return false;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return false;

                var avatar = App.AvatarWindow;
                if (avatar == null) return false;

                // Chat-suppression, the same question BarkService asks before every bark: an ambient
                // quip must not land on top of a conversation the user is actually having. Awareness
                // is the quieter of the two by design, so it defers rather than pre-empting.
                if (IsCompanionBusy()) return false;

                // Rare and above get the double bounce; Uncommon is a quip, not an event (doc 02 §3.2).
                avatar.SpeakAwarenessLine(line, doubleBounce: tier >= RarityTier.Rare);

                // The 60s outer floor is shared in BOTH directions or it is not a floor: BarkService
                // reports its barks to the arbiter's ledger, and an LLM line pushes BarkService's own
                // global gap forward. Without this the next bark trigger after the bubble closes fires
                // against a stale gap and speaks seconds after she just spoke.
                App.Bark?.NotifyExternalLineSpoken();

                // Self-echo guard: the same call BarkService makes after every spoken line, so she can
                // never trip an OCR/keyword trigger off her own bubble.
                App.KeywordTriggers?.MuteKeywordEcho(line, SelfEchoMuteMs);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarAwarenessSpeaker: line delivery failed");
                return false;
            }
        }

        /// <summary>
        /// Whether awareness is still switched on RIGHT NOW. The LLM leg can take eight seconds, and
        /// the user can close her eyes inside those eight seconds — delivering a line about what they
        /// were doing after they told her to stop watching is the worst possible moment for a trust
        /// surface. Read defensively; unreadable settings mean "do not speak".
        /// </summary>
        private static bool StillWatching()
        {
            try { return AwarenessObserver.IsEnabled; }
            catch { return false; }
        }

        /// <summary>Mirrors <c>BarkService.CompanionBusy</c>'s window and question.</summary>
        private static bool IsCompanionBusy()
        {
            try
            {
                int window = App.Settings?.Current?.BarkChatSuppressionMs ?? 10000;
                return App.AvatarWindow?.IsCompanionBusy(window) ?? false;
            }
            catch { return false; }
        }

        /// <summary>Matches <c>BarkService.SelfEchoMuteMs</c>: after speaking, mute that text for OCR/keywords.</summary>
        private const int SelfEchoMuteMs = 8000;

        /// <summary>
        /// Classifies the window in the foreground RIGHT NOW through the same
        /// <see cref="AppClusterMap"/> the frame's app id came from, so the staleness comparison is
        /// like-for-like. Deliberately not cached — "what is on screen now" is the entire question.
        /// Returns null when the title is unusable or unclassified, which reads as "unknown", not
        /// "different".
        /// </summary>
        private static string? ResolveForegroundAppId()
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return null;

            var sb = new StringBuilder(512);
            if (GetWindowText(handle, sb, sb.Capacity) <= 0) return null;

            var (_, app) = AppClusterMap.Classify(sb.ToString());
            return string.IsNullOrWhiteSpace(app) ? null : app;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    }

    /// <summary>
    /// The production line source: <see cref="AwarenessReactionService"/>, which builds awareness's own
    /// dedicated small reaction prompt (persona digest + angle cards + frame projection + recent ban
    /// list) and sends it with <c>AiCallOptions.Reaction</c>.
    ///
    /// <para><b>The real number, since a cost model built on the wrong one is worse than none.</b> The
    /// AWARENESS-authored zones are the ~700-900 tokens doc 02 §3.1 budgets, and
    /// <c>AwarenessPrompt.AuthoredTokens</c> measures exactly those. A cold, uncached request is
    /// roughly twice that once the constitutional safety block, its floor and the per-call tail are
    /// counted — see <c>AwarenessPrompt.TotalTokens</c>, which is what actually gets billed until a
    /// provider caches the (genuinely byte-stable) prefix.</para>
    ///
    /// <para><b>Why not <c>App.Brain.ReactAsync</c>.</b> That path exists for short ambient nudges and
    /// clamps its descriptor to <c>CompanionEvent.MaxChars</c> (~100 characters), which every
    /// projection exceeds — it would hand the model JSON cut mid-key. It also carries the
    /// multi-thousand-token companion chat prompt, which is exactly the cost the dedicated reaction
    /// prompt exists to avoid (doc 02: "~700-900 tokens in, ~40 out. Versus today's
    /// multi-thousand-token prompt for the same"). One consequence worth stating plainly: an awareness
    /// line does NOT land in the chat turn log, so the chat thread does not know what she just
    /// commented on. Train 4's memory seam is where that reconnects.</para>
    ///
    /// <para><b>Moderation is untouched.</b> The Layer-1 spine — <c>CheckInput</c> on the frame message,
    /// <c>CheckOutput</c> on the reply, <c>ModerationLog</c> — runs inside <c>IAiService.SendAsync</c>
    /// exactly as it does for chat. A refusal comes back as <c>AwarenessReaction.Refusal</c> and is
    /// treated here as "nothing usable", so the arbiter serves a bark and a refusal is never spoken.</para>
    ///
    /// <para><b>What crosses the wire is the projection and nothing else.</b> The frame message is
    /// <see cref="AwarenessProjection.BuildCloudProjection"/> — categories and bucketed numbers — and
    /// the fuller local projection is used only when the active provider is the machine-local Ollama
    /// path. Any doubt about which provider is live resolves to the cloud projection. That decision now
    /// lives inside <see cref="AwarenessReactionService"/>, so exactly one place makes it.</para>
    /// </summary>
    public sealed class BrainAwarenessLineSource : IAwarenessLineSource
    {
        private readonly Func<CompanionBrain?> _brain;
        private readonly AwarenessReactionService _reactions;

        public BrainAwarenessLineSource(
            Func<CompanionBrain?>? brain = null,
            Func<bool>? isLocalTransport = null,
            AwarenessReactionService? reactions = null)
        {
            _brain = brain ?? (() => App.Brain);
            // isLocalTransport stays in the signature because the projection choice remains the
            // caller's to override; it is answered inside the reaction service now, which is the one
            // place that assembles the prompt. Passing null keeps that service's own default.
            _reactions = reactions ?? new AwarenessReactionService(isMachineLocal: isLocalTransport);
        }

        /// <inheritdoc />
        public bool IsAvailable
        {
            get
            {
                try
                {
                    var brain = _brain();
                    if (!CompanionBrain.ShouldRoute(brain)) return false;

                    // Awareness is an AMBIENT source, and the brain's single-flight contract is
                    // "ambient requests are DROPPED when busy" (CompanionBrain §1.6). This leg does not
                    // go through the brain's gate — it has its own small prompt — so it has to honour
                    // the same rule here, or a quip runs concurrently with an in-flight chat reply and
                    // then stomps it: delivery goes through GigglePriority, which clears the speech
                    // queue and cancels the thinking animation.
                    if (brain != null && brain.IsBusy) return false;

                    return App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true;
                }
                catch { return false; }
            }
        }

        /// <inheritdoc />
        public async Task<AwarenessReply> RequestAsync(ContextFrame frame, CancellationToken cancellationToken)
        {
            if (frame == null) return AwarenessReply.Empty;

            // The reaction service never throws and never speaks: every ordinary failure (no
            // transport, prompt build failed, refusal, empty reply) comes back as a reason token, and
            // every reason other than a deliberate pass means "nothing usable" — which costs a bark,
            // not a crash, on a path that runs from a background timer.
            var reaction = await _reactions.GetAwarenessReactionAsync(frame, cancellationToken)
                                           .ConfigureAwait(false);

            if (reaction == null) return AwarenessReply.Empty;

            // A deliberate [PASS] is not a failure. It must survive as a pass so the arbiter refunds
            // the slot instead of answering her chosen silence with a canned bark (doc 02 §7 item 5).
            if (reaction.Passed) return AwarenessReply.Pass;

            if (!reaction.IsAiGenerated || reaction.Refusal != null || !reaction.HasLine)
                return AwarenessReply.Empty;

            return new AwarenessReply(reaction.Line, reaction.Callback, IsPass: false);
        }
    }
}
