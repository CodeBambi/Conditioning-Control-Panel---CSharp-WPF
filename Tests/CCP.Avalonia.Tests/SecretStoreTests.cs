using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The head's CoreSecrets provider. The two Linux tests talk to a real Secret Service, so they
/// run ONLY inside scripts/secrets-roundtrip.sh's container (throwaway gnome-keyring on its own
/// session bus) and skip everywhere else - never the user's keyring. The DPAPI test runs on
/// Windows CI.
/// </summary>
public sealed class SecretStoreTests
{
    private const string Json = "{\"AccessToken\":\"acc\",\"RefreshToken\":\"ref\",\"ExpiresAt\":\"2030-01-01T00:00:00Z\"}";

    [Theory]
    [InlineData("authtoken", "auth_token.dat", "ConditioningControlPanel_AuthToken_v1")]
    [InlineData("apikey", "api_key.dat", "ConditioningControlPanel_ApiKey_v1")]
    [InlineData("patreon_auth", "patreon_auth.dat", "ConditioningControlPanel_Patreon_v1")]
    [InlineData("patreon_cache", "patreon_cache.dat", "ConditioningControlPanel_Patreon_v1")]
    [InlineData("discord_auth", "discord_auth.dat", "ConditioningControlPanel_Discord_v1")]
    [InlineData("substar_cache", "substar_cache.dat", "ConditioningControlPanel_Substar_v1")]
    public void DpapiSlotsAreWpfs(string name, string file, string entropy)
    {
        var (path, e) = Dpapi.Slot("d", name);
        Assert.Equal(Path.Combine("d", file), path);
        Assert.Equal(entropy, Encoding.UTF8.GetString(e));
    }

    [Fact]
    public void DpapiReadsWpfWrittenFiles()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("DPAPI is Windows-only; runs on Windows CI.");
        else DpapiRoundTrip();
    }

    [SupportedOSPlatform("windows")]
    private static void DpapiRoundTrip()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-dpapi-").FullName;
        try
        {
            // Written the way WPF SecureTokenStorage.StoreTokens does (SecureTokenStorage.cs:59-67).
            var wpf = Encoding.UTF8.GetBytes("ConditioningControlPanel_Patreon_v1");
            File.WriteAllBytes(Path.Combine(dir, "patreon_auth.dat"),
                ProtectedData.Protect(Encoding.UTF8.GetBytes(Json), wpf, DataProtectionScope.CurrentUser));
            Assert.Equal(Json, Dpapi.Read(dir, "patreon_auth"));

            Assert.True(Dpapi.Write(dir, "authtoken", "tok"));
            var bytes = File.ReadAllBytes(Path.Combine(dir, "auth_token.dat"));
            Assert.Equal("tok", Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes,
                Encoding.UTF8.GetBytes("ConditioningControlPanel_AuthToken_v1"), DataProtectionScope.CurrentUser)));

            Assert.True(Dpapi.Write(dir, "authtoken", null));
            Assert.False(File.Exists(Path.Combine(dir, "auth_token.dat")));
            Assert.Null(Dpapi.Read(dir, "authtoken"));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>TestUserDataProfile sets CCP_USERDATA_DIR before this loads, as kc and CI sandboxes do.</summary>
    [Fact]
    public void ASandboxedProfileIsMemoryOnly()
    {
        Assert.True(SecretStore.Sandboxed);   // first: a broken flag must fail before anything reaches a keyring
        SecretStore.NotRemembered = false;
        SecretStore.Store("sandbox_probe", "v");
        Assert.Equal("v", SecretStore.Retrieve("sandbox_probe"));
        // Memory-only by design, not a missing Secret Service: no "no secret store" notice.
        Assert.False(SecretStore.NotRemembered);
    }

    [Fact]
    public void LibsecretRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("CCP_SECRETS_ROUNDTRIP") != "1")
            Assert.Skip("Container-only (scripts/secrets-roundtrip.sh): never the user's keyring.");
        SecretStore.Sandboxed = false;   // the test profile sets CCP_USERDATA_DIR; this run owns its keyring

        SecretStore.Store("patreon_auth", Json);
        Assert.Equal(Json, Libsecret.Lookup("patreon_auth"));   // in the keyring, not just the cache
        Assert.True(Libsecret.Write("authtoken", "tok"));
        Assert.Equal("tok", Libsecret.Lookup("authtoken"));
        Assert.Null(Libsecret.Lookup("discord_auth"));

        SecretStore.Store("patreon_auth", null);
        Assert.Null(Libsecret.Lookup("patreon_auth"));
        Assert.Null(SecretStore.Retrieve("patreon_auth"));
        Assert.True(Libsecret.Write("authtoken", null));
        Assert.True(Libsecret.Write("authtoken", null));        // clearing nothing is not a failure
        Assert.Null(Libsecret.Lookup("authtoken"));
    }

    [Fact]
    public void NoBusReadsNullAndWritesNoFile()
    {
        if (Environment.GetEnvironmentVariable("CCP_SECRETS_NOBUS") != "1")
            Assert.Skip("Run by scripts/secrets-roundtrip.sh with DBUS_SESSION_BUS_ADDRESS pointing at nothing.");
        SecretStore.Sandboxed = false;

        var home = Environment.GetEnvironmentVariable("HOME")!;
        string[] Files() => new[] { home, CorePaths.UserData }.Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)).Order().ToArray();
        var before = Files();

        Assert.Null(Libsecret.Lookup("authtoken"));
        Assert.False(Libsecret.Write("authtoken", "tok"));
        Assert.Null(SecretStore.Retrieve("apikey"));
        SecretStore.Store("authtoken", "tok");
        SecretStore.Store("discord_auth", Json);
        Assert.Equal("tok", SecretStore.Retrieve("authtoken"));  // this run only, from memory
        Assert.True(SecretStore.NotRemembered);
        Assert.Equal(before, Files());
    }
}
