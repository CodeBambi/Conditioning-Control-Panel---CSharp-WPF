using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Games.Dtrh;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Lane c1, the DtRH descent's desktop half (WPF Services/Chaos/DtrhHostService.cs 7.1.5): init's
/// volume / persona / mod content, the native sfx bank silent while she speaks, payload-state around a
/// covering video, world freeze + dive mute released on close, the mod's own content on the ccp.mod route,
/// and the Chaos art resolver. Process-wide seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DtrhHostFramesTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static GameWindow Dtrh() => new(new GameWindow.Game("dtrh", "launcher_game_dtrh_title", "dtrh/index.html"));

    [Fact]
    public async Task Ready_InitCarriesVolumePersonaAndModContent_ThenTheLoomList()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = Dtrh();
            var posted = new List<JObject>();
            w.Posted += json => posted.Add(JObject.Parse(json));
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                var init = posted.Single(p => (string?)p["type"] == "init");
                Assert.InRange((int)init["settings"]!["masterVolume"]!, 0, 100);
                Assert.False(string.IsNullOrEmpty((string?)init["modId"]));
                Assert.True(init.ContainsKey("modContent"));
                Assert.False((bool)init["m2Test"]!);
                Assert.NotNull(init["runSetup"]);
                var order = posted.Select(p => (string?)p["type"]).ToList();
                Assert.True(order.IndexOf("manifest") < order.IndexOf("loom-list"));
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Sfx_PlaysTheNativeCue_NeverLouderThanMaster_AndStaysSilentWhileSheSpeaks()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var played = new List<(string Path, float Volume)>();
            ChaosSfx.PlayOverride = (p, v) => played.Add((p, v));
            var w = Dtrh();
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"sfx\",\"name\":\"cards_in\",\"scale\":9}");
                Assert.Single(played);
                Assert.EndsWith("cards_in.mp3", played[0].Path);
                Assert.InRange(played[0].Volume, 0f, 1f);

                w.HandleMessage("{\"type\":\"vn-speaking\",\"on\":true}");
                w.HandleMessage("{\"type\":\"sfx\",\"name\":\"cards_in\"}");
                Assert.Single(played);

                w.HandleMessage("{\"type\":\"run-started\",\"difficulty\":\"Easy\"}");   // never carry a stale duck into a run
                w.HandleMessage("{\"type\":\"sfx\",\"name\":\"cards_in\"}");
                Assert.Equal(2, played.Count);

                w.HandleMessage("{\"type\":\"sfx\",\"name\":\"../../secret\"}");            // never leaves the sounds folder
                w.HandleMessage("{\"type\":\"sfx\",\"name\":\"no_such_cue\"}");             // a missing file is silence
                Assert.Equal(2, played.Count);
            }
            finally { ChaosSfx.PlayOverride = null; w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FreezeAndMute_ReachTheCoveringVideo_AndAreReleasedWhenTheWindowCloses()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var (oldPause, oldMute) = (GameWindow.DtrhVideoPause, GameWindow.DtrhVideoMute);
            var pauses = new List<bool>(); var mutes = new List<bool>();
            GameWindow.DtrhVideoPause = pauses.Add;
            GameWindow.DtrhVideoMute = mutes.Add;
            var w = Dtrh();
            var posted = new List<JObject>();
            w.Posted += json => posted.Add(JObject.Parse(json));
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
                w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");   // idempotent
                w.HandleMessage("{\"type\":\"mute-state\",\"on\":true}");
                Assert.Equal(new[] { true }, pauses);
                Assert.Equal(new[] { true }, mutes);

                w.OnDtrhVideoStartedForTest();
                w.OnDtrhVideoEndedForTest();
                var states = posted.Where(p => (string?)p["type"] == "payload-state").ToList();
                Assert.Equal(2, states.Count);
                Assert.True((bool)states[0]["on"]!);
                Assert.False((bool)states[1]["on"]!);
                Assert.Equal("video", (string?)states[0]["kind"]);
            }
            finally { w.Close(); }
            try
            {
                Assert.False(pauses[^1]);   // nothing native stays paused...
                Assert.False(mutes[^1]);    // ...or silent after the window dies
            }
            finally { (GameWindow.DtrhVideoPause, GameWindow.DtrhVideoMute) = (oldPause, oldMute); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ModContent_BuildsPoolsPortraitAndTint_MergesMedia_AndTheRouteServesMediaOnly()
    {
        var mod = Path.Combine(Path.GetTempPath(), "ccp-c1-mod-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(mod, "resources", "dtrh");
        Directory.CreateDirectory(Path.Combine(root, "barks"));
        Directory.CreateDirectory(Path.Combine(root, "portrait"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        File.WriteAllBytes(Path.Combine(root, "barks", "fall_sensory_001.mp3"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(root, "barks", "anything.mp3"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(root, "portrait", "default.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(root, "images", "own art.png"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(root, "dtrh.json"), "{\"tintA\":\"#FF0080\",\"imageMode\":\"replace\"}");
        var (oldBase, oldPath) = (DtrhModContent.UrlBase, DtrhModContent.InstalledPathOverride);
        try
        {
            DtrhModContent.UrlBase = () => "http://h/ccp.mod/";
            DtrhModContent.InstalledPathOverride = () => mod;
            var payload = JObject.FromObject(DtrhModContent.BuildInitPayload()!);
            Assert.Equal("255,0,128", (string?)payload["tint"]!["a"]);
            Assert.Equal("replace", (string?)payload["drift"]!["mode"]);
            Assert.Equal("http://h/ccp.mod/barks/fall_sensory_001.mp3", (string?)payload["drift"]!["pools"]!["sensory"]![0]!["url"]);
            Assert.Single(payload["drift"]!["pools"]!["drift"]!);   // an unnamed clip falls in the drift pool
            Assert.Equal("http://h/ccp.mod/portrait/default.png", (string?)payload["portrait"]!["defaultUrl"]);

            var m = new GameMediaManifest.Manifest();
            m.Images.Add(new GameMediaManifest.Entry("library.png", "x"));
            DtrhModContent.MergeMedia(m);
            Assert.Equal("mod:own art.png", Assert.Single(m.Images).Name);   // replace swaps the library out
            Assert.Equal("http://h/ccp.mod/images/own%20art.png", m.Images[0].Url);

            using var server = new WebAssetServer(mod) { ModRoot = DtrhModContent.ModDtrhRoot };
            Assert.Equal(Path.Combine(root, "images", "own art.png"), server.ResolveFile("/ccp.mod/images/own%20art.png"));
            Assert.Null(server.ResolveFile("/ccp.mod/dtrh.json"));              // media only
            Assert.Null(server.ResolveFile("/ccp.mod/../../mod.json"));
            Assert.Null(server.ResolveFile("/ccp.mod/images/missing.png"));

            DtrhModContent.InstalledPathOverride = () => null;                   // a built-in mod: today's behaviour
            Assert.Null(DtrhModContent.BuildInitPayload());
        }
        finally
        {
            (DtrhModContent.UrlBase, DtrhModContent.InstalledPathOverride) = (oldBase, oldPath);
            try { Directory.Delete(mod, true); } catch { }
        }
    }

    [Fact]
    public void ChaosArt_ResolvesByConvention_UserFolderFirst_AndTheShippedArtIsBesideTheExe()
    {
        var user = Path.Combine(Path.GetTempPath(), "ccp-c1-art-" + Guid.NewGuid().ToString("N"));
        var shipped = Path.Combine(Path.GetTempPath(), "ccp-c1-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(user, "boons"));
        Directory.CreateDirectory(Path.Combine(shipped, "boons"));
        File.WriteAllBytes(Path.Combine(user, "boons", "a.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(shipped, "boons", "a.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(shipped, "recap.png"), new byte[] { 1 });
        try
        {
            ChaosArt.RootsOverride = () => new[] { user, shipped };
            Assert.Equal(Path.Combine(user, "boons", "a.png"), ChaosArt.PathFor("boons", "a"));
            Assert.Equal(Path.Combine(shipped, "recap.png"), ChaosArt.FilePath("recap.png"));
            Assert.Null(ChaosArt.PathFor("boons", "missing"));
            Assert.Null(ChaosArt.FilePath(Path.Combine("..", "secret.png")));
            Assert.Null(ChaosArt.Resolve("boons", "a"));   // not a picture: null, never a throw
        }
        finally
        {
            ChaosArt.ResetForTest();
            try { Directory.Delete(user, true); Directory.Delete(shipped, true); } catch { }
        }
        // X14: the csproj ships the art, so the recap banner and the Brain Drain bubble sprite exist.
        Assert.NotNull(ChaosArt.FilePath("recap.png"));
        Assert.NotNull(ChaosArt.PathFor("bubbles", "braindrain_melt"));
    }
}
