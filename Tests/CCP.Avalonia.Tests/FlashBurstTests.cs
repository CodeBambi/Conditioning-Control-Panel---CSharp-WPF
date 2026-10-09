using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

/// <summary>Shatter, Pop sparks, the pendulum rig, the drag that keeps its place, the lucky chime.</summary>
public class FlashBurstTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static int OpaquePixels(RenderTargetBitmap rtb)
    {
        var stride = rtb.PixelSize.Width * 4;
        var bytes = new byte[stride * rtb.PixelSize.Height];
        var h = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(rtb.PixelSize), h.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { h.Free(); }
        var n = 0;
        for (var i = 3; i < bytes.Length; i += 4) if (bytes[i] > 0) n++;
        return n;
    }

    [Fact]
    public async Task Shards_draw_from_the_snapshot_fall_and_the_window_closes_when_done()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var src = new RenderTargetBitmap(new PixelSize(90, 60));
            using (var dc = src.CreateDrawingContext()) dc.FillRectangle(Brushes.Red, new Rect(0, 0, 90, 60));
            var state = FlashShatter.Create(100, 100, 90, 60, 0, 0, 400, 400, MotionLevel.Full, new Random(4));
            Assert.InRange(state.Shards.Length, 6, 9);

            using var canvas = new RenderTargetBitmap(new PixelSize(400, 400));
            using (var dc = canvas.CreateDrawingContext()) FlashBurstWindow.DrawShards(dc, state, src);
            Assert.InRange(OpaquePixels(canvas), 90 * 60 - 200, 90 * 60 + 200);   // whole picture, in pieces

            var w = FlashBurstWindow.CreateShatter(state, src, new PixelRect(0, 0, 400, 400))!;
            var closed = false;
            w.Closed += (_, _) => closed = true;
            for (var i = 0; i < 100 && !closed; i++) w.Tick(1 / 60.0);
            Assert.True(state.Done);
            Assert.True(closed);
            Assert.True(state.Shards.All(s => s.Dy > 0));   // gravity won
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Pop_sparks_spray_inside_their_box_and_end_with_the_exit()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var exit = FlashExit.Begin(FlashExitStyle.Pop, MotionLevel.Full, 7);
            Assert.Null(FlashBurstWindow.CreateSparks(FlashExit.Begin(FlashExitStyle.Melt, MotionLevel.Full, 7), 0, 0, 10, 10));
            var box = FlashBurstWindow.SparkBounds(200, 200, 160, 100);
            FlashExit.Step(exit, exit.DurationSec * 0.5);
            for (var i = 0; i < FlashExit.SparkCount(exit); i++)
            {
                var sp = FlashExit.Spark(exit, i);
                var x = 200 + sp.Dx * sp.Distance * 80;
                var y = 200 + sp.Dy * sp.Distance * 80;
                Assert.True(x - sp.RadiusPx >= box.X && x + sp.RadiusPx <= box.Right, $"spark {i} x");
                Assert.True(y - sp.RadiusPx >= box.Y && y + sp.RadiusPx <= box.Bottom, $"spark {i} y");
            }
            using var canvas = new RenderTargetBitmap(new PixelSize(400, 400));
            using (var dc = canvas.CreateDrawingContext()) FlashBurstWindow.DrawSparks(dc, exit, 200, 200, 80);
            Assert.True(OpaquePixels(canvas) > 0);

            var w = FlashBurstWindow.CreateSparks(exit, 200, 200, 160, 100)!;
            Assert.True(w.Tick(0.01));
            FlashExit.Step(exit, exit.DurationSec);
            Assert.False(w.Tick(0.01));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Pendulum_rig_turns_the_card_in_a_square_window_that_only_moves()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new FlashOverlayWindow();
            try
            {
                w.ApplyLook(FlashGlowLook.None, 0, 0, animate: false);
                var side = w.MakeRig(300, 400, 1.0);
                Assert.Equal(502, side);   // the diagonal, so any angle fits
                var m = FlashMotion.Create(FlashMotionStyle.Pendulum, 800, 300, 300, 400, 0, 0, 1920, 1080, MotionLevel.Full, new Random(2));
                Assert.Equal(FlashMotionStyle.Pendulum, m.Style);
                var rect = w.ApplyMotion(m, new PixelSize(side, side));
                Assert.Equal(new PixelSize(side, side), rect.Size);
                Assert.Equal(m.X + m.W / 2, rect.X + side / 2.0, 0);
                Assert.Equal(m.AngleRad, w.RigAngleRad, 6);

                FlashMotion.Step(m, 0.4);
                var again = w.ApplyMotion(m, rect.Size);
                Assert.Equal(rect.Size, again.Size);   // a move, never a resize
                Assert.Equal(m.AngleRad, w.RigAngleRad, 6);

                // A drag takes the rope off: the card stands level from then on.
                FlashDrag.Begin(m, m.X + 10, m.Y + 10, 0);
                w.ApplyMotion(m, rect.Size);
                Assert.Equal(0, w.RigAngleRad);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Rig_exit_frame_keeps_the_tilt_and_the_centre()
    {
        var content = new Border();
        var exit = FlashExit.Begin(FlashExitStyle.Pop, MotionLevel.Full, 2);
        FlashOverlayWindow.ApplyExitFrame(content, exit, new Size(200, 100), Math.PI / 8);
        var m = Assert.IsType<MatrixTransform>(content.RenderTransform).Matrix;
        var centre = new Point(100, 50).Transform(m);
        Assert.Equal(100, centre.X, 3);
        Assert.Equal(50, centre.Y, 3);
        var corner = new Point(200, 50).Transform(m);   // the right middle edge, turned 22.5 degrees
        Assert.True(corner.Y > 50 + 30);
    }

    [Fact]
    public async Task A_dragged_flash_stays_where_it_was_put_on_the_next_tick()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new FlashOverlayWindow { Position = new PixelPoint(100, 100) };
            try
            {
                w.Show();
                w.Fx = new FlashFxState(0, false, 5000, new PixelRect(0, 0, 1920, 1080));
                FlashOverlay.Active.Add((w, new PixelRect(100, 100, 200, 100)));
                w.Closed += (_, _) => FlashOverlay.Active.RemoveAll(e => e.Window == w);
                var m = FlashOverlay.EnsureMotion(w);
                Assert.Same(m, w.Motion);
                var d = FlashDrag.Begin(m, 150, 150, 1000);
                FlashDrag.Sample(d, 450, 350, 1100);
                FlashOverlay.DriftTick();
                Assert.Equal(new PixelPoint(400, 300), w.Position);
                // Let go slowly (a place): the drag ends, the rect stays.
                Assert.Equal(FlashDragOutcome.Place, FlashDrag.Release(m, 1400, MotionLevel.Full));
                FlashOverlay.DriftTick();
                FlashOverlay.DriftTick();
                Assert.Equal(new PixelPoint(400, 300), w.Position);
                Assert.Equal(new PixelPoint(400, 300), FlashOverlay.Active.Single(e => e.Window == w).Rect.Position);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_pendulum_shatter_breaks_at_its_frozen_angle_about_the_pivot()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new FlashOverlayWindow();
            try
            {
                w.ApplyLook(FlashGlowLook.None, 0, 0, animate: false);
                var side = w.MakeRig(400, 300, 1.0);
                var m = FlashMotion.Create(FlashMotionStyle.Pendulum, 700, 300, 400, 300, 0, 0, 1920, 1080, MotionLevel.Full, new Random(5));
                FlashOverlay.StartDrift(w, m);
                w.ApplyMotion(m, new PixelSize(side, side));
                w.Show();
                var s = FlashOverlay.BuildShatter(w, MotionLevel.Full, new Random(1))!;
                Assert.Equal(m.AngleRad, s.FrozenAngleRad, 6);
                Assert.Equal(m.PivotX, s.PivotX, 6);
                // Cut over the picture as it sits centred in the hanging card.
                Assert.Equal(m.PivotX, s.RectX + s.RectW / 2, 0);
                Assert.Equal(m.PivotY + m.Rope, s.RectY + s.RectH / 2, 0);
                Assert.True(s.RectW > 0 && s.RectW <= 400 && s.RectH <= 300);
                Assert.Null(FlashOverlay.BuildShatter(w, MotionLevel.Off, new Random(1)));
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Lucky_chime_follows_the_perk_switch_and_the_wpf_volume_curve()
    {
        var seam = FlashOverlay.PlayChime;
        try
        {
            var played = 0;
            FlashOverlay.PlayChime = (_, _) => played++;
            FlashOverlay.PlayLuckyChime(new AppSettings { SuppressPerkNotifications = true, MasterVolume = 100 }, new Random(1));
            Assert.Equal(0, played);
            Assert.Equal(0.35f, FlashOverlay.LuckyChimeVolume(100), 4);
            Assert.Equal((float)Math.Pow(0.5, 1.5) * 0.35f, FlashOverlay.LuckyChimeVolume(50), 4);
            Assert.Equal(new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" }, FlashOverlay.LuckyChimes);
        }
        finally { FlashOverlay.PlayChime = seam; }
    }

    [Fact]
    public void Pendulum_needs_its_own_grant_and_hangs_away_from_a_neighbour()
    {
        var seam = PrizeOwnership.IsGranted;
        try
        {
            var rect = new PixelRect(700, 300, 300, 200);
            var screen = new PixelRect(0, 0, 1920, 1080);
            var s = new AppSettings { FlashMotionStyle = FlashMotionStyle.Pendulum, MotionLevel = MotionLevel.Full };
            PrizeOwnership.IsGranted = id => id == PrizeOwnership.FlashDriftBounce;
            Assert.Null(FlashOverlay.BuildMotion(rect, screen, s, new Random(3)));
            PrizeOwnership.IsGranted = id => id == PrizeOwnership.FlashPendulum;
            var lone = FlashOverlay.BuildMotion(rect, screen, s, new Random(3))!;
            Assert.Equal(FlashMotionStyle.Pendulum, lone.Style);
            Assert.Equal(960, lone.PivotX, 3);
            var second = FlashOverlay.BuildMotion(rect, screen, s, new Random(3),
                new[] { new PendulumNeighbour(lone.PivotX, FlashPendulumRig.EffectivePhase(lone)) })!;
            Assert.True(Math.Abs(second.PivotX - 960) > 100);
        }
        finally { PrizeOwnership.IsGranted = seam; }
    }
}
