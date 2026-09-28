using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles SubscribeStar OAuth authentication and subscription validation.
    ///
    /// Mirrors <see cref="PatreonService"/> but with one structural difference:
    /// SubscribeStar's OAuth client form REQUIRES an https:// redirect URL, so we
    /// cannot register a raw http://localhost loopback the way Patreon does. Instead
    /// the redirect target is the proxy's https callback
    /// (https://codebambi-proxy.vercel.app/substar/callback), which stores no token: it
    /// binds the code to the state's PKCE challenge and 302-redirects the browser to our
    /// local listener (http://localhost:47834/callback/?code=...&state=...). We then POST
    /// /substar/token with the code and our verifier; the proxy adds the client_secret. Everything else (validate / refresh / cache / grace / storage) is Core's
    /// <see cref="ProviderSubscription"/>, shared with Patreon and every head: the proxy returns
    /// identical response shapes and SubscribeStar tiers map 1:1 onto Patreon tiers.
    /// </summary>
    public class SubscribeStarService : IDisposable
    {
        private readonly ProviderSubscription _core = new("substar");
        private LoopbackOAuth? _callbackListener;
        private CancellationTokenSource? _oauthCts;
        private bool _disposed;

        // Same hosted proxy as Patreon; SubscribeStar gets its own endpoints + port.
        private const string ProxyBaseUrl = ProviderSubscription.ProxyBaseUrl;
        private const int LocalCallbackPort = 47834; // Patreon=47832, Discord=47833
        private const int OAuthTimeoutMinutes = 5;

        public SubscribeStarService() => _core.TierChanged += (_, tier) => TierChanged?.Invoke(this, tier);

        /// <summary>The shared lifecycle behind this service (read by the canonical gates).</summary>
        public ProviderSubscription Core => _core;

        public event EventHandler<PatreonTier>? TierChanged;
        public event EventHandler<string>? AuthenticationFailed;

        public PatreonTier CurrentTier => _core.CurrentTier;
        public bool IsAuthenticated => _core.IsAuthenticated;
        public bool IsActiveSubscriber => _core.IsActive;
        public bool IsVerifying => _core.IsVerifying;
        public string? DisplayName { get => _core.DisplayName; set => _core.DisplayName = value; }
        public string? UnifiedUserId { get => _core.UnifiedUserId; set => _core.UnifiedUserId = value; }
        public bool IsWhitelisted => _core.IsWhitelisted;

        public Task InitializeAsync() => _core.InitializeAsync();
        public Task<PatreonTier> ValidateSubscriptionAsync(bool forceRefresh = false) => _core.ValidateSubscriptionAsync(forceRefresh);
        public string? GetAccessToken() => _core.GetAccessToken();
        public void Logout() => _core.Logout();

        /// <summary>
        /// Start OAuth2 browser flow (proxy-bridged — see class summary).
        /// </summary>
        public async Task StartOAuthFlowAsync()
        {
            if (IsVerifying) return;

            try
            {
                _core.IsVerifying = true;
                _oauthCts = new CancellationTokenSource();

                // Local callback listener for the proxy's final redirect + CSRF state
                // (Core LoopbackOAuth, shared with every head)
                _callbackListener = new LoopbackOAuth(LocalCallbackPort);
                var callbackUrl = _callbackListener.CallbackUrl;
                var state = _callbackListener.State;

                // PKCE: the proxy binds the code to this state's challenge, and only the holder of
                // the verifier can trade it. A consent link started by someone else is useless to them.
                var verifier = Chaster.ChasterClient.NewVerifier();
                var challenge = Chaster.ChasterClient.Challenge(verifier);

                App.Logger?.Information("Started SubscribeStar OAuth callback listener on {Url}", callbackUrl);

                // Open browser to the proxy authorize endpoint. The proxy redirects to
                // SubscribeStar using its registered https redirect_uri, so we do NOT
                // pass redirect_uri here (only the CSRF state, which round-trips, and the PKCE challenge).
                var authUrl = $"{ProxyBaseUrl}/substar/authorize?state={state}&code_challenge={challenge}&code_challenge_method=S256";

                // Robust open with fallbacks; on total failure copies the link to the clipboard
                // and prompts the user (machines with no default browser otherwise fail silently —
                // see ccp-bugs #404). The callback listener keeps waiting in the meantime.
                Helpers.BrowserLauncher.OpenUrlOrPrompt(authUrl, "sign in with SubscribeStar");

                // Wait for callback with timeout; answers the browser and validates state (CSRF)
                var query = await _callbackListener.WaitAsync(TimeSpan.FromMinutes(OAuthTimeoutMinutes),
                    "OAuth login timed out. Please try again.", LoopbackOAuth.SuccessHtml, LoopbackOAuth.FailureHtml, _oauthCts.Token);
                var code = query["code"];
                var error = query["error"];

                if (!string.IsNullOrEmpty(error))
                {
                    throw new Exception($"SubscribeStar authorization failed: {error}");
                }

                if (string.IsNullOrEmpty(code))
                {
                    throw new Exception("SubscribeStar sign-in returned no code. Please try again.");
                }

                // The proxy holds the client_secret; it trades the code only against our verifier.
                await _core.ExchangeCodeAsync(new { code, state, code_verifier = verifier });

                // Validate subscription immediately
                await ValidateSubscriptionAsync(forceRefresh: true);

                App.Logger?.Information("SubscribeStar OAuth flow completed successfully");
            }
            catch (OperationCanceledException)
            {
                App.Logger?.Information("SubscribeStar OAuth flow cancelled");
                throw;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "SubscribeStar OAuth flow failed");
                AuthenticationFailed?.Invoke(this, ex.Message);
                throw;
            }
            finally
            {
                _core.IsVerifying = false;
                StopCallbackListener();
            }
        }

        /// <summary>Cancel ongoing OAuth flow</summary>
        public void CancelOAuthFlow()
        {
            _oauthCts?.Cancel();
            StopCallbackListener();
        }

        private void StopCallbackListener()
        {
            try
            {
                _callbackListener?.Dispose();
                _callbackListener = null;
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _oauthCts?.Cancel();
            _oauthCts?.Dispose();
            StopCallbackListener();
            _core.Dispose();
        }
    }
}
