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
        private CancellationTokenSource? _oauthCts;
        private bool _disposed;

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
                _oauthCts = new CancellationTokenSource();

                // Core runs the flow (listener, authorize URL, state/PKCE, exchange, validate). The browser
                // opens through the robust launcher: on total failure it copies the link to the clipboard
                // and prompts the user (ccp-bugs #373/#374/#378/#404); the listener keeps waiting.
                await _core.SignInAsync(url => Helpers.BrowserLauncher.OpenUrlOrPrompt(url, "sign in with SubscribeStar"), _oauthCts.Token);

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
        }

        /// <summary>Cancel ongoing OAuth flow</summary>
        public void CancelOAuthFlow()
        {
            _oauthCts?.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _oauthCts?.Cancel();
            _oauthCts?.Dispose();
            _core.Dispose();
        }
    }
}
