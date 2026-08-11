using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Third rung of the sudden-death ladder: an identical bubble swarm on both machines (layout
    /// derived from the round seed). First to clear the swarm wins; if nobody clears it inside
    /// <see cref="GoonRoundConsts.BubbleRaceTimeoutMs"/> the higher pop count wins.
    ///
    /// <para>The round does NOT drive <c>BubbleService</c>. It emits a layout spec that Phase E
    /// spawns, and consumes pops through <see cref="IGoonBubbleFeed"/> — keeping the round
    /// headless and the swarm identical on both sides regardless of local bubble settings.</para>
    /// </summary>
    public sealed class BubbleRaceRound : IGoonRound
    {
        public GoonRoundKind Kind => GoonRoundKind.BubbleRace;

        /// <summary>
        /// Deterministic from the round seed. Per bubble the draw order is fixed
        /// (x, y, scale, spawn offset, drift angle, drift speed); only the COUNT depends on
        /// difficulty. Positions are screen-normalized so two different resolutions still get the
        /// same relative layout.
        /// </summary>
        internal static GoonBubbleRaceSpec BuildSpec(GoonRoundContext ctx)
        {
            var count = Math.Min(
                GoonRoundConsts.BubbleMaxCount,
                GoonRoundConsts.BubbleBaseCount + GoonRoundConsts.BubbleCountStep * (ctx.Difficulty - 1));

            var bubbles = new List<GoonBubbleSpawn>(count);
            for (int i = 0; i < count; i++)
            {
                // Keep spawns off the extreme screen edges so every bubble is reachable on any
                // aspect ratio (and can't hide under the HUD).
                var nx = 0.08 + ctx.Rng.NextDouble() * 0.84;
                var ny = 0.12 + ctx.Rng.NextDouble() * 0.76;
                var scale = 0.75 + ctx.Rng.NextDouble() * 0.6;
                var spawnOffset = ctx.Rng.NextInt(0, 1500 + 250 * ctx.Difficulty);
                var angle = ctx.Rng.NextDouble() * 360.0;
                var speed = 0.4 + ctx.Rng.NextDouble() * (0.4 + 0.2 * ctx.Difficulty);

                bubbles.Add(new GoonBubbleSpawn
                {
                    Index = i,
                    NormX = nx,
                    NormY = ny,
                    Scale = scale,
                    SpawnOffsetMs = spawnOffset,
                    DriftAngleDeg = angle,
                    DriftSpeed = speed,
                });
            }

            return new GoonBubbleRaceSpec
            {
                Count = count,
                TimeoutMs = GoonRoundConsts.BubbleRaceTimeoutMs,
                Difficulty = ctx.Difficulty,
                Bubbles = bubbles,
            };
        }

        public async Task<RoundResultMsg> RunAsync(GoonRoundContext ctx, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var spec = BuildSpec(ctx);
            var sw = new Stopwatch();
            var cleared = GoonTcs.Create<bool>();
            var popped = new HashSet<int>();
            var gate = new object();

            void OnPopped(object? s, GoonBubblePoppedEventArgs e)
            {
                lock (gate)
                {
                    // Ignore indices outside the spec and double-reports of the same bubble.
                    if (e.Index < 0 || e.Index >= spec.Count) return;
                    if (!popped.Add(e.Index)) return;
                    if (popped.Count >= spec.Count) cleared.TrySetResult(true);
                }
            }

            var feed = ctx.Inputs.Bubbles;
            feed.BubblePopped += OnPopped;

            try
            {
                GoonUi.Invoke(() => { ctx.Presenter.StartBubbleRace(spec); sw.Restart(); }, "StartBubbleRace");
                if (!sw.IsRunning) sw.Restart();

                var didClear = await GoonWait.SignalAsync(cleared.Task, spec.TimeoutMs, ct).ConfigureAwait(false);
                sw.Stop();

                int count;
                lock (gate) { count = popped.Count; }

                var elapsed = didClear
                    ? (int)Math.Min(spec.TimeoutMs, sw.ElapsedMilliseconds)
                    : spec.TimeoutMs;

                App.Logger?.Information(
                    "GoonSuddenDeath: bubble race round {Round} -> {Popped}/{Total} in {Ms}ms",
                    ctx.RoundNo, count, spec.Count, elapsed);

                return new RoundResultMsg
                {
                    RoundNo = ctx.RoundNo,
                    Completed = didClear,
                    ElapsedMs = elapsed,
                    Progress = count,
                };
            }
            finally
            {
                feed.BubblePopped -= OnPopped;
                GoonUi.Invoke(() => ctx.Presenter.EndBubbleRace(), "EndBubbleRace");
            }
        }
    }
}
