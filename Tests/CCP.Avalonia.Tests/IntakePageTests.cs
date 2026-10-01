using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Graded Intake slice 2: the page is served by WebAssetServer, the host answers its
/// "ready" with WPF's init, the media it names is served under the same token, and a finished run
/// reaches the Sessions list (WPF IntakeHostService.Launch / OnPageReady / OnQuizResult).</summary>
public sealed class IntakePageTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task TheServedPageGetsInitAndItsMediaFromTheSameOrigin()
    {
        var web = Directory.CreateTempSubdirectory("intake-web-").FullName;
        var assets = Directory.CreateTempSubdirectory("intake-assets-").FullName;
        Directory.CreateDirectory(Path.Combine(web, "intake"));
        File.WriteAllText(Path.Combine(web, "intake", "index.html"), "<html></html>");
        Directory.CreateDirectory(Path.Combine(assets, "images"));
        var gif = Path.Combine(assets, "images", "a b.gif");
        File.WriteAllText(gif, "GIF89a");
        var prevAssets = CorePaths.EffectiveAssetsProvider;
        CorePaths.EffectiveAssetsProvider = () => assets;
        using var server = new WebAssetServer(web) { AssetsRoot = () => assets };
        try
        {
            string gifUrl = "";
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                EnsureApp();
                var host = new IntakeHostWindow();
                try
                {
                    host.Load(server);
                    Assert.Equal(new Uri(server.Url("intake/index.html")), host.PageUrl);
                    Assert.Equal(1, host.Web.NavigationRequests);

                    // The window never leaves the served origin.
                    Assert.True(host.Web.AllowNavigation!(new Uri($"http://127.0.0.1:{server.Port}/intake/x.html")));
                    Assert.False(host.Web.AllowNavigation!(new Uri($"http://127.0.0.1:{server.Port + 1}/intake/index.html")));
                    Assert.False(host.Web.AllowNavigation!(new Uri("https://example.com/")));

                    var init = JObject.FromObject(host.InitMessage());
                    Assert.Equal("init", (string?)init["type"]);
                    Assert.Equal("default", (string?)init["config"]!["niche"]);   // no bank in this root
                    gifUrl = (string)init["config"]!["media"]!["gifs"]![0]!;
                    Assert.Equal($"http://127.0.0.1:{server.Port}/{WebAssetServer.AssetsPrefix}images/a%20b.gif", gifUrl);
                    // A sandbox never hands the page the real AI server.
                    Assert.True(SandboxNet.Active);
                    Assert.Equal(JTokenType.Null, init["ai"]!.Type);
                }
                finally { host.Close(); }
                return Task.CompletedTask;
            });

            Assert.Equal(gif, server.ResolveFile(new Uri(gifUrl).AbsolutePath));
            Assert.Null(server.ResolveFile("/" + WebAssetServer.AssetsPrefix + "../" + Path.GetFileName(web) + "/intake/index.html"));
            using var http = new HttpClient();
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(gifUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(gifUrl + "?ccp_t=" + server.Token)).StatusCode);
        }
        finally
        {
            CorePaths.EffectiveAssetsProvider = prevAssets;
            Directory.Delete(web, true);
            Directory.Delete(assets, true);
        }
    }

    [Fact]
    public async Task ADraftedSessionJoinsTheSessionsListAndCanBeRevealed()
    {
        var root = Directory.CreateTempSubdirectory("intake-drafted-").FullName;
        var custom = Path.Combine(root, "custom");
        var builtIn = Path.Combine(root, "built-in");
        Directory.CreateDirectory(custom);
        Directory.CreateDirectory(builtIn);
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                EnsureApp();
                var files = new SessionFileService(custom, builtIn);
                var manager = new SessionManager(files);
                manager.LoadAllSessions();
                var view = new PresetsTabView();
                view.UseSessionManager(manager);

                var session = Session.MorningDrift;
                session.Id = "intake-drafted";
                session.Name = "Drafted";
                var path = Path.Combine(custom, "drafted.session.json");
                Assert.Null(view.RevealSession(session.Id));

                view.RegisterExternallySavedSession(session, path);
                view.RegisterExternallySavedSession(session, path);   // a second register adds nothing
                Dispatcher.UIThread.RunJobs();

                Assert.Single(manager.AllSessions, s => s.Id == "intake-drafted");
                Assert.Same(session, view.RevealSession("intake-drafted"));
                Assert.True(File.Exists(path));
                return Task.CompletedTask;
            });
        }
        finally { Directory.Delete(root, true); }
    }
}
