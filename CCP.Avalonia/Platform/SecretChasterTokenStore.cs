using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>This head's Chaster token store: one <see cref="CoreSecrets"/> item ("chaster_auth"),
/// which <see cref="SecretStore"/> keeps in libsecret, in WPF's own chaster_auth.dat on Windows,
/// or in memory only in a sandbox. Never plaintext. The value is WPF's <see cref="PatreonTokenData"/>
/// JSON (access_token/refresh_token/expires_at), exactly as WPF's SecureTokenStorage("chaster")
/// behind DpapiChasterTokenStore writes it.</summary>
internal sealed class SecretChasterTokenStore : IChasterTokenStore
{
    internal const string Name = "chaster_auth";

    public ChasterStoredTokens? Read()
    {
        var json = CoreSecrets.Retrieve(Name);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var d = JsonConvert.DeserializeObject<PatreonTokenData>(json);
            return d == null || string.IsNullOrEmpty(d.RefreshToken) ? null
                : new ChasterStoredTokens(d.AccessToken, d.RefreshToken, DateTime.SpecifyKind(d.ExpiresAt, DateTimeKind.Utc));
        }
        catch (JsonException) { return null; }
    }

    public void Write(ChasterStoredTokens tokens) =>
        CoreSecrets.StoreOrThrow(Name, JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = tokens.AccessToken, RefreshToken = tokens.RefreshToken, ExpiresAt = tokens.ExpiresAtUtc }));

    public void Clear() => CoreSecrets.Store(Name, null);
}
