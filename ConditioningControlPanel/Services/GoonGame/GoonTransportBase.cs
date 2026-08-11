using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Marshals Goon Game callbacks onto the WPF dispatcher.
    ///
    /// Every transport raises its events from somewhere else — a SIPSorcery SCTP thread, an
    /// HttpClient continuation, a Task.Delay timer — and the match engine and HUD both touch WPF
    /// objects, so nothing may surface on those threads.
    ///
    /// Two guards from CLAUDE.md are non-negotiable here: a null Application (we are inside a
    /// console harness or the app is already gone) and a dispatcher that has begun shutting down
    /// (a queued BeginInvoke on a dying dispatcher is a classic shutdown crash in this codebase).
    /// With no Application at all we run inline, which is what makes the loopback transport usable
    /// from a plain test harness.
    /// </summary>
    internal static class GoonDispatch
    {
        public static void Post(Action action)
        {
            if (action == null) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                // No WPF app (headless harness). Inline is correct AND keeps ordering.
                try { action(); }
                catch (Exception ex) { App.Logger?.Error(ex, "[Goon] Inline dispatch failed"); }
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;

            if (dispatcher.CheckAccess())
            {
                try { action(); }
                catch (Exception ex) { App.Logger?.Error(ex, "[Goon] Dispatch failed"); }
                return;
            }

            try
            {
                // Normal, never Loaded — Loaded-priority work is starved in this app
                // (see CLAUDE.md / memory: dispatcher-loaded-priority-starved).
                dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try { action(); }
                    catch (Exception ex) { App.Logger?.Error(ex, "[Goon] Dispatched callback failed"); }
                }));
            }
            catch (Exception ex)
            {
                // BeginInvoke itself throws if the dispatcher shut down between the check and here.
                App.Logger?.Warning(ex, "[Goon] BeginInvoke rejected (dispatcher shutting down?)");
            }
        }
    }

    /// <summary>
    /// What every <see cref="IGoonTransport"/> in this folder shares: the serialize/route/raise
    /// pipeline, the clock hookup, and the state machine plumbing. Subclasses only implement
    /// "get this string to the peer" and "tell me when the channel is up".
    ///
    /// Inbound path, in order: raw string -> <see cref="GoonWire.Deserialize"/> (size-capped,
    /// never throws) -> the <see cref="MatchClock"/> gets first refusal (ping/pong are private to
    /// it) -> everything else surfaces on <see cref="MessageReceived"/>, on the dispatcher.
    /// </summary>
    public abstract class GoonTransportBase : IGoonTransport
    {
        private readonly object _stateGate = new();
        private GoonTransportState _state = GoonTransportState.Disconnected;
        private bool _disposed;

        protected GoonTransportBase(bool isHost, string tag, long clockTestSkewMs = 0)
        {
            IsHost = isHost;
            Tag = tag;
            // Host's monotonic clock IS the match clock; the guest measures its offset to it.
            MatchClock = new MatchClock(isHost, tag, clockTestSkewMs);
            MatchClock.Attach(SendAsync);
        }

        /// <summary>True on the side that minted the invite. Also decides who owns the match clock.</summary>
        public bool IsHost { get; }

        /// <summary>Log prefix for this transport instance.</summary>
        protected string Tag { get; }

        protected MatchClock MatchClock { get; }

        public IMatchClock Clock => MatchClock;

        public GoonTransportState State { get { lock (_stateGate) return _state; } }

        /// <summary>Last failure reason, for the lobby UI and for deciding whether to fall back to relay.</summary>
        public string? LastError { get; protected set; }

        public event EventHandler<GoonMessage>? MessageReceived;
        public event EventHandler<GoonTransportState>? StateChanged;

        public abstract Task<string?> CreateInviteAsync(CancellationToken ct);
        public abstract Task<bool> JoinAsync(string inviteCode, CancellationToken ct);
        public abstract Task CloseAsync();

        /// <summary>Push one already-serialized frame to the peer. Implementations may buffer.</summary>
        protected abstract Task SendRawAsync(string json, CancellationToken ct);

        public virtual async Task SendAsync(GoonMessage message, CancellationToken ct)
        {
            if (message == null) return;
            if (_disposed) return;

            var json = GoonWire.Serialize(message);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > GoonWire.MaxWireBytes)
            {
                // Our own bug, not the peer's. Dropping beats wedging the channel.
                App.Logger?.Error("[{Tag}] Refusing to send oversize {Type} ({Chars} chars)",
                    Tag, message.Type, json.Length);
                return;
            }

            await SendRawAsync(json, ct).ConfigureAwait(false);
        }

        /// <summary>Subclasses funnel every inbound frame through here.</summary>
        protected void HandleIncomingRaw(string? json)
        {
            if (_disposed) return;

            var message = GoonWire.Deserialize(json, Tag);
            if (message == null) return;                    // already logged

            // The clock's ping/pong is its own private conversation — the match engine must never
            // see it, and it must be answered off the dispatcher so a busy UI thread can't inflate
            // the peer's RTT measurement.
            if (MatchClock.TryHandleMessage(message)) return;

            GoonDispatch.Post(() => MessageReceived?.Invoke(this, message));
        }

        protected void SetState(GoonTransportState next, string? reason = null)
        {
            lock (_stateGate)
            {
                if (_state == next) return;
                _state = next;
            }

            if (reason != null)
                App.Logger?.Information("[{Tag}] State -> {State} ({Reason})", Tag, next, reason);
            else
                App.Logger?.Information("[{Tag}] State -> {State}", Tag, next);

            GoonDispatch.Post(() => StateChanged?.Invoke(this, next));
        }

        /// <summary>Runs the clock's ping rounds once the channel is usable. Fire-and-forget safe.</summary>
        protected void BeginClockSync(CancellationToken ct)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var ok = await MatchClock.SyncAsync(ct).ConfigureAwait(false);
                    if (!ok)
                        App.Logger?.Warning("[{Tag}] Initial clock sync did not converge — retrying once", Tag);
                    else
                        return;

                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    await MatchClock.SyncAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[{Tag}] Clock sync threw", Tag);
                }
            }, ct);
        }

        protected bool IsDisposed => _disposed;

        protected virtual void DisposeCore() { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { DisposeCore(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[{Tag}] Dispose failed", Tag); }
            try { MatchClock.Dispose(); } catch { }
            MessageReceived = null;
            StateChanged = null;
            GC.SuppressFinalize(this);
        }
    }
}
