using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Settings · Data ▸ Offline mode, ticked the way a user does: WPF ChkOfflineMode_Changed
/// stops the heartbeat and greys every online control (UpdateOfflineModeUI), and unticking gives
/// them back.</summary>
public sealed class OfflineModeShellTests
{
    private sealed class Ok : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    /// <summary>Factory reset's relaunch really starts the exe off Windows, even from a path with a
    /// space (the old single Arguments string reached sh as a syntax error).</summary>
    [Fact]
    public void FactoryResetRelaunchStartsTheExe()
    {
        if (System.OperatingSystem.IsWindows()) return;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp relaunch " + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var marker = System.IO.Path.Combine(dir, "ran");
            var exe = System.IO.Path.Combine(dir, "my app");
            System.IO.File.WriteAllText(exe, $"#!/bin/sh\ntouch \"{marker}\"\n");
            System.IO.File.SetUnixFileMode(exe, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute);
            using var p = System.Diagnostics.Process.Start(DataSettingsSection.RelaunchStartInfo(exe, delaySeconds: 0))!;
            Assert.True(p.WaitForExit(10_000));
            Assert.Equal(0, p.ExitCode);
            Assert.True(System.IO.File.Exists(marker));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task TickingOfflineStopsTheHeartbeatAndGreysTheOnlineControls_UntickingRestoresThem()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            (s.OfflineMode, s.OfflineUsername) = (false, "Bambi");   // a set name skips the username prompt
            var oldSync = AccountSeed.Sync;
            var sync = new SyncPush(() => null, () => false, new Ok());
            AccountSeed.Sync = sync;
            sync.StartHeartbeat();

            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();
                var data = shell.AppSettingsPage!.FindControl<DataSettingsSection>("SectionData")!;
                var account = shell.AppSettingsPage!.FindControl<AccountSettingsSection>("SectionAccount")!;
                var home = shell.Named<SettingsTabView>("SettingsTab")!;
                var patreon = account.FindControl<Button>("BtnPatreonLogin")!;
                var site = home.FindControl<RadioButton>("RbHypnoTube")!;
                var status = home.FindControl<TextBlock>("TxtBrowserStatus")!;
                var web = home.FindControl<WebHost>("BrowserWebHost")!;
                web.Navigate(new System.Uri("https://hypnotube.com/"));   // the browser was in use
                var offlineTip = Loc.Get("tooltip_disabled_in_offline_mode");

                data.FindControl<CheckBox>("ChkOfflineMode")!.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.True(s.OfflineMode);
                Assert.False(sync.HeartbeatRunning);
                Assert.False(patreon.IsEnabled);
                Assert.Equal(offlineTip, ToolTip.GetTip(patreon));
                Assert.False(site.IsEnabled);
                Assert.Equal("● Offline", status.Text);
                Assert.Equal("about:blank", web.Source?.ToString());
                // The privacy panel joins a window later (its dialog); it greys on arrival.
                var panel = new ProfilePrivacyPanel();
                var host = new Window { Content = panel };
                host.Show();
                Assert.False(panel.FindControl<Button>("BtnDiscordTabLogin")!.IsEnabled);
                host.Close();

                data.FindControl<CheckBox>("ChkOfflineMode")!.IsChecked = false;
                Dispatcher.UIThread.RunJobs();

                Assert.False(s.OfflineMode);
                Assert.True(patreon.IsEnabled);
                Assert.NotEqual(offlineTip, ToolTip.GetTip(patreon));
                Assert.True(site.IsEnabled);
                Assert.Equal("● Ready", status.Text);
                Assert.Equal("https://bambicloud.com/", web.Source?.ToString());

                // A saved offline mode greys them from startup (WPF LoadSettings).
                s.OfflineMode = true;
                var next = new MainShellWindow();
                var nextAccount = next.AppSettingsPage!.FindControl<AccountSettingsSection>("SectionAccount")!;
                Assert.False(nextAccount.FindControl<Button>("BtnPatreonLogin")!.IsEnabled);
                next.Close();
            }
            finally
            {
                shell.Close();
                sync.StopHeartbeat();
                AccountSeed.Sync = oldSync;
                (s.OfflineMode, s.OfflineUsername) = (false, null);
            }
            await Task.CompletedTask;
        });
    }
}
