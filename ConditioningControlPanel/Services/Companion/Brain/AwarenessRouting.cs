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

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return false;

                var avatar = App.AvatarWindow;
                if (avatar == null) return false;

                // Rare and above get the double bounce; Uncommon is a quip, not an event (doc 02 §3.2).
                avatar.SpeakAwarenessLine(line, doubleBounce: tier >= RarityTier.Rare);

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
    /// The production line source: <c>App.Brain.ReactAsync</c>, so an awareness reaction lands in the
    /// turn log as an <c>AmbientEvent</c> and the chat thread knows what she just commented on.
    ///
    /// <para><b>What crosses the wire is the projection and nothing else.</b> The event text is
    /// <see cref="AwarenessProjection.BuildCloudProjection"/> — categories and bucketed numbers — and
    /// the fuller local projection is used only when the active provider is the machine-local Ollama
    /// path. Any doubt about which provider is live resolves to the cloud projection.</para>
    /// </summary>
    public sealed class BrainAwarenessLineSource : IAwarenessLineSource
    {
        private readonly Func<CompanionBrain?> _brain;
        private readonly Func<bool> _isLocalTransport;
        private bool _warnedAboutClamp;

        public BrainAwarenessLineSource(Func<CompanionBrain?>? brain = null, Func<bool>? isLocalTransport = null)
        {
            _brain = brain ?? (() => App.Brain);
            _isLocalTransport = isLocalTransport ?? DefaultIsLocalTransport;
        }

        /// <inheritdoc />
        public bool IsAvailable
        {
            get
            {
                try
                {
                    if (!CompanionBrain.ShouldRoute(_brain())) return false;
                    return App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true;
                }
                catch { return false; }
            }
        }

        /// <inheritdoc />
        public async Task<AwarenessReply> RequestAsync(ContextFrame frame, CancellationToken cancellationToken)
        {
            var brain = _brain();
            if (brain == null || frame == null) return AwarenessReply.Empty;

            string projection;
            try
            {
                projection = _isLocalTransport()
                    ? AwarenessProjection.BuildLocalProjection(frame)
                    : AwarenessProjection.BuildCloudProjection(frame);
            }
            catch (Exception ex)
            {
                // The privacy layer could not answer. Drop the frame rather than improvise a
                // descriptor — an improvised one is exactly how content leaks past a projection.
                App.Logger?.Warning(ex, "BrainAwarenessLineSource: projection failed, dropping the frame");
                return AwarenessReply.Empty;
            }

            // CompanionEvent clamps an ambient descriptor to ~100 characters (25 tokens), which a
            // projection always exceeds. Sending it anyway would hand the model a JSON fragment cut
            // mid-key, so this path stands down and the arbiter falls back to a bark instead. See the
            // integration note: the reaction prompt needs a structured-context hook, which is the
            // prompt package's deliverable.
            if (new CompanionEvent(projection).Normalized().Length < projection.Trim().Length)
            {
                if (!_warnedAboutClamp)
                {
                    _warnedAboutClamp = true;
                    App.Logger?.Warning(
                        "[AWARE] awareness projection ({Length} chars) exceeds CompanionEvent.MaxChars ({Max}); " +
                        "LLM awareness lines stand down until the reaction prompt takes structured context",
                        projection.Length, CompanionEvent.MaxChars);
                }
                return AwarenessReply.Empty;
            }

            try
            {
                var result = await brain.ReactAsync(new CompanionEvent(projection), cancellationToken)
                    .ConfigureAwait(false);

                // A refusal, a canned fallback or a dropped ambient call are all "nothing usable".
                // Moderation already logged whatever it needed to; the arbiter serves a bark.
                if (result == null || result.Refusal != null || !result.IsAiGenerated) return AwarenessReply.Empty;
                return AwarenessReply.Parse(result.Text);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BrainAwarenessLineSource: reaction call failed");
                return AwarenessReply.Empty;
            }
        }

        /// <summary>
        /// True only when the active provider is provably the machine-local one. Anything else — a
        /// cloud provider, a custom endpoint, an unreadable setting — is treated as remote, because the
        /// fuller projection may only ever be built for a transport that cannot forward it.
        /// </summary>
        private static bool DefaultIsLocalTransport()
        {
            try
            {
                return App.Settings?.Current?.CompanionPrompt?.AiProvider == Models.AiProviderType.Local;
            }
            catch { return false; }
        }
    }
}
