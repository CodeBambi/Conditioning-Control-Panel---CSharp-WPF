using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>ai#8: the BYO API key is saved protected, reads back, and never sits in settings in the clear.</summary>
public sealed class ApiKeyProtectorTests
{
    [Fact]
    public void Dpapi_RoundTrips_AndIsNotTheKey_OnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var stored = ApiKeyProtector.Protect("sk-test-123");
        Assert.NotEqual("sk-test-123", stored);
        Assert.DoesNotContain("sk-test", stored);
        Assert.Equal("sk-test-123", ApiKeyProtector.Unprotect(stored));
    }

    [Fact]
    public void SecretStorePath_KeepsOnlyAMarker_LegacyPlainReadsAsItself_EmptyRevokes()
    {
        var (oldGet, oldSet, oldDpapi) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ApiKeyProtector.UseDpapi);
        var secrets = new Dictionary<string, string?>();
        try
        {
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            ApiKeyProtector.UseDpapi = false;

            var stored = ApiKeyProtector.Protect("sk-linux");
            Assert.Equal(ApiKeyProtector.Marker, stored);
            Assert.Equal("sk-linux", ApiKeyProtector.Unprotect(stored));

            Assert.Equal("sk-plain-legacy", ApiKeyProtector.Unprotect("sk-plain-legacy"));   // WPF FormatException fallback

            Assert.Equal("", ApiKeyProtector.Protect(""));
            Assert.Null(secrets[ApiKeyProtector.SecretName]);

            CoreSecrets.StoreProvider = null;   // no store at all: nothing saved, never the clear key
            Assert.Equal("", ApiKeyProtector.Protect("sk-nowhere"));
        }
        finally
        {
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ApiKeyProtector.UseDpapi) = (oldGet, oldSet, oldDpapi);
        }
    }

    [Fact]
    public Task EngineRoomBox_SavesWhatIsTyped_TheInitialEmptyPushNeverWipesAStoredKey() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var p = CoreSettings.Current.CompanionPrompt;
        var (oldGet, oldSet, oldDpapi, oldKey) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ApiKeyProtector.UseDpapi, p.OpenAiCompatibleApiKey);
        var secrets = new Dictionary<string, string?>();
        try
        {
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            ApiKeyProtector.UseDpapi = false;
            p.OpenAiCompatibleApiKey = "already-stored";

            var vm = new EngineRoomVm(new Border());
            vm.CustomApiKey = "";                         // the OneWayToSource attach push
            Assert.Equal("already-stored", p.OpenAiCompatibleApiKey);

            vm.CustomApiKey = "sk-typed";
            Assert.Equal(ApiKeyProtector.Marker, p.OpenAiCompatibleApiKey);
            Assert.Equal("sk-typed", secrets[ApiKeyProtector.SecretName]);
            Assert.Equal("", vm.CustomApiKey);           // never read back

            vm.CustomApiKey = "";                         // emptied box revokes
            Assert.Equal("", p.OpenAiCompatibleApiKey);
            Assert.NotNull(vm.LoginCommand);             // ai#16: the sign-in button has a door
        }
        finally
        {
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ApiKeyProtector.UseDpapi) = (oldGet, oldSet, oldDpapi);
            p.OpenAiCompatibleApiKey = oldKey;
        }
        return Task.CompletedTask;
    });
}
