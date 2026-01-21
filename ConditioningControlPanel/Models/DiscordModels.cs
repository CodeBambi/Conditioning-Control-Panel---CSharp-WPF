using System;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Discord OAuth token data stored securely via DPAPI
    /// </summary>
    public class DiscordTokenData
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
    /// OAuth token response from Discord (via proxy)
    /// </summary>
    public class DiscordTokenResponse
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

        [JsonProperty("scope")]
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonProperty("error")]
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonProperty("error_description")]
        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    /// <summary>
    /// Discord user info from validation endpoint
    /// </summary>
    public class DiscordUserResponse
    {
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("username")]
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonProperty("discriminator")]
        [JsonPropertyName("discriminator")]
        public string Discriminator { get; set; } = string.Empty;

        [JsonProperty("global_name")]
        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }

        [JsonProperty("avatar")]
        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonProperty("email")]
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonProperty("verified")]
        [JsonPropertyName("verified")]
        public bool Verified { get; set; }

        [JsonProperty("error")]
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>
        /// Get the display name (global_name if set, otherwise username)
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayName => !string.IsNullOrEmpty(GlobalName) ? GlobalName : Username;

        /// <summary>
        /// Get the avatar URL
        /// </summary>
        public string? GetAvatarUrl(int size = 128)
        {
            if (string.IsNullOrEmpty(Avatar))
                return null;

            var extension = Avatar.StartsWith("a_") ? "gif" : "png";
            return $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.{extension}?size={size}";
        }
    }

    /// <summary>
    /// Cached Discord user state
    /// </summary>
    public class DiscordCachedState
    {
        [JsonProperty("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;

        [JsonProperty("global_name")]
        public string? GlobalName { get; set; }

        [JsonProperty("avatar")]
        public string? Avatar { get; set; }

        [JsonProperty("email")]
        public string? Email { get; set; }

        [JsonProperty("custom_display_name")]
        public string? CustomDisplayName { get; set; }

        [JsonProperty("last_verified")]
        public DateTime LastVerified { get; set; }

        [JsonProperty("cache_expires_at")]
        public DateTime CacheExpiresAt { get; set; }

        /// <summary>
        /// Check if the cache has expired (older than 24 hours)
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsExpired => DateTime.UtcNow >= CacheExpiresAt;

        /// <summary>
        /// Get the display name
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayName => !string.IsNullOrEmpty(GlobalName) ? GlobalName : Username;
    }

    /// <summary>
    /// Discord webhook payload for achievement announcements
    /// </summary>
    public class DiscordWebhookPayload
    {
        [JsonProperty("content")]
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonProperty("username")]
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonProperty("avatar_url")]
        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }

        [JsonProperty("embeds")]
        [JsonPropertyName("embeds")]
        public DiscordEmbed[]? Embeds { get; set; }
    }

    /// <summary>
    /// Discord embed for rich webhook messages
    /// </summary>
    public class DiscordEmbed
    {
        [JsonProperty("title")]
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonProperty("description")]
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonProperty("color")]
        [JsonPropertyName("color")]
        public int? Color { get; set; }

        [JsonProperty("timestamp")]
        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonProperty("footer")]
        [JsonPropertyName("footer")]
        public DiscordEmbedFooter? Footer { get; set; }

        [JsonProperty("thumbnail")]
        [JsonPropertyName("thumbnail")]
        public DiscordEmbedMedia? Thumbnail { get; set; }

        [JsonProperty("fields")]
        [JsonPropertyName("fields")]
        public DiscordEmbedField[]? Fields { get; set; }
    }

    public class DiscordEmbedFooter
    {
        [JsonProperty("text")]
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonProperty("icon_url")]
        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }
    }

    public class DiscordEmbedMedia
    {
        [JsonProperty("url")]
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    public class DiscordEmbedField
    {
        [JsonProperty("name")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("value")]
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        [JsonProperty("inline")]
        [JsonPropertyName("inline")]
        public bool Inline { get; set; }
    }
}
