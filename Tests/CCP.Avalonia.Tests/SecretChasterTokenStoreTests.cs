using System;
using System.IO;
using System.Text;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Chaster link's tokens go through the head's SecretStore (memory-only in this
/// sandboxed test profile), in WPF's chaster_auth.dat slot and JSON shape.</summary>
public sealed class SecretChasterTokenStoreTests
{
    [Fact]
    public void TokensRoundTripThroughTheSecretStoreAndClear()
    {
        Assert.True(SecretStore.Sandboxed);   // never a real keyring
        SecretStore.Seed();
        var store = new SecretChasterTokenStore();
        var t = new ChasterStoredTokens("acc", "ref", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        store.Write(t);
        Assert.Equal(t, store.Read());
        Assert.Contains("\"ExpiresAt\"", SecretStore.Retrieve(SecretChasterTokenStore.Name));   // WPF SecureTokenStorage shape

        store.Clear();
        Assert.Null(store.Read());
        var (path, entropy) = Dpapi.Slot("d", SecretChasterTokenStore.Name);
        Assert.Equal(Path.Combine("d", "chaster_auth.dat"), path);
        Assert.Equal("ConditioningControlPanel_Chaster_v1", Encoding.UTF8.GetString(entropy));
    }
}
