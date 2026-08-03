using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Sudden-death round used whenever either side is playing NoCam (a staring contest needs two
    /// cameras). After a seed-determined 2-6 s wait — sprinkled with seed-determined feints once the
    /// difficulty climbs — a stimulus fires and the lower reaction time wins.
    ///
    /// <para><b>Fairness.</b> The stimulus instant and the input instant are both stamped on a LOCAL
    /// <see cref="Stopwatch"/>; only the delta ms is exchanged, so network latency (P2P or relay)
    /// never enters the measurement. If Phase E raises <see cref="IGoonReactionFeed.StimulusRendered"/>
    /// the baseline is rebased onto the frame that actually hit the screen. Deltas below
    /// <see cref="GoonConsts.SuspectReactionMs"/> are scored but flagged
    /// <see cref="RoundResultMsg.Suspect"/>.</para>
    ///
    /// <para>Pressing before the real stimulus (including on a feint) is a false start and loses the
    /// round outright.</para>
    /// </summary>
    public sealed class ReactionDuelRound : IGoonRound
    {
        public GoonRoundKind Kind => GoonRoundKind.ReactionDuel;

        /// <summary>Progress value that marks a false start, for the recap badge.</summary>
        internal const int ProgressFalseStart = 1;

        /// <summary>
        /// Deterministic from the round seed: the real delay first, then one draw per feint. Feints
        /// only appear from difficulty 2 and are always spaced at least 400 ms before the real
        /// stimulus so they can never be mistaken for it by timing alone.
        /// </summary>
        internal static GoonReactionDuelSpec BuildSpec(GoonRoundContext ctx)
        {
            var delay = ctx.Rng.NextInt(GoonRoundConsts.ReactionMinDelayMs, GoonRoundConsts.ReactionMaxDelayMs + 1);

            var decoyCount = Math.Clamp(ctx.Difficulty - 1, 0, GoonRoundConsts.ReactionMaxDecoys);
            var decoys = new List<int>(decoyCount);
            var earliest = 400;
            var latest = delay - 400;
            for (int i = 0; i < decoyCount && earliest < latest; i++)
            {
                var offset = ctx.Rng.NextInt(earliest, latest);
                decoys.Add(offset);
                earliest = offset + 300;    // keep feints apart and ordered
            }

            return new GoonReactionDuelSpec
            {
                DelayMs = delay,
                MaxResponseMs = GoonRoundConsts.ReactionMaxResponseMs,
                Difficulty = ctx.Difficulty,
                DecoyOffsetsMs = decoys,
            };
        }

        public async Task<RoundResultMsg> RunAsync(GoonRoundContext ctx, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var spec = BuildSpec(ctx);
            var sw = new Stopwatch();
            var gate = new object();
            var falseStart = GoonTcs.Create<bool>();
            var pressed = GoonTcs.Create<int>();     // reaction ms, already baseline-corrected

            bool realFired = false;
            bool settled = false;
            long baselineMs = 0;                     // sw elapsed at the moment the stimulus was presented

            void OnPress(object? s, EventArgs e)
            {
                var raw = sw.ElapsedMilliseconds;
                lock (gate)
                {
                    if (settled) return;
                    settled = true;
                    if (!realFired)
                    {
                        falseStart.TrySetResult(true);
                        return;
                    }
                    var delta = (int)Math.Max(0, raw - baselineMs);
                    pressed.TrySetResult(delta);
                }
            }

            void OnRendered(object? s, EventArgs e)
            {
                var at = sw.ElapsedMilliseconds;
                lock (gate)
                {
                    // Only meaningful between the real stimulus firing and the first input.
                    if (settled || !realFired) return;
                    baselineMs = at;
                }
            }

            var feed = ctx.Inputs.Reaction;
            feed.InputPressed += OnPress;
            feed.StimulusRendered += OnRendered;

            try
            {
                GoonUi.Invoke(() => { ctx.Presenter.ArmReactionDuel(spec); sw.Restart(); }, "ArmReactionDuel");
                if (!sw.IsRunning) sw.Restart();

                // Feints. Any input during this stretch already settled as a false start.
                foreach (var decoyAt in spec.DecoyOffsetsMs)
                {
                    if (!await WaitElapsedAsync(sw, decoyAt, falseStart.Task, ct).ConfigureAwait(false))
                        return FalseStartResult(ctx, sw);
                    GoonUi.Invoke(() => ctx.Presenter.FireReactionStimulus(GoonStimulusKind.Decoy), "FireDecoy");
                }

                if (!await WaitElapsedAsync(sw, spec.DelayMs, falseStart.Task, ct).ConfigureAwait(false))
                    return FalseStartResult(ctx, sw);

                // The real one. Baseline is taken inside the same UI turn as the render call, then
                // refined by StimulusRendered if Phase E provides it.
                GoonUi.Invoke(() =>
                {
                    ctx.Presenter.FireReactionStimulus(GoonStimulusKind.Real);
                    lock (gate)
                    {
                        if (settled) return;
                        baselineMs = sw.ElapsedMilliseconds;
                        realFired = true;
                    }
                }, "FireStimulus");

                lock (gate)
                {
                    if (!realFired && !settled)
                    {
                        // Dispatcher unavailable (headless / shutting down): still run the round honestly.
                        baselineMs = sw.ElapsedMilliseconds;
                        realFired = true;
                    }
                }

                var got = await GoonWait.SignalAsync(pressed.Task, spec.MaxResponseMs, ct).ConfigureAwait(false);
                if (!got)
                {
                    lock (gate) { settled = true; }
                    App.Logger?.Information("GoonSuddenDeath: reaction duel round {Round} — no input within {Ms}ms", ctx.RoundNo, spec.MaxResponseMs);
                    return new RoundResultMsg
                    {
                        RoundNo = ctx.RoundNo,
                        Completed = false,
                        ElapsedMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds),
                        ReactionMs = null,
                    };
                }

                var reactionMs = pressed.Task.Result;
                var suspect = reactionMs < GoonConsts.SuspectReactionMs;
                if (suspect)
                    App.Logger?.Warning("GoonSuddenDeath: reaction {Ms}ms is below the suspect floor ({Floor}ms) — scoring it, badged", reactionMs, GoonConsts.SuspectReactionMs);

                return new RoundResultMsg
                {
                    RoundNo = ctx.RoundNo,
                    Completed = true,
                    ElapsedMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds),
                    ReactionMs = reactionMs,
                    Suspect = suspect,
                };
            }
            finally
            {
                feed.InputPressed -= OnPress;
                feed.StimulusRendered -= OnRendered;
                GoonUi.Invoke(() => ctx.Presenter.EndReactionDuel(), "EndReactionDuel");
            }
        }

        private static RoundResultMsg FalseStartResult(GoonRoundContext ctx, Stopwatch sw)
        {
            App.Logger?.Information("GoonSuddenDeath: reaction duel round {Round} lost to a false start at {Ms}ms", ctx.RoundNo, sw.ElapsedMilliseconds);
            return new RoundResultMsg
            {
                RoundNo = ctx.RoundNo,
                Completed = false,
                ElapsedMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds),
                ReactionMs = null,
                Progress = ProgressFalseStart,
            };
        }

        /// <summary>Waits until the round stopwatch passes <paramref name="targetMs"/>. Returns false if
        /// the abort signal (false start) fired first.</summary>
        private static async Task<bool> WaitElapsedAsync(Stopwatch sw, int targetMs, Task abort, CancellationToken ct)
        {
            while (true)
            {
                if (abort.IsCompleted) return false;
                var remaining = targetMs - (int)sw.ElapsedMilliseconds;
                if (remaining <= 0) return true;
                var slice = Math.Min(remaining, 50);
                await Task.Delay(slice, ct).ConfigureAwait(false);
            }
        }
    }
}
