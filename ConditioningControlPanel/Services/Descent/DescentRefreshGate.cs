using System;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>What <see cref="DescentRefreshGate.Ask"/> wants the service to do with one request.</summary>
    internal enum DescentRefreshVerdict
    {
        /// <summary>Go now. The caller stamps the fetch with <see cref="DescentRefreshGate.BeginFetch"/>.</summary>
        Fetch,

        /// <summary>
        /// Not now, but soon: ask again after the retry delay. The want is remembered, so the
        /// retry only has to check <see cref="DescentRefreshGate.Pending"/> when it lands.
        /// </summary>
        Defer,

        /// <summary>
        /// No account or no auth token yet. The want is remembered and the service fires it
        /// from <see cref="DescentService.OnSignedIn"/> once a login path declares the account
        /// usable.
        /// </summary>
        WaitForSignIn,

        /// <summary>
        /// Nothing to do: offline mode (the hard floor, never remembered), or a fetch with these
        /// same credentials already answered inside the floor and this request is the burst the
        /// floor exists to coalesce.
        /// </summary>
        Skip,
    }

    /// <summary>
    /// THE VAT'S REFRESH MEMORY. Pure: no clock, no settings, no network, so the sign-in and
    /// throttle rules can be pinned by tests without a dispatcher.
    ///
    /// <para><b>Why it exists.</b> DescentService.RefreshAsync used to answer every request it could
    /// not serve with a silent <c>false</c>: no token yet, a fetch already in flight, or inside the
    /// 10s floor. A request made before the auth token landed was simply gone, and the surfaces
    /// that asked (the Trainer Card on open, MainWindow on profile-loaded) never asked again. The
    /// only recovery was the 60s background poll or a sign-out. This gate remembers every want it
    /// cannot serve right now and says when to come back for it.</para>
    ///
    /// <para><b>The floor is still a floor.</b> A request inside the 10s window after a fetch that
    /// ANSWERED with the SAME credentials is redundant and stays dropped; that is the burst
    /// coalescing the floor was built for. A request inside the window after a fetch that failed,
    /// or that went out with other credentials (the token was rotated or healed), is deferred to
    /// the end of the window instead, because the answer it would have coalesced into is not one
    /// this account can use.</para>
    /// </summary>
    internal sealed class DescentRefreshGate
    {
        /// <summary>
        /// Floor between two fetches, whatever asks. The Trainer Card polls at 60s and a sync can
        /// land at any moment; without this, a burst of syncs would each fire a profile GET.
        /// </summary>
        public static readonly TimeSpan MinFetchInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// A deferred retry never lands sooner than this, so an in-flight fetch that outlives the
        /// floor (a hung request has a 30s client timeout) cannot turn the retry into a busy loop.
        /// </summary>
        public static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(1);

        private DateTime _lastFetchUtc = DateTime.MinValue;
        private string? _lastFetchUnifiedId;
        private string? _lastFetchToken;
        private bool _lastFetchAnswered;

        /// <summary>A request was made that no fetch has answered yet.</summary>
        public bool Pending { get; private set; }

        /// <summary>The first reason still waiting, for the log line when it finally goes out.</summary>
        public string? PendingReason { get; private set; }

        /// <summary>When the last fetch went out (UTC); MinValue until one has.</summary>
        public DateTime LastFetchUtc => _lastFetchUtc;

        public DescentRefreshVerdict Ask(
            DateTime nowUtc,
            string? unifiedId,
            string? authToken,
            bool offline,
            bool inFlight,
            bool force,
            string reason,
            out TimeSpan retryIn)
        {
            retryIn = TimeSpan.Zero;

            // OFFLINE MODE IS A HARD FLOOR, not a preference this feature gets to outrank, and
            // not a want to remember either: the user turned networking off.
            if (offline) return DescentRefreshVerdict.Skip;

            if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(authToken))
            {
                Remember(reason);
                return DescentRefreshVerdict.WaitForSignIn;
            }

            if (inFlight)
            {
                // The fetch in the air may have gone out with older credentials than these, so
                // its answer is not assumed to cover this request. If it does answer, EndFetch
                // clears the want and the retry finds nothing to do.
                Remember(reason);
                retryIn = RemainingFloor(nowUtc);
                return DescentRefreshVerdict.Defer;
            }

            var age = nowUtc - _lastFetchUtc;
            if (!force && age < MinFetchInterval)
            {
                if (_lastFetchAnswered
                    && string.Equals(unifiedId, _lastFetchUnifiedId, StringComparison.Ordinal)
                    && string.Equals(authToken, _lastFetchToken, StringComparison.Ordinal))
                {
                    return DescentRefreshVerdict.Skip;   // the floor doing its job
                }

                Remember(reason);
                retryIn = Clamp(MinFetchInterval - age);
                return DescentRefreshVerdict.Defer;
            }

            return DescentRefreshVerdict.Fetch;
        }

        /// <summary>Stamp the fetch that is about to go out, with the credentials it carries.</summary>
        public void BeginFetch(DateTime nowUtc, string unifiedId, string authToken)
        {
            _lastFetchUtc = nowUtc;
            _lastFetchUnifiedId = unifiedId;
            _lastFetchToken = authToken;
            _lastFetchAnswered = false;
        }

        /// <summary>
        /// The fetch came back. <paramref name="answered"/> is "the server sent a readable user
        /// node", whether or not it carried a descent block; a null node (non-2xx, network) is not
        /// an answer, and a want made during that fetch stays standing.
        /// </summary>
        public void EndFetch(bool answered)
        {
            _lastFetchAnswered = answered;
            if (!answered) return;
            Pending = false;
            PendingReason = null;
        }

        /// <summary>Logout. Forget the floor, the credentials the last fetch used, and any want.</summary>
        public void Reset()
        {
            _lastFetchUtc = DateTime.MinValue;
            _lastFetchUnifiedId = null;
            _lastFetchToken = null;
            _lastFetchAnswered = false;
            Pending = false;
            PendingReason = null;
        }

        private void Remember(string reason)
        {
            Pending = true;
            PendingReason ??= reason;   // the first ask is the one that explains the log
        }

        private TimeSpan RemainingFloor(DateTime nowUtc)
            => Clamp(MinFetchInterval - (nowUtc - _lastFetchUtc));

        private static TimeSpan Clamp(TimeSpan delay)
            => delay < MinRetryDelay ? MinRetryDelay : delay;
    }
}
