using System;
using System.Text.Json;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>This head's Chaster token store: one <see cref="CoreSecrets"/> item ("chaster_auth"),
/// which <see cref="SecretStore"/> keeps in libsecret, in WPF's own chaster_auth.dat on Windows
/// (same JSON shape as WPF's SecureTokenStorage), or in memory only in a sandbox. Never plaintext.</summary>
internal sealed class SecretChasterTokenStore : IChasterTokenStore
{
    internal const string Name = "chaster_auth";

    private sealed record Wire(string AccessToken, string RefreshToken, DateTime ExpiresAt);

    public ChasterStoredTokens? Read()
    {
        var json = CoreSecrets.Retrieve(Name);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var w = JsonSerializer.Deserialize<Wire>(json);
            return w == null || string.IsNullOrEmpty(w.RefreshToken) ? null
                : new ChasterStoredTokens(w.AccessToken, w.RefreshToken, DateTime.SpecifyKind(w.ExpiresAt, DateTimeKind.Utc));
        }
        catch (JsonException) { return null; }
    }

    public void Write(ChasterStoredTokens tokens) =>
        CoreSecrets.StoreOrThrow(Name, JsonSerializer.Serialize(new Wire(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc)));

    public void Clear() => CoreSecrets.Store(Name, null);
}
