using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane k14: the gif rain (WPF ChaosGifCascadeOverlay) on the Back Room's click-through windows, its
/// decode budget, its IN and OUT, the room's gif-rain primitive, and the page rect of a gif-from mapped onto the
/// desktop (WPF BackRoomOverlayMath.MapFrom). Process-wide overlay state and seams, so the class runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class GifCascadeTests
{
    private static void Platform()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static RoomPicture Frames(int n)
    {
        var list = new List<Bitmap>();
        for (int i = 0; i < n; i++)
            list.Add(new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul));
        return new RoomPicture(list, TimeSpan.FromMilliseconds(40));
    }

    /// <summary>Swaps every seam, runs the body, puts every seam back and takes the rain down.</summary>
    private static Task WithSeams(List<string> pool, Func<Window, List<(string Path, bool Animate)>, Task> body, long fileLength = 1000)
        => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Platform();
            var (pick, decode, length, can) = (GifCascadeOverlay.PickFiles, GifCascadeOverlay.Decode, GifCascadeOverlay.FileLength, GifCascadeOverlay.CanAnimate);
            var decoded = new List<(string, bool)>();
            var host = new Window();
            BackRoomOverlays.SkipPlatformChecksForTests = true;
            GifCascadeOverlay.PickFiles = _ => pool.ToList();
            GifCascadeOverlay.Decode = (path, _, animate) => { lock (decoded) decoded.Add((path, animate)); return Frames(animate ? 3 : 1); };
            GifCascadeOverlay.FileLength = _ => fileLength;
            GifCascadeOverlay.CanAnimate = p => p.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            host.Show();
            try { await body(host, decoded); }
            finally
            {
                GifCascadeOverlay.CloseActive();
                await GifCascadeOverlay.Loading;
                (GifCascadeOverlay.PickFiles, GifCascadeOverlay.Decode, GifCascadeOverlay.FileLength, GifCascadeOverlay.CanAnimate) = (pick, decode, length, can);
                BackRoomOverlays.SkipPlatformChecksForTests = false;
                host.Close();
            }
        });

    [Fact]
    public Task ItRainsOnClickThroughWindows_FallsIn_FallsOut_AndClosesItself() => WithSeams(
        new List<string> { "C:/a/one.png" }, async (host, decoded) =>
        {
            // 2 clips a second for one second, 400 px, 3.6 px a frame.
            GifCascadeOverlay.Show(host, "test", 2, 1, 400, 3.6, 0.9, 0.45);
            await GifCascadeOverlay.Loading;
            Assert.True(GifCascadeOverlay.IsUp && GifCascadeOverlay.IsRaining);
            Assert.Equal("test", GifCascadeOverlay.Owner);
            var window = Assert.Single(GifCascadeOverlay.Windows);
            Assert.False(window.IsHitTestVisible);
            Assert.False(window.ShowActivated);

            // IN: the first clip is born above the top edge, small, at the asked opacity (clamped 0..1).
            var first = Assert.Single(GifCascadeOverlay.Clips);
            Assert.InRange(first.Y, -400, -300);                        // a real tick may have moved it a little
            Assert.Equal(0.9, first.Image.Opacity, 6);
            Assert.False(first.Animated);
            Assert.Null(first.Image.Effect);
            Assert.NotNull(first.Image.Source);                         // the off-thread decode landed

            // It moves down on Advance, and more spawn inside the window (one every 500 ms).
            GifCascadeOverlay.Advance(100);
            Assert.True(GifCascadeOverlay.Clips.First().Y > -400);
            for (int i = 0; i < 9; i++) GifCascadeOverlay.Advance(100);
            Assert.True(GifCascadeOverlay.Alive >= 2);

            // OUT: after the spawn window nothing new is born; every clip leaves off the bottom and the windows close.
            int born = decoded.Count;
            for (int i = 0; i < 400 && GifCascadeOverlay.IsUp; i++) GifCascadeOverlay.Advance(100);
            await GifCascadeOverlay.Loading;
            Assert.False(GifCascadeOverlay.IsUp);
            Assert.False(GifCascadeOverlay.IsRaining);
            Assert.Equal(0, GifCascadeOverlay.Alive);
            Assert.True(decoded.Count <= born + 1);
            Assert.False(window.IsVisible);
        });

    [Fact]
    public Task TheDecodeBudgetHolds_ThreeAnimate_FourteenAlive_AHeavyGifFallsAsAStill() => WithSeams(
        new List<string> { "C:/a/one.gif", "C:/b/one.gif", "C:/c/two.gif" }, async (host, decoded) =>
        {
            // A dense, slow rain: 100 clips a second for 30 s, barely moving, so they pile up.
            GifCascadeOverlay.Show(host, "test", 100, 30, 400, 0.5, 2.0, 1);   // opacity 2.0 is clamped
            await GifCascadeOverlay.Loading;
            for (int i = 0; i < 60; i++) GifCascadeOverlay.Advance(33);
            await GifCascadeOverlay.Loading;
            Assert.Equal(GifCascadeOverlay.MaxConcurrent, GifCascadeOverlay.Alive);
            Assert.Equal(GifCascadeOverlay.MaxAnimated, GifCascadeOverlay.AnimatedAlive);
            Assert.Equal(GifCascadeOverlay.MaxAnimated, GifCascadeOverlay.Clips.Count(c => c.Animated));
            Assert.All(GifCascadeOverlay.Clips, c => Assert.InRange(c.Image.Opacity, 0, 1));
            // Past the budget a gif is decoded as a still (animate = false), never as a fourth animation.
            lock (decoded)
            {
                Assert.Equal(GifCascadeOverlay.MaxAnimated, decoded.Count(d => d.Animate));
                Assert.True(decoded.Count(d => !d.Animate) >= GifCascadeOverlay.MaxConcurrent - GifCascadeOverlay.MaxAnimated);
            }
            // Same file name in two folders is two pictures: the pool keeps whole paths.
            Assert.Contains(GifCascadeOverlay.Clips, c => c.Path == "C:/a/one.gif" || c.Path == "C:/b/one.gif");

            // Panic / close: everything goes at once and the budget is handed back.
            GifCascadeOverlay.CloseActive();
            Assert.False(GifCascadeOverlay.IsUp || GifCascadeOverlay.IsRaining);
            Assert.Equal(0, GifCascadeOverlay.AnimatedAlive);
        });

    [Fact]
    public Task AGifOverTheByteCapNeverAnimates() => WithSeams(
        new List<string> { "C:/a/huge.gif" }, async (host, decoded) =>
        {
            GifCascadeOverlay.Show(host, "test", 2, 2, 400, 3.6, 0.9, 0.45);
            await GifCascadeOverlay.Loading;
            Assert.Equal(0, GifCascadeOverlay.AnimatedAlive);
            lock (decoded) Assert.All(decoded, d => Assert.False(d.Animate));
        }, fileLength: GifCascadeOverlay.AnimatedMaxBytes + 1);

    [Fact]
    public Task AnEmptyPoolRainsNothing_AndRemoteEntriesAreSkipped() => WithSeams(
        new List<string> { "https://example.invalid/a.webp" }, async (host, decoded) =>
        {
            GifCascadeOverlay.Show(host, "test", 2, 2, 400, 3.6, 0.9, 0.45);
            await GifCascadeOverlay.Loading;
            Assert.False(GifCascadeOverlay.IsUp);
            Assert.False(GifCascadeOverlay.IsRaining);
            Assert.Empty(decoded);
        });

    [Fact]
    public Task TheRoomsGifRainUsesTheCascade_AndTheRoomsStopAllTakesOnlyItsOwnDown() => WithSeams(
        new List<string> { "C:/a/one.png", "C:/a/two.png" }, async (host, _) =>
        {
            var keep = BackRoomFxHead.Host;
            BackRoomFxHead.Host = host;
            try
            {
                Assert.Contains(FxPrim.GifRain, BackRoomFxHead.Supported);
                var head = new BackRoomFxHead();
                head.GifRain(14, 3000, 0.9);
                await GifCascadeOverlay.Loading;
                Assert.True(GifCascadeOverlay.IsRaining);
                Assert.Equal(BackRoomOverlays.RainOwner, GifCascadeOverlay.Owner);

                // A second rain while one falls is dropped (WPF: a second cascade on top is noise).
                var windows = GifCascadeOverlay.Windows.ToList();
                head.GifRain(14, 3000, 0.9);
                await GifCascadeOverlay.Loading;
                Assert.Equal(windows, GifCascadeOverlay.Windows.ToList());

                BackRoomOverlays.StopAll();
                Assert.False(GifCascadeOverlay.IsUp || GifCascadeOverlay.IsRaining);

                // Somebody else's cascade survives the room's teardown.
                GifCascadeOverlay.Show(host, "chaos", 2, 2, 400, 3.6, 0.9, 0.45);
                await GifCascadeOverlay.Loading;
                BackRoomOverlays.StopAll();
                Assert.True(GifCascadeOverlay.IsRaining);
            }
            finally { BackRoomFxHead.Host = keep; }
        });

    [Fact]
    public void ScaleAt_GrowsFromTheStartScaleToFullBy75PercentDown()
    {
        Assert.Equal(1, GifCascadeOverlay.ScaleAt(-400, 1000, 1));
        Assert.Equal(0.45, GifCascadeOverlay.ScaleAt(-400, 1000, 0.45), 6);
        Assert.Equal(0.45 + 0.55 * 0.5, GifCascadeOverlay.ScaleAt(375, 1000, 0.45), 6);
        Assert.Equal(1, GifCascadeOverlay.ScaleAt(750, 1000, 0.45), 6);
        Assert.Equal(1, GifCascadeOverlay.ScaleAt(5000, 1000, 0.45), 6);
    }

    // ---- gif-from: the page rect on the desktop ---------------------------------------------------

    private static readonly IReadOnlyList<(Rect, bool)> TwoScreens = new[]
    {
        (new Rect(0, 0, 1920, 1080), true),
        (new Rect(1920, 0, 2560, 1440), false),
    };

    [Fact]
    public void MapFrom_PutsThePageRectOnTheRoomsScreen_ThroughTheCssScale()
    {
        // The room's web view sits on the second monitor at 150%: 1200 x 800 px on screen = 800 x 533 CSS px.
        var vp = new RoomViewport(false, new Rect(2000, 100, 1200, 800), 1.5);
        var t = BackRoomFromMap.MapFrom(new FxCssRect(100, 40, 60, 44), vp, TwoScreens);
        Assert.Equal(1, t.ScreenIndex);
        Assert.False(t.Centred);
        Assert.Equal(new Rect(2000 + 150, 100 + 60, 90, 66), t.RectPx);

        // No rect, a rect smaller than 8 px, larger than the viewport, or wholly outside it: the room control's centre.
        foreach (var bad in new FxCssRect?[] { null, new FxCssRect(10, 10, 4, 4), new FxCssRect(0, 0, 900, 100), new FxCssRect(5000, 10, 60, 44), new FxCssRect(double.NaN, 0, 60, 44) })
        {
            var c = BackRoomFromMap.MapFrom(bad, vp, TwoScreens);
            Assert.True(c.Centred);
            Assert.Equal(1, c.ScreenIndex);
            Assert.Equal(2600, c.RectPx.Center.X, 6);
            Assert.Equal(500, c.RectPx.Center.Y, 6);
            Assert.Equal(RoomOverlayMath.CentreBoxW * 1.5, c.RectPx.Width, 6);
        }

        // No viewport, or a minimised room: the centre of the primary screen.
        foreach (var none in new[] { null, new RoomViewport(true, default, 1) })
        {
            var p = BackRoomFromMap.MapFrom(new FxCssRect(100, 40, 60, 44), none, TwoScreens);
            Assert.True(p.Centred);
            Assert.Equal(0, p.ScreenIndex);
            Assert.Equal(new Point(960, 540), p.RectPx.Center);
        }
        Assert.Equal(-1, BackRoomFromMap.MapFrom(null, vp, Array.Empty<(Rect, bool)>()).ScreenIndex);
    }

    [Fact]
    public Task AGifFromStartsFromThePagesRect_WhenTheViewportIsKnown_ElseFromTheCentre() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var load = BackRoomOverlays.LoadPicture;
        var host = new Window();
        BackRoomOverlays.SkipPlatformChecksForTests = true;
        BackRoomOverlays.LoadPicture = (_, _) => Frames(1);
        host.Show();
        try
        {
            // No viewport: the centre box of the stage, as before.
            BackRoomOverlays.GifFrom(host, "C:/a/one.gif", 4.0 / 3, 3400, 1, 1, null);
            await BackRoomOverlays.Loading;
            var stage = Assert.Single(BackRoomOverlays.WindowsOf(BackRoomOverlays.From));
            Assert.True(BackRoomOverlays.LastFrom.Centred);
            Assert.Equal(stage.Width / 2, BackRoomOverlays.LastFrom.From.Center.X, 3);
            Assert.Equal(RoomOverlayMath.CentreBoxW, BackRoomOverlays.LastFrom.From.Width, 6);

            // A viewport on that screen: the rect lands at control + css * k, in the stage's DIPs.
            var s = stage.ScreenPx;
            double k = stage.Scale;
            var vp = new RoomViewport(false, new Rect(s.X + 100 * k, s.Y + 50 * k, 400 * k, 300 * k), k);
            BackRoomOverlays.GifFrom(host, "C:/a/one.gif", 4.0 / 3, 3400, 1, 1, null, new FxCssRect(20, 30, 60, 44), vp);
            await BackRoomOverlays.Loading;
            Assert.False(BackRoomOverlays.LastFrom.Centred);
            Assert.Equal(120, BackRoomOverlays.LastFrom.From.X, 3);
            Assert.Equal(80, BackRoomOverlays.LastFrom.From.Y, 3);
            Assert.Equal(60, BackRoomOverlays.LastFrom.From.Width, 3);
            Assert.Equal(44, BackRoomOverlays.LastFrom.From.Height, 3);
            var image = Assert.Single(BackRoomOverlays.ImagesOf(BackRoomOverlays.From));
            Assert.Equal(120, Canvas.GetLeft(image), 3);
            Assert.Equal(80, Canvas.GetTop(image), 3);
        }
        finally
        {
            BackRoomOverlays.StopAll();
            BackRoomOverlays.LoadPicture = load;
            BackRoomOverlays.SkipPlatformChecksForTests = false;
            host.Close();
        }
    });
}
