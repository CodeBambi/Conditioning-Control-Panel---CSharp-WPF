using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

// ============================================================================
// GOON GAME — integration facade (App.GoonGame).
//
// Owns the per-match plumbing the individual Wave-1 pieces deliberately do not:
//   * one GoonSignalingClient per match session, shared by both transports so a
//     relay fallback re-uses the room (and the burned weekly pass) instead of
//     minting a second one;
//   * the WebRTC-first / relay-fallback dance (GoonWebRtcTransport.IceFailed ->
//     GoonRelayTransport.AdoptRoom -> GoonMatchService.AdoptLobby);
//   * GoonMatchService construction with the REAL deterministic RNG
//     (seed => new GoonRng(seed)) and a sudden-death runner attached.
//
// The fallback REBUILDS the match service (it binds its transport for life), so
// UI must track CurrentMatchChanged and re-subscribe rather than caching the
// first CurrentMatch instance. The rebuild window is safe: ICE can only fail in
// Lobby, before the hello/consent exchange has happened.
//
// This facade does NOT gate on premium/pass state — the server enforces passes
// at /v2/goon/invite (402 no_pass). UI may pre-gate for niceness, never here.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    public sealed class GoonGameService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly object _gate = new();

        private GoonSignalingClient? _signaling;
        private GoonTransportBase? _transport;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public GoonGameService()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        /// <summary>The live match service, or null when idle. Replaced on relay fallback.</summary>
        public GoonMatchService? CurrentMatch { get; private set; }

        /// <summary>
        /// CurrentMatch was created, replaced (relay fallback), or torn down. UI re-subscribes
        /// its match event handlers here.
        /// </summary>
        public event EventHandler? CurrentMatchChanged;

        /// <summary>Neither P2P nor relay could connect. Arg = machine reason ("ice_timeout", server error codes...).</summary>
        public event EventHandler<string>? ConnectFailed;

        /// <summary>
        /// Phase E replaces this with a factory that wires the real presenter + input feeds:
        /// () => new GoonSuddenDeathRunner(presenter, inputs). Default is the headless runner
        /// (rounds still resolve deterministically; nothing renders).
        /// </summary>
        public Func<IGoonSuddenDeathRunner> RunnerFactory { get; set; } = () => new GoonSuddenDeathRunner();

        public bool IsBusy => CurrentMatch != null;

        // ------------------------------------------------------------- entry

        /// <summary>Host path: mints an invite and returns the code to show the user (null = failure).</summary>
        public async Task<string?> HostAsync(CancellationToken ct = default)
        {
            if (_disposed || !BeginSession(isHost: true, out var webrtc, out var match)) return null;

            var code = await match.CreateInviteAsync(_cts!.Token).ConfigureAwait(true);
            if (string.IsNullOrEmpty(code))
            {
                var reason = webrtc.LastError ?? "invite_failed";
                await TeardownAsync().ConfigureAwait(true);
                RaiseConnectFailed(reason);
                return null;
            }

            _ = WatchChannelAsync(webrtc, isHost: true, _cts.Token);
            return code;
        }

        /// <summary>Guest path: redeems a code the user typed.</summary>
        public async Task<bool> JoinAsync(string inviteCode, CancellationToken ct = default)
        {
            if (_disposed || !BeginSession(isHost: false, out var webrtc, out var match)) return false;

            var ok = await match.JoinAsync(inviteCode, _cts!.Token).ConfigureAwait(true);
            if (!ok)
            {
                var reason = webrtc.LastError ?? "join_failed";
                await TeardownAsync().ConfigureAwait(true);
                RaiseConnectFailed(reason);
                return false;
            }

            _ = WatchChannelAsync(webrtc, isHost: false, _cts.Token);
            return true;
        }

        /// <summary>
        /// User-initiated exit from wherever we are. In Live/SuddenDeath this is a Mercy
        /// (CancelMatch routes it); earlier phases just fold the lobby. Always tears the
        /// plumbing down to idle.
        /// </summary>
        public async Task LeaveAsync()
        {
            var match = CurrentMatch;
            if (match != null)
            {
                try { match.CancelMatch("left"); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GG] cancel on leave threw"); }
            }
            await TeardownAsync().ConfigureAwait(true);
        }

        // ------------------------------------------------------------ innards

        private bool BeginSession(bool isHost, out GoonWebRtcTransport webrtc, out GoonMatchService match)
        {
            lock (_gate)
            {
                if (CurrentMatch != null)
                {
                    webrtc = null!;
                    match = null!;
                    return false;
                }

                _cts = new CancellationTokenSource();
                _signaling = new GoonSignalingClient();
                webrtc = new GoonWebRtcTransport(isHost, _signaling);
                _transport = webrtc;
                match = BuildMatch(webrtc, isHost);
            }

            RaiseSafe(CurrentMatchChanged);
            return true;
        }

        private GoonMatchService BuildMatch(IGoonTransport transport, bool isHost)
        {
            // The real Phase A RNG — both clients derive identical ramps/rounds from the
            // combined seed. (The service's internal splitmix64 fallback is standalone-only.)
            var match = new GoonMatchService(transport, isHost, seed => new GoonRng(seed));

            IGoonSuddenDeathRunner runner;
            try { runner = RunnerFactory?.Invoke() ?? new GoonSuddenDeathRunner(); }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] runner factory threw — using headless runner");
                runner = new GoonSuddenDeathRunner();
            }
            match.SuddenDeathRunner = runner;

            CurrentMatch = match;
            return match;
        }

        private async Task WatchChannelAsync(GoonWebRtcTransport webrtc, bool isHost, CancellationToken ct)
        {
            try
            {
                var ok = await webrtc.WaitForChannelAsync(ct).ConfigureAwait(false);
                if (ok || ct.IsCancellationRequested || _disposed) return;

                if (!webrtc.IceFailed || webrtc.Code == null || webrtc.Token == null)
                {
                    // Signaling-level failure (server down, room expired, peer never came) —
                    // a relay would fail the same way. Surface and fold.
                    await OnDispatcherAsync(async () =>
                    {
                        var reason = webrtc.LastError ?? "signaling_failed";
                        await TeardownAsync().ConfigureAwait(true);
                        RaiseConnectFailed(reason);
                    }).ConfigureAwait(false);
                    return;
                }

                await OnDispatcherAsync(() => FallBackToRelayAsync(webrtc, isHost, ct)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] channel watch failed");
                RaiseConnectFailed("connect_error");
            }
        }

        private async Task FallBackToRelayAsync(GoonWebRtcTransport webrtc, bool isHost, CancellationToken ct)
        {
            if (_disposed || ct.IsCancellationRequested || _transport != webrtc) return;

            var code = webrtc.Code!;
            var token = webrtc.Token!;
            App.Logger?.Information("[GG] P2P failed, adopting room {Code} over relay", code);

            // Old match first (it unsubscribes the dying transport; Dispose sends nothing),
            // then the transport. The shared signaling client stays — the relay needs it.
            CurrentMatch?.Dispose();
            CurrentMatch = null;
            try { await webrtc.CloseAsync().ConfigureAwait(true); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GG] webrtc close threw during fallback"); }
            webrtc.Dispose();

            var relay = new GoonRelayTransport(isHost, _signaling);
            _transport = relay;
            var match = BuildMatch(relay, isHost);
            match.AdoptLobby();
            relay.AdoptRoom(code, token);
            RaiseSafe(CurrentMatchChanged);

            var ok = await relay.WaitForChannelAsync(ct).ConfigureAwait(true);
            if (!ok && !ct.IsCancellationRequested && !_disposed)
            {
                var reason = relay.LastError ?? "relay_failed";
                await TeardownAsync().ConfigureAwait(true);
                RaiseConnectFailed(reason);
            }
        }

        private async Task TeardownAsync()
        {
            GoonMatchService? match;
            GoonTransportBase? transport;
            GoonSignalingClient? signaling;
            CancellationTokenSource? cts;

            lock (_gate)
            {
                match = CurrentMatch;
                CurrentMatch = null;
                transport = _transport;
                _transport = null;
                signaling = _signaling;
                _signaling = null;
                cts = _cts;
                _cts = null;
            }

            try { cts?.Cancel(); } catch { /* already gone */ }

            match?.Dispose();
            if (transport != null)
            {
                try { await transport.CloseAsync().ConfigureAwait(true); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GG] transport close threw"); }
                transport.Dispose();
            }
            signaling?.Dispose();
            cts?.Dispose();

            RaiseSafe(CurrentMatchChanged);
        }

        // ------------------------------------------------------------ helpers

        private Task OnDispatcherAsync(Func<Task> body)
        {
            if (_dispatcher.HasShutdownStarted) return Task.CompletedTask;
            if (_dispatcher.CheckAccess()) return body();
            return _dispatcher.InvokeAsync(body, DispatcherPriority.Normal).Task.Unwrap();
        }

        private void RaiseConnectFailed(string reason)
        {
            if (_dispatcher.HasShutdownStarted) return;
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                try { ConnectFailed?.Invoke(this, reason); }
                catch (Exception ex) { App.Logger?.Error(ex, "[GG] ConnectFailed handler threw"); }
            });
        }

        private void RaiseSafe(EventHandler? handler)
        {
            if (handler == null || _dispatcher.HasShutdownStarted) return;
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                try { handler.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { App.Logger?.Error(ex, "[GG] match-changed handler threw"); }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = TeardownAsync();
        }
    }
}
