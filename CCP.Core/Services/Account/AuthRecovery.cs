using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// THE 401 PATH (WPF ProfileSyncService.HandleUnauthorizedAsync / TryRecoverAuthTokenAsync, moved).
    /// A 401 is not "signed out": the token may have been rotated by another device or the server may
    /// have hiccuped. Two strategies, each on its own cooldown so a burst of 401s cannot spam the
    /// server: ask <c>/v2/auth/restore-session</c> whether the stored token still stands (30 s), then
    /// have a signed-in provider re-validate so the server re-issues one (2 min).
    ///
    /// <para>A FAILED RECOVERY KEEPS THE TOKEN. It may still be valid for other endpoints, and
    /// clearing it would turn one flaky answer into a signed-out app. Callers retry their request ONCE
    /// and only when <see cref="HandleUnauthorizedAsync"/> answered true and a token is in hand (#879:
    /// retrying with the same dead token only burns a round trip).</para>
    /// </summary>
    public static class AuthRecovery
    {
        private const string ServerUrl = "https://codebambi-proxy.vercel.app";
        public static readonly TimeSpan RestoreSessionCooldown = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ProviderRevalidateCooldown = TimeSpan.FromMinutes(2);

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static DateTime _lastRestoreSessionAttempt = DateTime.MinValue;
        private static DateTime _lastProviderRevalidateAttempt = DateTime.MinValue;
        private static HttpClient? _http;

        /// <summary>The head's provider re-validate: forces the signed-in provider (Patreon first, then
        /// Discord) to validate again, which makes the server re-issue the auth token. Returns after the
        /// attempt; this class decides success by whether the stored token changed. Null = no providers.</summary>
        public static volatile Func<Task<bool>>? ProviderRevalidate;

        /// <summary>A recovery succeeded (WPF restarts the heartbeat here).</summary>
        public static volatile Action? Recovered;

        /// <summary>Tests: the transport, the clock and a clean slate.</summary>
        internal static HttpMessageHandler? HandlerForTest;
        internal static Func<DateTime> Now = () => DateTime.UtcNow;
        internal static Func<Models.AppSettings>? SettingsForTest;
        private static Models.AppSettings Settings => SettingsForTest?.Invoke() ?? CoreSettings.Current;
        private static void Save() { if (SettingsForTest == null) CoreSettings.Save(suppressCloudBackup: true); }
        internal static void ResetForTest()
        {
            _lastRestoreSessionAttempt = _lastProviderRevalidateAttempt = DateTime.MinValue;
            _http = null;
            HandlerForTest = null;
            SettingsForTest = null;
            Now = () => DateTime.UtcNow;
        }

        private static HttpClient Http =>
            HandlerForTest != null ? new HttpClient(HandlerForTest, disposeHandler: false)
            : _http ??= V2AuthService.Configure(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });

        /// <summary>True only when the session was genuinely recovered. Anything but a 401 is false.</summary>
        public static async Task<bool> HandleUnauthorizedAsync(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.Unauthorized) return false;
            return await HandleUnauthorizedAsync().ConfigureAwait(false);
        }

        /// <summary>The same, for a caller that only holds the status code (the game net proxies).</summary>
        public static async Task<bool> HandleUnauthorizedAsync()
        {
            // One recovery at a time; waiters fall straight out on the cooldowns the winner claimed.
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                Log.Information("[Auth] 401 received - attempting token recovery");
                if (await TryRecoverAuthTokenAsync().ConfigureAwait(false))
                {
                    Log.Information("[Auth] Token recovered successfully");
                    try { Recovered?.Invoke(); } catch (Exception ex) { Log.Debug("[Auth] Recovered hook: {E}", ex.Message); }
                    return true;
                }
            }
            finally { Gate.Release(); }

            Log.Warning("[Auth] 401 - recovery failed or on cooldown, token kept for retry");
            return false;
        }

        private static async Task<bool> TryRecoverAuthTokenAsync()
        {
            if (string.IsNullOrEmpty(Settings.UnifiedId)) return false;

            if (TryClaimCooldown(ref _lastRestoreSessionAttempt, RestoreSessionCooldown)
                && await TryRestoreSessionAsync().ConfigureAwait(false))
                return true;

            if (TryClaimCooldown(ref _lastProviderRevalidateAttempt, ProviderRevalidateCooldown)
                && await TryProviderRevalidateAsync().ConfigureAwait(false))
                return true;

            return false;
        }

        private static bool TryClaimCooldown(ref DateTime lastAttempt, TimeSpan cooldown)
        {
            var now = Now();
            if (now - lastAttempt <= cooldown) return false;
            lastAttempt = now;
            return true;
        }

        /// <summary>WPF TryRestoreSessionAsync. Must not call HandleUnauthorizedAsync on its own reply (would recurse).</summary>
        private static async Task<bool> TryRestoreSessionAsync()
        {
            try
            {
                var settings = Settings;
                var unifiedId = settings.UnifiedId;
                var storedToken = settings.AuthToken;
                if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(storedToken)) return false;

                var body = JsonConvert.SerializeObject(new { unified_id = unifiedId, client_version = CoreReleaseContent.AppVersion });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/auth/restore-session");
                request.Headers.Add("X-Auth-Token", storedToken);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await Http.SendAsync(request).ConfigureAwait(false);

                // Contract D: the id being restored is a merge tombstone; there is no token to recover for it.
                if (await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false)) return false;

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("[Auth] restore-session failed: {Status}", response.StatusCode);
                    return false;
                }

                // The token is still valid on the server (the 401 was transient). The server does not
                // rotate here; if it ever sends a new one, adopt it.
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                string? newToken = null;
                try { newToken = JObject.Parse(json)["auth_token"]?.ToString(); } catch { /* an empty 200 is still a yes */ }
                if (!string.IsNullOrEmpty(newToken) && string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                {
                    settings.AuthToken = newToken;
                    Save();
                    Log.Information("[Auth] Auth token refreshed from restore-session");
                }
                else Log.Information("[Auth] restore-session confirmed token is still valid (transient 401)");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[Auth] restore-session recovery failed: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>WPF TryProviderRevalidateAsync: success is a DIFFERENT token on disk afterwards.</summary>
        private static async Task<bool> TryProviderRevalidateAsync()
        {
            var before = Settings.AuthToken;
            try
            {
                if (ProviderRevalidate is not { } revalidate || !await revalidate().ConfigureAwait(false))
                {
                    Log.Warning("[Auth] No provider session available to re-issue the auth token");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Auth] Provider re-validate failed: {Error}", ex.Message);
                return false;
            }

            var after = Settings.AuthToken;
            if (!string.IsNullOrEmpty(after) && after != before)
            {
                Log.Information("[Auth] Provider re-validate issued a fresh auth token");
                return true;
            }
            Log.Warning("[Auth] Provider re-validate did not issue a new auth token");
            return false;
        }
    }
}
