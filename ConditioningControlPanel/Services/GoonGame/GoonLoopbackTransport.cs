using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>Knobs for a <see cref="GoonLoopbackTransport"/> pair.</summary>
    public sealed class GoonLoopbackOptions
    {
        /// <summary>One-way delay applied to every frame, in ms. 25 ms is a good LAN/near-peer.</summary>
        public int LatencyMs { get; set; } = 25;

        /// <summary>Random extra delay in [0, JitterMs). Never reorders — see the class remarks.</summary>
        public int JitterMs { get; set; } = 10;

        /// <summary>
        /// Fake skew applied to the GUEST's local clock. Non-zero on purpose: with both sides on the
        /// same monotonic clock the offset would be 0 and the sync would "pass" without proving
        /// anything. A weird prime-ish number makes an unconverted timestamp obvious in a log.
        /// </summary>
        public long GuestClockSkewMs { get; set; } = 3517;

        /// <summary>Seed for the jitter generator, so a loopback run is reproducible.</summary>
        public ulong JitterSeed { get; set; } = 0xC0FFEEUL;

        /// <summary>Preset: a realistic peer-to-peer link.</summary>
        public static GoonLoopbackOptions P2P() => new();

        /// <summary>Preset: the relay path's feel — slow and lumpy, to shake out fire-at-timestamp bugs.</summary>
        public static GoonLoopbackOptions Relay() => new() { LatencyMs = 900, JitterMs = 600 };

        /// <summary>Preset: instant delivery. Only for tests that are asserting logic, not timing.</summary>
        public static GoonLoopbackOptions Instant() => new() { LatencyMs = 0, JitterMs = 0, GuestClockSkewMs = 0 };
    }

    /// <summary>
    /// Two connected <see cref="IGoonTransport"/>s and the clock sync that binds them.
    /// Dispose to tear both down.
    /// </summary>
    public sealed class GoonLoopbackPair : IDisposable
    {
        internal GoonLoopbackPair(GoonLoopbackTransport host, GoonLoopbackTransport guest)
        {
            Host = host;
            Guest = guest;
        }

        public GoonLoopbackTransport Host { get; }
        public GoonLoopbackTransport Guest { get; }

        /// <summary>
        /// Brings both sides up and runs both clock syncs concurrently. Await this before scheduling
        /// anything: <see cref="MatchClock.ScheduleAt"/> refuses to fire on an unsynced clock, which
        /// is the single most common cause of a Phase B/D test doing nothing at all.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            Host.MarkConnected();
            Guest.MarkConnected();

            var hostSync = Host.SyncClockAsync(ct);
            var guestSync = Guest.SyncClockAsync(ct);
            var results = await Task.WhenAll(hostSync, guestSync).ConfigureAwait(false);
            return results[0] && results[1];
        }

        /// <summary>Cuts delivery in both directions for a while — for exercising the wobbly/abandon UI.</summary>
        public void SimulateOutage(int ms)
        {
            Host.SimulateOutage(ms);
            Guest.SimulateOutage(ms);
        }

        public void Dispose()
        {
            try { Host.Dispose(); } catch { }
            try { Guest.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// An in-process <see cref="IGoonTransport"/> pair. This is the rig Phases B, D and E develop
    /// and play-test against: no server, no NAT, no second machine, but the same message types, the
    /// same clock behaviour and the same dispatcher marshalling as the real thing.
    ///
    /// It models the two properties of the real channel that actually change how calling code has to
    /// be written:
    ///  - <b>Latency</b>, so code that (wrongly) acts on arrival instead of on a shared timestamp
    ///    visibly desyncs here rather than in front of a player. Try
    ///    <see cref="GoonLoopbackOptions.Relay"/> — if the feature survives 900 ms it will survive
    ///    the relay fallback.
    ///  - <b>Clock skew</b>, so a raw local timestamp that should have been a match timestamp is off
    ///    by seconds and impossible to miss.
    ///
    /// Delivery is ORDERED even under jitter: each frame's delivery instant is clamped to be no
    /// earlier than the previous frame's. A naive per-message random delay would reorder, which the
    /// real ordered/reliable SCTP channel never does — a mock that is harsher than reality just
    /// teaches callers to defend against something that cannot happen.
    /// </summary>
    public sealed class GoonLoopbackTransport : GoonTransportBase
    {
        private readonly GoonLoopbackOptions _options;
        private readonly GoonRng _jitter;
        private readonly Channel<(long DeliverAtLocalMs, string Json)> _outbound;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _linkGate = new();

        private GoonLoopbackTransport? _peer;
        private long _lastDeliverAtLocalMs;
        private long _outageUntilLocalMs;

        private GoonLoopbackTransport(bool isHost, GoonLoopbackOptions options)
            : base(isHost, isHost ? "GoonLoop:host" : "GoonLoop:guest",
                   clockTestSkewMs: isHost ? 0 : options.GuestClockSkewMs)
        {
            _options = options;
            // Different sub-streams per direction so host and guest jitter independently.
            _jitter = GoonRng.Derive(options.JitterSeed, isHost ? "loop-host" : "loop-guest");
            _outbound = Channel.CreateUnbounded<(long, string)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
        }

        /// <summary>Builds two transports wired to each other. Call <see cref="GoonLoopbackPair.ConnectAsync"/> next.</summary>
        public static GoonLoopbackPair CreatePair(GoonLoopbackOptions? options = null)
        {
            var opts = options ?? new GoonLoopbackOptions();
            var host = new GoonLoopbackTransport(true, opts);
            var guest = new GoonLoopbackTransport(false, opts);
            host._peer = guest;
            guest._peer = host;
            host.StartPump();
            guest.StartPump();
            return new GoonLoopbackPair(host, guest);
        }

        // ------------------------------------------------------------------ IGoonTransport

        /// <summary>
        /// There is no server here, so this just reports the state change and hands back a fixed
        /// code — Phase B can drive the same host code path it uses for real.
        /// </summary>
        public override Task<string?> CreateInviteAsync(CancellationToken ct)
        {
            MarkConnected();
            return Task.FromResult<string?>("LOOPBK");
        }

        public override Task<bool> JoinAsync(string inviteCode, CancellationToken ct)
        {
            MarkConnected();
            return Task.FromResult(true);
        }

        public override Task CloseAsync()
        {
            if (State == GoonTransportState.Closed) return Task.CompletedTask;
            SetState(GoonTransportState.Closed, "loopback closed");
            try { _cts.Cancel(); } catch { }
            MatchClock.Stop();
            return Task.CompletedTask;
        }

        protected override Task SendRawAsync(string json, CancellationToken ct)
        {
            if (IsDisposed) return Task.CompletedTask;

            var peer = _peer;
            if (peer == null || peer.IsDisposed) return Task.CompletedTask;

            long deliverAt;
            lock (_linkGate)
            {
                var now = Environment.TickCount64;
                var jitter = _options.JitterMs > 0 ? _jitter.NextInt(0, _options.JitterMs) : 0;
                var earliest = now + _options.LatencyMs + jitter;

                // Ordered channel: never deliver before the frame that went out ahead of this one.
                if (earliest < _lastDeliverAtLocalMs) earliest = _lastDeliverAtLocalMs;
                if (earliest < _outageUntilLocalMs) earliest = _outageUntilLocalMs;

                _lastDeliverAtLocalMs = earliest;
                deliverAt = earliest;
            }

            _outbound.Writer.TryWrite((deliverAt, json));
            return Task.CompletedTask;
        }

        // ------------------------------------------------------------------ harness helpers

        /// <summary>Flips this side to connected without any signaling. Used by the pair factory.</summary>
        public void MarkConnected()
        {
            if (State == GoonTransportState.ConnectedP2P) return;
            // ConnectedP2P rather than a mode of its own: from the caller's point of view a loopback
            // link behaves exactly like a healthy direct channel, and B/D should not be writing
            // branches for a transport that never ships.
            SetState(GoonTransportState.ConnectedP2P, "loopback link up");
        }

        /// <summary>Runs this side's clock sync. The pair factory awaits both.</summary>
        public Task<bool> SyncClockAsync(CancellationToken ct = default) => MatchClock.SyncAsync(ct);

        /// <summary>
        /// Holds every frame from this side for <paramref name="ms"/>, then delivers the backlog in
        /// order. Models a Wi-Fi drop: nothing is lost, everything is late. Pair it with
        /// GoonConsts.TickStaleMs / TickDeadMs to exercise the wobbly and abandon paths.
        /// </summary>
        public void SimulateOutage(int ms)
        {
            lock (_linkGate) _outageUntilLocalMs = Environment.TickCount64 + Math.Max(0, ms);
            App.Logger?.Information("[{Tag}] Simulating a {Ms}ms outage", Tag, ms);
        }

        /// <summary>Frames written but not yet delivered. Handy in a test assertion.</summary>
        public int InFlightCount => _outbound.Reader.Count;

        // ------------------------------------------------------------------ pump

        private void StartPump()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var reader = _outbound.Reader;
                    while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                    {
                        while (reader.TryRead(out var item))
                        {
                            var wait = item.DeliverAtLocalMs - Environment.TickCount64;
                            if (wait > 0) await Task.Delay((int)wait, _cts.Token).ConfigureAwait(false);

                            var peer = _peer;
                            if (peer == null || peer.IsDisposed) continue;
                            peer.HandleIncomingRaw(item.Json);
                        }
                    }
                }
                catch (OperationCanceledException) { /* closed */ }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[{Tag}] Loopback pump died", Tag);
                }
            });
        }

        protected override void DisposeCore()
        {
            try { _cts.Cancel(); } catch { }
            try { _outbound.Writer.TryComplete(); } catch { }
            try { _cts.Dispose(); } catch { }
            _peer = null;
        }
    }
}
