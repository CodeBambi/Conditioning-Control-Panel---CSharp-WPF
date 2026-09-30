using System;
using System.IO;
using System.Text;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Chaster link's tokens go through the head's SecretStore (memory-only in this
/// sandboxed test profile), in WPF's chaster_auth.dat slot and WPF's PatreonTokenData JSON.</summary>
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
        Assert.Contains("\"expires_at\"", SecretStore.Retrieve(SecretChasterTokenStore.Name));   // WPF's shape

        store.Clear();
        Assert.Null(store.Read());
        var (path, entropy) = Dpapi.Slot("d", SecretChasterTokenStore.Name);
        Assert.Equal(Path.Combine("d", "chaster_auth.dat"), path);
        Assert.Equal("ConditioningControlPanel_Chaster_v1", Encoding.UTF8.GetString(entropy));
    }

    [Fact]
    public void ReadsTheJsonWpfWrites()
    {
        SecretStore.Seed();
        // What WPF's SecureTokenStorage.StoreTokens writes (JsonConvert of PatreonTokenData).
        CoreSecrets.Store(SecretChasterTokenStore.Name,
            "{\"access_token\":\"AT\",\"refresh_token\":\"RT\",\"expires_at\":\"2030-01-01T00:00:00Z\"}");

        var t = new SecretChasterTokenStore().Read();

        Assert.Equal(new ChasterStoredTokens("AT", "RT", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)), t);
        CoreSecrets.Store(SecretChasterTokenStore.Name, null);
    }
}
