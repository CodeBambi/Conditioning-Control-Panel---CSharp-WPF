using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using CCP.Avalonia.Tests.Board;
using ConditioningControlPanel.Avalonia.Controls.Home;
using ConditioningControlPanel.Avalonia.Views.Windows;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests.Home;

/// <summary>
/// The Home logo dial must MOVE (owner, 2026-10-09: "the logo isnt animating now"). Each frame the
/// renderer draws at a later phase has to differ from the one before, on the same renderer, so a
/// cached layer can never pin the dial to its first frame; and the dial on the real Home has to
/// put those frames on its surface.
/// </summary>
public class LogoDialMotionTests
{
    private static string Artwork()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Assets", "branding", "ccp-logo.jpg")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, "Assets", "branding", "ccp-logo.jpg");
    }

    private static byte[] Frame(DashboardLogoRenderer r, int side, double phase, double drive)
    {
        using var bmp = new SKBitmap(side, side, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp)) { c.Clear(SKColors.Transparent); r.Draw(c, side, side, phase, drive); }
        return bmp.Bytes;
    }

    private static int Diff(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return int.MaxValue;
        int n = 0;
        for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 8) n++;
        return n;
    }

    [Fact]
    public void SuccessiveFramesOnOneRendererDiffer()
    {
        using var s = File.OpenRead(Artwork());
        using var r = new DashboardLogoRenderer(s);
        const int side = 360;
        byte[]? prev = null;
        for (int i = 0; i < 6; i++)
        {
            var f = Frame(r, side, i * .35, .2);
            if (prev != null) Assert.True(Diff(prev, f) > 200, $"frame {i} matches frame {i - 1}: {Diff(prev, f)} bytes differ");
            prev = f;
        }
    }

    [Fact]
    public Task The_dial_on_Home_puts_new_frames_on_its_surface() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow { Width = 1600, Height = 1000 };
            shell.Show();
            void Pump()
            {
                Thread.Sleep(40);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);   // the worker frame
                Dispatcher.UIThread.RunJobs();
            }
            for (int i = 0; i < 6; i++) Pump();
            var dial = shell.GetVisualDescendants().OfType<AnimatedLogoDial>().First();
            Assert.True(dial.HasArtwork, "dial fell back to the wordmark");
            Assert.True(dial.IsAnimating, "dial clock is parked");
            byte[]? first = null;
            int changes = 0, paints0 = dial.Surface.PaintCount;
            for (int i = 0; i < 12; i++)
            {
                Pump();
                using var b = dial.Surface.CopyBacking();
                Assert.NotNull(b);
                var bytes = b!.Bytes;
                if (first != null && Diff(first, bytes) > 200) changes++;
                first = bytes;
            }
            Assert.True(dial.Surface.PaintCount > paints0, "surface never repainted");
            Assert.True(dial.WorkerFrames > 0, "the worker never published a frame");
            Assert.True(changes >= 6, $"only {changes} of 11 surface frames changed (paints {dial.Surface.PaintCount - paints0})");
        }
        finally
        {
            shell?.Close();
            BoardHeadTests.Unpin();
        }
        return Task.CompletedTask;
    });
}
