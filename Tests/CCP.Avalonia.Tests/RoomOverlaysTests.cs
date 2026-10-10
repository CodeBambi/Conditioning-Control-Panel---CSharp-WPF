using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane k12: the spiral's hold and video spiral, the Back Room's own overlay windows (hero, glitch
/// wash, gif-from, wash picture, tunnel, Loom spiral), the loopback-to-file map and Deeper's screen shake.
/// Process-wide overlay state, so the class runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class RoomOverlaysTests
{
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    private static void Platform()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static async Task SettleSpiral()
    {
        for (var i = 0; i < 100 && SpiralOverlay.Decoding is { } d; i++) await d;
    }

    private static RoomPicture TwoFrames() => new(new List<Bitmap>
    {
        new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul),
        new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul),
    }, TimeSpan.FromMilliseconds(40));

    // ---- the spiral's hold ------------------------------------------------------------------------

    [Fact]
    public Task AHoldShowsTheSpiralWithTheUsersSwitchOff_AndReleaseTakesItDown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var dir = Directory.CreateTempSubdirectory("ccp-k12-").FullName;
        var gif = Path.Combine(dir, "woven.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        var s = CoreSettings.Current;
        var saved = s.SpiralEnabled;
        var host = new Window();
        host.Show();
        try
        {
            SpiralOverlay.SkipPlatformChecksForTests = true;
            await SettleSpiral();
            s.SpiralEnabled = false;
            SpiralOverlay.Hold(host, SpiralOverlay.RoomOwner, new SpiralHold(0.5, gif, AllScreens: true, Slow: true));
            await SettleSpiral();
            Assert.True(SpiralOverlay.IsShowing);
            Assert.False(s.SpiralEnabled);                                   // the switch is never written
            Assert.Equal(0.5, SpiralOverlay.Shown[0].Spiral.Opacity, 10);    // the hold's own opacity, as given

            // A caller's fade is a run of holds: the same windows repaint.
            var window = SpiralOverlay.Shown[0];
            SpiralOverlay.Hold(host, SpiralOverlay.RoomOwner, new SpiralHold(0.2, gif, AllScreens: true, Slow: true));
            Assert.Same(window, SpiralOverlay.Shown[0]);
            Assert.Equal(0.2, window.Spiral.Opacity, 10);

            SpiralOverlay.Release(SpiralOverlay.RoomOwner, host);
            Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
            Assert.Null(SpiralOverlay.ActiveHold);
        }
        finally
        {
            SpiralOverlay.ReleaseAllHolds();
            SpiralOverlay.CloseAll();
            SpiralOverlay.SkipPlatformChecksForTests = false;
            s.SpiralEnabled = saved;
            host.Close();
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]
    public Task AVideoSpiralPlaysIntoTheWindows_AndStopsWithThem() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var s = CoreSettings.Current;
        var saved = s.SpiralEnabled;
        var start = SpiralOverlay.VideoStart;
        var host = new Window();
        host.Show();
        var player = new FakePlayer();
        Action<Bitmap>? show = null;
        (string Path, bool Slow)? asked = null;
        try
        {
            SpiralOverlay.SkipPlatformChecksForTests = true;
            SpiralOverlay.VideoStart = (path, slow, frame) => { asked = (path, slow); show = frame; return Task.FromResult<IDisposable?>(player); };
            await SettleSpiral();
            s.SpiralEnabled = false;
            Assert.True(SpiralOverlay.IsVideo("C:/x/loop.MP4"));
            Assert.False(SpiralOverlay.IsVideo("C:/x/loop.gif"));

            SpiralOverlay.Hold(host, SpiralOverlay.DeeperOwner, new SpiralHold(0.6, "C:/nowhere/loop.mp4"));
            await Task.Yield();
            Assert.True(SpiralOverlay.IsShowing);
            Assert.True(SpiralOverlay.IsVideoPlaying);
            Assert.Equal(("C:/nowhere/loop.mp4", false), asked);
            Assert.False(SpiralOverlay.IsAnimating);                          // no frame clock: the player owns the pace

            var frame = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            show!(frame);
            Assert.Same(frame, SpiralOverlay.Shown[0].Spiral.Source);

            SpiralOverlay.Release(SpiralOverlay.DeeperOwner, host);
            Assert.False(SpiralOverlay.IsShowing);
            Assert.True(player.Disposed);
            Assert.False(SpiralOverlay.IsVideoPlaying);
        }
        finally
        {
            SpiralOverlay.VideoStart = start;
            SpiralOverlay.ReleaseAllHolds();
            SpiralOverlay.CloseAll();
            SpiralOverlay.SkipPlatformChecksForTests = false;
            s.SpiralEnabled = saved;
            host.Close();
        }
    });

    private sealed class FakePlayer : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }

    // ---- the room's picture windows ---------------------------------------------------------------

    [Fact]
    public Task TheHeroPictureComesUp_PaysOnceItIsOn_AndStopAllTakesItDown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var load = BackRoomOverlays.LoadPicture;
        var host = new Window();
        host.Show();
        int shown = 0;
        try
        {
            BackRoomOverlays.SkipPlatformChecksForTests = true;
            BackRoomOverlays.LoadPicture = (_, _) => TwoFrames();
            BackRoomOverlays.Full(BackRoomOverlays.Hero, host, "C:/a/one.gif", 1000, 0.8, () => shown++);
            await BackRoomOverlays.Loading;
            Assert.True(BackRoomOverlays.IsUp(BackRoomOverlays.Hero));
            Assert.Equal(1, shown);
            var image = Assert.Single(BackRoomOverlays.ImagesOf(BackRoomOverlays.Hero));
            Assert.NotNull(image.Source);
            Assert.InRange(image.Opacity, 0, 1);
            Assert.False(Assert.Single(BackRoomOverlays.WindowsOf(BackRoomOverlays.Hero)).IsHitTestVisible);

            // gif-from and the wash picture share the same lifetime rules.
            BackRoomOverlays.GifFrom(host, "C:/a/two.gif", 4.0 / 3, 3400, 1, 1, () => shown++);
            await BackRoomOverlays.Loading;
            BackRoomOverlays.WashWithPicture(host, "C:/a/three.gif", BackRoomFxPlan.WashPeak, BackRoomFxHead.WashEnvelope, () => shown++);
            await BackRoomOverlays.Loading;
            Assert.True(BackRoomOverlays.IsUp(BackRoomOverlays.From) && BackRoomOverlays.IsUp(BackRoomOverlays.WashPicture));
            Assert.Equal(3, shown);

            BackRoomOverlays.StopAll();
            Assert.False(BackRoomOverlays.IsUp(BackRoomOverlays.Hero) || BackRoomOverlays.IsUp(BackRoomOverlays.From)
                || BackRoomOverlays.IsUp(BackRoomOverlays.WashPicture));
        }
        finally
        {
            BackRoomOverlays.StopAll();
            BackRoomOverlays.LoadPicture = load;
            BackRoomOverlays.SkipPlatformChecksForTests = false;
            host.Close();
        }
    });

    [Fact]
    public Task APictureStoppedBeforeItsDecodeLands_NeverShows_AndNeverPays() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var load = BackRoomOverlays.LoadPicture;
        var host = new Window();
        host.Show();
        int shown = 0;
        using var gate = new ManualResetEventSlim(false);
        try
        {
            BackRoomOverlays.SkipPlatformChecksForTests = true;
            BackRoomOverlays.LoadPicture = (_, _) => { gate.Wait(5000); return TwoFrames(); };
            BackRoomOverlays.Full(BackRoomOverlays.Hero, host, "C:/a/one.gif", 1000, 0.8, () => shown++);
            BackRoomOverlays.StopAll();          // panic, suspend or the room closing while the decode runs
            gate.Set();
            await BackRoomOverlays.Loading;
            Assert.False(BackRoomOverlays.IsUp(BackRoomOverlays.Hero));
            Assert.Equal(0, shown);

            // An undecodable picture shows nothing either.
            BackRoomOverlays.LoadPicture = (_, _) => null;
            BackRoomOverlays.Full(BackRoomOverlays.Hero, host, "C:/a/bad.gif", 1000, 0.8, () => shown++);
            await BackRoomOverlays.Loading;
            Assert.False(BackRoomOverlays.IsUp(BackRoomOverlays.Hero));
            Assert.Equal(0, shown);
        }
        finally
        {
            gate.Set();
            BackRoomOverlays.StopAll();
            BackRoomOverlays.LoadPicture = load;
            BackRoomOverlays.SkipPlatformChecksForTests = false;
            host.Close();
        }
    });

    [Fact]
    public Task TunnelVisionOpensOnALevel_AndCancelClosesItAtOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var host = new Window();
        host.Show();
        try
        {
            BackRoomOverlays.SkipPlatformChecksForTests = true;
            BackRoomOverlays.Tunnel(host, 0);                                 // nothing wanted, nothing opened
            Assert.False(BackRoomOverlays.IsUp(BackRoomOverlays.TunnelSlot));
            BackRoomOverlays.Tunnel(host, 0.8);
            Assert.True(BackRoomOverlays.IsUp(BackRoomOverlays.TunnelSlot));
            BackRoomOverlays.CancelTunnel();
            Assert.False(BackRoomOverlays.IsUp(BackRoomOverlays.TunnelSlot));
            Assert.Equal(0, BackRoomOverlays.TunnelLevel);
        }
        finally
        {
            BackRoomOverlays.StopAll();
            BackRoomOverlays.SkipPlatformChecksForTests = false;
            host.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void TheCurvesMatchTheWpfNumbers_AndNeverLeaveZeroToOne()
    {
        Assert.Equal(0, RoomOverlayMath.SpiralEnvelope(0, 4000, null));
        Assert.Equal(1, RoomOverlayMath.SpiralEnvelope(1250, 4000, null), 6);
        Assert.Equal(0.5, RoomOverlayMath.SpiralEnvelope(4250, 4000, null), 6);       // half way out
        Assert.Equal(0.5, RoomOverlayMath.SpiralEnvelope(2250, 20000, 2000), 6);      // a release starts the fade early
        Assert.True(RoomOverlayMath.SpiralDone(4500, 4000, null));
        Assert.False(RoomOverlayMath.SpiralDone(4499, 4000, null));

        Assert.Equal(0, RoomOverlayMath.FullEnvelope(0, 1000, 0.8));
        Assert.Equal(0.8, RoomOverlayMath.FullEnvelope(600, 1000, 0.8), 6);
        Assert.Equal(0.4, RoomOverlayMath.FullEnvelope(1350, 1000, 0.8), 6);
        Assert.Equal(0, RoomOverlayMath.FullEnvelope(1700, 1000, 0.8));
        for (double age = -50; age < 2000; age += 37) Assert.InRange(RoomOverlayMath.FullEnvelope(age, 1000, 5), 0, 1);

        var open = RoomOverlayMath.Tunnel(1600, 1200, 0);
        var shut = RoomOverlayMath.Tunnel(1600, 1200, 1);
        Assert.Equal(0, open.Alpha);
        Assert.Equal(1000, open.Inner, 6);
        Assert.Equal(0.94, shut.Alpha, 6);
        Assert.True(shut.Inner > 0);                                                   // the centre never closes

        var from = new Rect(770, 578, 60, 44);
        var start = RoomOverlayMath.GifFrom(0, 3400, from, 1600, 1200, 4.0 / 3, 1, 1);
        Assert.Equal(from, start.Image);
        var grown = RoomOverlayMath.GifFrom(700, 3400, from, 1600, 1200, 4.0 / 3, 1, 1);
        Assert.Equal(0, grown.Image.X, 6);
        Assert.Equal(1600, grown.Image.Width, 6);
        Assert.Equal(1200, grown.Image.Height, 6);
        Assert.Equal(0.55, grown.DimAlpha, 6);
        Assert.Equal(0, RoomOverlayMath.GifFrom(3400, 3400, from, 1600, 1200, 4.0 / 3, 1, 1).ImageAlpha);

        var model = new RoomTunnelModel();
        model.Set(1, 0);
        Assert.True(model.Step(100, 100) > 0);
        model.Step(100 + RoomTunnelModel.StaleMs + 1, 16);                             // nobody refreshed the want
        Assert.Equal(0, model.Want);
    }

    // ---- the dealt url back to a file -------------------------------------------------------------

    [Fact]
    public void ADealtLoopbackUrlMapsBackToItsFile_AndNothingElseDoes()
    {
        var web = Directory.CreateTempSubdirectory("ccp-k12-web-").FullName;
        var assets = Directory.CreateTempSubdirectory("ccp-k12-assets-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(assets, "images", "set a"));
            var file = Path.Combine(assets, "images", "set a", "one.gif");
            File.WriteAllBytes(file, Convert.FromBase64String(TinyGif));
            Directory.CreateDirectory(Path.Combine(web, "backroom", "stations", "slot", "fallback"));
            var loop = Path.Combine(web, "backroom", "stations", "slot", "fallback", "gif3.webp");
            File.WriteAllBytes(loop, new byte[] { 1 });

            using var server = new WebAssetServer(web) { AssetsRoot = () => assets };
            var dealt = server.ToLoopback(new Uri("https://ccp.assets/images/set%20a/one.gif")).AbsoluteUri;
            Assert.StartsWith("http://127.0.0.1:", dealt);
            Assert.Equal(Path.GetFullPath(file), BackRoomFxHead.TryLocalPath(dealt, server));                       // the whole path
            Assert.Equal(Path.GetFullPath(file), BackRoomFxHead.TryLocalPath(dealt + "#.gif", server));              // the animated hint rides after
            Assert.Equal(Path.GetFullPath(file), BackRoomFxHead.TryLocalPath("https://ccp.assets/images/set%20a/one.gif", server));
            Assert.Equal(Path.GetFullPath(loop), BackRoomFxHead.TryLocalPath("https://ccp.game/backroom/stations/slot/fallback/gif3.webp", server));
            Assert.Null(BackRoomFxHead.TryLocalPath("https://ccp.assets/images/missing.gif", server));
            Assert.Null(BackRoomFxHead.TryLocalPath("https://ccp.assets/../../windows/win.ini", server));
            Assert.Null(BackRoomFxHead.TryLocalPath("https://example.com/one.gif", server));
            Assert.Null(BackRoomFxHead.TryLocalPath("file:///C:/windows/win.ini", server));
            Assert.Null(BackRoomFxHead.TryLocalPath(null, server));

            // A dealt clip is not drawable: the stand-in ladder ends on the bundled loop for the key.
            Assert.True(BackRoomFxHead.IsDrawablePicture("http://127.0.0.1:1/ccp.assets/a.webp?ccp_t=x#.gif"));
            Assert.False(BackRoomFxHead.IsDrawablePicture("https://cdn.example/clip.mp4"));
            Assert.Equal(loop, BackRoomFxHead.StandInFor("g7", Array.Empty<string>(), web));
            Assert.Equal(file, BackRoomFxHead.StandInFor("g7", new[] { file }, web));                                 // the player's own animated file first
            Assert.Null(BackRoomFxHead.StandInFor("g1", null, web));
        }
        finally
        {
            try { Directory.Delete(web, true); Directory.Delete(assets, true); } catch { }
        }
    }

    [Fact]
    public void TheHeadSaysItShowsOnlyWhatItCanShow()
    {
        foreach (var p in new[] { FxPrim.GifFull, FxPrim.GifFrom, FxPrim.GlitchBubbles, FxPrim.SpiralFull, FxPrim.SpiralLoom, FxPrim.Wash })
            Assert.Contains(p, BackRoomFxHead.Supported);
        Assert.Contains(FxPrim.GifRain, BackRoomFxHead.Supported);         // lane k14: the cascade (GifCascadeOverlay)
    }

    // ---- Deeper's screen shake --------------------------------------------------------------------

    [Fact]
    public Task AShakeMovesTheWindowsContent_AndStopPutsItsOwnTransformBack() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var windows = ScreenShake.WindowsProvider;
        var own = new RotateTransform(3);
        var content = new Border { RenderTransform = own };
        var window = new Window { Content = content };
        window.Show();
        try
        {
            ScreenShake.WindowsProvider = () => new[] { window };
            ScreenShake.Shake(0, 500);
            Assert.False(ScreenShake.IsRunning);
            ScreenShake.Shake(0.5, 500);
            Assert.True(ScreenShake.IsRunning);
            Assert.IsType<TranslateTransform>(content.RenderTransform);
            ScreenShake.Shake(1, 500);                                         // a new shake replaces, never stacks
            Assert.Equal(1, ScreenShake.TargetCount);
            ScreenShake.Stop();
            Assert.False(ScreenShake.IsRunning);
            Assert.Same(own, content.RenderTransform);

            Assert.Equal(14, ScreenShake.AmplitudeAt(0, 600, 14));
            Assert.Equal(7, ScreenShake.AmplitudeAt(500, 600, 14), 6);        // easing out over the last third
            Assert.Equal(0, ScreenShake.AmplitudeAt(600, 600, 14));
        }
        finally
        {
            ScreenShake.Stop();
            ScreenShake.WindowsProvider = windows;
            window.Close();
        }
        return Task.CompletedTask;
    });
}
