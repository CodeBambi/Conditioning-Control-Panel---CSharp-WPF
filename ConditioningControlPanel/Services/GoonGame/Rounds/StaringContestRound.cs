using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Cam-vs-cam sudden-death round: both players endure the SAME flash barrage (schedule derived
    /// from the round seed) and the first one to blink after the round starts loses. If neither
    /// blinks for the whole duration the round is decided on average attention, and a tie there is
    /// a draw that escalates into the next, harder round.
    ///
    /// <para>Nothing from the camera crosses the wire: the blink stream is consumed locally through
    /// <see cref="IGoonAttentionFeed"/> and only the resulting <see cref="RoundResultMsg"/> is sent.
    /// This round never touches the webcam or GazeFocusService itself — Phase E bridges
    /// <c>App.Webcam.OnBlink</c> and the gaze stream into the feed.</para>
    /// </summary>
    public sealed class StaringContestRound : IGoonRound
    {
        public GoonRoundKind Kind => GoonRoundKind.StaringContest;

        /// <summary>
        /// Deterministic from the round seed. Draw order per beat is fixed
        /// (offset jitter, duration, intensity, x, y, scale), so both machines build the
        /// same barrage; only the beat COUNT and duration depend on difficulty.
        /// </summary>
        internal static GoonStaringContestSpec BuildSpec(GoonRoundContext ctx)
        {
            var durationMs = Math.Min(
                GoonRoundConsts.StaringMaxDurationMs,
                GoonRoundConsts.StaringBaseDurationMs + GoonRoundConsts.StaringDurationStepMs * (ctx.Difficulty - 1));

            var beatCount = Math.Min(
                GoonRoundConsts.StaringMaxBeats,
                GoonRoundConsts.StaringBaseBeats + GoonRoundConsts.StaringBeatsStep * (ctx.Difficulty - 1));

            var beats = new List<GoonFlashBeat>(beatCount);
            var spacing = durationMs / (double)Math.Max(1, beatCount);

            for (int i = 0; i < beatCount; i++)
            {
                // Jitter each beat inside its own slot so the barrage feels organic but stays
                // evenly distributed across the round (and can never bunch at the very end).
                var jitter = ctx.Rng.NextDouble() * spacing * 0.6;
                var offset = (int)Math.Round(i * spacing + jitter);
                var duration = 120 + ctx.Rng.NextInt(0, 180);
                var intensity = 0.45 + ctx.Rng.NextDouble() * 0.55;
                var nx = ctx.Rng.NextDouble();
                var ny = ctx.Rng.NextDouble();
                var scale = 0.6 + ctx.Rng.NextDouble() * 0.8;

                beats.Add(new GoonFlashBeat
                {
                    OffsetMs = Math.Min(offset, Math.Max(0, durationMs - duration)),
                    DurationMs = duration,
                    Intensity = intensity,
                    NormX = nx,
                    NormY = ny,
                    Scale = scale,
                });
            }

            return new GoonStaringContestSpec
            {
                DurationMs = durationMs,
                Difficulty = ctx.Difficulty,
                Beats = beats,
            };
        }

        public async Task<RoundResultMsg> RunAsync(GoonRoundContext ctx, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var spec = BuildSpec(ctx);
            var blinked = GoonTcs.Create<bool>();
            var sw = new Stopwatch();

            // Attention samples are averaged for the both-survived tiebreak. Ints only —
            // Progress is the wire field and carries 0..100.
            double attentionSum = 0;
            int attentionSamples = 0;
            var sampleLock = new object();

            void OnBlink(object? s, EventArgs e) => blinked.TrySetResult(true);
            void OnSample(object? s, GoonAttentionSampleEventArgs e)
            {
                lock (sampleLock)
                {
                    attentionSum += Math.Clamp(e.Attention01, 0.0, 1.0);
                    attentionSamples++;
                }
            }

            var feed = ctx.Inputs.Attention;
            feed.BlinkDetected += OnBlink;
            feed.AttentionSample += OnSample;

            try
            {
                GoonUi.Invoke(() => { ctx.Presenter.StartStaringContest(spec); sw.Restart(); }, "StartStaringContest");
                if (!sw.IsRunning) sw.Restart();

                var survived = !await GoonWait.SignalAsync(blinked.Task, spec.DurationMs, ct).ConfigureAwait(false);
                sw.Stop();

                int attentionPct;
                lock (sampleLock)
                {
                    attentionPct = attentionSamples > 0
                        ? (int)Math.Round(attentionSum / attentionSamples * 100.0)
                        : 0;
                }

                var elapsed = survived
                    ? spec.DurationMs
                    : (int)Math.Min(spec.DurationMs, sw.ElapsedMilliseconds);

                App.Logger?.Information(
                    "GoonSuddenDeath: staring contest round {Round} -> {Outcome} at {Ms}ms (attention {Pct}%)",
                    ctx.RoundNo, survived ? "survived" : "blinked", elapsed, attentionPct);

                return new RoundResultMsg
                {
                    RoundNo = ctx.RoundNo,
                    Completed = survived,       // survived the full barrage without blinking
                    ElapsedMs = elapsed,        // time to blink, or the full duration
                    Progress = Math.Clamp(attentionPct, 0, 100),
                };
            }
            finally
            {
                feed.BlinkDetected -= OnBlink;
                feed.AttentionSample -= OnSample;
                GoonUi.Invoke(() => ctx.Presenter.EndStaringContest(), "EndStaringContest");
            }
        }
    }
}
