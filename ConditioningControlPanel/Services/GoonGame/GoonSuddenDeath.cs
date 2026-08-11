using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// ============================================================================
// GOON GAME — Phase D. The synchronized sudden-death harness.
//
// When the endurance phase runs out with nobody having mercied, the match goes
// to escalating synchronized rounds:
//
//     QuickDrawLockCard -> StaringContest (cam+cam) | ReactionDuel (any NoCam)
//                       -> BubbleRace -> repeat at Difficulty + 1
//
// Per round the two clients agree a schedule (host proposes RoundNo/Kind/
// FireAtMatchMs; both contribute half the seed, XOR = the round seed, so
// neither side can pick the round they are about to be graded on), fire at the
// shared-clock instant, measure LOCALLY, exchange RoundResultMsg, and judge the
// pair with the same pure function on both machines. Net -NetLossThreshold
// ends sudden death.
//
// Nothing here fires on message arrival: every start is a shared-clock
// timestamp >= now + GoonConsts.MinScheduleBufferMs (plan §Transport).
//
// Seam: implements Phase B's IGoonSuddenDeathRunner. GoonMatchService owns the
// message pump and forwards round traffic through HandleMessage; this class
// never subscribes to the transport itself.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Runs the sudden-death ladder. Phase E supplies the presenter (render side) and the input
    /// feeds; Phase B supplies the context (transport, clock, rng, modes) at StartAsync.
    /// </summary>
    public sealed class GoonSuddenDeathRunner : IGoonSuddenDeathRunner, IDisposable
    {
        private readonly PeerInbox<RoundScheduleMsg> _schedules = new();
        private readonly PeerInbox<RoundResultMsg> _results = new();
        private readonly Dictionary<int, ulong> _myContributions = new();

        private GoonSuddenDeathContext? _ctx;
        private CancellationTokenSource? _cts;
        private Task? _runTask;
        private bool _disposed;
        private string? _abortReason;

        public GoonSuddenDeathRunner(IGoonRoundPresenter? presenter = null, IGoonRoundInputs? inputs = null)
        {
            Presenter = presenter ?? new GoonNullRoundPresenter();
            Inputs = inputs ?? new GoonNullRoundInputs();
        }

        // ------------------------------------------------------------- wiring

        /// <summary>Render side (Phase E). Never null; defaults to the headless presenter.</summary>
        public IGoonRoundPresenter Presenter { get; set; }

        /// <summary>Input feeds (Phase E). Never null; defaults to feeds that never fire.</summary>
        public IGoonRoundInputs Inputs { get; set; }

        /// <summary>Countdown shown before each round, on top of GoonConsts.MinScheduleBufferMs.</summary>
        public int CountdownMs { get; set; } = GoonRoundConsts.CountdownMs;

        // ------------------------------------------------------------- state

        public bool IsRunning { get; private set; }

        /// <summary>Net rounds from the LOCAL player's point of view: +1 per win, -1 per loss.</summary>
        public int NetScore { get; private set; }

        /// <summary>Round currently being scheduled/played (1-based). 0 before the first round.</summary>
        public int CurrentRoundNo { get; private set; }

        // ------------------------------------------------------------ events

        /// <summary>Local player took the round. Arg = round number (Phase B seam).</summary>
        public event EventHandler<int>? RoundWon;
        /// <summary>Local player dropped the round. Arg = round number (Phase B seam).</summary>
        public event EventHandler<int>? RoundLost;
        /// <summary>Net-loss threshold hit. Arg: true = the LOCAL player lost (Phase B seam).</summary>
        public event EventHandler<bool>? NetLossReached;

        /// <summary>Every round outcome including draws, with both measurements — for the HUD and recap.</summary>
        public event EventHandler<GoonRoundOutcomeEventArgs>? RoundResolved;
        /// <summary>Mercy, disconnect, or a protocol stall. The match engine decides what that means.</summary>
        public event EventHandler<string>? Aborted;

        // ------------------------------------------------------------- entry

        public Task StartAsync(GoonSuddenDeathContext context, CancellationToken ct)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GoonSuddenDeathRunner));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (IsRunning) return _runTask ?? Task.CompletedTask;

            _ctx = context;
            _runTask = RunLadderAsync(context, ct);
            return _runTask;
        }

        private async Task RunLadderAsync(GoonSuddenDeathContext ctx, CancellationToken ct)
        {
            if (ctx.Transport == null) throw new InvalidOperationException("GoonSuddenDeath: no transport in context.");
            var clock = ctx.Clock ?? ctx.Transport.Clock;
            if (clock == null) throw new InvalidOperationException("GoonSuddenDeath: no match clock available.");

            var netLossThreshold = Math.Max(1, ctx.NetLossThreshold);

            IsRunning = true;
            NetScore = 0;
            CurrentRoundNo = 0;
            _abortReason = null;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            App.Logger?.Information("[GG] sudden death: starting ladder (host={Host}, modes {Local}/{Remote}, net loss {Net})",
                ctx.IsHost, ctx.LocalMode, ctx.RemoteMode, netLossThreshold);

            try
            {
                for (int roundNo = 1; ; roundNo++)
                {
                    token.ThrowIfCancellationRequested();
                    CurrentRoundNo = roundNo;

                    var kind = KindFor(roundNo, ctx.LocalMode, ctx.RemoteMode, ctx.AllowedRoundKinds);
                    var difficulty = DifficultyFor(roundNo);

                    var agreed = await AgreeScheduleAsync(ctx, clock, roundNo, kind, difficulty, token).ConfigureAwait(false);
                    if (agreed == null)
                    {
                        RaiseAborted("schedule_timeout");
                        return;
                    }

                    kind = agreed.Kind;
                    difficulty = agreed.Difficulty;

                    var intro = new GoonRoundIntro
                    {
                        RoundNo = roundNo,
                        Kind = kind,
                        Difficulty = difficulty,
                        FireAtMatchMs = agreed.FireAtMatchMs,
                    };
                    GoonUi.Invoke(() => Presenter.ShowRoundIntro(intro), "ShowRoundIntro");

                    var lateBy = clock.NowMatchMs - agreed.FireAtMatchMs;
                    if (lateBy > 0)
                        App.Logger?.Warning("[GG] sudden death: round {Round} schedule was already {Late}ms in the past — firing now", roundNo, lateBy);
                    else
                        await GoonSchedule.WaitUntilMatchMsAsync(clock, agreed.FireAtMatchMs, token).ConfigureAwait(false);

                    var local = await RunRoundAsync(ctx, kind, roundNo, difficulty, agreed.Seed, agreed.FireAtMatchMs, clock, token).ConfigureAwait(false);
                    local.RoundNo = roundNo;

                    await SendAsync(ctx, local, token).ConfigureAwait(false);

                    var raw = await _results.WaitAsync(roundNo, GoonRoundConsts.PeerResultTimeoutMs, token).ConfigureAwait(false);
                    if (raw == null)
                    {
                        App.Logger?.Warning("[GG] sudden death: no peer result for round {Round} within {Ms}ms", roundNo, GoonRoundConsts.PeerResultTimeoutMs);
                        RaiseAborted("peer_result_timeout");
                        return;
                    }
                    // Clean on INGEST, once, so everything downstream — judge, verdict UI, outcome
                    // event — sees the same sane numbers. `raw` is not used again past this line.
                    var peer = SanitizePeerResult(kind, raw);

                    var verdict = GoonRoundJudge.Decide(kind, local, peer);
                    NetScore += verdict switch
                    {
                        GoonRoundVerdict.Win => 1,
                        GoonRoundVerdict.Loss => -1,
                        _ => 0,
                    };

                    var outcome = new GoonRoundOutcomeEventArgs
                    {
                        RoundNo = roundNo,
                        Kind = kind,
                        Difficulty = difficulty,
                        Verdict = verdict,
                        NetScore = NetScore,
                        Local = local,
                        Peer = peer,
                    };

                    App.Logger?.Information(
                        "[GG] sudden death: round {Round} ({Kind}, difficulty {Diff}) -> {Verdict}, net {Net}",
                        roundNo, kind, difficulty, verdict, NetScore);

                    GoonUi.Invoke(() => Presenter.ShowRoundVerdict(outcome), "ShowRoundVerdict");
                    RaiseOutcome(outcome);

                    if (NetScore <= -netLossThreshold || NetScore >= netLossThreshold)
                    {
                        var localLost = NetScore <= -netLossThreshold;
                        App.Logger?.Information("[GG] sudden death: net loss reached after {Rounds} rounds (local lost = {Lost})", roundNo, localLost);
                        GoonUi.Invoke(() => NetLossReached?.Invoke(this, localLost), "NetLossReached");
                        return;
                    }

                    await Task.Delay(GoonRoundConsts.InterRoundGapMs, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Mercy, match end, or dispose. Never a round result.
                RaiseAborted(_abortReason ?? "cancelled");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] sudden death: ladder failed");
                RaiseAborted("error: " + ex.Message);
            }
            finally
            {
                IsRunning = false;
                _schedules.Clear();
                _results.Clear();
                try { _cts?.Dispose(); } catch { /* already gone */ }
                _cts = null;
            }
        }

        /// <summary>
        /// Tear down immediately — mercy, abandon, or dispose. Idempotent. Mercy stays available at
        /// every point of a round: the loop is cancelled between awaits, the running round unwinds
        /// through its finally (presenter teardown, feed unsubscribe) and nothing further is raised.
        /// </summary>
        public async Task StopAsync()
        {
            _abortReason ??= "stopped";
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* already finished */ }

            var task = _runTask;
            if (task == null || task.IsCompleted) return;

            try
            {
                // Bounded: a wedged round must not hold up the recap.
                await Task.WhenAny(task, Task.Delay(2000)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("[GG] sudden death: StopAsync wait threw: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Round traffic forwarded by the match engine. Safe from any thread; unrelated types ignored.
        /// </summary>
        public void HandleMessage(GoonMessage message)
        {
            switch (message)
            {
                case RoundScheduleMsg s:
                    _schedules.Post(s.RoundNo, s);
                    break;
                case RoundResultMsg r:
                    _results.Post(r.RoundNo, r);
                    break;
                case MercyMsg:
                    // The match engine also handles this; aborting here just stops the ladder sooner.
                    App.Logger?.Information("[GG] sudden death: peer mercied mid-ladder — stopping rounds");
                    _abortReason = "peer_mercy";
                    try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
                    break;
            }
        }

        /// <summary>
        /// The peer's RoundResultMsg, made safe to judge (2026-08-06).
        /// <para>
        /// GoonRoundJudge.Decide compares THEIR numbers against OURS with &lt; and &gt; and nothing
        /// else — it has no opinion about sign or plausibility, and it must not grow one, because it
        /// has to stay a pure antisymmetric function of two results. So the untrusted half is cleaned
        /// up HERE, on ingest, before it is judged, shown or emitted: a peer reporting ReactionMs = -5
        /// would otherwise beat every human alive, and ElapsedMs = -1 wins a quick draw the same way.
        /// </para><para>
        /// Suspect is RECOMPUTED rather than read: it is self-reported, it gates nothing, and a cheat
        /// would simply send false — so the flag that reaches the recap is OUR verdict on THEIR
        /// claimed number, which is the only version of it that means anything.
        /// </para>
        /// MIRROR: sanitizePeerRoundResult() in Resources/web/goon/core/suddenDeath.js.
        /// </summary>
        internal static RoundResultMsg SanitizePeerResult(GoonRoundKind kind, RoundResultMsg peer)
        {
            if (peer == null) return peer!;

            // Per-kind range for Progress, which is a different quantity in every round (see the
            // RoundResultMsg doc comment): attention percent, bubbles popped, mistakes typed,
            // false-start flag. Anything unknown falls back to the non-negative-integer floor.
            int progressMax = kind switch
            {
                GoonRoundKind.StaringContest => 100,
                GoonRoundKind.BubbleRace => GoonRoundConsts.BubbleMaxCount,
                GoonRoundKind.ReactionDuel => 1,
                _ => int.MaxValue,
            };

            // ReactionDuelSpec.MaxResponseMs IS this constant, and the runner does not hold the spec
            // — the round owns it and returns only the measurement.
            int? reactionMs = peer.ReactionMs.HasValue
                ? Math.Clamp(peer.ReactionMs.Value, 0, GoonRoundConsts.ReactionMaxResponseMs)
                : null;

            return new RoundResultMsg
            {
                RoundNo = Math.Max(0, peer.RoundNo),
                Completed = peer.Completed,
                ElapsedMs = Math.Max(0, peer.ElapsedMs),
                ReactionMs = reactionMs,
                Suspect = reactionMs.HasValue && reactionMs.Value < GoonConsts.SuspectReactionMs,
                Progress = Math.Clamp(peer.Progress, 0, progressMax),
            };
        }

        // -------------------------------------------------------- scheduling

        private sealed class AgreedRound
        {
            public GoonRoundKind Kind { get; init; }
            public int Difficulty { get; init; }
            public long FireAtMatchMs { get; init; }
            public ulong Seed { get; init; }
        }

        /// <summary>
        /// Host proposes, guest echoes; the round seed is the XOR of the two contributions, so
        /// neither client can choose the content on its own. A contribution is minted once per round
        /// and reused across schedule retries, so bumping FireAt never changes the seed.
        /// </summary>
        private async Task<AgreedRound?> AgreeScheduleAsync(GoonSuddenDeathContext ctx, IMatchClock clock,
            int roundNo, GoonRoundKind kind, int difficulty, CancellationToken ct)
        {
            var mine = ContributionFor(roundNo);

            if (ctx.IsHost)
            {
                for (int attempt = 0; attempt < GoonRoundConsts.ScheduleAttempts; attempt++)
                {
                    // Fire-at is always in the future by at least the schedule buffer + countdown;
                    // a retry pushes it further out rather than reusing a deadline we already missed.
                    var fireAt = clock.NowMatchMs + GoonConsts.MinScheduleBufferMs + CountdownMs + attempt * 2000;

                    await SendAsync(ctx, new RoundScheduleMsg
                    {
                        RoundNo = roundNo,
                        Kind = kind,
                        FireAtMatchMs = fireAt,
                        SeedContribution = mine,
                        Difficulty = difficulty,
                    }, ct).ConfigureAwait(false);

                    var budget = (int)Math.Max(500, fireAt - GoonRoundConsts.SeedDeadlineSlackMs - clock.NowMatchMs);
                    var reply = await _schedules.WaitAsync(roundNo, budget, ct).ConfigureAwait(false);
                    if (reply != null)
                    {
                        return new AgreedRound
                        {
                            Kind = kind,
                            Difficulty = difficulty,
                            FireAtMatchMs = fireAt,
                            Seed = GoonRng.CombineSeeds(mine, reply.SeedContribution),
                        };
                    }

                    App.Logger?.Warning("[GG] sudden death: guest did not return a seed half for round {Round} (attempt {Attempt})", roundNo, attempt + 1);
                }
                return null;
            }

            // Guest: wait for the proposal, answer with our half, then keep listening for a
            // re-proposal (the host bumps FireAt if our reply was slow) right up to the fire instant.
            var proposal = await _schedules.WaitAsync(roundNo, GoonRoundConsts.ScheduleWaitTimeoutMs, ct).ConfigureAwait(false);
            if (proposal == null)
            {
                App.Logger?.Warning("[GG] sudden death: no round {Round} proposal from host within {Ms}ms", roundNo, GoonRoundConsts.ScheduleWaitTimeoutMs);
                return null;
            }

            var expected = KindFor(roundNo, ctx.LocalMode, ctx.RemoteMode, ctx.AllowedRoundKinds);
            if (proposal.Kind != expected)
            {
                // Follow the host so the ladder cannot deadlock, but leave a trail: the kind is a pure
                // function of round number + both attention modes + the caps intersection, so a
                // mismatch means the two sides disagree about the modes or about the intersection.
                // A kind outside the intersection lands here too — same warn-but-follow treatment.
                var outsideCaps = !IsAllowed(proposal.Kind, ctx.AllowedRoundKinds);
                App.Logger?.Warning("[GG] sudden death: host proposed {Actual} for round {Round}, expected {Expected}{Caps} — following the host",
                    proposal.Kind, roundNo, expected, outsideCaps ? " (outside the caps intersection)" : "");
            }

            var fireAtGuest = proposal.FireAtMatchMs;
            var seed = GoonRng.CombineSeeds(mine, proposal.SeedContribution);

            while (true)
            {
                await SendAsync(ctx, new RoundScheduleMsg
                {
                    RoundNo = roundNo,
                    Kind = proposal.Kind,
                    FireAtMatchMs = fireAtGuest,
                    SeedContribution = mine,
                    Difficulty = proposal.Difficulty,
                }, ct).ConfigureAwait(false);

                var untilFire = (int)(fireAtGuest - clock.NowMatchMs);
                if (untilFire <= 0) break;

                var update = await _schedules.WaitAsync(roundNo, untilFire, ct).ConfigureAwait(false);
                if (update == null) break;          // nothing new before the fire instant — go

                fireAtGuest = update.FireAtMatchMs; // host bumped the schedule; re-ack with the same seed half
            }

            return new AgreedRound
            {
                Kind = proposal.Kind,
                Difficulty = Math.Max(1, proposal.Difficulty),
                FireAtMatchMs = fireAtGuest,
                Seed = seed,
            };
        }

        private ulong ContributionFor(int roundNo)
        {
            if (_myContributions.TryGetValue(roundNo, out var existing)) return existing;
            var value = GoonRng.NewSeedContribution();
            _myContributions[roundNo] = value;
            return value;
        }

        // ------------------------------------------------------------ ladder

        /// <summary>
        /// Pure ladder shape before caps: quick draw, then the attention round (staring needs two
        /// cameras, otherwise the reaction duel), then the bubble race — and around again one
        /// difficulty step harder. Both clients compute this identically from the round number and
        /// the two modes.
        /// </summary>
        public static GoonRoundKind KindFor(int roundNo, GoonAttentionMode localMode, GoonAttentionMode remoteMode)
        {
            var step = (Math.Max(1, roundNo) - 1) % 3;
            return step switch
            {
                0 => GoonRoundKind.QuickDrawLockCard,
                1 => (localMode == GoonAttentionMode.Cam && remoteMode == GoonAttentionMode.Cam)
                        ? GoonRoundKind.StaringContest
                        : GoonRoundKind.ReactionDuel,
                _ => GoonRoundKind.BubbleRace,
            };
        }

        /// <summary>
        /// The ladder shape after the caps intersection is applied. The rung ORDER and the
        /// difficulty escalation never change — a rung whose kind both clients cannot run is
        /// substituted with the reaction duel, which every client must support
        /// (<see cref="GoonCapabilities.UniversalRound"/>). StaringContest therefore needs BOTH
        /// conditions: two cameras AND membership in <paramref name="allowed"/>.
        ///
        /// Pure and computed from data both sides hold identically (round number, both attention
        /// modes, the intersection), so host and guest agree without an extra message.
        /// </summary>
        public static GoonRoundKind KindFor(int roundNo, GoonAttentionMode localMode, GoonAttentionMode remoteMode,
            IReadOnlyList<GoonRoundKind>? allowed)
        {
            var kind = KindFor(roundNo, localMode, remoteMode);
            if (IsAllowed(kind, allowed)) return kind;
            return GoonCapabilities.UniversalRound;     // ReactionDuel — the universal fallback
        }

        /// <summary>A null/empty intersection is treated as "reaction duel only" (the caps floor).</summary>
        private static bool IsAllowed(GoonRoundKind kind, IReadOnlyList<GoonRoundKind>? allowed)
        {
            if (allowed == null || allowed.Count == 0) return kind == GoonCapabilities.UniversalRound;
            return allowed.Contains(kind);
        }

        /// <summary>Difficulty climbs by one every full pass through the three rungs.</summary>
        public static int DifficultyFor(int roundNo) => 1 + (Math.Max(1, roundNo) - 1) / 3;

        private static IGoonRound CreateRound(GoonRoundKind kind) => kind switch
        {
            GoonRoundKind.QuickDrawLockCard => new QuickDrawRound(),
            GoonRoundKind.StaringContest => new StaringContestRound(),
            GoonRoundKind.ReactionDuel => new ReactionDuelRound(),
            GoonRoundKind.BubbleRace => new BubbleRaceRound(),
            _ => new QuickDrawRound(),
        };

        private async Task<RoundResultMsg> RunRoundAsync(GoonSuddenDeathContext ctx, GoonRoundKind kind, int roundNo,
            int difficulty, ulong seed, long fireAtMatchMs, IMatchClock clock, CancellationToken ct)
        {
            var roundCtx = new GoonRoundContext
            {
                RoundNo = roundNo,
                Difficulty = difficulty,
                Seed = seed,
                Rng = CreateRng(ctx, seed),
                Clock = clock,
                FireAtMatchMs = fireAtMatchMs,
                Presenter = Presenter,
                Inputs = Inputs,
            };

            try
            {
                return await CreateRound(kind).RunAsync(roundCtx, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;      // mercy / match end — the loop's handler turns this into an abort
            }
            catch (Exception ex)
            {
                // A broken round is a round we did not complete, not a crashed match.
                App.Logger?.Error(ex, "[GG] sudden death: round {Round} ({Kind}) threw", roundNo, kind);
                return new RoundResultMsg { RoundNo = roundNo, Completed = false, ElapsedMs = 0 };
            }
        }

        /// <summary>Per-round PRNG. Uses Phase A's factory when supplied, else a GoonRng on the round seed.</summary>
        private static IGoonRng CreateRng(GoonSuddenDeathContext ctx, ulong seed)
        {
            var factory = ctx.RngFactory;
            return factory != null ? factory(seed) : new GoonRng(seed);
        }

        // ---------------------------------------------------------- plumbing

        private static async Task SendAsync(GoonSuddenDeathContext ctx, GoonMessage msg, CancellationToken ct)
        {
            try
            {
                await ctx.Transport.SendAsync(msg, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Losing a send is survivable: the schedule retry / result timeout paths cover it.
                App.Logger?.Warning("[GG] sudden death: send of {Type} failed: {Error}", msg.Type, ex.Message);
            }
        }

        private void RaiseOutcome(GoonRoundOutcomeEventArgs outcome)
        {
            GoonUi.Invoke(() =>
            {
                RoundResolved?.Invoke(this, outcome);
                switch (outcome.Verdict)
                {
                    case GoonRoundVerdict.Win: RoundWon?.Invoke(this, outcome.RoundNo); break;
                    case GoonRoundVerdict.Loss: RoundLost?.Invoke(this, outcome.RoundNo); break;
                }
            }, "RoundOutcome");
        }

        private void RaiseAborted(string reason)
        {
            App.Logger?.Information("[GG] sudden death: ladder aborted ({Reason})", reason);
            GoonUi.Invoke(() => Aborted?.Invoke(this, reason), "Aborted");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _abortReason ??= "disposed";
            try { _cts?.Cancel(); } catch { /* already gone */ }
            try { _cts?.Dispose(); } catch { /* already gone */ }
            _cts = null;
        }

        // --------------------------------------------------------- peer inbox

        /// <summary>
        /// Keyed one-shot mailbox. A message that arrives before anyone waits for it is held, so the
        /// protocol never depends on who reached the await first (relay mode reorders freely).
        /// </summary>
        private sealed class PeerInbox<T> where T : class
        {
            private readonly object _gate = new();
            private readonly Dictionary<int, Queue<T>> _pending = new();
            private readonly Dictionary<int, TaskCompletionSource<T>> _waiters = new();

            public void Post(int key, T item)
            {
                TaskCompletionSource<T>? waiter = null;
                lock (_gate)
                {
                    if (_waiters.TryGetValue(key, out var w))
                    {
                        _waiters.Remove(key);
                        waiter = w;
                    }
                    else
                    {
                        if (!_pending.TryGetValue(key, out var q))
                            _pending[key] = q = new Queue<T>();
                        q.Enqueue(item);
                    }
                }
                waiter?.TrySetResult(item);
            }

            public async Task<T?> WaitAsync(int key, int timeoutMs, CancellationToken ct)
            {
                TaskCompletionSource<T> tcs;
                lock (_gate)
                {
                    if (_pending.TryGetValue(key, out var q) && q.Count > 0)
                        return q.Dequeue();

                    if (!_waiters.TryGetValue(key, out var existing))
                        _waiters[key] = existing = GoonTcs.Create<T>();
                    tcs = existing;
                }

                var got = await GoonWait.SignalAsync(tcs.Task, timeoutMs, ct).ConfigureAwait(false);

                lock (_gate)
                {
                    if (!got)
                    {
                        // Re-check under the lock: a Post could have landed while we were timing out.
                        if (tcs.Task.IsCompletedSuccessfully) return tcs.Task.Result;
                        if (_waiters.TryGetValue(key, out var cur) && ReferenceEquals(cur, tcs))
                            _waiters.Remove(key);
                        return null;
                    }
                }

                return tcs.Task.Result;
            }

            public void Clear()
            {
                lock (_gate)
                {
                    _pending.Clear();
                    _waiters.Clear();
                }
            }
        }
    }

    /// <summary>
    /// Pure round judgement. Called with the same two results on both machines (arguments swapped),
    /// so it MUST be antisymmetric: Decide(k, a, b) == Win exactly when Decide(k, b, a) == Loss.
    /// <para>
    /// IT TRUSTS ITS ARGUMENTS. Peer results are cleaned by GoonSuddenDeathRunner.SanitizePeerResult
    /// on ingest; do not add clamping in here, or the two calls (one per machine, arguments swapped)
    /// stop being the same function.
    /// </para>
    /// </summary>
    public static class GoonRoundJudge
    {
        public static GoonRoundVerdict Decide(GoonRoundKind kind, RoundResultMsg mine, RoundResultMsg theirs)
        {
            if (mine == null || theirs == null) return GoonRoundVerdict.Draw;

            switch (kind)
            {
                case GoonRoundKind.QuickDrawLockCard:
                    // Solved beats unsolved; two solves go to the faster typist.
                    if (mine.Completed && theirs.Completed) return Lower(mine.ElapsedMs, theirs.ElapsedMs);
                    if (mine.Completed) return GoonRoundVerdict.Win;
                    if (theirs.Completed) return GoonRoundVerdict.Loss;
                    return GoonRoundVerdict.Draw;

                case GoonRoundKind.StaringContest:
                    // Completed = survived the barrage without blinking.
                    if (mine.Completed && theirs.Completed) return Higher(mine.Progress, theirs.Progress);  // avg attention %
                    if (mine.Completed) return GoonRoundVerdict.Win;
                    if (theirs.Completed) return GoonRoundVerdict.Loss;
                    return Higher(mine.ElapsedMs, theirs.ElapsedMs);   // whoever blinked later

                case GoonRoundKind.ReactionDuel:
                    // Completed = clean input inside the window; a false start never completes.
                    var mineOk = mine.Completed && mine.ReactionMs.HasValue;
                    var theirsOk = theirs.Completed && theirs.ReactionMs.HasValue;
                    if (mineOk && theirsOk) return Lower(mine.ReactionMs!.Value, theirs.ReactionMs!.Value);
                    if (mineOk) return GoonRoundVerdict.Win;
                    if (theirsOk) return GoonRoundVerdict.Loss;
                    return GoonRoundVerdict.Draw;

                case GoonRoundKind.BubbleRace:
                    // Cleared beats not-cleared; two clears go to the faster; otherwise most popped.
                    if (mine.Completed && theirs.Completed) return Lower(mine.ElapsedMs, theirs.ElapsedMs);
                    if (mine.Completed) return GoonRoundVerdict.Win;
                    if (theirs.Completed) return GoonRoundVerdict.Loss;
                    return Higher(mine.Progress, theirs.Progress);

                default:
                    return GoonRoundVerdict.Draw;
            }
        }

        private static GoonRoundVerdict Lower(int mine, int theirs) =>
            mine < theirs ? GoonRoundVerdict.Win : mine > theirs ? GoonRoundVerdict.Loss : GoonRoundVerdict.Draw;

        private static GoonRoundVerdict Higher(int mine, int theirs) =>
            mine > theirs ? GoonRoundVerdict.Win : mine < theirs ? GoonRoundVerdict.Loss : GoonRoundVerdict.Draw;
    }
}
