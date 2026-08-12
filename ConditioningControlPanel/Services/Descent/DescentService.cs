using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE DESCENT — the desktop app's reader for the server's `descent` block.
    ///
    /// ONE REQUEST, ONE TRUTH. The block rides inside the `user` node of
    /// GET /v2/user/profile (ccp-server server.js :14334), which is the endpoint
    /// desktop's own auth already fits: X-Auth-Token out of
    /// App.Settings.Current.AuthToken, unified_id on the query string. It is NOT on
    /// /v2/user/sync's response, so the moment today's XP lands in the server vat is
    /// the moment this service has to go and ask again — see RequestRefresh's callers.
    ///
    /// READ-ONLY, ALWAYS. Nothing here POSTs, and nothing downstream of it is
    /// allowed to either: progression is server-authoritative and a client that can
    /// write its own vat is a client that can fill it.
    ///
    /// TRI-STATE: <see cref="Current"/> is null both for "never fetched" and for
    /// "the server sent no usable block". Both mean the same thing to every surface
    /// — render nothing — so they are deliberately not distinguished.
    /// </summary>
    public sealed class DescentService
    {
        /// <summary>
        /// Floor between two fetches, whatever asks. The Trainer Card polls at 60s
        /// and a sync can land at any moment; without this, a burst of syncs would
        /// each fire a profile GET.
        /// </summary>
        private static readonly TimeSpan MinFetchInterval = TimeSpan.FromSeconds(10);

        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _lastFetchUtc = DateTime.MinValue;

        /// <summary>The last well-formed block, or null. See the tri-state note above.</summary>
        public DescentBlock? Current { get; private set; }

        /// <summary>
        /// Raised on the UI thread after every accepted fetch, including one that
        /// turned the block off (payload arrived without it). Surfaces subscribe and
        /// re-read <see cref="Current"/>.
        /// </summary>
        public event EventHandler? BlockChanged;

        /// <summary>
        /// Fire-and-forget refresh. Safe to call from event handlers and from the
        /// sync path — it swallows everything, because a failed vat read must never
        /// disturb the thing that asked for it.
        /// </summary>
        public void RequestRefresh(string reason)
        {
            _ = Task.Run(async () =>
            {
                try { await RefreshAsync(reason).ConfigureAwait(false); }
                catch (Exception ex) { Log.Debug("[Descent] refresh '{Reason}' failed: {Error}", reason, ex.Message); }
            });
        }

        /// <summary>
        /// Fetch and parse the block. Returns true when a fetch actually happened
        /// (whether or not it produced a block); false when it was skipped — no
        /// account, no auth token, a fetch already in flight, or inside the floor.
        /// </summary>
        public async Task<bool> RefreshAsync(string reason, bool force = false)
        {
            string? unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId)) return false;
            if (string.IsNullOrEmpty(App.Settings?.Current?.AuthToken)) return false;

            if (!_gate.Wait(0)) return false;
            try
            {
                if (!force && DateTime.UtcNow - _lastFetchUtc < MinFetchInterval) return false;
                _lastFetchUtc = DateTime.UtcNow;

                var userNode = await new V2AuthService().GetUserProfileNodeAsync(unifiedId).ConfigureAwait(false);
                if (userNode is null)
                {
                    // A failed read is NOT "the user has no descent". Leave the last
                    // known block standing rather than blanking a vat over a flaky
                    // network — the alternative is a meter that flickers out of
                    // existence every time Vercel hiccups.
                    Log.Debug("[Descent] profile read returned nothing ({Reason})", reason);
                    return true;
                }

                var block = DescentReader.ParseFromUserNode(userNode);
                bool had = Current is not null;
                Current = block;

                if (block is null && had)
                    Log.Information("[Descent] block withdrawn by server ({Reason})", reason);
                else if (block is not null && !had)
                    Log.Information("[Descent] block armed for this account ({Reason})", reason);

                RaiseBlockChanged();
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Marshal to the UI thread the way the rest of the app does: bail out
        /// entirely if the dispatcher is gone or shutting down (CLAUDE.md async
        /// rules 6/8 — a fire-and-forget callback on a dead dispatcher is a crash
        /// report nobody can read).
        /// </summary>
        private void RaiseBlockChanged()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted) return;

                if (dispatcher.CheckAccess())
                {
                    BlockChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try { BlockChanged?.Invoke(this, EventArgs.Empty); }
                    catch (Exception ex) { Log.Debug("[Descent] BlockChanged handler threw: {Error}", ex.Message); }
                }));
            }
            catch (Exception ex)
            {
                Log.Debug("[Descent] could not raise BlockChanged: {Error}", ex.Message);
            }
        }
    }
}
