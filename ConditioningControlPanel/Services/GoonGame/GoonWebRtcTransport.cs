using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The primary Goon Game transport: a WebRTC data channel between the two players, brokered by
    /// the /v2/goon/signal mailbox and then entirely peer-to-peer.
    ///
    /// Shape (plan, "Architecture"):
    ///   host  CreateInviteAsync -> /v2/goon/invite mints a code -> short-poll /v2/goon/signal
    ///         until the guest joins -> offer/answer/ICE through the mailbox -> DTLS -> channel.
    ///   guest JoinAsync(code)   -> /v2/goon/join -> same mailbox from the other side.
    /// Once the channel opens the mailbox is dropped and nothing but the two clients is involved.
    ///
    /// DATA CHANNEL ONLY. No RTP tracks are ever added, so no codec is ever negotiated and there is
    /// no path — deliberate or accidental — for mic or camera to reach the peer. The webcam's
    /// derived attention percentage travels as an integer in StateTickMsg, and that is all.
    ///
    /// Channel config: ordered + reliable (no maxRetransmits, no maxPacketLifeTime). Match traffic
    /// is small and every message matters; SCTP's ordering saves the match engine from having to
    /// reason about a payload arriving before the tick that funded it.
    ///
    /// ICE: public STUN only (Google + Cloudflare). No TURN by design — running relay infrastructure
    /// for media is exactly what the plan refuses. A NAT pair that can't be punched gives up after
    /// <see cref="GoonConsts.IceTimeoutMs"/>, sets <see cref="IceFailed"/>, and the caller opens a
    /// <see cref="GoonRelayTransport"/> on the SAME room (see <see cref="Code"/>/<see cref="Token"/>).
    /// </summary>
    public sealed class GoonWebRtcTransport : GoonTransportBase
    {
        /// <summary>
        /// Public STUN. Two providers because one of them will be blocked on someone's network, and
        /// the cost of a second server is one extra binding request.
        /// </summary>
        private static readonly string[] StunUrls =
        {
            "stun:stun.l.google.com:19302",
            "stun:stun1.l.google.com:19302",
            "stun:stun.cloudflare.com:3478",
        };

        private const string ChannelLabel = "goon";
        private const int SignalPollMs = 2000;          // setup only; stops when the channel opens
        private const int SignalFailureLimit = 5;
        // Bounded so a peer that never comes back can't grow this without limit. 128 frames is far
        // more than a 5 s reconnect grace can produce at the 3 s tick cadence.
        private const int ResumeBufferMax = 128;

        private readonly GoonSignalingClient _signaling;
        private readonly bool _ownsSignaling;
        private readonly string _role;

        private readonly ConcurrentQueue<GoonSignalItem> _outSignals = new();
        private readonly SemaphoreSlim _signalWake = new(0, 1);
        private readonly List<RTCIceCandidateInit> _pendingRemoteCandidates = new();
        private readonly ConcurrentQueue<string> _resumeBuffer = new();
        private readonly object _peerGate = new();

        private CancellationTokenSource? _cts;
        private Task? _signalPump;
        private RTCPeerConnection? _pc;
        private RTCDataChannel? _dc;
        private long _signalCursor;
        private bool _remoteDescriptionSet;
        private bool _negotiationStarted;
        private bool _everConnected;
        private int _channelOpenSignalled;   // 0/1, Interlocked — see HandleChannelOpen
        private CancellationTokenSource? _reconnectGrace;

        /// <param name="isHost">Host mints the invite and owns the match clock.</param>
        /// <param name="signaling">Shared client; pass one in so a fallback relay reuses the room.</param>
        public GoonWebRtcTransport(bool isHost, GoonSignalingClient? signaling = null)
            : base(isHost, isHost ? "GoonP2P:host" : "GoonP2P:guest")
        {
            _ownsSignaling = signaling == null;
            _signaling = signaling ?? new GoonSignalingClient();
            _role = isHost ? "host" : "guest";
        }

        /// <summary>Invite code for this room. Survives an ICE failure so the relay can adopt it.</summary>
        public string? Code { get; private set; }

        /// <summary>Room token for this side. Same lifetime note as <see cref="Code"/>.</summary>
        public string? Token { get; private set; }

        /// <summary>Set when ICE gave up. The caller's cue to open a relay transport on this room.</summary>
        public bool IceFailed { get; private set; }

        /// <summary>Display name the server reported for the opponent (guest side, from /join).</summary>
        public string? PeerDisplayName { get; private set; }

        // ------------------------------------------------------------------ setup

        public override async Task<string?> CreateInviteAsync(CancellationToken ct)
        {
            if (!IsHost) throw new InvalidOperationException("CreateInviteAsync is the host path.");

            SetState(GoonTransportState.Signaling, "minting invite");
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var invite = await _signaling.CreateInviteAsync(
                App.Settings?.Current?.UserDisplayName ?? "", preferRelay: false, _cts.Token).ConfigureAwait(false);

            if (invite == null)
            {
                LastError = _signaling.LastError;
                SetState(GoonTransportState.Disconnected, LastError);
                return null;
            }

            Code = invite.Code;
            Token = invite.Token;
            StartSignalPump();
            return invite.Code;
        }

        public override async Task<bool> JoinAsync(string inviteCode, CancellationToken ct)
        {
            if (IsHost) throw new InvalidOperationException("JoinAsync is the guest path.");

            SetState(GoonTransportState.Signaling, "redeeming code");
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
            PeerDisplayName = join.PeerDisplayName;
            StartSignalPump();
            return true;
        }

        /// <summary>
        /// Resolves when the data channel is open, or when ICE gives up
        /// (<see cref="GoonConsts.IceTimeoutMs"/> after negotiation starts). False means "fall back
        /// to relay" — check <see cref="IceFailed"/> to tell that apart from a signaling failure.
        /// </summary>
        public async Task<bool> WaitForChannelAsync(CancellationToken ct)
        {
            var deadline = Environment.TickCount64 + GoonConsts.IceTimeoutMs;
            var negotiationSeen = false;

            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                if (State == GoonTransportState.ConnectedP2P) return true;
                if (State == GoonTransportState.Disconnected && LastError != null) return false;
                if (State == GoonTransportState.Closed) return false;

                // The ICE budget starts when there is actually an ICE process to time out — waiting
                // for a human to type the code must not eat into it.
                if (!negotiationSeen && _negotiationStarted)
                {
                    negotiationSeen = true;
                    deadline = Environment.TickCount64 + GoonConsts.IceTimeoutMs;
                }

                if (negotiationSeen && Environment.TickCount64 > deadline)
                {
                    IceFailed = true;
                    LastError = "ice_timeout";
                    App.Logger?.Warning("[{Tag}] ICE did not complete in {Ms}ms — relay fallback", Tag, GoonConsts.IceTimeoutMs);
                    SetState(GoonTransportState.Disconnected, "ice_timeout");
                    return false;
                }

                try { await Task.Delay(100, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
            }

            return State == GoonTransportState.ConnectedP2P;
        }

        // ------------------------------------------------------------------ signaling pump

        private void StartSignalPump()
        {
            if (_signalPump != null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            _signalPump = Task.Run(() => SignalPumpAsync(token), token);
        }

        /// <summary>
        /// Post-and-drain against /v2/goon/signal every <see cref="SignalPollMs"/>, waking early
        /// whenever we have something to send (an offer waiting 2 s for the next tick is 2 s of
        /// setup latency for nothing). Exits as soon as the channel opens — this mailbox is a
        /// setup-only facility and must not keep costing requests during a 12-minute match.
        /// </summary>
        private async Task SignalPumpAsync(CancellationToken ct)
        {
            var failures = 0;

            try
            {
                while (!ct.IsCancellationRequested && !IsDisposed)
                {
                    if (State == GoonTransportState.ConnectedP2P) break;

                    var outgoing = new List<GoonSignalItem>();
                    while (_outSignals.TryDequeue(out var item)) outgoing.Add(item);

                    var result = await _signaling.SignalAsync(
                        Code ?? "", Token ?? "", _role, _signalCursor, outgoing, ct).ConfigureAwait(false);

                    if (result == null)
                    {
                        // Re-queue what we tried to send; a transient 5xx must not eat the offer.
                        foreach (var item in outgoing) _outSignals.Enqueue(item);

                        failures++;
                        if (failures >= SignalFailureLimit)
                        {
                            LastError = _signaling.LastError ?? "signaling_failed";
                            SetState(GoonTransportState.Disconnected, LastError);
                            return;
                        }

                        // The server told us how long to wait; otherwise back off gently.
                        var wait = _signaling.RetryAfterSeconds is int s && s > 0
                            ? Math.Min(s, 30) * 1000
                            : SignalPollMs * failures;
                        await Task.Delay(wait, ct).ConfigureAwait(false);
                        continue;
                    }

                    failures = 0;
                    _signalCursor = result.Cursor;

                    if (result.PeerGone)
                    {
                        LastError = "peer_left";
                        SetState(GoonTransportState.Disconnected, "peer left the room");
                        return;
                    }

                    foreach (var msg in result.Messages)
                        await HandleSignalAsync(msg, ct).ConfigureAwait(false);

                    // Host waits for the guest to appear before spending anything on ICE.
                    if (IsHost && result.PeerJoined && !_negotiationStarted)
                        await StartPeerAsync(null, ct).ConfigureAwait(false);

                    if (State == GoonTransportState.ConnectedP2P) break;

                    await _signalWake.WaitAsync(SignalPollMs, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* closing */ }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[{Tag}] Signaling pump died", Tag);
                LastError = "signaling_error";
                SetState(GoonTransportState.Disconnected, "signaling_error");
            }
        }

        private void EnqueueSignal(string kind, string data)
        {
            _outSignals.Enqueue(new GoonSignalItem { Kind = kind, Data = data });
            try { _signalWake.Release(); } catch (SemaphoreFullException) { /* already awake */ }
            catch (ObjectDisposedException) { }
        }

        private async Task HandleSignalAsync(GoonSignalItem item, CancellationToken ct)
        {
            switch (item.Kind)
            {
                case "offer":
                    if (IsHost) return;                    // hosts make offers, they don't take them
                    if (!RTCSessionDescriptionInit.TryParse(item.Data, out var offer)) return;
                    await StartPeerAsync(offer, ct).ConfigureAwait(false);
                    break;

                case "answer":
                {
                    if (!IsHost) return;
                    if (!RTCSessionDescriptionInit.TryParse(item.Data, out var answer)) return;
                    RTCPeerConnection? pc;
                    lock (_peerGate) pc = _pc;
                    if (pc == null) return;
                    var r = pc.setRemoteDescription(answer);
                    if (r != SetDescriptionResultEnum.OK)
                    {
                        App.Logger?.Warning("[{Tag}] setRemoteDescription(answer) -> {Result}", Tag, r);
                        LastError = "sdp_rejected";
                        return;
                    }
                    lock (_peerGate) _remoteDescriptionSet = true;
                    DrainPendingCandidates();
                    break;
                }

                case "ice":
                {
                    if (!RTCIceCandidateInit.TryParse(item.Data, out var cand)) return;
                    lock (_peerGate)
                    {
                        // Candidates routinely land before the description they belong to.
                        if (_pc == null || !_remoteDescriptionSet)
                        {
                            _pendingRemoteCandidates.Add(cand);
                            return;
                        }
                    }
                    TryAddCandidate(cand);
                    break;
                }

                case "bye":
                    LastError = "peer_bye";
                    SetState(GoonTransportState.Disconnected, "peer said bye");
                    break;
            }
        }

        private void DrainPendingCandidates()
        {
            List<RTCIceCandidateInit> pending;
            lock (_peerGate)
            {
                if (_pendingRemoteCandidates.Count == 0) return;
                pending = new List<RTCIceCandidateInit>(_pendingRemoteCandidates);
                _pendingRemoteCandidates.Clear();
            }
            foreach (var c in pending) TryAddCandidate(c);
        }

        private void TryAddCandidate(RTCIceCandidateInit cand)
        {
            try
            {
                RTCPeerConnection? pc;
                lock (_peerGate) pc = _pc;
                pc?.addIceCandidate(cand);
            }
            catch (Exception ex)
            {
                // One unusable candidate is normal (IPv6-only, a stale srflx); the pair still works.
                App.Logger?.Debug(ex, "[{Tag}] Rejected a remote ICE candidate", Tag);
            }
        }

        // ------------------------------------------------------------------ peer connection

        /// <param name="remoteOffer">Null on the host (we make the offer), the guest's inbound offer otherwise.</param>
        private async Task StartPeerAsync(RTCSessionDescriptionInit? remoteOffer, CancellationToken ct)
        {
            lock (_peerGate)
            {
                if (_negotiationStarted) return;
                _negotiationStarted = true;
            }

            SetState(GoonTransportState.ConnectingP2P, IsHost ? "offering" : "answering");

            try
            {
                var config = new RTCConfiguration
                {
                    iceServers = new List<RTCIceServer>(),
                };
                foreach (var url in StunUrls) config.iceServers.Add(new RTCIceServer { urls = url });

                var pc = new RTCPeerConnection(config);
                lock (_peerGate) _pc = pc;

                pc.onicecandidate += cand =>
                {
                    // Trickle: every candidate goes out the moment it is gathered, so the pair can
                    // start checking before gathering completes.
                    if (cand == null || IsDisposed) return;
                    try { EnqueueSignal("ice", cand.toJSON()); }
                    catch (Exception ex) { App.Logger?.Debug(ex, "[{Tag}] Failed to queue local candidate", Tag); }
                };

                pc.onconnectionstatechange += s => OnPeerStateChanged(s);

                pc.oniceconnectionstatechange += s =>
                    App.Logger?.Debug("[{Tag}] ICE state {State}", Tag, s);

                if (IsHost)
                {
                    var dc = await pc.createDataChannel(ChannelLabel, new RTCDataChannelInit
                    {
                        ordered = true,   // no maxRetransmits/maxPacketLifeTime => fully reliable
                    }).ConfigureAwait(false);
                    WireChannel(dc);

                    var offer = pc.createOffer(null);
                    await pc.setLocalDescription(offer).ConfigureAwait(false);
                    EnqueueSignal("offer", offer.toJSON());
                }
                else
                {
                    pc.ondatachannel += dc => WireChannel(dc);

                    var r = pc.setRemoteDescription(remoteOffer!);
                    if (r != SetDescriptionResultEnum.OK)
                    {
                        App.Logger?.Warning("[{Tag}] setRemoteDescription(offer) -> {Result}", Tag, r);
                        LastError = "sdp_rejected";
                        SetState(GoonTransportState.Disconnected, "sdp_rejected");
                        return;
                    }
                    lock (_peerGate) _remoteDescriptionSet = true;
                    DrainPendingCandidates();

                    var answer = pc.createAnswer(null);
                    await pc.setLocalDescription(answer).ConfigureAwait(false);
                    EnqueueSignal("answer", answer.toJSON());
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[{Tag}] Peer connection setup failed", Tag);
                IceFailed = true;
                LastError = "peer_setup_failed";
                SetState(GoonTransportState.Disconnected, "peer_setup_failed");
            }
        }

        private void OnPeerStateChanged(RTCPeerConnectionState s)
        {
            App.Logger?.Information("[{Tag}] Peer connection {State}", Tag, s);
            if (IsDisposed) return;

            switch (s)
            {
                case RTCPeerConnectionState.connected:
                    CancelReconnectGrace();
                    break;

                case RTCPeerConnectionState.disconnected:
                case RTCPeerConnectionState.failed:
                    if (_everConnected) BeginReconnectGrace(s.ToString());
                    else
                    {
                        IceFailed = true;
                        LastError = "ice_failed";
                        SetState(GoonTransportState.Disconnected, "ice_failed");
                    }
                    break;

                case RTCPeerConnectionState.closed:
                    if (State != GoonTransportState.Closed)
                        SetState(GoonTransportState.Disconnected, "peer connection closed");
                    break;
            }
        }

        private void WireChannel(RTCDataChannel dc)
        {
            lock (_peerGate) _dc = dc;

            dc.onopen += HandleChannelOpen;

            dc.onmessage += (_, _, data) =>
            {
                if (IsDisposed || data == null) return;
                try { HandleIncomingRaw(Encoding.UTF8.GetString(data)); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[{Tag}] Failed to read inbound frame", Tag); }
            };

            dc.onclose += () =>
            {
                if (IsDisposed) return;
                App.Logger?.Information("[{Tag}] Data channel closed", Tag);
                if (_everConnected && State != GoonTransportState.Closed)
                    BeginReconnectGrace("data channel closed");
            };

            dc.onerror += err => App.Logger?.Warning("[{Tag}] Data channel error: {Error}", Tag, err);

            // SIPSorcery raises ondatachannel on the ANSWERING side with the channel already in
            // `open` — the SCTP stream is negotiated before the event reaches us. Subscribing to
            // onopen alone therefore never fires on the guest, which strands it until the ICE
            // watchdog gives up and needlessly drops a perfectly good match to relay. Verified on
            // SIPSorcery 10.0.13. HandleChannelOpen is idempotent so the offerer, whose onopen does
            // fire, is unaffected.
            if (dc.readyState == RTCDataChannelState.open) HandleChannelOpen();
        }

        /// <summary>The channel is usable. Idempotent — see the note in <see cref="WireChannel"/>.</summary>
        private void HandleChannelOpen()
        {
            if (IsDisposed) return;
            if (Interlocked.Exchange(ref _channelOpenSignalled, 1) == 1) return;

            _everConnected = true;
            CancelReconnectGrace();
            SetState(GoonTransportState.ConnectedP2P, "data channel open");
            FlushResumeBuffer();
            BeginClockSync(_cts?.Token ?? CancellationToken.None);
        }

        // ------------------------------------------------------------------ reconnect / resume

        /// <summary>
        /// Hold the match together across a blip. For <see cref="GoonConsts.ReconnectGraceMs"/> the
        /// transport reports Reconnecting and buffers outbound frames instead of dropping them; if
        /// SCTP comes back the buffer flushes in order and the match never noticed. Past the grace
        /// we surface Disconnected and it becomes the match engine's abandon flow — which the plan
        /// is explicit must never be recorded as a mercy.
        /// </summary>
        private void BeginReconnectGrace(string reason)
        {
            lock (_peerGate)
            {
                if (_reconnectGrace != null) return;
                _reconnectGrace = new CancellationTokenSource();
            }

            SetState(GoonTransportState.Reconnecting, reason);
            // Arm the open handler again so a channel that comes back flushes the resume buffer.
            Interlocked.Exchange(ref _channelOpenSignalled, 0);

            var graceToken = _reconnectGrace.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(GoonConsts.ReconnectGraceMs, graceToken).ConfigureAwait(false);
                    if (IsDisposed) return;
                    if (State != GoonTransportState.Reconnecting) return;

                    LastError = "reconnect_timeout";
                    App.Logger?.Warning("[{Tag}] Peer did not resume within {Ms}ms", Tag, GoonConsts.ReconnectGraceMs);
                    SetState(GoonTransportState.Disconnected, "reconnect_timeout");
                }
                catch (OperationCanceledException) { /* resumed */ }
                catch (Exception ex) { App.Logger?.Warning(ex, "[{Tag}] Reconnect grace failed", Tag); }
            });
        }

        private void CancelReconnectGrace()
        {
            CancellationTokenSource? cts;
            lock (_peerGate) { cts = _reconnectGrace; _reconnectGrace = null; }
            if (cts == null) return;
            try { cts.Cancel(); cts.Dispose(); } catch { }
        }

        private void FlushResumeBuffer()
        {
            RTCDataChannel? dc;
            lock (_peerGate) dc = _dc;
            if (dc == null || dc.readyState != RTCDataChannelState.open) return;

            var sent = 0;
            while (_resumeBuffer.TryDequeue(out var json))
            {
                try { dc.send(json); sent++; }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "[{Tag}] Resume flush failed after {Sent} frames", Tag, sent);
                    break;
                }
            }
            if (sent > 0) App.Logger?.Information("[{Tag}] Resumed: flushed {Sent} buffered frames", Tag, sent);
        }

        // ------------------------------------------------------------------ send

        protected override Task SendRawAsync(string json, CancellationToken ct)
        {
            if (IsDisposed) return Task.CompletedTask;

            RTCDataChannel? dc;
            lock (_peerGate) dc = _dc;

            if (dc != null && dc.readyState == RTCDataChannelState.open)
            {
                try
                {
                    dc.send(json);
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "[{Tag}] send failed — buffering for resume", Tag);
                }
            }

            if (State == GoonTransportState.Reconnecting || State == GoonTransportState.ConnectingP2P)
            {
                if (_resumeBuffer.Count < ResumeBufferMax) _resumeBuffer.Enqueue(json);
                else App.Logger?.Warning("[{Tag}] Resume buffer full — dropping a frame", Tag);
            }
            else
            {
                App.Logger?.Warning("[{Tag}] Dropping a frame — channel not open (state {State})", Tag, State);
            }

            return Task.CompletedTask;
        }

        // ------------------------------------------------------------------ teardown

        public override async Task CloseAsync()
        {
            if (State == GoonTransportState.Closed) return;

            // Best-effort courtesy so the peer stops polling immediately instead of waiting out the
            // room TTL. Never blocks the close.
            try
            {
                if (!string.IsNullOrEmpty(Code) && !string.IsNullOrEmpty(Token) && State == GoonTransportState.Signaling)
                {
                    using var byeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _signaling.SignalAsync(Code!, Token!, _role, _signalCursor,
                        new[] { new GoonSignalItem { Kind = "bye", Data = "" } }, byeCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { App.Logger?.Debug(ex, "[{Tag}] bye failed (harmless)", Tag); }

            SetState(GoonTransportState.Closed, "closed by us");
            TearDown();
        }

        private void TearDown()
        {
            CancelReconnectGrace();
            try { _cts?.Cancel(); } catch { }

            RTCDataChannel? dc;
            RTCPeerConnection? pc;
            lock (_peerGate) { dc = _dc; pc = _pc; _dc = null; _pc = null; }

            try { dc?.close(); } catch { }
            try { pc?.close(); } catch { }
            MatchClock.Stop();
        }

        protected override void DisposeCore()
        {
            TearDown();
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            try { _signalWake.Dispose(); } catch { }
            if (_ownsSignaling) _signaling.Dispose();
        }
    }
}
