using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>THE LOOM window (WPF LoomHostService): the page's ready gets the saved-spiral list and
/// nothing else, save / delete answer loom-result and re-post the list, names from the page cannot
/// leave the Spirals folder, panic closes it, and the Spiral card opens it and follows the store.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LoomHostTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static readonly byte[] Gif = "GIF89a"u8.ToArray().Concat(new byte[10]).Append((byte)0x3B).ToArray();

    private static string Frame(string type, params (string K, JToken V)[] rest)
    {
        var o = new JObject { ["type"] = type };
        foreach (var (k, v) in rest) o[k] = v;
        return o.ToString();
    }

    [Fact]
    public async Task Ready_PostsOnlyTheList_SaveAndDeleteAnswerAndRepost()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new GameWindow(GameWindow.Games[GameWindow.LoomId]);
            var posted = new List<JObject>();
            w.Posted += json => posted.Add(JObject.Parse(json));
            var gif = Path.Combine(DtrhLoomStore.SpiralsFolder, "loom_k14-weave.gif");
            w.Show();
            try
            {
                Assert.Equal("dtrh/loom.html", w.Spec.Page);
                Assert.Equal("browser_data_loom", w.Web.Profile);

                w.HandleMessage(Frame("ready"));
                Assert.True(w.IsReady);
                Assert.Single(posted);                                   // no init, no manifest (WPF OnReady)
                Assert.Equal("loom-list", (string?)posted[0]["type"]);

                posted.Clear();
                w.HandleMessage(Frame("loom-save", ("name", "K14 Weave"), ("gifBase64", Convert.ToBase64String(Gif)),
                    ("params", new JObject { ["arms"] = 3 })));
                var result = posted.First(p => (string?)p["type"] == "loom-result");
                Assert.Equal("save", (string?)result["op"]);
                Assert.True((bool)result["ok"]!);
                Assert.Equal("k14-weave", (string?)result["slug"]);
                Assert.True(File.Exists(gif));
                var list = posted.Last(p => (string?)p["type"] == "loom-list");
                var row = list["spirals"]!.Single(s => (string?)s["slug"] == "k14-weave");
                // The thumbnail comes off the asset server's ccp.spirals route (the request gate), and it resolves.
                var url = new Uri((string)row["url"]!);
                Assert.Contains("ccp.spirals/loom_k14-weave.gif", url.AbsolutePath);
                Assert.Equal(3, (int)row["params"]!["arms"]!);
                Assert.True(WebAssetServer.Shared.Hosts.ContainsKey("ccp.spirals"));
                Assert.Equal(Path.GetFullPath(gif), WebAssetServer.Shared.ResolveFile("ccp.spirals/loom_k14-weave.gif"));
                Assert.Null(WebAssetServer.Shared.ResolveFile("ccp.spirals/loom_k14-weave.json"));
                Assert.Null(WebAssetServer.Shared.ResolveFile("ccp.spirals/..%2Fsettings.json"));

                // A second save of the same name without overwrite is refused, never clobbered.
                w.HandleMessage(Frame("loom-save", ("name", "K14 Weave"), ("gifBase64", Convert.ToBase64String(Gif))));
                Assert.Equal("exists", (string?)posted.Last(p => (string?)p["type"] == "loom-result")["error"]);

                posted.Clear();
                w.HandleMessage(Frame("loom-delete", ("slug", "k14-weave")));
                result = posted.First(p => (string?)p["type"] == "loom-result");
                Assert.Equal("delete", (string?)result["op"]);
                Assert.True((bool)result["ok"]!);
                Assert.False(File.Exists(gif));
                Assert.Contains(posted, p => (string?)p["type"] == "loom-list");

                // sfx and an unknown slug on reveal are swallowed without a frame back.
                posted.Clear();
                w.HandleMessage(Frame("sfx", ("name", "definitely-not-a-cue"), ("scale", 9)));
                w.HandleMessage(Frame("loom-reveal", ("slug", "../../windows")));
                Assert.Empty(posted);
            }
            finally
            {
                try { File.Delete(gif); File.Delete(Path.ChangeExtension(gif, ".json")); } catch { }
                w.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("..\\..\\evil")]
    [InlineData("C:/Windows/evil")]
    [InlineData("evil.exe")]
    [InlineData("/etc/evil")]
    public async Task PageNames_NeverLeaveTheSpiralsFolder_OrChooseAnExtension(string name)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new GameWindow(GameWindow.Games[GameWindow.LoomId]);
            var posted = new List<JObject>();
            w.Posted += json => posted.Add(JObject.Parse(json));
            var root = Path.GetFullPath(DtrhLoomStore.SpiralsFolder);
            string? written = null;
            try
            {
                w.HandleMessage(Frame("loom-save", ("name", name), ("gifBase64", Convert.ToBase64String(Gif))));
                var result = posted.Single(p => (string?)p["type"] == "loom-result");
                if ((bool)result["ok"]!)
                {
                    var slug = (string)result["slug"]!;
                    Assert.Matches("^[a-z0-9_-]{1,24}$", slug);
                    written = Path.Combine(root, "loom_" + slug + ".gif");
                    Assert.True(File.Exists(written));                    // inside Spirals, host-chosen name
                }
                // A delete frame carrying the raw name is refused (slugs are whitelisted, never sanitised on delete).
                posted.Clear();
                w.HandleMessage(Frame("loom-delete", ("slug", name)));
                result = posted.Single(p => (string?)p["type"] == "loom-result");
                Assert.False((bool)result["ok"]!);
                Assert.Equal("bad-name", (string?)result["error"]);
                if (written != null) Assert.True(File.Exists(written));
            }
            finally
            {
                if (written != null) { try { File.Delete(written); File.Delete(Path.ChangeExtension(written, ".json")); } catch { } }
                w.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Panic_ClosesTheLoom_AndItStopsFollowingTheStore()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new GameWindow(GameWindow.Games[GameWindow.LoomId]);
            var posted = new List<JObject>();
            w.Posted += json => posted.Add(JObject.Parse(json));
            w.Show();
            w.HandleMessage(Frame("ready"));
            Assert.True(GameWindow.IsAnyOpen());
            GameWindow.CloseAllForPanic();
            Assert.True(w.IsClosedOrClosing);
            Assert.False(w.IsVisible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SpiralCard_ShowsTheLoomButton_OpensIt_AndOffersVideoFiles()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var keep = SpiralFeatureControl.OpenLoom;
            int opened = 0;
            SpiralFeatureControl.OpenLoom = () => opened++;
            try
            {
                var card = new SpiralFeatureControl();
                var btn = card.FindControl<Button>("BtnOpenLoom")!;
                Assert.True(btn.IsVisible);
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, opened);

                var types = SpiralFeatureControl.SpiralPickerTypes();
                var patterns = types.SelectMany(t => t.Patterns ?? Array.Empty<string>()).ToList();
                foreach (var ext in new[] { "*.gif", "*.png", "*.mp4", "*.webm", "*.mov", "*.avi", "*.mkv" })
                    Assert.Contains(ext, patterns);
            }
            finally { SpiralFeatureControl.OpenLoom = keep; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SpiralCard_LibraryFollowsTheStore_WhileItIsOnScreen()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var card = new SpiralFeatureControl();
            var host = new Window { Content = card, Width = 900, Height = 700 };
            var gif = Path.Combine(DtrhLoomStore.SpiralsFolder, "loom_k14-live.gif");
            host.Show();
            try
            {
                var panel = card.FindControl<Panel>("SpiralLibraryPanel")!;
                int before = panel.Children.Count;
                var (ok, _, _) = DtrhLoomStore.Save("k14 live", Convert.ToBase64String(Gif), null, false);
                Assert.True(ok);
                for (int i = 0; i < 20 && panel.Children.Count == before; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }
                Assert.Equal(before + 1, panel.Children.Count);

                Assert.True(DtrhLoomStore.Delete("k14-live").Ok);
                for (int i = 0; i < 20 && panel.Children.Count != before; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }
                Assert.Equal(before, panel.Children.Count);
            }
            finally
            {
                try { File.Delete(gif); File.Delete(Path.ChangeExtension(gif, ".json")); } catch { }
                host.Close();
            }
        });
    }
}
