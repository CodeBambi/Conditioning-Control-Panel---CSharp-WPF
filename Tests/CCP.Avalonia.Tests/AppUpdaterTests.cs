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

    [Fact]
    public async Task LinuxPillShowsVersionOpensPageAndNeverDownloads()
    {
        var feed = new FakeFeed();
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        var oldOpen = AppUpdater.OpenUrl;
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

                var shell = new MainShellWindow();
                shell.Show();
                await AppUpdater.StartupAsync(shell);

                Assert.False(File.Exists(ReleaseFeed.AttemptFilePath(CorePaths.UserData)));
                Assert.False(File.Exists(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData)));
                var pill = shell.Named<Button>("BtnUpdateAvailable")!;
                Assert.Equal(Loc.GetF("btn_update_to_version", "99.1.0"), pill.Content);
                Assert.Equal(Loc.GetF("tooltip_update_to_version_download", "99.1.0"), ToolTip.GetTip(pill));

                await AppUpdater.PillClickedAsync(shell);
                Assert.Equal(ReleaseLinks.ReleasesPageUrl, opened);
                // Only the feed was read: no releases/tags lookup, no asset GET.
                Assert.Equal(new[] { "/api/releases/latest" }, feed.Requests);
                shell.Close();
            }
            finally
            {
                AppUpdater.Handler = null;
                AppUpdater.OpenUrl = oldOpen;
                CoreReleaseContent.AppVersionProvider = oldVersion;
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null);
            }
        });
    }
}
