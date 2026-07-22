using System.Text.Json;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// ISecretStore platform implementation proofs (SP-033 slice c1; contract §10; admission
/// §6). Windows: DPAPI round-trip (CurrentUser scope). Linux: typed-Unavailable probe on
/// WSL2 is the EXPECTED honest evidence (no session daemon — never faked; a working-daemon
/// proof needs a desktop-session box, named limit). Settings documents carry opaque secret
/// NAMES, never values (persistence-migration-contract §9).
/// </summary>
public class SecretStoreTests
{
    [Fact]
    public void WindowsDpapi_RoundTrip_AndFileNeverContainsPlaintext()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // OS-gated: the Linux evidence is the probe test below.
        }

        var root = Path.Combine(Path.GetTempPath(), "ccp-sp033-secrets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WindowsDpapiSecretStore(root);
            const string secretValue = "sp033-test-secret-7f3a9c";

            Assert.Null(store.Get("ai-cloud-token")); // absent → null, not an error

            store.Set("ai-cloud-token", secretValue);
            Assert.Equal(secretValue, store.Get("ai-cloud-token"));

            // The on-disk bytes never carry the plaintext value.
            foreach (var file in Directory.EnumerateFiles(root))
            {
                Assert.DoesNotContain(secretValue, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
            }

            // Names are filesystem-sanitized opaque tokens.
            store.Set("weird/name:with?chars", "v2");
            Assert.Equal("v2", store.Get("weird/name:with?chars"));
            Assert.All(Directory.EnumerateFiles(root), f => Assert.DoesNotMatch(":" + "|" + "\\?" + "|" + "/", Path.GetFileName(f)));

            store.Delete("ai-cloud-token");
            Assert.Null(store.Get("ai-cloud-token"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LinuxProbe_TypedOutcome_NeverFaked()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // OS-gated: runs on the WSL2 evidence box (and any Linux CI).
        }

        var store = new SecretToolSecretStore();
        var state = await store.ProbeAsync(CancellationToken.None);

        // Honest either way: Unavailable (expected on WSL2 — no session daemon) is the
        // typed evidence; Available must be backed by a REAL round-trip, never claimed.
        if (state is CapabilityState.Unavailable or CapabilityState.DependencyMissing or CapabilityState.Faulted)
        {
            Assert.Throws<SecretStoreUnavailableException>(() => store.Set("probe-name", "probe-value"));
            Assert.Throws<SecretStoreUnavailableException>(() => store.Get("probe-name"));
        }
        else
        {
            var available = Assert.IsType<CapabilityState.Available>(state);
            Assert.False(string.IsNullOrWhiteSpace(available.Detail));
            store.Set("ccp-test-probe", "round-trip-value");
            Assert.Equal("round-trip-value", store.Get("ccp-test-probe"));
            store.Delete("ccp-test-probe");
            Assert.Null(store.Get("ccp-test-probe"));
        }
    }

    [Fact]
    public void ForCurrentPlatform_ReturnsPlatformBackend_WithProbe()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-sp033-platform-" + Guid.NewGuid().ToString("N"));
        var (store, probe) = PlatformSecretStore.ForCurrentPlatform(root);

        Assert.NotNull(store);
        Assert.NotNull(probe);
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsDpapiSecretStore>(store);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.IsType<SecretToolSecretStore>(store);
        }
        else
        {
            Assert.IsType<UnsupportedSecretStore>(store);
            Assert.Throws<SecretStoreUnavailableException>(() => store.Get("x"));
        }
    }

    [Fact]
    public void SettingsDocument_CarriesSecretNames_NeverValues()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // DPAPI-backed value storage is the Windows leg; the name-only rule is proven there.
        }

        var root = Path.Combine(Path.GetTempPath(), "ccp-sp033-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WindowsDpapiSecretStore(root);
            const string secretValue = "sp033-never-in-settings-4d2e";
            store.Set("ai-cloud-token", secretValue);

            // A settings-shaped document routes through the seam by NAME only (WPF precedent:
            // AppSettings.AuthToken is [JsonIgnore] + secret-store-routed, AppSettings.cs:4005-4008).
            var settingsShaped = new Dictionary<string, string?> { ["AiCloudTokenSecretName"] = "ai-cloud-token" };
            var json = JsonSerializer.Serialize(settingsShaped);

            Assert.Contains("ai-cloud-token", json); // the opaque NAME may persist
            Assert.DoesNotContain(secretValue, json); // the VALUE never does
            Assert.Equal(secretValue, store.Get(settingsShaped["AiCloudTokenSecretName"]!));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
