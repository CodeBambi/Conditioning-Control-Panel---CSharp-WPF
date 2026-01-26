using System;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Patreon subscription tier levels
    /// </summary>
    public enum PatreonTier
    {
        /// <summary>Not subscribed or not authenticated</summary>
        None = 0,
        /// <summary>Level 1 ($5/mo) - AI Chatbot access</summary>
        Level1 = 1,
        /// <summary>Level 2 ($10/mo) - AI Chatbot + Window Awareness</summary>
        Level2 = 2
    }

    /// <summary>
    /// Token data stored securely via DPAPI
    /// </summary>
    public class PatreonTokenData
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonProperty("expires_at")]
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Check if the access token has expired
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// OAuth token response from hosted proxy
    /// </summary>
    public class PatreonTokenResponse
    {
        [JsonProperty("access_token")]
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonProperty("refresh_token")]
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonProperty("expires_in")]
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("token_type")]
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";

        [JsonProperty("error")]
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonProperty("error_description")]
        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    /// <summary>
    /// Subscription validation response from hosted proxy
    /// </summary>
    public class PatreonSubscriptionResponse
    {
        [JsonProperty("is_active")]
        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }

        [JsonProperty("tier")]
        [JsonPropertyName("tier")]
        public PatreonTier Tier { get; set; }

        [JsonProperty("next_billing_date")]
        [JsonPropertyName("next_billing_date")]
        public DateTime? NextBillingDate { get; set; }

        [JsonProperty("patreon_user_id")]
        [JsonPropertyName("patreon_user_id")]
        public string? PatreonUserId { get; set; }

        [JsonProperty("patron_name")]
        [JsonPropertyName("patron_name")]
        public string? PatronName { get; set; }

        [JsonProperty("patron_email")]
        [JsonPropertyName("patron_email")]
        public string? PatronEmail { get; set; }

        [JsonProperty("display_name")]
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        /// <summary>
        /// Whether the user is on the server-side whitelist
        /// </summary>
        [JsonProperty("is_whitelisted")]
        [JsonPropertyName("is_whitelisted")]
        public bool IsWhitelisted { get; set; }

        /// <summary>
        /// Unified user ID for cross-provider account linking
        /// </summary>
        [JsonProperty("unified_id")]
        [JsonPropertyName("unified_id")]
        public string? UnifiedId { get; set; }

        /// <summary>
        /// Whether this user needs to register (choose display name)
        /// </summary>
        [JsonProperty("needs_registration")]
        [JsonPropertyName("needs_registration")]
        public bool NeedsRegistration { get; set; }

        [JsonProperty("error")]
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    /// <summary>
    /// Cached subscription state for 24-hour cache
    /// </summary>
    public class PatreonCachedState
    {
        [JsonProperty("tier")]
        public PatreonTier Tier { get; set; }

        [JsonProperty("is_active")]
        public bool IsActive { get; set; }

        [JsonProperty("last_verified")]
        public DateTime LastVerified { get; set; }

        [JsonProperty("cache_expires_at")]
        public DateTime CacheExpiresAt { get; set; }

        [JsonProperty("patron_name")]
        public string? PatronName { get; set; }

        [JsonProperty("patron_email")]
        public string? PatronEmail { get; set; }

        /// <summary>
        /// Custom display name chosen by user on first login (can only be set once)
        /// </summary>
        [JsonProperty("display_name")]
        public string? DisplayName { get; set; }

        /// <summary>
        /// Whether the user is whitelisted (from server)
        /// </summary>
        [JsonProperty("is_whitelisted")]
        public bool IsWhitelisted { get; set; }

        /// <summary>
        /// Unified user ID for cross-provider account linking
        /// </summary>
        [JsonProperty("unified_id")]
        public string? UnifiedId { get; set; }

        /// <summary>
        /// Check if the cache has expired (older than 24 hours)
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsExpired => DateTime.UtcNow >= CacheExpiresAt;
    }

    /// <summary>
    /// AI chat request to hosted proxy
    /// </summary>
    public class ProxyChatRequest
    {
        [JsonProperty("messages")]
        public ProxyChatMessage[] Messages { get; set; } = Array.Empty<ProxyChatMessage>();

        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; } = 60;

        [JsonProperty("temperature")]
        public double Temperature { get; set; } = 0.9;
    }

    /// <summary>
    /// Chat message for proxy request
    /// </summary>
    public class ProxyChatMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; } = string.Empty;

        [JsonProperty("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// AI chat response from hosted proxy (legacy)
    /// </summary>
    public class ProxyChatResponse
    {
        [JsonProperty("content")]
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonProperty("error")]
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonProperty("requests_remaining")]
        [JsonPropertyName("requests_remaining")]
        public int? RequestsRemaining { get; set; }
    }

    // ============================================================
    // OpenRouter API Models
    // ============================================================

    /// <summary>
    /// OpenRouter chat completion request (OpenAI-compatible)
    /// </summary>
    public class OpenRouterChatRequest
    {
        [JsonProperty("model")]
        [JsonPropertyName("model")]
        public string Model { get; set; } = "anthropic/claude-3.5-sonnet";

        [JsonProperty("messages")]
        [JsonPropertyName("messages")]
        public OpenRouterMessage[] Messages { get; set; } = Array.Empty<OpenRouterMessage>();

        [JsonProperty("max_tokens")]
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 100;

        [JsonProperty("temperature")]
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.9;
    }

    /// <summary>
    /// OpenRouter chat message
    /// </summary>
    public class OpenRouterMessage
    {
        [JsonProperty("role")]
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonProperty("content")]
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// OpenRouter chat completion response
    /// </summary>
    public class OpenRouterChatResponse
    {
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonProperty("choices")]
        [JsonPropertyName("choices")]
        public OpenRouterChoice[]? Choices { get; set; }

        [JsonProperty("error")]
        [JsonPropertyName("error")]
        public OpenRouterError? Error { get; set; }
    }

    /// <summary>
    /// OpenRouter response choice
    /// </summary>
    public class OpenRouterChoice
    {
        [JsonProperty("message")]
        [JsonPropertyName("message")]
        public OpenRouterMessage? Message { get; set; }

        [JsonProperty("finish_reason")]
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    /// <summary>
    /// OpenRouter error response
    /// </summary>
    public class OpenRouterError
    {
        [JsonProperty("message")]
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonProperty("code")]
        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }
}
