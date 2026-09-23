using System;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// The app's token store: the same DPAPI file scheme Patreon and SubscribeStar use
/// (chaster_auth.dat, current-user scope), with the last read kept in memory. The tab asks
/// "is an account linked" on every priced event, and that must not be a disk read and a decrypt
/// each time a remote picture lands.
/// </summary>
public sealed class DpapiChasterTokenStore : IChasterTokenStore
{
    private readonly SecureTokenStorage _storage = new("chaster");
    private readonly object _gate = new();
    private ChasterStoredTokens? _cached;
    private bool _loaded;

    public ChasterStoredTokens? Read()
    {
        lock (_gate)
        {
            if (_loaded) return _cached;
            var data = _storage.RetrieveTokens();
            _cached = data == null || string.IsNullOrEmpty(data.RefreshToken)
                ? null
                : new ChasterStoredTokens(data.AccessToken, data.RefreshToken, DateTime.SpecifyKind(data.ExpiresAt, DateTimeKind.Utc));
            _loaded = true;
            return _cached;
        }
    }

    public void Write(ChasterStoredTokens tokens)
    {
        lock (_gate)
        {
            _storage.StoreTokens(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc);
            _cached = tokens;
            _loaded = true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _storage.ClearTokens();
            _cached = null;
            _loaded = true;
        }
    }
}
