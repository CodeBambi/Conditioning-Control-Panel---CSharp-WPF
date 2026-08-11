using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The shared match clock: an NTP-style offset between the two players' monotonic clocks, plus
    /// the fire-at-timestamp scheduler that everything synchronized in the Goon Game runs on.
    ///
    /// The rule the whole design rests on (plan, "Transport decisions"): nothing synchronized ever
    /// fires ON MESSAGE ARRIVAL. The sender picks an instant at least
    /// <see cref="GoonConsts.MinScheduleBufferMs"/> in the future, both clients schedule against
    /// their own converted local instant, and network latency stops mattering. That is what lets
    /// the relay fallback stay playable.
    ///
    /// Roles: the HOST's monotonic clock IS the match clock (its offset is pinned at 0); the GUEST
    /// measures its offset to the host. Both sides still run ping rounds — the host needs the RTT
    /// (for scheduler lead) and needs proof the peer answers before it lets anything be scheduled.
    ///
    /// Time base: an <see cref="Stopwatch"/> anchored to <see cref="Environment.TickCount64"/> at
    /// construction. Stopwatch alone isn't comparable to TickCount64 (which
    /// <see cref="IMatchClock.MatchMsToLocalMs"/> promises); TickCount64 alone has ~15.6 ms
    /// resolution, which is fine for scheduling and awful for measuring an RTT. The anchor gives
    /// both. Wall clock is never consulted — an NTP correction or a DST flip mid-match must not
    /// move a scheduled payload.
    /// </summary>
    public sealed class MatchClock : IMatchClock, IDisposable
    {
        // A ping burst that lands in a burst of network activity gives bad samples; spacing the
        // rounds out lets a transient queue drain between them.
        private const int PingSpacingMs = 120;
        private const int PingTimeoutMs = 3000;
        private const int MinUsableSamples = 3;
        // Resyncs whose RTT is wildly worse than the best we have ever seen are a congestion blip,
        // not a clock correction. Sampling them would drag the offset around for no reason.
        private const double ResyncRttRejectFactor = 4.0;

        private readonly bool _isClockMaster;
        private readonly string _tag;
        private readonly long _anchorTickCount;
        private readonly Stopwatch _sw;
        private readonly long _testSkewMs;

        private readonly object _gate = new();
        private readonly Dictionary<int, long> _pending = new();      // seq -> local send ms
        private readonly List<(double Rtt, double Offset)> _samples = new();

        private Func<GoonMessage, CancellationToken, Task>? _send;
        private CancellationTokenSource? _cts;
        private Timer? _resyncTimer;
        private int _seq;
        private long _offsetMs;          // add to local ms to get match ms
        private double _rttMs;
        private double _bestRttMs = double.MaxValue;
        private bool _synced;
        private bool _disposed;

        /// <param name="isClockMaster">True for the host. A master pins its offset at 0.</param>
        /// <param name="tag">Log prefix, e.g. "GoonP2P" / "GoonRelay" / "GoonLoop:guest".</param>
        /// <param name="testSkewMs">TEST ONLY, and zero in every production path. Adds a fake skew
        /// to this instance's local clock so the loopback pair can prove the sync actually
        /// converges instead of trivially agreeing. Note the one consequence: while it is non-zero,
        /// <see cref="MatchMsToLocalMs"/> is offset from <see cref="Environment.TickCount64"/> by
        /// exactly the skew. Compare against <see cref="NowLocalMs"/>, never against TickCount64
        /// directly, and the distinction never bites you.</param>
        public MatchClock(bool isClockMaster, string tag = "GoonClock", long testSkewMs = 0)
        {
            _isClockMaster = isClockMaster;
            _tag = tag;
            _testSkewMs = testSkewMs;
            _anchorTickCount = Environment.TickCount64;
            _sw = Stopwatch.StartNew();
        }

        // ------------------------------------------------------------------ IMatchClock

        public bool IsSynced { get { lock (_gate) return _synced; } }

        public long NowMatchMs { get { lock (_gate) return NowLocalMs + _offsetMs; } }

        public double RttMs { get { lock (_gate) return _rttMs; } }

        public long MatchMsToLocalMs(long matchMs) { lock (_gate) return matchMs - _offsetMs; }

        // ------------------------------------------------------------------ local time

        /// <summary>Monotonic local ms, directly comparable to <see cref="Environment.TickCount64"/>.</summary>
        public long NowLocalMs => _anchorTickCount + _sw.ElapsedMilliseconds + _testSkewMs;

        /// <summary>Offset currently applied to reach match time. 0 on the host, by definition.</summary>
        public long OffsetMs { get { lock (_gate) return _offsetMs; } }

        public bool IsClockMaster => _isClockMaster;

        /// <summary>Raised once, on the UI dispatcher, the first time the clock reaches sync.</summary>
        public event EventHandler? Synced;

        // ------------------------------------------------------------------ wiring

        /// <summary>
        /// Hands the clock its outbound path. The transport calls this as soon as the channel is
        /// usable and before <see cref="SyncAsync"/>.
        /// </summary>
        public void Attach(Func<GoonMessage, CancellationToken, Task> sendAsync)
        {
            _send = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        }

        /// <summary>
        /// Offer an inbound message to the clock. Returns true if the clock consumed it, in which
        /// case the transport must NOT surface it — clock traffic is private to this class and
        /// would only be noise in the match engine's MessageReceived.
        /// </summary>
        public bool TryHandleMessage(GoonMessage message)
        {
            switch (message)
            {
                case ClockPingMsg ping:
                    // Answer immediately and off the caller's thread; a queued pong is a lie about
                    // when we saw the ping and lands as a bogus offset on the far side.
                    _ = RespondAsync(ping);
                    return true;

                case ClockPongMsg pong:
                    HandlePong(pong);
                    return true;

                default:
                    return false;
            }
        }

        private async Task RespondAsync(ClockPingMsg ping)
        {
            var pong = new ClockPongMsg
            {
                Seq = ping.Seq,
                EchoSentLocalMs = ping.SentLocalMs,
                PongLocalMs = NowLocalMs,
            };
            try
            {
                var send = _send;
                if (send == null) return;
                await send(pong, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* channel going away */ }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[{Tag}] Failed to answer clock ping {Seq}", _tag, ping.Seq);
            }
        }

        private void HandlePong(ClockPongMsg pong)
        {
            long t0;
            lock (_gate)
            {
                if (!_pending.TryGetValue(pong.Seq, out t0)) return;   // late or duplicate
                _pending.Remove(pong.Seq);
            }

            var t3 = NowLocalMs;
            var rtt = t3 - t0;
            if (rtt < 0) return;

            // Classic NTP: assume the two legs are symmetric, so the peer's clock read t1 at our
            // local midpoint. offset = t1 - (t0 + t3) / 2.
            var offset = pong.PongLocalMs - (t0 + t3) / 2.0;
            lock (_gate) _samples.Add((rtt, offset));
        }

        // ------------------------------------------------------------------ sync

        /// <summary>
        /// Runs <see cref="GoonConsts.ClockPingRounds"/> ping/pong rounds, adopts the result, then
        /// arms the periodic re-sync (<see cref="GoonConsts.ClockResyncIntervalMs"/>). Safe to call
        /// once per channel open; a second call just re-syncs.
        /// </summary>
        public async Task<bool> SyncAsync(CancellationToken ct)
        {
            if (_disposed) return false;
            if (_send == null)
            {
                App.Logger?.Warning("[{Tag}] SyncAsync with no transport attached", _tag);
                return false;
            }

            _cts ??= new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);

            var ok = await RunRoundsAsync(GoonConsts.ClockPingRounds, linked.Token).ConfigureAwait(false);
            if (ok) ArmResync();
            return ok;
        }

        private async Task<bool> RunRoundsAsync(int rounds, CancellationToken ct)
        {
            lock (_gate) { _samples.Clear(); _pending.Clear(); }

            for (int i = 0; i < rounds && !ct.IsCancellationRequested; i++)
            {
                int seq;
                var ping = new ClockPingMsg();
                lock (_gate)
                {
                    seq = ++_seq;
                    ping.Seq = seq;
                    ping.SentLocalMs = NowLocalMs;
                    _pending[seq] = ping.SentLocalMs;
                }

                try
                {
                    var send = _send;
                    if (send == null) return false;
                    await send(ping, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "[{Tag}] Clock ping {Seq} failed to send", _tag, seq);
                    lock (_gate) _pending.Remove(seq);
                    continue;
                }

                try { await Task.Delay(PingSpacingMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
            }

            // Give the last few pongs a chance to land before we score the burst.
            var deadline = NowLocalMs + PingTimeoutMs;
            while (NowLocalMs < deadline && !ct.IsCancellationRequested)
            {
                lock (_gate) { if (_pending.Count == 0) break; }
                try { await Task.Delay(50, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
            }

            return Adopt();
        }

        /// <summary>Turns the collected samples into an offset + RTT. Returns false if too few landed.</summary>
        private bool Adopt()
        {
            bool firstSync;
            long newOffset;
            double medianRtt;

            lock (_gate)
            {
                var stale = _pending.Count;
                _pending.Clear();

                if (_samples.Count < MinUsableSamples)
                {
                    App.Logger?.Warning("[{Tag}] Clock sync got only {N} usable samples ({Stale} unanswered) — not adopting",
                        _tag, _samples.Count, stale);
                    return false;
                }

                var byRtt = _samples.OrderBy(s => s.Rtt).ToList();
                medianRtt = byRtt[byRtt.Count / 2].Rtt;

                // Score only the better half of the burst: a sample whose RTT was inflated by a
                // queue on one leg has an asymmetric — i.e. wrong — offset. Median of what's left
                // discards the rest.
                var keep = Math.Max(MinUsableSamples, byRtt.Count / 2);
                var best = byRtt.Take(keep).Select(s => s.Offset).OrderBy(o => o).ToList();
                var medianOffset = best[best.Count / 2];

                // A resync that arrived through a congested channel gets ignored rather than
                // dragging a good offset around.
                if (_synced && byRtt[0].Rtt > _bestRttMs * ResyncRttRejectFactor && _bestRttMs < double.MaxValue)
                {
                    App.Logger?.Information("[{Tag}] Rejecting resync — best RTT {Rtt:F0}ms vs baseline {Base:F0}ms",
                        _tag, byRtt[0].Rtt, _bestRttMs);
                    _samples.Clear();
                    return false;
                }

                _bestRttMs = Math.Min(_bestRttMs, byRtt[0].Rtt);
                _rttMs = medianRtt;

                // The host IS the match clock. It runs the rounds purely for the RTT and for the
                // liveness proof; adopting a non-zero offset would make it drift away from itself.
                newOffset = _isClockMaster ? 0 : (long)Math.Round(medianOffset);
                var delta = newOffset - _offsetMs;
                _offsetMs = newOffset;

                firstSync = !_synced;
                _synced = true;
                _samples.Clear();

                if (!firstSync && Math.Abs(delta) > 50)
                {
                    // Not an error: already-scheduled fires re-read the clock as they wait, so a
                    // correction lands on them too. Logged because a big jump is worth seeing when
                    // someone reports a desynced round.
                    App.Logger?.Information("[{Tag}] Clock corrected by {Delta}ms (offset {Offset}ms, rtt {Rtt:F0}ms)",
                        _tag, delta, newOffset, medianRtt);
                }
            }

            if (firstSync)
            {
                App.Logger?.Information("[{Tag}] Clock synced: offset={Offset}ms rtt={Rtt:F0}ms master={Master}",
                    _tag, newOffset, medianRtt, _isClockMaster);
                GoonDispatch.Post(() => Synced?.Invoke(this, EventArgs.Empty));
            }
            return true;
        }

        private void ArmResync()
        {
            if (_disposed) return;
            _resyncTimer ??= new Timer(async _ =>
            {
                // Timer callbacks are fire-and-forget by nature — everything inside is caught.
                try
                {
                    if (_disposed) return;
                    var token = _cts?.Token ?? CancellationToken.None;
                    if (token.IsCancellationRequested) return;
                    await RunRoundsAsync(GoonConsts.ClockPingRounds, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "[{Tag}] Clock resync failed", _tag);
                }
            }, null, GoonConsts.ClockResyncIntervalMs, GoonConsts.ClockResyncIntervalMs);
        }

        // ------------------------------------------------------------------ scheduling

        /// <summary>
        /// A shared-clock instant that is safely schedulable right now: the minimum buffer, plus a
        /// full RTT (so the peer still has a buffer's worth of lead after the message flies), plus
        /// whatever extra the caller wants. Phases B and D should build every fire_at_match_ms
        /// through this rather than hand-rolling "now + 1000".
        /// </summary>
        public long SafeFireAt(int extraMs = 0)
        {
            var rtt = (int)Math.Min(RttMs, 5000);
            return NowMatchMs + GoonConsts.MinScheduleBufferMs + rtt + Math.Max(0, extraMs);
        }

        /// <summary>True if <paramref name="fireAtMatchMs"/> still has the minimum buffer left.</summary>
        public bool IsFireable(long fireAtMatchMs) =>
            fireAtMatchMs - NowMatchMs >= GoonConsts.MinScheduleBufferMs;

        /// <summary>
        /// Runs <paramref name="action"/> on the WPF dispatcher at a shared-clock instant.
        ///
        /// Returns null — and does nothing — if the instant is already inside
        /// <see cref="GoonConsts.MinScheduleBufferMs"/>, or if the clock isn't synced. Rejecting is
        /// deliberate: silently firing late would make one player's round start after the other's,
        /// which is exactly the unfairness the fire-at-timestamp rule exists to prevent. Callers
        /// treat null as "drop this event and log it", never as "fire it now".
        ///
        /// Dispose the handle to cancel.
        /// </summary>
        public IDisposable? ScheduleAt(long fireAtMatchMs, Action action, string label = "")
        {
            if (action == null) return null;
            if (_disposed) return null;

            if (!IsSynced)
            {
                App.Logger?.Warning("[{Tag}] Refusing to schedule '{Label}' — clock not synced", _tag, label);
                return null;
            }

            var lead = fireAtMatchMs - NowMatchMs;
            if (lead < GoonConsts.MinScheduleBufferMs)
            {
                App.Logger?.Warning("[{Tag}] Refusing to schedule '{Label}' — only {Lead}ms lead (need {Min}ms)",
                    _tag, label, lead, GoonConsts.MinScheduleBufferMs);
                return null;
            }

            var item = new ScheduledFire(this, fireAtMatchMs, action, label);
            item.Start();
            return item;
        }

        /// <summary>
        /// One pending fire-at-timestamp. Re-reads the clock as it waits, so a mid-flight re-sync
        /// correction moves the fire with it instead of leaving it stranded at the old instant.
        /// </summary>
        private sealed class ScheduledFire : IDisposable
        {
            private readonly MatchClock _clock;
            private readonly long _fireAtMatchMs;
            private readonly Action _action;
            private readonly string _label;
            private readonly CancellationTokenSource _cts = new();

            public ScheduledFire(MatchClock clock, long fireAtMatchMs, Action action, string label)
            {
                _clock = clock;
                _fireAtMatchMs = fireAtMatchMs;
                _action = action;
                _label = label;
            }

            public void Start() => _ = RunAsync();

            private async Task RunAsync()
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        var remaining = _fireAtMatchMs - _clock.NowMatchMs;
                        if (remaining <= 0) break;

                        // Sleep in slices: near the target we want short hops so a clock correction
                        // is picked up promptly; far out, long hops keep the timer cheap. The last
                        // ~20 ms is left to the dispatcher hand-off, which is the dominant jitter.
                        var slice = remaining > 400 ? Math.Min(remaining - 200, 1000) : remaining;
                        await Task.Delay((int)Math.Max(1, slice), _cts.Token).ConfigureAwait(false);
                    }

                    if (_cts.IsCancellationRequested) return;

                    GoonDispatch.Post(() =>
                    {
                        var slip = _clock.NowMatchMs - _fireAtMatchMs;
                        if (slip > 250)
                        {
                            App.Logger?.Information("[{Tag}] Scheduled '{Label}' fired {Slip}ms late",
                                _clock._tag, _label, slip);
                        }
                        _action();
                    });
                }
                catch (OperationCanceledException) { /* disposed */ }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[{Tag}] Scheduled fire '{Label}' failed", _clock._tag, _label);
                }
            }

            public void Dispose()
            {
                try { _cts.Cancel(); _cts.Dispose(); } catch { /* already gone */ }
            }
        }

        // ------------------------------------------------------------------ lifetime

        /// <summary>Stops pings and re-syncs. The clock keeps reading time (the recap still wants it).</summary>
        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _resyncTimer?.Dispose(); } catch { }
            _resyncTimer = null;
            lock (_gate) _pending.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            _send = null;
            _sw.Stop();
        }
    }
}
