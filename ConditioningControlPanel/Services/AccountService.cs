using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Centralized service for unified account management across Patreon and Discord.
/// Handles the unified login flow: lookup -> register/link -> profile sync.
/// </summary>
public static class AccountService
{
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Handle post-authentication flow for any provider.
    /// This is called after OAuth completes to check for existing account and prompt for registration if needed.
    /// Returns true if login flow completed successfully, false if user cancelled.
    /// </summary>
    public static async Task<bool> HandlePostAuthAsync(Window owner, string provider)
    {
        try
        {
            App.Logger?.Information("AccountService: Handling post-auth for {Provider}", provider);

            // Get the access token from the appropriate service
            var accessToken = provider == "patreon"
                ? App.Patreon?.GetAccessToken()
                : App.Discord?.GetAccessToken();

            if (string.IsNullOrEmpty(accessToken))
            {
                App.Logger?.Warning("AccountService: No access token available for {Provider}", provider);
                return false;
            }

            // Check if another provider is already logged in with a unified account
            var otherProvider = provider == "patreon" ? "discord" : "patreon";

            // Only consider existing login if the OTHER provider is actually authenticated
            var otherProviderIsAuthenticated = provider == "patreon"
                ? App.Discord?.IsAuthenticated == true
                : App.Patreon?.IsAuthenticated == true;

            var existingUnifiedId = otherProviderIsAuthenticated ? App.UnifiedUserId : null;
            var existingDisplayName = otherProviderIsAuthenticated ? App.UserDisplayName : null;

            // Step 1: Look up existing unified account for this provider
            var lookupResult = await LookupAccountAsync(provider, accessToken);

            if (lookupResult == null)
            {
                App.Logger?.Warning("AccountService: Lookup failed for {Provider}", provider);
                return false;
            }

            // Step 2: If user is already logged in with another provider, handle linking
            if (!string.IsNullOrEmpty(existingUnifiedId) && otherProviderIsAuthenticated)
            {
                // Case A: This provider is already linked to the SAME unified account - perfect!
                if (lookupResult.Exists && lookupResult.UnifiedId == existingUnifiedId)
                {
                    App.Logger?.Information("AccountService: {Provider} already linked to same account {UnifiedId}",
                        provider, existingUnifiedId);

                    // Update service properties
                    if (provider == "patreon" && App.Patreon != null)
                    {
                        App.Patreon.UnifiedUserId = existingUnifiedId;
                        App.Patreon.DisplayName = existingDisplayName;
                    }
                    else if (provider == "discord" && App.Discord != null)
                    {
                        App.Discord.UnifiedUserId = existingUnifiedId;
                        App.Discord.CustomDisplayName = existingDisplayName;
                    }

                    return true;
                }

                // Case B: This provider is linked to a DIFFERENT unified account - REJECT!
                if (lookupResult.Exists && lookupResult.UnifiedId != existingUnifiedId)
                {
                    App.Logger?.Warning("AccountService: Conflict! {Provider} is linked to \"{NewName}\" but already logged in as \"{ExistingName}\" via {OtherProvider}. Rejecting.",
                        provider, lookupResult.DisplayName, existingDisplayName, otherProvider);

                    // Show error and logout this provider - we don't allow conflicting accounts
                    MessageBox.Show(
                        owner,
                        $"This {provider} account is already linked to a different profile (\"{lookupResult.DisplayName}\").\n\n" +
                        $"You are currently logged in as \"{existingDisplayName}\" via {otherProvider}.\n\n" +
                        $"You cannot link a {provider} account that belongs to someone else.\n\n" +
                        $"If you want to use \"{lookupResult.DisplayName}\" instead, please logout of {otherProvider} first.",
                        "Cannot Link Account",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // Logout the conflicting provider
                    if (provider == "patreon")
                        App.Patreon?.Logout();
                    else
                        App.Discord?.Logout();

                    return false;
                }

                // Case C: This provider is NOT linked to any account - auto-link to existing
                if (!lookupResult.Exists)
                {
                    App.Logger?.Information("AccountService: Auto-linking {Provider} to existing account {UnifiedId}",
                        provider, existingUnifiedId);

                    var linkResult = await LinkProviderAsync(provider, accessToken, existingUnifiedId);
                    if (linkResult.Success)
                    {
                        if (provider == "patreon" && App.Patreon != null)
                        {
                            App.Patreon.UnifiedUserId = existingUnifiedId;
                            App.Patreon.DisplayName = existingDisplayName;
                        }
                        else if (provider == "discord" && App.Discord != null)
                        {
                            App.Discord.UnifiedUserId = existingUnifiedId;
                            App.Discord.CustomDisplayName = existingDisplayName;
                        }

                        App.Logger?.Information("AccountService: Successfully linked {Provider} to {UnifiedId}", provider, existingUnifiedId);
                        return true;
                    }
                }
            }

            // Step 3: No other provider logged in - check if this provider has existing account
            if (lookupResult.Exists && lookupResult.HasDisplayName)
            {
                // Returning user - NO prompt needed
                App.UnifiedUserId = lookupResult.UnifiedId;

                // Update service properties
                if (provider == "patreon" && App.Patreon != null)
                {
                    App.Patreon.UnifiedUserId = lookupResult.UnifiedId;
                    App.Patreon.DisplayName = lookupResult.DisplayName;
                }
                else if (provider == "discord" && App.Discord != null)
                {
                    App.Discord.UnifiedUserId = lookupResult.UnifiedId;
                    App.Discord.CustomDisplayName = lookupResult.DisplayName;
                }

                App.Logger?.Information("AccountService: Returning user found - {DisplayName} ({UnifiedId})",
                    lookupResult.DisplayName, lookupResult.UnifiedId);

                // Load profile and start sync
                await App.ProfileSync?.LoadProfileAsync();
                App.ProfileSync?.StartHeartbeat();

                return true;
            }

            // Step 4: Check for auto-link opportunity (same email as existing account)
            if (lookupResult.CanAutoLink && !string.IsNullOrEmpty(lookupResult.AutoLinkUnifiedId))
            {
                App.Logger?.Information("AccountService: Auto-link opportunity found for {Provider} -> {DisplayName}",
                    provider, lookupResult.AutoLinkDisplayName);

                // Auto-link the provider to existing account
                var linkResult = await LinkProviderAsync(provider, accessToken, lookupResult.AutoLinkUnifiedId);

                if (linkResult.Success)
                {
                    App.UnifiedUserId = linkResult.UnifiedId;

                    if (provider == "patreon" && App.Patreon != null)
                    {
                        App.Patreon.UnifiedUserId = linkResult.UnifiedId;
                        App.Patreon.DisplayName = lookupResult.AutoLinkDisplayName;
                    }
                    else if (provider == "discord" && App.Discord != null)
                    {
                        App.Discord.UnifiedUserId = linkResult.UnifiedId;
                        App.Discord.CustomDisplayName = lookupResult.AutoLinkDisplayName;
                    }

                    App.Logger?.Information("AccountService: Auto-linked {Provider} to {UnifiedId}", provider, linkResult.UnifiedId);

                    await App.ProfileSync?.LoadProfileAsync();
                    App.ProfileSync?.StartHeartbeat();

                    return true;
                }
            }

            // Step 5: First-time user - needs to choose display name
            return await PromptForRegistrationAsync(owner, provider, accessToken, lookupResult);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "AccountService: HandlePostAuthAsync failed for {Provider}", provider);
            return false;
        }
    }

    /// <summary>
    /// Look up if a provider account is linked to a unified user
    /// </summary>
    private static async Task<LookupResult?> LookupAccountAsync(string provider, string accessToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/auth/lookup")
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(new { provider }),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                App.Logger?.Warning("AccountService: Lookup failed - {Status}: {Body}", response.StatusCode, json);
                return null;
            }

            var data = JObject.Parse(json);

            return new LookupResult
            {
                Exists = data["exists"]?.Value<bool>() ?? false,
                UnifiedId = data["unified_id"]?.Value<string>(),
                DisplayName = data["display_name"]?.Value<string>(),
                HasDisplayName = data["has_display_name"]?.Value<bool>() ?? false,
                NeedsRegistration = data["needs_registration"]?.Value<bool>() ?? true,
                CanAutoLink = data["can_auto_link"]?.Value<bool>() ?? false,
                AutoLinkUnifiedId = data["auto_link_unified_id"]?.Value<string>(),
                AutoLinkDisplayName = data["auto_link_display_name"]?.Value<string>(),
                ProviderData = data["provider_data"] as JObject
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "AccountService: LookupAccountAsync failed");
            return null;
        }
    }

    /// <summary>
    /// Register a new unified user with a display name
    /// </summary>
    private static async Task<RegisterResult> RegisterUserAsync(string provider, string accessToken, string displayName)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/auth/register")
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(new { display_name = displayName, provider }),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);

            if (response.IsSuccessStatusCode && data["success"]?.Value<bool>() == true)
            {
                return new RegisterResult
                {
                    Success = true,
                    UnifiedId = data["unified_id"]?.Value<string>(),
                    DisplayName = data["display_name"]?.Value<string>()
                };
            }

            return new RegisterResult
            {
                Success = false,
                Error = data["error"]?.Value<string>() ?? "Registration failed",
                CanClaim = data["can_claim"]?.Value<bool>() ?? false,
                ExistingUnifiedId = data["existing_unified_id"]?.Value<string>()
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "AccountService: RegisterUserAsync failed");
            return new RegisterResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Link a provider to an existing unified user
    /// </summary>
    private static async Task<LinkResult> LinkProviderAsync(string provider, string accessToken, string unifiedId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/auth/link-provider")
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(new { unified_id = unifiedId, provider }),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);

            if (response.IsSuccessStatusCode && data["success"]?.Value<bool>() == true)
            {
                return new LinkResult
                {
                    Success = true,
                    UnifiedId = data["unified_id"]?.Value<string>()
                };
            }

            return new LinkResult
            {
                Success = false,
                Error = data["error"]?.Value<string>() ?? "Linking failed"
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "AccountService: LinkProviderAsync failed");
            return new LinkResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Claim an existing display name (for account merging)
    /// </summary>
    private static async Task<LinkResult> ClaimAccountAsync(string provider, string accessToken, string displayName)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/auth/claim")
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(new { display_name = displayName, provider }),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);

            if (response.IsSuccessStatusCode && data["success"]?.Value<bool>() == true)
            {
                return new LinkResult
                {
                    Success = true,
                    UnifiedId = data["unified_id"]?.Value<string>()
                };
            }

            return new LinkResult
            {
                Success = false,
                Error = data["error"]?.Value<string>() ?? "Claim failed"
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "AccountService: ClaimAccountAsync failed");
            return new LinkResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Prompt the user to choose a display name for registration
    /// </summary>
    private static async Task<bool> PromptForRegistrationAsync(Window owner, string provider, string accessToken, LookupResult lookupResult)
    {
        bool nameSet = false;

        while (!nameSet)
        {
            // Show display name dialog
            var dialog = new DisplayNameDialog
            {
                Owner = owner,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.Activated += (s, args) => dialog.Topmost = false;

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.DisplayName))
            {
                var trimmedName = dialog.DisplayName.Trim();

                // Try to register with this name
                var result = await RegisterUserAsync(provider, accessToken, trimmedName);

                if (result.Success)
                {
                    App.UnifiedUserId = result.UnifiedId;

                    if (provider == "patreon" && App.Patreon != null)
                    {
                        App.Patreon.UnifiedUserId = result.UnifiedId;
                        App.Patreon.DisplayName = result.DisplayName;
                    }
                    else if (provider == "discord" && App.Discord != null)
                    {
                        App.Discord.UnifiedUserId = result.UnifiedId;
                        App.Discord.CustomDisplayName = result.DisplayName;
                    }

                    App.Logger?.Information("AccountService: Registered new user {DisplayName} ({UnifiedId})",
                        result.DisplayName, result.UnifiedId);

                    nameSet = true;

                    await App.ProfileSync?.LoadProfileAsync();
                    App.ProfileSync?.StartHeartbeat();
                }
                else if (result.CanClaim)
                {
                    // Name belongs to another provider account - offer to claim/link
                    var claimResult = MessageBox.Show(
                        owner,
                        $"The name \"{trimmedName}\" belongs to an existing account.\n\n" +
                        "If this is your account from another login method, click Yes to link them.\n\n" +
                        "This will merge your progress and allow you to login with either method.",
                        "Link Existing Account?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (claimResult == MessageBoxResult.Yes)
                    {
                        var claimResponse = await ClaimAccountAsync(provider, accessToken, trimmedName);

                        if (claimResponse.Success)
                        {
                            App.UnifiedUserId = claimResponse.UnifiedId;

                            if (provider == "patreon" && App.Patreon != null)
                            {
                                App.Patreon.UnifiedUserId = claimResponse.UnifiedId;
                                App.Patreon.DisplayName = trimmedName;
                            }
                            else if (provider == "discord" && App.Discord != null)
                            {
                                App.Discord.UnifiedUserId = claimResponse.UnifiedId;
                                App.Discord.CustomDisplayName = trimmedName;
                            }

                            App.Logger?.Information("AccountService: Claimed account {DisplayName} ({UnifiedId})",
                                trimmedName, claimResponse.UnifiedId);

                            nameSet = true;

                            MessageBox.Show(owner, "Accounts linked successfully!", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                            await App.ProfileSync?.LoadProfileAsync();
                            App.ProfileSync?.StartHeartbeat();
                        }
                        else
                        {
                            MessageBox.Show(owner,
                                claimResponse.Error ?? "Failed to link accounts.\n\nMake sure you're using the same email on both platforms.",
                                "Link Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }
                else
                {
                    // Name is taken by someone else
                    MessageBox.Show(owner,
                        result.Error ?? "This name is already taken. Please choose another.",
                        "Name Unavailable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                // User cancelled
                App.Logger?.Information("AccountService: User cancelled registration");
                return false;
            }
        }

        return true;
    }

    #region Result Classes

    private class LookupResult
    {
        public bool Exists { get; set; }
        public string? UnifiedId { get; set; }
        public string? DisplayName { get; set; }
        public bool HasDisplayName { get; set; }
        public bool NeedsRegistration { get; set; }
        public bool CanAutoLink { get; set; }
        public string? AutoLinkUnifiedId { get; set; }
        public string? AutoLinkDisplayName { get; set; }
        public JObject? ProviderData { get; set; }
    }

    private class RegisterResult
    {
        public bool Success { get; set; }
        public string? UnifiedId { get; set; }
        public string? DisplayName { get; set; }
        public string? Error { get; set; }
        public bool CanClaim { get; set; }
        public string? ExistingUnifiedId { get; set; }
    }

    private class LinkResult
    {
        public bool Success { get; set; }
        public string? UnifiedId { get; set; }
        public string? Error { get; set; }
    }

    #endregion
}
