using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Wave A4 (social#2, #4, #35): Settings &gt; Account login/link and GDPR export, the Profile card buttons.</summary>
public sealed class AccountButtonsParityTests
{
    private sealed class Reply(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastPath;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            LastPath = r.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task Link_StoresTheLinkAndTheRotatedToken_AlreadyLinkedIsQuietSuccess_DifferentUserFails()
    {
        var s = CoreSettings.Current;
        var (oldId, oldTok, oldD, oldP) = (s.UnifiedId, s.AuthToken, s.HasLinkedDiscord, s.HasLinkedPatreon);
        try
        {
            s.UnifiedId = null;
            Assert.Equal(AccountLink.Outcome.NoAccount, (await AccountLink.LinkAsync("discord", accessToken: "a")).outcome);

            s.UnifiedId = "u-1"; s.AuthToken = "old"; s.HasLinkedDiscord = false; s.HasLinkedPatreon = false;
            var ok = new Reply(HttpStatusCode.OK, "{\"success\":true,\"auth_token\":\"new\"}");
            var (outcome, _) = await AccountLink.LinkAsync("discord", new V2AuthService(() => s, ok), "a");
            Assert.Equal(AccountLink.Outcome.Linked, outcome);
            Assert.Equal("/v2/auth/link", ok.LastPath);
            Assert.True(s.HasLinkedDiscord);
            Assert.Equal("new", s.AuthToken);

            var already = new Reply(HttpStatusCode.Conflict, "{\"error\":\"Patreon already linked to this account\"}");
            Assert.Equal(AccountLink.Outcome.AlreadyLinked, (await AccountLink.LinkAsync("patreon", new V2AuthService(() => s, already), "a")).outcome);
            Assert.True(s.HasLinkedPatreon);

            var other = new Reply(HttpStatusCode.Conflict, "{\"error\":\"Patreon account already linked to a different user\"}");
            var (failed, error) = await AccountLink.LinkAsync("patreon", new V2AuthService(() => s, other), "a");
            Assert.Equal(AccountLink.Outcome.Failed, failed);
            Assert.Contains("different user", error);
        }
        finally { (s.UnifiedId, s.AuthToken, s.HasLinkedDiscord, s.HasLinkedPatreon) = (oldId, oldTok, oldD, oldP); }
    }

    [Fact]
    public async Task Export_PostsToExportData_AndPrettyPrints_RefusesWithoutAnAccount()
    {
        var wire = new Reply(HttpStatusCode.OK, "{\"user\":{\"id\":\"u-1\"}}");
        var v2 = new V2AuthService(() => new ConditioningControlPanel.Models.AppSettings(), wire);
        var (success, _, json) = await v2.ExportDataAsync("u-1");
        Assert.True(success);
        Assert.Equal("/v2/user/export-data", wire.LastPath);
        Assert.Contains("\n", json);   // indented for the save file

        var (refused, error, none) = await v2.ExportDataAsync(null);
        Assert.False(refused);
        Assert.NotNull(error);
        Assert.Null(none);
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task AccountSection_WithACloudIdentity_OpensDataPrivacyAndOffersTheDiscordLink() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (oldId, oldD) = (s.UnifiedId, s.HasLinkedDiscord);
        try
        {
            s.UnifiedId = null;
            var section = new AccountSettingsSection();
            Assert.False(section.FindControl<Border>("DataPrivacySection")!.IsVisible);
            Assert.False(section.FindControl<Border>("AccountLinkingSection")!.IsVisible);

            s.UnifiedId = "u-1"; s.HasLinkedDiscord = false;
            section.RefreshProviderRows();
            Assert.True(section.FindControl<Border>("DataPrivacySection")!.IsVisible);   // Export my data is reachable
            Assert.True(section.FindControl<Border>("AccountLinkingSection")!.IsVisible);
            Assert.True(section.FindControl<Button>("BtnLinkDiscord")!.IsVisible);
            Assert.False(section.FindControl<Border>("CloudSettingsBackupSection")!.IsVisible);   // no backup service: stays shut
        }
        finally { (s.UnifiedId, s.HasLinkedDiscord) = (oldId, oldD); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ProfileCard_DiscordButtonCarriesTheId_ShareGatedOnSignIn() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var view = new DiscordTabView();
        view.ShowDiscordDm("123", "Someone");
        var dm = view.FindControl<Button>("BtnProfileDiscord")!;
        Assert.True(dm.IsVisible);
        Assert.Equal("123", dm.Tag);
        Assert.Equal("https://discord.com/users/123", DiscordTabView.DiscordProfileUrl("123"));
        view.ShowDiscordDm(null, null);
        Assert.False(dm.IsVisible);

        view.RefreshProfileChrome();
        Assert.Equal(CoreAccount.IsLoggedIn, view.FindControl<Button>("BtnProfileShare")!.IsEnabled);
        return Task.CompletedTask;
    });
}
