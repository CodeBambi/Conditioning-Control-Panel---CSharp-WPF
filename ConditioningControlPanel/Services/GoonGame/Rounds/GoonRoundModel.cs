using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// ============================================================================
// GOON GAME — Phase D. Sudden-death round model: the *specs* a round generates
// deterministically from its shared seed, the *input feeds* a round consumes,
// and the presenter it renders through.
//
// Design rule (plan §Phase D): a round NEVER touches a CCP service directly.
// It produces a spec (identical byte-for-byte on both machines because it is
// derived only from the round seed + difficulty) and consumes input events.
// Phase E implements IGoonRoundPresenter over the real UI/services and
// IGoonRoundInputs over the real input sources. Everything here is headless
// and unit-testable with the GoonFake* feeds at the bottom of this file.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    // ------------------------------------------------------------------ specs

    /// <summary>
    /// The quick-draw lock card BOTH players receive at the same shared-clock instant.
    /// Phrase/repeats come from the round seed, never from either player's own phrase
    /// pool (those are mod-specific and would differ between the two machines).
    /// <para>
    /// <see cref="Strict"/> is always false: strict mode disables Esc, and Esc must stay
    /// mapped to Mercy for the whole match (plan §safety rails). Same reason there is no
    /// lockdown flag anywhere in this model.
    /// </para>
    /// </summary>
    public sealed class GoonLockCardSpec
    {
        public string Phrase { get; init; } = "";
        public int Repeats { get; init; } = 1;
        /// <summary>Always false in GG — Esc must remain available as Mercy.</summary>
        public bool Strict { get; init; }
        /// <summary>GG never forces voice; a receiver that has voice mode on may still use it.</summary>
        public bool Voice { get; init; }
        /// <summary>Round gives up after this long and scores the card as unsolved.</summary>
        public int TimeLimitMs { get; init; } = GoonRoundConsts.QuickDrawTimeoutMs;
    }

    /// <summary>One flash in the staring-contest barrage. Positions are screen-normalized (0..1).</summary>
    public sealed class GoonFlashBeat
    {
        public int OffsetMs { get; init; }
        public int DurationMs { get; init; }
        public double Intensity { get; init; }
        public double NormX { get; init; }
        public double NormY { get; init; }
        public double Scale { get; init; }
    }

    /// <summary>Synchronized flash barrage both players endure while trying not to blink.</summary>
    public sealed class GoonStaringContestSpec
    {
        public int DurationMs { get; init; }
        public int Difficulty { get; init; }
        public IReadOnlyList<GoonFlashBeat> Beats { get; init; } = Array.Empty<GoonFlashBeat>();
    }

    public enum GoonStimulusKind
    {
        /// <summary>A feint. Pressing on a decoy is a false start.</summary>
        Decoy,
        /// <summary>The real stimulus — the local Stopwatch measurement starts here.</summary>
        Real,
    }

    /// <summary>Reaction duel: seed-determined delay, seed-determined decoys, bounded response window.</summary>
    public sealed class GoonReactionDuelSpec
    {
        public int DelayMs { get; init; }
        public int MaxResponseMs { get; init; } = GoonRoundConsts.ReactionMaxResponseMs;
        public int Difficulty { get; init; }
        /// <summary>Offsets (ms from round start) of feint stimuli. Pressing on one = false start.</summary>
        public IReadOnlyList<int> DecoyOffsetsMs { get; init; } = Array.Empty<int>();
    }

    /// <summary>One bubble in the race layout. Position/size are screen-normalized (0..1).</summary>
    public sealed class GoonBubbleSpawn
    {
        public int Index { get; init; }
        public double NormX { get; init; }
        public double NormY { get; init; }
        public double Scale { get; init; }
        public int SpawnOffsetMs { get; init; }
        public double DriftAngleDeg { get; init; }
        public double DriftSpeed { get; init; }
    }

    /// <summary>Identical swarm on both machines; first to clear wins, else most cleared at timeout.</summary>
    public sealed class GoonBubbleRaceSpec
    {
        public int Count { get; init; }
        public int TimeoutMs { get; init; } = GoonRoundConsts.BubbleRaceTimeoutMs;
        public int Difficulty { get; init; }
        public IReadOnlyList<GoonBubbleSpawn> Bubbles { get; init; } = Array.Empty<GoonBubbleSpawn>();
    }

    /// <summary>Handed to the presenter as soon as the round is scheduled, so the UI can count down.</summary>
    public sealed class GoonRoundIntro
    {
        public int RoundNo { get; init; }
        public GoonRoundKind Kind { get; init; }
        public int Difficulty { get; init; }
        public long FireAtMatchMs { get; init; }
    }

    // --------------------------------------------------------------- verdicts

    public enum GoonRoundVerdict { Win, Loss, Draw }

    public sealed class GoonRoundOutcomeEventArgs : EventArgs
    {
        public int RoundNo { get; init; }
        public GoonRoundKind Kind { get; init; }
        public int Difficulty { get; init; }
        public GoonRoundVerdict Verdict { get; init; }
        /// <summary>Running net score from the LOCAL player's point of view (+1 win, -1 loss).</summary>
        public int NetScore { get; init; }
        public RoundResultMsg Local { get; init; } = new();
        public RoundResultMsg Peer { get; init; } = new();
        /// <summary>Reaction below <see cref="GoonConsts.SuspectReactionMs"/> — scored, but badge it.</summary>
        public bool LocalSuspect => Local.Suspect;
        public bool PeerSuspect => Peer.Suspect;
    }

    public sealed class GoonSuddenDeathResultEventArgs : EventArgs
    {
        public bool LocalPlayerLost { get; init; }
        public int NetScore { get; init; }
        public int RoundsPlayed { get; init; }
    }

    // ----------------------------------------------------------- input feeds

    public sealed class GoonLockCardSolvedEventArgs : EventArgs
    {
        /// <summary>Mistakes made while typing; carried into the recap, not into scoring.</summary>
        public int Mistakes { get; init; }
    }

    /// <summary>
    /// Local lock-card completion. Phase E bridges <c>LockCardService.LockCardCompleted</c>
    /// (phrase-matched against the round's <see cref="GoonLockCardSpec.Phrase"/>) into
    /// <see cref="Solved"/>, and an Esc/dismiss into <see cref="Abandoned"/>.
    /// </summary>
    public interface IGoonLockCardFeed
    {
        event EventHandler<GoonLockCardSolvedEventArgs>? Solved;
        event EventHandler? Abandoned;
    }

    public sealed class GoonAttentionSampleEventArgs : EventArgs
    {
        /// <summary>Rolling attention, 0..1.</summary>
        public double Attention01 { get; init; }
        public bool LookingAway { get; init; }
    }

    /// <summary>
    /// Webcam-derived attention. Phase E bridges <c>App.Webcam.OnBlink</c> into
    /// <see cref="BlinkDetected"/> and the gaze/attention stream into
    /// <see cref="AttentionSample"/>. Nothing from the camera ever leaves the machine —
    /// only the derived round result does.
    /// </summary>
    public interface IGoonAttentionFeed
    {
        event EventHandler? BlinkDetected;
        event EventHandler<GoonAttentionSampleEventArgs>? AttentionSample;
    }

    /// <summary>
    /// Reaction-duel input. Phase E raises <see cref="InputPressed"/> on the first qualifying
    /// key/click, and (optionally but preferably) <see cref="StimulusRendered"/> on the frame
    /// the stimulus actually hit the screen — the round rebases its Stopwatch onto that instant
    /// so presentation latency does not count against the player.
    /// </summary>
    public interface IGoonReactionFeed
    {
        event EventHandler? InputPressed;
        event EventHandler? StimulusRendered;
    }

    public sealed class GoonBubblePoppedEventArgs : EventArgs
    {
        /// <summary>Index into <see cref="GoonBubbleRaceSpec.Bubbles"/>. Duplicates are ignored.</summary>
        public int Index { get; init; }
    }

    /// <summary>Bubble-race input. Phase E bridges the pop callback of the bubbles it spawned.</summary>
    public interface IGoonBubbleFeed
    {
        event EventHandler<GoonBubblePoppedEventArgs>? BubblePopped;
    }

    /// <summary>The four feeds a round may consume, supplied once by Phase E.</summary>
    public interface IGoonRoundInputs
    {
        IGoonLockCardFeed LockCard { get; }
        IGoonAttentionFeed Attention { get; }
        IGoonReactionFeed Reaction { get; }
        IGoonBubbleFeed Bubbles { get; }
    }

    // ------------------------------------------------------------- presenter

    /// <summary>
    /// Render side. Phase E implements this over the existing services/UI:
    /// lock card -> <c>App.LockCard.ShowLockCard(spec.Phrase, spec.Repeats, customStrict:false)</c>,
    /// staring contest -> flash barrage through <c>App.Flash</c> / the compositor,
    /// bubble race -> <c>App.Bubbles</c>, reaction duel -> the GG HUD overlay.
    /// Every method is invoked on the UI dispatcher by the round.
    /// </summary>
    public interface IGoonRoundPresenter
    {
        void ShowRoundIntro(GoonRoundIntro intro);

        void ShowLockCard(GoonLockCardSpec spec);
        void HideLockCard();

        void StartStaringContest(GoonStaringContestSpec spec);
        void EndStaringContest();

        void ArmReactionDuel(GoonReactionDuelSpec spec);
        void FireReactionStimulus(GoonStimulusKind kind);
        void EndReactionDuel();

        void StartBubbleRace(GoonBubbleRaceSpec spec);
        void EndBubbleRace();

        void ShowRoundVerdict(GoonRoundOutcomeEventArgs outcome);
    }

    /// <summary>Headless presenter — used by tests and as the default before Phase E wires the UI.</summary>
    public class GoonNullRoundPresenter : IGoonRoundPresenter
    {
        public virtual void ShowRoundIntro(GoonRoundIntro intro) { }
        public virtual void ShowLockCard(GoonLockCardSpec spec) { }
        public virtual void HideLockCard() { }
        public virtual void StartStaringContest(GoonStaringContestSpec spec) { }
        public virtual void EndStaringContest() { }
        public virtual void ArmReactionDuel(GoonReactionDuelSpec spec) { }
        public virtual void FireReactionStimulus(GoonStimulusKind kind) { }
        public virtual void EndReactionDuel() { }
        public virtual void StartBubbleRace(GoonBubbleRaceSpec spec) { }
        public virtual void EndBubbleRace() { }
        public virtual void ShowRoundVerdict(GoonRoundOutcomeEventArgs outcome) { }
    }

    // ------------------------------------------------------------ round core

    /// <summary>Everything a round needs. Constructed by the harness once the seed is agreed.</summary>
    public sealed class GoonRoundContext
    {
        public int RoundNo { get; init; }
        public int Difficulty { get; init; } = 1;
        public ulong Seed { get; init; }
        /// <summary>Deterministic PRNG seeded from <see cref="Seed"/> — identical on both machines.</summary>
        public IGoonRng Rng { get; init; } = null!;
        public IMatchClock Clock { get; init; } = null!;
        /// <summary>Shared-clock instant the round starts. Never "on message arrival".</summary>
        public long FireAtMatchMs { get; init; }
        public IGoonRoundPresenter Presenter { get; init; } = new GoonNullRoundPresenter();
        public IGoonRoundInputs Inputs { get; init; } = new GoonNullRoundInputs();
    }

    /// <summary>
    /// One synchronized round. Returns the LOCAL measurement only; the harness exchanges it
    /// and judges the pair. Implementations must be side-effect free apart from presenter calls.
    /// </summary>
    public interface IGoonRound
    {
        GoonRoundKind Kind { get; }
        Task<RoundResultMsg> RunAsync(GoonRoundContext ctx, CancellationToken ct);
    }

    /// <summary>Phase-D tuning. Kept out of GoonContracts because it is round-internal, not wire.</summary>
    public static class GoonRoundConsts
    {
        /// <summary>Countdown shown before a round fires, on top of GoonConsts.MinScheduleBufferMs.</summary>
        public const int CountdownMs = 3000;

        // Quick draw
        public const int QuickDrawTimeoutMs = 60000;
        public const int QuickDrawMaxRepeats = 3;

        // Staring contest
        public const int StaringBaseDurationMs = 20000;
        public const int StaringDurationStepMs = 5000;
        public const int StaringMaxDurationMs = 45000;
        public const int StaringBaseBeats = 12;
        public const int StaringBeatsStep = 6;
        public const int StaringMaxBeats = 48;

        // Reaction duel
        public const int ReactionMinDelayMs = 2000;
        public const int ReactionMaxDelayMs = 6000;
        public const int ReactionMaxResponseMs = 2000;
        public const int ReactionMaxDecoys = 3;

        // Bubble race
        public const int BubbleRaceTimeoutMs = 30000;
        public const int BubbleBaseCount = 6;
        public const int BubbleCountStep = 3;
        public const int BubbleMaxCount = 18;

        // Harness
        /// <summary>How long we wait for the peer's round result after posting our own.</summary>
        public const int PeerResultTimeoutMs = 25000;
        /// <summary>How long a guest waits for the host's round schedule.</summary>
        public const int ScheduleWaitTimeoutMs = 30000;
        /// <summary>Slack before FireAt by which the peer's seed contribution must have landed.</summary>
        public const int SeedDeadlineSlackMs = 300;
        /// <summary>Attempts to agree a schedule before the harness gives up on the round.</summary>
        public const int ScheduleAttempts = 2;
        /// <summary>Gap between rounds so verdict UI can breathe.</summary>
        public const int InterRoundGapMs = 2500;
    }

    // ------------------------------------------------------------- utilities

    /// <summary>Shared-clock waiting. Re-reads the clock every slice so a 30 s re-sync is honoured.</summary>
    internal static class GoonSchedule
    {
        private const int SliceMs = 100;

        public static async Task WaitUntilMatchMsAsync(IMatchClock clock, long targetMatchMs, CancellationToken ct)
        {
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var remaining = targetMatchMs - clock.NowMatchMs;
                if (remaining <= 0) return;
                var slice = (int)Math.Min(remaining, SliceMs);
                await Task.Delay(slice, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Awaits a one-shot signal with a timeout, without leaking the timer task's cancellation.
    /// Returns true if the signal won the race, false on timeout.
    /// </summary>
    internal static class GoonWait
    {
        public static async Task<bool> SignalAsync(Task signal, int timeoutMs, CancellationToken ct)
        {
            if (signal.IsCompleted) return true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delay = Task.Delay(timeoutMs, linked.Token);
            var winner = await Task.WhenAny(signal, delay).ConfigureAwait(false);
            if (!ReferenceEquals(winner, delay))
            {
                linked.Cancel();      // stop the timer
                try { await delay.ConfigureAwait(false); } catch (OperationCanceledException) { }
                return true;
            }
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    /// <summary>
    /// Presenter dispatch. Uses <see cref="ConditioningControlPanel.Helpers.DispatcherHelper"/> when a WPF
    /// application is running (so shutdown/null-dispatcher are handled the codebase way), and runs inline
    /// when there is none — that keeps rounds runnable headlessly in tests. Presenter exceptions are
    /// swallowed and logged: a broken render must never take the match down mid-round.
    /// </summary>
    internal static class GoonUi
    {
        public static void Invoke(Action action, string what)
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app?.Dispatcher == null)
                {
                    action();   // headless (unit tests / no WPF app)
                    return;
                }
                if (app.Dispatcher.HasShutdownStarted) return;
                ConditioningControlPanel.Helpers.DispatcherHelper.RunOnUISync(action);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("GoonSuddenDeath: presenter call {What} failed: {Error}", what, ex.Message);
            }
        }
    }

    /// <summary>Creates a TCS that never runs continuations inline on the raising thread.</summary>
    internal static class GoonTcs
    {
        public static TaskCompletionSource<T> Create<T>() =>
            new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // ------------------------------------------------------------ null feeds

    public sealed class GoonNullLockCardFeed : IGoonLockCardFeed
    {
#pragma warning disable CS0067 // never raised by design — this is the "no input wired yet" feed
        public event EventHandler<GoonLockCardSolvedEventArgs>? Solved;
        public event EventHandler? Abandoned;
#pragma warning restore CS0067
    }

    public sealed class GoonNullAttentionFeed : IGoonAttentionFeed
    {
#pragma warning disable CS0067
        public event EventHandler? BlinkDetected;
        public event EventHandler<GoonAttentionSampleEventArgs>? AttentionSample;
#pragma warning restore CS0067
    }

    public sealed class GoonNullReactionFeed : IGoonReactionFeed
    {
#pragma warning disable CS0067
        public event EventHandler? InputPressed;
        public event EventHandler? StimulusRendered;
#pragma warning restore CS0067
    }

    public sealed class GoonNullBubbleFeed : IGoonBubbleFeed
    {
#pragma warning disable CS0067
        public event EventHandler<GoonBubblePoppedEventArgs>? BubblePopped;
#pragma warning restore CS0067
    }

    public sealed class GoonNullRoundInputs : IGoonRoundInputs
    {
        public IGoonLockCardFeed LockCard { get; } = new GoonNullLockCardFeed();
        public IGoonAttentionFeed Attention { get; } = new GoonNullAttentionFeed();
        public IGoonReactionFeed Reaction { get; } = new GoonNullReactionFeed();
        public IGoonBubbleFeed Bubbles { get; } = new GoonNullBubbleFeed();
    }

    // ------------------------------------------------------------ fake feeds
    // Deterministic drivers for offline tests and the mock-transport play-test.
    // They are ordinary public types (no #if) so a future test project can use them.

    public sealed class GoonFakeLockCardFeed : IGoonLockCardFeed
    {
        public event EventHandler<GoonLockCardSolvedEventArgs>? Solved;
        public event EventHandler? Abandoned;
        public void RaiseSolved(int mistakes = 0) => Solved?.Invoke(this, new GoonLockCardSolvedEventArgs { Mistakes = mistakes });
        public void RaiseAbandoned() => Abandoned?.Invoke(this, EventArgs.Empty);
    }

    public sealed class GoonFakeAttentionFeed : IGoonAttentionFeed
    {
        public event EventHandler? BlinkDetected;
        public event EventHandler<GoonAttentionSampleEventArgs>? AttentionSample;
        public void RaiseBlink() => BlinkDetected?.Invoke(this, EventArgs.Empty);
        public void RaiseSample(double attention01, bool lookingAway = false) =>
            AttentionSample?.Invoke(this, new GoonAttentionSampleEventArgs { Attention01 = attention01, LookingAway = lookingAway });
    }

    public sealed class GoonFakeReactionFeed : IGoonReactionFeed
    {
        public event EventHandler? InputPressed;
        public event EventHandler? StimulusRendered;
        public void RaisePress() => InputPressed?.Invoke(this, EventArgs.Empty);
        public void RaiseRendered() => StimulusRendered?.Invoke(this, EventArgs.Empty);
    }

    public sealed class GoonFakeBubbleFeed : IGoonBubbleFeed
    {
        public event EventHandler<GoonBubblePoppedEventArgs>? BubblePopped;
        public void RaisePop(int index) => BubblePopped?.Invoke(this, new GoonBubblePoppedEventArgs { Index = index });
    }

    /// <summary>Bundle of the four fakes, for headless round tests.</summary>
    public sealed class GoonFakeRoundInputs : IGoonRoundInputs
    {
        public GoonFakeLockCardFeed FakeLockCard { get; } = new();
        public GoonFakeAttentionFeed FakeAttention { get; } = new();
        public GoonFakeReactionFeed FakeReaction { get; } = new();
        public GoonFakeBubbleFeed FakeBubbles { get; } = new();

        public IGoonLockCardFeed LockCard => FakeLockCard;
        public IGoonAttentionFeed Attention => FakeAttention;
        public IGoonReactionFeed Reaction => FakeReaction;
        public IGoonBubbleFeed Bubbles => FakeBubbles;
    }
}
