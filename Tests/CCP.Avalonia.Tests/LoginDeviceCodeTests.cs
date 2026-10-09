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
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Settings · Log in -> "Sign in via Web": Core V2DeviceCodeService over a stubbed server, polled on a stepped clock.</summary>
public sealed class LoginDeviceCodeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Stepped clock whose timers fire only on <see cref="Advance"/> (Task.Delay(TimeSpan, TimeProvider)).</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = T0;
        private readonly List<T> _timers = new();
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new T(this, () => cb(state));
            _timers.Add(t);
            t.Change(due, period);
            return t;
        }
        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            for (T? next; (next = _timers.Find(t => t.Due <= end)) != null;)
            {
                _now = next.Due!.Value;
                next.Due = null;
                next.Fire();
            }
            _now = end;
        }
        private sealed class T(Clock clock, Action fire) : ITimer
        {
            public DateTimeOffset? Due;
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) { Due = due == Timeout.InfiniteTimeSpan ? null : clock._now + due; return true; }
            public void Dispose() => Due = null;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    /// <summary>The proxy: initiate hands out ABCDEF, each poll answers the next queued (status, body).</summary>
    private sealed class Wire(DateTimeOffset expires, params (HttpStatusCode, string)[] polls) : HttpMessageHandler
    {
        public int Polls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/v2/auth/device/initiate")
                return Reply(HttpStatusCode.OK, $$"""{"code":"ABCDEF","session_id":"sess-1234567","expires_at":"{{expires:O}}"}""");
            if (path == "/v2/auth/device/poll")
            {
                var (status, body) = polls[Math.Min(Polls++, polls.Length - 1)];
                return Reply(status, body);
            }
            return Reply(HttpStatusCode.NotFound, "{}"); // the post-login profile read: nothing to adopt
        }
        private static Task<HttpResponseMessage> Reply(HttpStatusCode s, string body) =>
            Task.FromResult(new HttpResponseMessage(s) { Content = new StringContent(body) });
    }

    /// <summary>Runs dispatcher work until <paramref name="done"/>; bounded, no wall-clock waits.</summary>
    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.True(done());
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Run(Wire wire, Func<LoginDialog, Clock, AppSettings, Task> body)
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
        (s.UnifiedId, s.OfflineMode, s.OfflineUsername) = (null, false, "Bambi");
        var (oldCodes, oldClock, oldV2, oldShell, oldUid, oldSync) =
            (LoginDialog.DeviceCodes, LoginDialog.Clock, AccountSeed.NewV2, ExternalOpener.Shell, CoreAccount.UnifiedUserId, AccountSeed.Sync);
        var clock = new Clock();
        LoginDialog.DeviceCodes = () => new V2DeviceCodeService(wire);
        LoginDialog.Clock = clock;
        AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
        AccountSeed.Sync = null;
        ExternalOpener.Shell = _ => true; // never a real browser
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            Click(shell.Named<SettingsTabView>("SettingsTab")!.FindControl<Button>("BtnUnifiedLogin")!);
            Dispatcher.UIThread.RunJobs();
            var dialog = shell.OwnedWindows.OfType<LoginDialog>().Single();
            Click(dialog.FindControl<Button>("BtnLoginDeviceCode")!);
            await Until(() => dialog.FindControl<StackPanel>("DeviceCodePanel")!.IsVisible);
            Assert.Equal("ABC-DEF", dialog.FindControl<TextBlock>("TxtDeviceCode")!.Text);
            Assert.Equal(V2DeviceCodeService.VerificationUrl, dialog.FindControl<TextBox>("TxtVerificationUrl")!.Text);
            Assert.Equal("Waiting for browser confirmation...", dialog.FindControl<TextBlock>("TxtDeviceStatus")!.Text);
            await body(dialog, clock, s);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.Close();
            CoreSettings.ServiceProvider = null;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldSet);
            (LoginDialog.DeviceCodes, LoginDialog.Clock, AccountSeed.NewV2, ExternalOpener.Shell, CoreAccount.UnifiedUserId, AccountSeed.Sync) =
                (oldCodes, oldClock, oldV2, oldShell, oldUid, oldSync);
        }
    }

    [Fact]
    public Task TheBrowserConfirmationSignsInAfterPollingEveryThreeSeconds()
    {
        var wire = new Wire(T0.AddMinutes(10),
            (HttpStatusCode.Accepted, "{}"),
            (HttpStatusCode.OK, """{"auth_token":"tok-dev","unified_id":"u_dev","user":{"unified_id":"u_dev","display_name":"Bambi Dev"}}"""));
        return AvaloniaTestDispatcher.RunAsync(() => Run(wire, async (dialog, clock, s) =>
            {
                clock.Advance(TimeSpan.FromSeconds(2.9));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, wire.Polls); // WPF waits 3 s before the first poll

                clock.Advance(TimeSpan.FromSeconds(0.1));
                await Until(() => wire.Polls == 1);
                Dispatcher.UIThread.RunJobs();
                Assert.True(dialog.IsVisible); // 202: still waiting

                clock.Advance(TimeSpan.FromSeconds(3));
                await Until(() => !dialog.IsVisible);
                await dialog.DevicePoll;
                Assert.Equal(2, wire.Polls);
                Assert.Equal("device_code", dialog.Result?.Provider);
                Assert.Equal("u_dev", s.UnifiedId);
                Assert.Equal("tok-dev", s.AuthToken);
                Assert.Equal("u_dev", CoreAccount.UnifiedUserId);
                await dialog.ProfileLoad;
            }));
    }

    [Fact]
    public Task AnExpiredCodeSaysSoAndReturnsToTheProviders()
    {
        var wire = new Wire(T0.AddSeconds(5), (HttpStatusCode.Accepted, "{}"));
        return AvaloniaTestDispatcher.RunAsync(() => Run(wire, async (dialog, clock, s) =>
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                await Until(() => wire.Polls == 1);
                Dispatcher.UIThread.RunJobs();
                clock.Advance(TimeSpan.FromSeconds(3)); // past the server's expires_at: no second poll
                await Until(() => dialog.OwnedWindows.OfType<MessageDialog>().Any());
                var box = dialog.OwnedWindows.OfType<MessageDialog>().Single();
                Assert.Equal("Sign-in code expired. Please try again.", box.FindControl<TextBlock>("TxtMessage")!.Text);
                box.Close();
                await dialog.DevicePoll;
                Assert.Equal(1, wire.Polls);
                Assert.True(dialog.FindControl<StackPanel>("ProviderPanel")!.IsVisible);
                Assert.False(dialog.FindControl<StackPanel>("DeviceCodePanel")!.IsVisible);
                Assert.Null(s.UnifiedId);

                clock.Advance(TimeSpan.FromMinutes(1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, wire.Polls); // the loop is over
            }));
    }

    /// <summary>WPF BrowserLauncher.OpenUrlOrPrompt: a sign-in link no browser takes is copied and the user told so.</summary>
    [Fact]
    public Task AnUnopenableSignInLinkIsCopiedAndExplained() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldShell = ExternalOpener.Shell;
        ExternalOpener.Shell = _ => false; // nothing handles the link
        var dialog = new LoginDialog();
        try
        {
            dialog.Show();
            const string url = "http://127.0.0.1:9/oauth"; // loopback: the only kind a sandboxed test may open
            var open = dialog.OpenBrowserAsync(url, "sign in with Patreon");
            await Until(() => dialog.OwnedWindows.OfType<MessageDialog>().Any());
            var box = dialog.OwnedWindows.OfType<MessageDialog>().Single();
            Assert.Equal(Loc.Get("title_open_link_in_browser"), box.FindControl<TextBlock>("TxtTitle")!.Text);
            Assert.Equal(Loc.GetF("msg_browser_no_default_for", "sign in with Patreon") + Loc.GetF("msg_browser_link_copied", url),
                box.FindControl<TextBlock>("TxtMessage")!.Text);
            box.Close();
            await open;
        }
        finally
        {
            dialog.Close();
            ExternalOpener.Shell = oldShell;
        }
    });
}
