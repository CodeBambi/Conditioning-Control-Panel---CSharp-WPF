using System;
using System.Net;

namespace ConditioningControlPanel.Services
{
    /// <summary>What a <c>/patreon/refresh</c> attempt tells us about the grant itself.</summary>
    public enum PatreonRefreshOutcome
    {
        /// <summary>New tokens are stored. The grant is alive.</summary>
        Refreshed,

        /// <summary>
        /// The grant will not refresh again. Either the service said so outright, or it has been
        /// failing long enough that nothing else explains it.
        /// </summary>
        Refused,

        /// <summary>
        /// Nobody answered, or the answer was the service's own problem and has not been going on
        /// long. Says nothing about the grant and must never be read as a dead one.
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
    /// <para><b>The proxy cannot tell us the grant is dead, today.</b> Read off CCP-Server
    /// origin/main <c>proxy/server.js</c>: <c>POST /patreon/refresh</c> calls
    /// <c>refreshAccessToken()</c>, which THROWS on any non-ok answer from Patreon - including the
    /// invalid_grant a revoked refresh token earns - and the route's catch maps every throw to
    /// <c>500 {error:'Internal server error'}</c>. The only 4xx it can send is the 400 for a
    /// missing refresh_token, which this client never triggers. A server-side 401 for a refused
    /// grant is proposed separately and would make the verdict immediate; until then the client
    /// must not wait on a deploy.</para>
    ///
    /// <para>So there are two rungs. A 4xx answer (or a 2xx carrying an OAuth error body) is a
    /// refusal at once. Any OTHER failing answer is a refusal only once the stored access token's
    /// expiry has been in the past for more than <see cref="StaleAfter"/>: a healthy install
    /// refreshes on the first launch after expiry, so an expiry sitting days in the past while the
    /// proxy keeps answering is a grant that cannot be refreshed. Inside those three days it stays
    /// Unavailable, so one bad evening nags nobody - and even a genuine multi-day outage would at
    /// worst OFFER a repair (the quiet row; prominent only once the 14-day grace has also lapsed)
    /// and grant nothing.</para>
    ///
    /// <para>A thrown request and a timeout are never a verdict at any age: nothing answered, so
    /// there is nothing to read. 429 is the same - the service asking for less, not a word about
    /// the token.</para>
    /// </summary>
    public static class PatreonGrantHealth
    {
        /// <summary>
        /// How long an access-token expiry has to sit in the past, while the proxy keeps failing
        /// for reasons of its own, before the grant behind it is treated as dead. Three days is
        /// several launches past the first one that would have refreshed it.
        /// </summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

        /// <param name="status">The HTTP status the proxy answered with, or null if nothing did.</param>
        /// <param name="oauthError">The <c>error</c> field of an otherwise-OK token response.</param>
        /// <param name="threw">The request threw instead of returning.</param>
        /// <param name="accessTokenExpiresAtUtc">
        /// When the stored access token expired. Null (unknown) can never make a grant dead.
        /// </param>
        /// <param name="nowUtc">Passed in so the rule stays pure and testable.</param>
        /// <param name="responseBody">The error body of a failing answer, when it was read.</param>
        public static PatreonRefreshOutcome Classify(
            HttpStatusCode? status,
            string? oauthError,
            bool threw,
            DateTime? accessTokenExpiresAtUtc,
            DateTime nowUtc,
            string? responseBody = null)
        {
            if (threw || status == null) return PatreonRefreshOutcome.Unavailable;

            var code = (int)status.Value;

            // The body names the grant dead: 401 {reason:'grant_dead'} from a fixed proxy, or the
            // 500 an unfixed one sends with Patreon's invalid_grant inside. A verdict at once,
            // whatever the age of the expiry (Sep 2026: ~3.7k of these a day, retried for ever).
            if (code >= 400 && BodySaysGrantDead(responseBody)) return PatreonRefreshOutcome.Refused;

            // A fixed proxy names its own trouble (500 provider_config = our secret, 502/503
            // provider_busy = Patreon down). That is never the grant, however stale the expiry.
            if (code >= 500 && BodySaysProviderTrouble(responseBody)) return PatreonRefreshOutcome.Unavailable;

            // Answers that mean "later" by definition, whatever else is going on.
            if (code == (int)HttpStatusCode.RequestTimeout || code == 429)
                return PatreonRefreshOutcome.Unavailable;

            // A 4xx is the token being turned down. Immediate, and the shape a server-side 401
            // would give us.
            if (code >= 400 && code < 500) return PatreonRefreshOutcome.Refused;

            // The proxy's own error - which is also what it sends today for a revoked grant. Only
            // the age of the unrefreshed expiry tells those two apart.
            if (code >= 500)
                return HasBeenStaleTooLong(accessTokenExpiresAtUtc, nowUtc)
                    ? PatreonRefreshOutcome.Refused
                    : PatreonRefreshOutcome.Unavailable;

            // A redirect we did not follow: no verdict either way.
            if (code >= 300) return PatreonRefreshOutcome.Unavailable;

            // 2xx with an OAuth error body (invalid_grant and friends) is a refusal in a 200.
            if (!string.IsNullOrWhiteSpace(oauthError)) return PatreonRefreshOutcome.Refused;

            return PatreonRefreshOutcome.Refreshed;
        }

        /// <summary>
        /// Whether a refresh error body says the grant itself is gone: the proxy's own
        /// <c>grant_dead</c> reason, or the provider's OAuth <c>invalid_grant</c> passed through.
        /// Shared by the Patreon and Discord refresh paths.
        /// </summary>
        public static bool BodySaysGrantDead(string? body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            return body.IndexOf("grant_dead", StringComparison.OrdinalIgnoreCase) >= 0
                || body.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The proxy's own "not the grant" reasons (CCP-Server #233).</summary>
        public static bool BodySaysProviderTrouble(string? body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            return body.IndexOf("provider_config", StringComparison.OrdinalIgnoreCase) >= 0
                || body.IndexOf("provider_busy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Whether this outcome should light the Reconnect row. Only a refusal does; everything
        /// else leaves the grant's health exactly as it was.
        /// </summary>
        public static bool MarksGrantDead(PatreonRefreshOutcome outcome)
            => outcome == PatreonRefreshOutcome.Refused;

        /// <summary>
        /// Every Patreon <c>StoreTokens</c> call stamps <c>DateTime.UtcNow.AddSeconds(...)</c>, but
        /// the value goes through JSON on the way back, so its Kind cannot be trusted. Unspecified
        /// is taken at its word as UTC (which is what <c>PatreonTokenData.IsExpired</c> already
        /// assumes); Local is converted.
        /// </summary>
        private static bool HasBeenStaleTooLong(DateTime? expiresAt, DateTime nowUtc)
        {
            if (expiresAt == null) return false;

            var expiry = expiresAt.Value.Kind == DateTimeKind.Local
                ? expiresAt.Value.ToUniversalTime()
                : DateTime.SpecifyKind(expiresAt.Value, DateTimeKind.Utc);

            return nowUtc - expiry > StaleAfter;
        }
    }

    /// <summary>
    /// Refresh tokens this session already heard refused. Checked before every
    /// <c>/patreon/refresh</c> and <c>/discord/refresh</c> call so a dead grant is asked about once
    /// per launch, not on every validate, 401 heal and re-validate that follows it.
    ///
    /// <para>In memory and keyed by a hash of the token itself: a reconnect stores a NEW refresh
    /// token, which is not on the list, so the repair is never blocked. Grants nothing and
    /// removes nothing; it only stops the same refused request going out again.</para>
    /// </summary>
    public static class DeadRefreshTokens
    {
        private static readonly System.Collections.Generic.HashSet<string> _dead = new(StringComparer.Ordinal);
        private static readonly object _gate = new();

        public static bool IsDead(string? refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return false;
            var key = Fingerprint(refreshToken);
            lock (_gate) return _dead.Contains(key);
        }

        public static void MarkDead(string? refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return;
            var key = Fingerprint(refreshToken);
            lock (_gate) _dead.Add(key);
        }

        internal static void ResetForTests()
        {
            lock (_gate) _dead.Clear();
        }

        private static string Fingerprint(string token)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(hash);
        }
    }
}
