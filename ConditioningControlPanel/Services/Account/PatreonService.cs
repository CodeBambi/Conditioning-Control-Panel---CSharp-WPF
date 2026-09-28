using System;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Patreon for the WPF head: the OAuth browser flow and the display-name calls. The token
    /// lifecycle (exchange, refresh, validate, tier, whitelist, cache, grace, GrantLooksDead) is
    /// Core's <see cref="ProviderSubscription"/>, shared with every head; tokens stay in WPF's own
    /// SecureTokenStorage files, reached through CoreSecrets.
    /// </summary>
    public class PatreonService : IDisposable
    {
        private readonly ProviderSubscription _core = new("patreon")
        {
            PeerDisplayName = () => App.Discord?.CustomDisplayName
        };
        private CancellationTokenSource? _oauthCts;
        private bool _disposed;


        public PatreonService() => _core.TierChanged += (_, tier) => TierChanged?.Invoke(this, tier);

        /// <summary>The shared lifecycle behind this service (read by the SubscribeStar-folding gates).</summary>
        public ProviderSubscription Core => _core;

        public event EventHandler<PatreonTier>? TierChanged;
        public event EventHandler<string>? AuthenticationFailed;

        public PatreonTier CurrentTier => _core.CurrentTier;
        public bool IsAuthenticated => _core.IsAuthenticated;
        /// <summary>See <see cref="ProviderSubscription.GrantLooksDead"/>. Read by PatreonReconnectRule only.</summary>
        public bool GrantLooksDead => _core.GrantLooksDead;
        public bool IsActivePatron => _core.IsActive;
        public bool IsVerifying => _core.IsVerifying;
        public string? DisplayName { get => _core.DisplayName; set => _core.DisplayName = value; }
        public string? UnifiedUserId { get => _core.UnifiedUserId; set => _core.UnifiedUserId = value; }
        public bool NeedsDisplayNameMigration { get => _core.NeedsDisplayNameMigration; private set => _core.NeedsDisplayNameMigration = value; }
        public bool NeedsRegistration => _core.NeedsRegistration;

        /// <summary>No display name yet on ANY provider (Discord's counts, to avoid re-prompting).</summary>
        public bool IsFirstLogin => IsAuthenticated
            && string.IsNullOrEmpty(DisplayName)
            && string.IsNullOrEmpty(App.Discord?.CustomDisplayName);

        public bool IsWhitelisted => _core.IsWhitelisted;

        public void SetWhitelistStatus(bool whitelisted, PatreonTier minTier = PatreonTier.Level2)
            => _core.SetWhitelistStatus(whitelisted, minTier);

        /// <summary>
        /// A server-confirmed tier rise that did not come through Patreon OAuth (see
        /// EntitlementTierSync). Only tells every TierChanged subscriber to repaint. UI thread.
        /// </summary>
        public void NotifyEntitlementRaised(PatreonTier tier) => TierChanged?.Invoke(this, tier);

        /// <summary>The canonical AI gate; SubscribeStar and the offline grace are folded in.</summary>
        public bool HasAiAccess => HasPremiumAccess;

        /// <summary>The canonical premium (tier 1) gate: see <see cref="ProviderSubscription.HasPremiumAccess"/>.</summary>
        public bool HasPremiumAccess => ProviderSubscription.HasPremiumAccess(_core, App.SubscribeStar?.Core, App.Settings?.Current);

        /// <summary>
        /// The goon-game HOST bar (tier 2+ or whitelisted, either provider, or the Lab's own 14-day
        /// grace). Advisory: the server refuses on its own. See <see cref="ProviderSubscription.HasLabAccess"/>.
        /// </summary>
        public bool HasLabAccess => ProviderSubscription.HasLabAccess(_core, App.SubscribeStar?.Core, App.Settings?.Current);

        /// <summary>False until <see cref="InitializeAsync"/> settled this launch (#1048).</summary>
        public bool EntitlementResolved => _core.EntitlementResolved;

        public Task InitializeAsync() => _core.InitializeAsync();

        public Task<PatreonTier> ValidateSubscriptionAsync(bool forceRefresh = false) => _core.ValidateSubscriptionAsync(forceRefresh);

        public string? GetAccessToken() => _core.GetAccessToken();

        public void Logout() => _core.Logout();

        /// <summary>
        /// Start OAuth2 browser flow
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
                await _core.SignInAsync(url => Helpers.BrowserLauncher.OpenUrlOrPrompt(url, "sign in with Patreon"), _oauthCts.Token);

                App.Logger?.Information("Patreon OAuth flow completed successfully");
            }
            catch (OperationCanceledException)
            {
                App.Logger?.Information("Patreon OAuth flow cancelled");
                throw;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Patreon OAuth flow failed");
                AuthenticationFailed?.Invoke(this, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Cancel ongoing OAuth flow
        /// </summary>
        public void CancelOAuthFlow()
        {
            _oauthCts?.Cancel();
        }

        /// <summary>
        /// Set the user's display name (can only be set once) and save to server
        /// </summary>
        /// <returns>True if successful, false if name is taken or other error</returns>
        public async Task<(bool Success, string? Error)> SetDisplayNameAsync(string displayName)
        {
            if (!string.IsNullOrEmpty(DisplayName))
            {
                App.Logger?.Warning("Attempted to change display name, but it's already set");
                return (false, "Display name is already set");
            }

            var trimmedName = displayName.Trim();

            // Check if name is already taken on server
            var checkResult = await CheckDisplayNameAvailableAsync(trimmedName);
            if (!checkResult.Available)
            {
                App.Logger?.Warning("Display name is already taken ({Chars} chars)", trimmedName.Length);
                return (false, checkResult.Error ?? "This name is already taken. Please choose another.");
            }

            DisplayName = trimmedName;

            // Update the cached state with the new display name
            var cachedState = _core.RetrieveCachedState();
            if (cachedState != null)
            {
                cachedState.DisplayName = DisplayName;
                _core.StoreCachedState(cachedState);
            }

            // Save to server so it syncs across devices
            await SaveDisplayNameToServerAsync(DisplayName);

            App.Logger?.Information("Display name set ({Chars} chars)", DisplayName?.Length ?? 0);
            NeedsDisplayNameMigration = false;
            return (true, null);
        }

        /// <summary>
        /// Migrate existing local display name to server (for legacy users).
        /// Returns success if migrated, or false if name is taken and user needs to pick a new one.
        /// </summary>
        public async Task<(bool Success, string? Error)> TryMigrateDisplayNameAsync()
        {
            if (!NeedsDisplayNameMigration || string.IsNullOrEmpty(DisplayName))
            {
                return (true, null); // Nothing to migrate
            }

            App.Logger?.Information("Attempting to migrate local display name to server ({Chars} chars)", DisplayName?.Length ?? 0);

            // Check if the name is available
            var checkResult = await CheckDisplayNameAvailableAsync(DisplayName);
            if (!checkResult.Available)
            {
                App.Logger?.Warning("Migration failed - display name is already taken ({Chars} chars)", DisplayName?.Length ?? 0);
                // Clear the local name so user can pick a new one
                DisplayName = null;
                var cachedState = _core.RetrieveCachedState();
                if (cachedState != null)
                {
                    cachedState.DisplayName = null;
                    _core.StoreCachedState(cachedState);
                }
                NeedsDisplayNameMigration = false;
                return (false, checkResult.Error ?? "This name is already taken by another user. Please choose a different name.");
            }

            // Name is available - sync to server
            await SaveDisplayNameToServerAsync(DisplayName);
            NeedsDisplayNameMigration = false;
            App.Logger?.Information("Successfully migrated display name to server");
            return (true, null);
        }

        /// <summary>
        /// Check if a display name is available (not already taken)
        /// </summary>
        public async Task<(bool Available, string? Error)> CheckDisplayNameAvailableAsync(string displayName)
        {
            try
            {
                var tokens = _core.RetrieveTokens();
                if (tokens == null)
                {
                    return (true, null); // Can't check, allow optimistically
                }

                _core.Http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _core.Http.GetAsync($"/user/check-display-name?name={Uri.EscapeDataString(displayName)}");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<DisplayNameCheckResult>();
                    return (result?.Available ?? true, result?.Error);
                }

                // If endpoint doesn't exist or errors, allow optimistically
                return (true, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to check display name availability");
                return (true, null); // Allow optimistically on error
            }
        }

        private class DisplayNameCheckResult
        {
            public bool Available { get; set; }
            public string? Error { get; set; }
        }

        /// <summary>
        /// Save display name to the server for cross-device sync
        /// </summary>
        private async Task SaveDisplayNameToServerAsync(string displayName)
        {
            try
            {
                var tokens = _core.RetrieveTokens();
                if (tokens == null)
                {
                    App.Logger?.Warning("Cannot save display name: no tokens available");
                    return;
                }

                _core.Http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _core.Http.PostAsJsonAsync("/user/set-display-name", new
                {
                    display_name = displayName
                });

                if (response.IsSuccessStatusCode)
                {
                    App.Logger?.Information("Display name saved to server successfully");
                }
                else
                {
                    App.Logger?.Warning("Failed to save display name to server: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to save display name to server");
                // Don't throw - local save was successful, server sync is best-effort
            }
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
