using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The fallback Goon Game transport: every message goes through the proxy's /v2/goon/relay
    /// mailbox instead of a peer-to-peer channel. Used when ICE never completes — symmetric NATs,
    /// corporate networks, anything the plan's no-TURN rule leaves unpunchable.
    ///
    /// This is a degraded path but NOT a broken one, and that is a design property rather than an
    /// accident: everything synchronized fires at a shared-clock instant at least
    /// <see cref="GoonConsts.MinScheduleBufferMs"/> in the future, and every reaction measurement is
    /// a LOCAL stopwatch delta that never touches the network. A second of relay latency shifts
    /// nothing that decides the match.
    ///
    /// Request shape: one post-and-drain per cycle (outbound frames and the inbound cursor ride the
    /// same call). The client hints <see cref="RelayWaitMs"/> as a long-poll budget and then re-posts
    /// as soon as the answer lands, but never faster than <see cref="RelayMinGapMs"/>. So against a
    /// server that holds the request we get roughly one call per 8 s with near-live delivery, and
    /// against one that answers instantly we settle at exactly the plan's 2 s cadence. Backoff on
    /// 429 doubles up to a ceiling, same discipline as RemoteControlService's poll timer.
    /// </summary>
    public sealed class GoonRelayTransport : GoonTransportBase
    {
        /// <summary>Long-poll budget we ask the server for. Comfortably under a serverless timeout.</summary>
        private const int RelayWaitMs = 8000;
        /// <summary>Floor between two relay calls, so a non-holding server can't be hammered.</summary>
        private const int RelayMinGapMs = 2000;
        private const double BackoffMaxSeconds = 60.0;
        /// <summary>Consecutive failures before the UI is told the connection is wobbly.</summary>
        private const int FailuresBeforeReconnecting = 3;
        /// <summary>Outbound frames held while the mailbox is unreachable.</summary>
        private const int OutboxMax = 256;

        private readonly GoonSignalingClient _signaling;
        private readonly bool _ownsSignaling;
        private readonly string _role;

        private readonly ConcurrentQueue<string> _outbox = new();
        private readonly SemaphoreSlim _wake = new(0, 1);

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private long _cursor;
        private double _backoffSeconds;
        private long _firstFailureLocalMs;
        private bool _connectedOnce;

        public GoonRelayTransport(bool isHost, GoonSignalingClient? signaling = null)
            : base(isHost, isHost ? "GoonRelay:host" : "GoonRelay:guest")
        {
            _ownsSignaling = signaling == null;
            _signaling = signaling ?? new GoonSignalingClient();
            _role = isHost ? "host" : "guest";
        }

        public string? Code { get; private set; }
        public string? Token { get; private set; }

        /// <summary>
        /// Take over a room that <see cref="GoonWebRtcTransport"/> already opened, after ICE gave up.
        /// No second /invite or /join call — the server room, the invite code the user already read
        /// out, and the weekly pass that was already burned all stay as they are.
        /// </summary>
        public void AdoptRoom(string code, string token)
        {
            Code = GoonSignalingClient.NormalizeCode(code);
            Token = token;
            _cts ??= new CancellationTokenSource();
            SetState(GoonTransportState.Signaling, "adopting room from failed P2P");
            StartLoop();
        }

        // ------------------------------------------------------------------ setup

        public override async Task<string?> CreateInviteAsync(CancellationToken ct)
        {
            if (!IsHost) throw new InvalidOperationException("CreateInviteAsync is the host path.");

            SetState(GoonTransportState.Signaling, "minting invite (relay)");
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var invite = await _signaling.CreateInviteAsync(
                App.Settings?.Current?.UserDisplayName ?? "", preferRelay: true, _cts.Token).ConfigureAwait(false);

            if (invite == null)
            {
                LastError = _signaling.LastError;
                SetState(GoonTransportState.Disconnected, LastError);
                return null;
            }

            Code = invite.Code;
            Token = invite.Token;
            StartLoop();
            return invite.Code;
        }

        public override async Task<bool> JoinAsync(string inviteCode, CancellationToken ct)
        {
            if (IsHost) throw new InvalidOperationException("JoinAsync is the guest path.");

            SetState(GoonTransportState.Signaling, "redeeming code (relay)");
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var join = await _signaling.JoinAsync(
                inviteCode, App.Settings?.Current?.UserDisplayName ?? "", _cts.Token).ConfigureAwait(false);

            if (join == null)
            {
                LastError = _signaling.LastError;
                SetState(GoonTransportState.Disconnected, LastError);
                return false;
            }

            Code = GoonSignalingClient.NormalizeCode(inviteCode);
            Token = join.Token;
            StartLoop();
            return true;
        }

        /// <summary>Resolves true once the mailbox has answered at least once.</summary>
        public async Task<bool> WaitForChannelAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                if (State == GoonTransportState.ConnectedRelay) return true;
                if (State == GoonTransportState.Disconnected && LastError != null) return false;
                if (State == GoonTransportState.Closed) return false;
                try { await Task.Delay(100, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
            }
            return State == GoonTransportState.ConnectedRelay;
        }

        // ------------------------------------------------------------------ loop

        private void StartLoop()
        {
            if (_loop != null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            _loop = Task.Run(() => LoopAsync(token), token);
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            var failures = 0;

            try
            {
                while (!ct.IsCancellationRequested && !IsDisposed)
                {
                    var started = Environment.TickCount64;

                    var outgoing = new List<string>();
                    while (_outbox.TryDequeue(out var frame)) outgoing.Add(frame);

                    var result = await _signaling.RelayAsync(
                        Code ?? "", Token ?? "", _role, _cursor, outgoing, RelayWaitMs, ct).ConfigureAwait(false);

                    if (result == null)
                    {
                        // Put the frames back at the head of the queue's semantics we can offer —
                        // order within this batch is preserved, and the batch as a whole retries.
                        foreach (var frame in outgoing) _outbox.Enqueue(frame);

                        failures++;
                        HandleFailure(failures);

                        var wait = ComputeBackoffMs();
                        try { await Task.Delay(wait, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { return; }
                        continue;
                    }

                    failures = 0;
                    _firstFailureLocalMs = 0;
                    _backoffSeconds = 0;
                    _cursor = result.Cursor;

                    if (!_connectedOnce)
                    {
                        _connectedOnce = true;
                        SetState(GoonTransportState.ConnectedRelay, "relay mailbox live");
                        BeginClockSync(ct);
                    }
                    else if (State == GoonTransportState.Reconnecting)
                    {
                        SetState(GoonTransportState.ConnectedRelay, "relay recovered");
                    }

                    foreach (var frame in result.Frames) HandleIncomingRaw(frame);

                    // Pace: never faster than the floor, but if the server held the request we have
                    // already spent that time and go straight back around.
                    var elapsed = Environment.TickCount64 - started;
                    var gap = (int)Math.Max(0, RelayMinGapMs - elapsed);
                    if (gap > 0)
                    {
                        // Woken early by a send, so an outbound payload doesn't sit in the outbox
                        // waiting out the cadence.
                        try { await _wake.WaitAsync(gap, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { return; }
                    }
                }
            }
            catch (OperationCanceledException) { /* closing */ }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[{Tag}] Relay loop died", Tag);
                LastError = "relay_error";
                SetState(GoonTransportState.Disconnected, "relay_error");
            }
        }

        private void HandleFailure(int consecutive)
        {
            var now = Environment.TickCount64;
            if (_firstFailureLocalMs == 0) _firstFailureLocalMs = now;

            if (_signaling.LastError == GoonSignalingClient.ErrorRateLimited)
            {
                _backoffSeconds = _backoffSeconds <= 0
                    ? RelayMinGapMs / 1000.0 * 2
                    : Math.Min(_backoffSeconds * 2, BackoffMaxSeconds);
                App.Logger?.Warning("[{Tag}] Relay rate limited (429) — backing off to {Seconds}s", Tag, _backoffSeconds);
            }

            if (_signaling.LastError == GoonSignalingClient.ErrorExpired ||
                _signaling.LastError == GoonSignalingClient.ErrorNotDeployed ||
                _signaling.LastError == GoonSignalingClient.ErrorUnauthorized)
            {
                LastError = _signaling.LastError;
                SetState(GoonTransportState.Disconnected, LastError);
                return;
            }

            if (consecutive >= FailuresBeforeReconnecting &&
                State == GoonTransportState.ConnectedRelay)
            {
                SetState(GoonTransportState.Reconnecting, _signaling.LastError ?? "relay unreachable");
            }

            // Past the tick-dead threshold this is an abandon, not a blip. The match engine decides
            // what that means; the plan is explicit that it is never auto-scored as a mercy.
            if (_connectedOnce && now - _firstFailureLocalMs > GoonConsts.TickDeadMs)
            {
                LastError = "relay_unreachable";
                SetState(GoonTransportState.Disconnected, "relay unreachable past abandon threshold");
            }
        }

        private int ComputeBackoffMs()
        {
            if (_backoffSeconds > 0) return (int)(_backoffSeconds * 1000);
            if (_signaling.RetryAfterSeconds is int s && s > 0) return Math.Min(s, 30) * 1000;
            return RelayMinGapMs;
        }

        // ------------------------------------------------------------------ send

        protected override Task SendRawAsync(string json, CancellationToken ct)
        {
            if (IsDisposed) return Task.CompletedTask;

            if (_outbox.Count >= OutboxMax)
            {
                App.Logger?.Warning("[{Tag}] Outbox full ({Max}) — dropping a frame", Tag, OutboxMax);
                return Task.CompletedTask;
            }

            _outbox.Enqueue(json);
            // Cut the cadence short so a payload or a mercy leaves now rather than in up to 2 s.
            try { _wake.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
            return Task.CompletedTask;
        }

        // ------------------------------------------------------------------ teardown

        public override Task CloseAsync()
        {
            if (State == GoonTransportState.Closed) return Task.CompletedTask;
            SetState(GoonTransportState.Closed, "closed by us");
            TearDown();
            return Task.CompletedTask;
        }

        private void TearDown()
        {
            try { _cts?.Cancel(); } catch { }
            MatchClock.Stop();
        }

        protected override void DisposeCore()
        {
            TearDown();
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            try { _wake.Dispose(); } catch { }
            if (_ownsSignaling) _signaling.Dispose();
        }
    }
}
