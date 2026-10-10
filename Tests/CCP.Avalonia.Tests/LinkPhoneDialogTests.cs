using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Settings · "📱 Link phone" -> LinkPhoneDialog over the Core wire (stubbed server, stepped clock).</summary>
public sealed class LinkPhoneDialogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = T0;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Wire(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public readonly List<(string Path, string Body, string? Auth)> Sent = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Sent.Add((r.RequestUri!.AbsolutePath, await r.Content!.ReadAsStringAsync(ct), r.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null));
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    /// <summary>Opens Settings from the shell, clicks Link phone, returns the dialog after its fetch.</summary>
    private static async Task Run(Wire wire, string? unifiedId, Func<LinkPhoneDialog, Clock, Task> body)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldGet, oldSet) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>(); // memory only: never the keyring
        (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (n => secrets.GetValueOrDefault(n), (n, v) => secrets[n] = v);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.UnifiedId, s.AuthToken, s.OfflineMode, s.OfflineUsername) = (unifiedId, "tok-test", false, "Bambi");
        var (oldAuth, oldClock) = (LinkPhoneDialog.Auth, LinkPhoneDialog.Clock);
        var clock = new Clock();
        LinkPhoneDialog.Auth = () => new V2AuthService(() => s, wire);
        LinkPhoneDialog.Clock = clock;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            var home = shell.Named<SettingsTabView>("SettingsTab")!;
            home.FindControl<Button>("BtnLinkPhone")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var dialog = shell.OwnedWindows.OfType<LinkPhoneDialog>().Single();
            await dialog.Fetch;
            try { await body(dialog, clock); }
            finally { dialog.Close(); }
            Assert.False(dialog.CountdownRunning); // closing stops the 1 s countdown
        }
        finally
        {
            shell.Close();
            CoreSettings.ServiceProvider = null;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldSet);
            (LinkPhoneDialog.Auth, LinkPhoneDialog.Clock) = (oldAuth, oldClock);
        }
    }

    private static string? Text(Window w, string name) => w.FindControl<TextBlock>(name)!.Text;

    [Fact]
    public Task ACodeShowsAsQrAndDashedTextThenExpires() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var wire = new Wire(HttpStatusCode.OK,
            $$"""{"success":true,"link_code":"ABCDEF","expires_at":"{{T0.AddMinutes(3):O}}","qr_payload":"ccpmobile://link?c=ABCDEF"}""");
        await Run(wire, "u_me", (d, clock) =>
        {
            var (path, body, auth) = wire.Sent.Single();
            Assert.Equal("/v2/auth/device/authorize", path);
            Assert.Contains("\"unified_id\": \"u_me\"", body);
            Assert.Equal("tok-test", auth);
            Assert.Equal("ABC-DEF", Text(d, "TxtLinkCode"));
            Assert.NotNull(d.FindControl<Image>("ImgQrCode")!.Source);
            Assert.Equal("Code expires in 3:00", Text(d, "TxtStatus"));
            Assert.True(d.CountdownRunning);

            clock.Now = T0.AddSeconds(95);
            d.Tick();
            Assert.Equal("Code expires in 1:25", Text(d, "TxtStatus"));

            clock.Now = T0.AddMinutes(3);
            d.Tick();
            Assert.Equal(Loc.Get("msg_link_phone_code_expired"), Text(d, "TxtStatus"));
            Assert.Equal("--- ---", Text(d, "TxtLinkCode"));
            Assert.Null(d.FindControl<Image>("ImgQrCode")!.Source);
            Assert.False(d.CountdownRunning);
            Assert.True(d.FindControl<Button>("BtnRefresh")!.IsEnabled);
            return Task.CompletedTask;
        });
    });

    [Fact]
    public Task RefusalsAreWordedAsInWpfAndSignedOutSendsNothing() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var limited = new Wire(HttpStatusCode.TooManyRequests, """{"error":"rate_limited"}""");
        await Run(limited, "u_me", (d, _) =>
        {
            Assert.Equal("Too many codes requested. Wait a minute and try again.", Text(d, "TxtStatus"));
            Assert.Equal("--- ---", Text(d, "TxtLinkCode"));
            Assert.True(d.FindControl<Button>("BtnRefresh")!.IsEnabled);
            Assert.False(d.CountdownRunning);
            return Task.CompletedTask;
        });

        var none = new Wire(HttpStatusCode.OK, "{}");
        await Run(none, null, (d, _) =>
        {
            Assert.Empty(none.Sent);
            Assert.Equal("You need to be logged in to link a phone.", Text(d, "TxtStatus"));
            return Task.CompletedTask;
        });
    });
}
