using System;

namespace ConditioningControlPanel.Core.Services;

/// <summary>
/// Secure auth-token storage seam for <see cref="Models.AppSettings.AuthToken"/>.
/// Each head backs it via <see cref="Wire"/> at startup (WPF: the DPAPI
/// <c>ConditioningControlPanel.Services.SecureAuthTokenStore</c>; Avalonia heads:
/// <c>ISecretStore</c>). Unwired (tests, uninitialized) it behaves as an empty store.
/// </summary>
public static class SecureAuthTokenStore
{
    private static Func<string?>? _retrieve;
    private static Action<string?>? _store;
    private static string? _cached;
    private static bool _loaded;

    public static void Wire(Func<string?> retrieve, Action<string?> store)
    {
        _retrieve = retrieve;
        _store = store;
        _cached = null;
        _loaded = false;
    }

    public static string? Retrieve()
    {
        if (_loaded) return _cached;
        _cached = _retrieve?.Invoke();
        _loaded = true;
        return _cached;
    }

    public static void Store(string? value)
    {
        _cached = value;
        _loaded = true;
        _store?.Invoke(value);
    }

    /// <summary>
    /// Drop the in-memory copy (call on app exit to reduce memory exposure).
    /// Does not touch the backing store.
    /// </summary>
    public static void ClearMemoryCache()
    {
        _cached = null;
        _loaded = false;
    }
}
