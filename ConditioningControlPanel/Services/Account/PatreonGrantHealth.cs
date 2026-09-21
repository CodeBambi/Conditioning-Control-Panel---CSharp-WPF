using System.Net;

namespace ConditioningControlPanel.Services
{
    /// <summary>What a <c>/patreon/refresh</c> attempt tells us about the grant itself.</summary>
    public enum PatreonRefreshOutcome
    {
        /// <summary>New tokens are stored. The grant is alive.</summary>
        Refreshed,

        /// <summary>
        /// The service answered and said no. A refresh token is only refused when it has been
        /// revoked, replaced or was never valid, so this will not start working by itself.
        /// </summary>
        Refused,

        /// <summary>
        /// Nobody answered, or the answer was the service's own problem. Says nothing about the
        /// grant and must never be read as a dead one.
        /// </summary>
        Unavailable
    }

    /// <summary>
    /// Telling a revoked Patreon grant apart from a bad afternoon.
    ///
    /// <para>Needed because <c>SecureTokenStorage.HasValidTokens()</c> only asks whether the .dat
    /// holds a non-empty access token, and <see cref="PatreonService.ValidateSubscriptionAsync"/>
    /// deliberately KEEPS the tokens through a failed refresh (#585: a proxy hiccup must not drop a
    /// paying subscriber). So <c>IsAuthenticated</c> stays true forever after the grant dies, which
    /// is how the ticket's patron spent six weeks with the repair button hidden from her.</para>
    ///
    /// <para>Conservative on purpose: only an ANSWER counts, and only a 4xx one. A thrown request,
    /// a timeout, a 5xx and a 429 are the network having a moment, and a patron on a flaky
    /// connection must not be nagged. Being wrong the other way costs one extra launch.</para>
    /// </summary>
    public static class PatreonGrantHealth
    {
        /// <param name="status">The HTTP status the proxy answered with, or null if nothing did.</param>
        /// <param name="oauthError">The <c>error</c> field of an otherwise-OK token response.</param>
        /// <param name="threw">The request threw instead of returning.</param>
        public static PatreonRefreshOutcome Classify(HttpStatusCode? status, string? oauthError, bool threw)
        {
            if (threw || status == null) return PatreonRefreshOutcome.Unavailable;

            var code = (int)status.Value;

            // The service's own trouble, or ours for asking too often. Try again later.
            if (code >= 500 || code == (int)HttpStatusCode.RequestTimeout || code == 429)
                return PatreonRefreshOutcome.Unavailable;

            // Any other 4xx is the token being turned down.
            if (code >= 400) return PatreonRefreshOutcome.Refused;

            // 2xx with an OAuth error body (invalid_grant and friends) is the same refusal in a 200.
            if (!string.IsNullOrWhiteSpace(oauthError)) return PatreonRefreshOutcome.Refused;

            return PatreonRefreshOutcome.Refreshed;
        }

        /// <summary>
        /// Whether this outcome should light the Reconnect row. Only a refusal does; everything
        /// else leaves the grant's health exactly as it was.
        /// </summary>
        public static bool MarksGrantDead(PatreonRefreshOutcome outcome)
            => outcome == PatreonRefreshOutcome.Refused;
    }
}
