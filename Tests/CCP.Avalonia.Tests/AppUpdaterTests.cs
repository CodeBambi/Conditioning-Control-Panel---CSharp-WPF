using System;
using System.Collections.Generic;
using System.IO;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The updater on Linux: sandbox stays offline, WPF's pending outcome is consumed, the pill
/// names the fake newer version and clicking it opens the releases page without downloading.</summary>
public sealed class AppUpdaterTests
{
    private sealed class FakeFeed : HttpMessageHandler
    {
        public readonly List<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            const string json = """
                {"tag_name":"v99.1.0","body":"notes","assets":[{"name":"ConditioningControlPanel-99.1.0-Setup.exe","size":1234,
                 "browser_download_url":"http://127.0.0.1:9/dl/ConditioningControlPanel-99.1.0-Setup.exe"}]}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/api", true)]
    [InlineData("http://[::1]:9/api", true)]
    [InlineData("http://localhost:9/api", true)]
    [InlineData("http://loopback:9/", false)]
    [InlineData("http://u:p@127.0.0.1/", false)]
    [InlineData("ftp://127.0.0.1/", false)]
    [InlineData("https://api.github.com/x", false)]
    public void OnlyLiteralLoopbackOverridesAreHonoured(string url, bool honoured)
    {
        // One Core rule for CCP_UPDATE_API_URL and CCP_CONTENT_BASE_URL.
        Assert.Equal(honoured, LoopbackUrl.IsHonoured(url, out _));
        try
        {
            Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", url);
            Assert.Equal(honoured, AppUpdater.ApiBase() != null);   // sandboxed: refused = no network
        }
        finally { Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null); }
    }

    [Fact]
    public void TransientErrorsFollowWpfRules()
    {
        Assert.True(AppUpdater.IsTransientNetworkError(new InvalidOperationException("x", new System.Net.Sockets.SocketException())));
        Assert.True(AppUpdater.IsTransientNetworkError(new InvalidOperationException("The connection was closed")));
        Assert.False(AppUpdater.IsTransientNetworkError(new InvalidOperationException("Could not find Setup.exe")));
    }

    [Fact]
    public async Task LinuxPillShowsVersionOpensPageAndNeverDownloads()
    {
        // Windows CI is not an Inno install (no unins000/registry key), so CheckAsync stays quiet there.
        if (OperatingSystem.IsWindows()) Assert.Skip("Linux notify path; the Windows install path needs an Inno install.");
        var feed = new FakeFeed();
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        var oldOpen = AppUpdater.OpenUrl;
        var oldAccepted = CoreSettings.Current.HasAcceptedAgeVerification;
        string? opened = null;
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            try
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();

                // A sandbox with no loopback feed never reaches GitHub.
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null);
                Assert.Null(AppUpdater.ApiBase());
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "https://api.github.com/x");
                Assert.Null(AppUpdater.ApiBase());
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api");
                Assert.Equal("http://127.0.0.1:9/api", AppUpdater.ApiBase());

                AppUpdater.Handler = feed;
                AppUpdater.OpenUrl = (_, url) => { opened = url; return Task.CompletedTask; };
                CoreReleaseContent.AppVersionProvider = () => "6.11.3";

                // WPF's marker from a run that installed fine: consumed silently on first run.
                File.WriteAllText(ReleaseFeed.AttemptFilePath(CorePaths.UserData), "6.11.3");
                File.WriteAllText(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData), "0");

                CoreSettings.Current.HasAcceptedAgeVerification = false;
                ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
                var shell = new MainShellWindow();
                shell.Show();
                var startup = AppUpdater.StartupAsync(shell);
                await Task.Delay(1500);
                Assert.Null(AppUpdater.OpenDialog);   // waits behind the age gate
                CoreSettings.Current.HasAcceptedAgeVerification = true;
                for (var i = 0; i < 100 && AppUpdater.OpenDialog is null; i++) await Task.Delay(50);
                var dialog = AppUpdater.OpenDialog;
                Assert.NotNull(dialog);   // offered once at startup, as WPF App.xaml.cs:4903
                Assert.Equal(Loc.Get("btn_download_installer_manually"),
                    ((TextBlock)dialog!.FindControl<Button>("BtnInstall")!.Content!).Text);   // Linux: notify variant

                Assert.False(File.Exists(ReleaseFeed.AttemptFilePath(CorePaths.UserData)));
                Assert.False(File.Exists(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData)));
                var pill = shell.Named<Button>("BtnUpdateAvailable")!;
                Assert.Equal(Loc.GetF("btn_update_to_version", "99.1.0"), pill.Content);
                Assert.Equal(Loc.GetF("tooltip_update_to_version_download", "99.1.0"), ToolTip.GetTip(pill));

                await AppUpdater.PillClickedAsync(shell);
                Assert.Equal(ReleaseLinks.ReleasesPageUrl, opened);
                // Only the feed was read: no releases/tags lookup, no asset GET.
                Assert.Equal(new[] { "/api/releases/latest" }, feed.Requests);

                // "Later": the version is skipped for 24h, so the next startup neither lights nor asks.
                dialog.FindControl<Button>("BtnLater")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await startup;
                Assert.Equal("99.1.0", ReleaseFeed.GetSkippedUpdateVersion(CorePaths.UserData));
                var shell2 = new MainShellWindow();
                shell2.Show();
                await AppUpdater.StartupAsync(shell2);
                Assert.Null(AppUpdater.OpenDialog);
                Assert.NotEqual(Loc.GetF("btn_update_to_version", "99.1.0"), shell2.Named<Button>("BtnUpdateAvailable")!.Content);
                shell2.Close();
                shell.Close();
            }
            finally
            {
                AppUpdater.Handler = null;
                AppUpdater.OpenUrl = oldOpen;
                CoreReleaseContent.AppVersionProvider = oldVersion;
                CoreSettings.Current.HasAcceptedAgeVerification = oldAccepted;
                ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null);
            }
        });
    }
}
