using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

/// <summary>The flash juice on the port's overlay window: glow card, corners, exit frames, linger.</summary>
public class FlashFxTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task Glow_wraps_the_picture_in_a_shadowed_card()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new FlashOverlayWindow();
            try
            {
                var glow = FlashFxRules.ResolveGlow(true, PerformanceTier.Quality, isLucky: true, 0, false);
                w.ApplyLook(glow, 12, glow.BlurRadius / 2, animate: false);
                var pad = Assert.IsType<Border>(w.Content);
                Assert.Equal(new Thickness(12), pad.Padding);
                var card = Assert.IsType<Border>(pad.Child);
                Assert.Equal(24, card.BoxShadow[0].Blur);
                Assert.Equal(Color.FromArgb(229, 0xFF, 0xD7, 0x00), card.BoxShadow[0].Color);
                var clip = Assert.IsType<Border>(card.Child);
                Assert.True(clip.ClipToBounds);
                Assert.Equal(new CornerRadius(12), clip.CornerRadius);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Plain_flash_keeps_the_bare_image()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new FlashOverlayWindow();
            try
            {
                w.ApplyLook(FlashGlowLook.None, 0, 0, animate: false);
                Assert.IsType<Image>(w.Content);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Exit_frame_scales_about_the_pivot_and_fades()
    {
        var content = new Border();
        var exit = FlashExit.Begin(FlashExitStyle.Spiral, MotionLevel.Full, 2);
        FlashExit.Step(exit, exit.DurationSec);   // the end: collapsed to the centre, gone
        FlashOverlayWindow.ApplyExitFrame(content, exit, new Size(200, 100));
        var m = Assert.IsType<MatrixTransform>(content.RenderTransform).Matrix;
        var centre = new Point(100, 50).Transform(m);
        Assert.Equal(100, centre.X, 3);
        Assert.Equal(50, centre.Y, 3);
        Assert.Equal(0, content.Opacity, 3);
    }

    [Fact]
    public async Task Linger_only_lengthens_a_live_flash()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new FlashOverlayWindow();
            try
            {
                w.ExpiresAt = DateTime.Now.AddMinutes(5);
                var before = w.ExpiresAt;
                w.BoostLifetime(2000);   // a stay flash keeps its minutes
                Assert.Equal(before, w.ExpiresAt);
                w.ExpiresAt = DateTime.Now;
                w.BoostLifetime(2000);
                Assert.True(w.ExpiresAt > DateTime.Now.AddMilliseconds(1500));
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }
}
