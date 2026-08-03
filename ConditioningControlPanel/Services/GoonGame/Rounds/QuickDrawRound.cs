using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Sudden-death round 1 of the ladder: both players get the IDENTICAL lock card at the same
    /// shared-clock instant and race to type it out. Fastest solve wins the round.
    ///
    /// <para><b>Why the phrase is generated here and not drawn from LockCardService.</b>
    /// The enabled phrase pool is per-mod and per-user (<c>AppSettings.LockCardPhrases</c> comes from
    /// the active mod), so a draw from the local pool would hand the two players different cards.
    /// The pool below is fixed in code, so index N is the same string on both machines. Everything
    /// else about the card is the real thing: Phase E renders it through
    /// <c>LockCardService.ShowLockCard(customPhrase, customRepeats, customStrict:false)</c>, which
    /// already accepts an externally supplied challenge.</para>
    ///
    /// <para><b>No lockdown.</b> <c>customStrict</c> stays false so Esc keeps working — Esc is the
    /// Mercy ladder for the whole match (plan §safety rails). GG never sets strict/lockdown.</para>
    /// </summary>
    public sealed class QuickDrawRound : IGoonRound
    {
        public GoonRoundKind Kind => GoonRoundKind.QuickDrawLockCard;

        /// <summary>
        /// Fixed, mod-independent phrase pool. ASCII only (these get typed), well under
        /// <see cref="GoonConsts.OpponentTextMaxChars"/>, and authored here rather than sourced from
        /// either player — no opponent-supplied text enters a sudden-death card.
        /// </summary>
        internal static readonly string[] PhrasePool =
        {
            "I hold the line",
            "Focus is a choice I keep making",
            "Steady hands steady breath",
            "I am still here and still counting",
            "One more breath one more second",
            "My edge is my own",
            "Slow down and stay sharp",
            "Nothing shakes me tonight",
            "I answer only to the timer",
            "Keep the rhythm keep the room",
            "Composure over everything",
            "I do not blink first",
            "Quiet mind quick hands",
            "Hold steady finish clean",
            "The clock moves I do not",
            "I outlast the noise",
        };

        /// <summary>
        /// Deterministic from the round seed: phrase index, then repeat count. Draw order is fixed,
        /// so both machines consume the RNG identically.
        /// </summary>
        internal static GoonLockCardSpec BuildSpec(GoonRoundContext ctx)
        {
            var idx = ctx.Rng.NextInt(0, PhrasePool.Length);
            var repeats = Math.Clamp(ctx.Difficulty, 1, GoonRoundConsts.QuickDrawMaxRepeats);
            return new GoonLockCardSpec
            {
                Phrase = PhrasePool[idx],
                Repeats = repeats,
                Strict = false,     // never: Esc must stay mapped to Mercy
                Voice = false,      // GG never forces voice; the receiver's own setting still applies
                TimeLimitMs = GoonRoundConsts.QuickDrawTimeoutMs,
            };
        }

        public async Task<RoundResultMsg> RunAsync(GoonRoundContext ctx, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var spec = BuildSpec(ctx);
            var solved = GoonTcs.Create<GoonLockCardSolvedEventArgs>();
            var abandoned = GoonTcs.Create<bool>();
            var sw = new Stopwatch();

            void OnSolved(object? s, GoonLockCardSolvedEventArgs e) => solved.TrySetResult(e);
            void OnAbandoned(object? s, EventArgs e) => abandoned.TrySetResult(true);

            var feed = ctx.Inputs.LockCard;
            feed.Solved += OnSolved;
            feed.Abandoned += OnAbandoned;

            try
            {
                // Render, then start the local Stopwatch in the same UI turn: elapsed time is measured
                // locally end-to-end and only the delta ever crosses the wire.
                GoonUi.Invoke(() => { ctx.Presenter.ShowLockCard(spec); sw.Restart(); }, "ShowLockCard");
                if (!sw.IsRunning) sw.Restart();   // headless / dispatcher unavailable

                var either = Task.WhenAny(solved.Task, abandoned.Task);
                var signalled = await GoonWait.SignalAsync(either, spec.TimeLimitMs, ct).ConfigureAwait(false);
                sw.Stop();

                if (!signalled)
                {
                    App.Logger?.Information("GoonSuddenDeath: quick-draw round {Round} timed out at {Ms}ms", ctx.RoundNo, spec.TimeLimitMs);
                    return new RoundResultMsg { RoundNo = ctx.RoundNo, Completed = false, ElapsedMs = spec.TimeLimitMs };
                }

                if (solved.Task.IsCompletedSuccessfully)
                {
                    var e = solved.Task.Result;
                    var ms = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds);
                    return new RoundResultMsg
                    {
                        RoundNo = ctx.RoundNo,
                        Completed = true,
                        ElapsedMs = ms,
                        Progress = e.Mistakes,
                    };
                }

                // Dismissed without solving — not a mercy, just a lost card.
                return new RoundResultMsg
                {
                    RoundNo = ctx.RoundNo,
                    Completed = false,
                    ElapsedMs = (int)Math.Min(spec.TimeLimitMs, sw.ElapsedMilliseconds),
                };
            }
            finally
            {
                feed.Solved -= OnSolved;
                feed.Abandoned -= OnAbandoned;
                GoonUi.Invoke(() => ctx.Presenter.HideLockCard(), "HideLockCard");
            }
        }
    }
}
