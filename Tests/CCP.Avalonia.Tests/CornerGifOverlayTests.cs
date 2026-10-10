using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>win-corner-gif: the standalone corner overlays (WPF CornerGifService) driven from the Corner GIFs
/// dialog: enable -> click-through topmost window in the picked corner, animating; a second slot in the same
/// corner waits the 400 ms stagger and sits 40 DIP in; panic closes both without touching the settings; a
/// surviving sentinel force-disables the slots at the next start.</summary>
public sealed class CornerGifOverlayTests
{
    // 4x4, three frames, 30 ms each (same PIL-made GIF as SpiralOverlayTests).
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = TimeSpan.FromSeconds(10).Ticks;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    [Fact]
    public async Task DialogToggleShowsCornerOverlaysStaggeredAndPanicAndSentinelStopThem()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-cornergif-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.CornerGifOverlays, s.PanicKeyEnabled, s.PanicKey);
            var handler = CoreCornerGif.RefreshHandler;
            var clock = new SteppedClock();
            var shell = new MainShellWindow();
            CornerGifWindow? dialog = null;
            shell.Show();
            try
            {
                CornerGifOverlay.StopAll();
                CornerGifOverlay.Clock = clock;
                CornerGifOverlay.SkipPlatformChecksForTests = true;
                CornerGifOverlay.ManualFramesForTests = true;
                CornerGifOverlay.Seed(() => shell);
                (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
                s.CornerGifOverlays = new List<CornerGifOverlaySetting>
                {
                    new() { GifPath = gif, Position = CornerPosition.BottomRight, Size = 200, Opacity = 50 },
                    new() { GifPath = gif, Position = CornerPosition.BottomRight, Size = 100, Opacity = 30 },
                };

                // The user path: Corner GIFs dialog, slot 1's switch on.
                dialog = new CornerGifWindow();
                dialog.Show();
                var toggles = dialog.GetLogicalDescendants().OfType<CheckBox>().ToList();
                toggles[0].IsChecked = true;
                await Settle();
                Assert.Equal(new[] { 0 }, CornerGifOverlay.ShownSlots);
                var w = CornerGifOverlay.WindowFor(0)!;
                var screen = shell.Screens.Primary ?? shell.Screens.All[0];
                double k = screen.Scaling;
                Assert.True(w.Topmost && !w.ShowInTaskbar && !w.ShowActivated && !w.IsHitTestVisible);
                Assert.Equal(0.5, w.Spiral.Opacity, 3);
                Assert.Equal(screen.Bounds.Right - (int)Math.Round(200 * k), w.Position.X);
                Assert.Equal(screen.Bounds.Bottom - (int)Math.Round(200 * k), w.Position.Y);
                Assert.True(File.Exists(CornerGifOverlay.SentinelPath));
                var first = w.Spiral.Source;
                CornerGifOverlay.Advance(0);
                Assert.Equal(1, CornerGifOverlay.FrameIndexFor(0));
                Assert.NotSame(first, w.Spiral.Source);

                // Slot 2 in the same corner: a full rebuild, slot 2 waits out the 400 ms stagger.
                toggles[1].IsChecked = true;
                await Settle();
                Assert.Equal(new[] { 0 }, CornerGifOverlay.ShownSlots);
                clock.Now += TimeSpan.FromMilliseconds(CornerGifPlanner.StaggerMs).Ticks;
                CornerGifOverlay.Pump();
                await Settle();
                Assert.Equal(new[] { 0, 1 }, CornerGifOverlay.ShownSlots.OrderBy(i => i));
                var w2 = CornerGifOverlay.WindowFor(1)!;
                Assert.Equal(screen.Bounds.Right - (int)Math.Round((100 + 40) * k), w2.Position.X);   // same-corner nudge

                // Panic: both close, the switches stay on (a panic does not reconfigure the app).
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
                Assert.Empty(CornerGifOverlay.ShownSlots);
                Assert.False(CornerGifOverlay.HasActiveOverlays);
                Assert.False(File.Exists(CornerGifOverlay.SentinelPath));
                Assert.True(s.CornerGifOverlays.All(o => o.Enabled));

                // #709: a launch that finds the sentinel force-disables the slots instead of replaying them.
                File.WriteAllText(CornerGifOverlay.SentinelPath, "x");
                CornerGifOverlay.RestoreOnStartup(shell);
                await Settle();
                Assert.Empty(CornerGifOverlay.ShownSlots);
                Assert.True(s.CornerGifOverlays.All(o => !o.Enabled));
                Assert.False(File.Exists(CornerGifOverlay.SentinelPath));
            }
            finally
            {
                dialog?.Close();
                CornerGifOverlay.StopAll();
                CornerGifOverlay.SkipPlatformChecksForTests = false;
                CornerGifOverlay.ManualFramesForTests = false;
                CornerGifOverlay.Clock = TimeProvider.System;
                CoreCornerGif.RefreshHandler = handler;
                (s.CornerGifOverlays, s.PanicKeyEnabled, s.PanicKey) = saved;
                shell.Close();
                Directory.Delete(dir, true);
            }
        });
    }

    /// <summary>P04: the seam is seeded from App startup, not only by this test.</summary>
    [Fact]
    public void AppStartupSeedsTheSurfaceAndRestores()
    {
        var src = System.Text.RegularExpressions.Regex.Replace(   // P23: comments never count
            File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")), @"//[^\n]*|/\*.*?\*/", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.Contains("CornerGifOverlay.Seed(", src);
        Assert.Contains("CornerGifOverlay.RestoreOnStartup(", src);
    }

    /// <summary>Lane c1 (U13): the session-owned slot. It draws the session's own art in its corner as an
    /// animated gif, the user's own slots stand down while it is up and come back on the handback, and
    /// panic takes it off the screen.</summary>
    [Fact]
    public async Task SessionSlot_ShowsItsOwnGif_StandaloneSlotsYield_AndComeBackOnTheHandback()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-cornergif-session-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = s.CornerGifOverlays;
            var handler = CoreCornerGif.RefreshHandler;
            var clock = new SteppedClock();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CornerGifOverlay.StopAll();
                CornerGifOverlay.Clock = clock;
                CornerGifOverlay.SkipPlatformChecksForTests = true;
                CornerGifOverlay.ManualFramesForTests = true;
                CornerGifOverlay.Seed(() => shell);
                s.CornerGifOverlays = new List<CornerGifOverlaySetting>
                {
                    new() { Enabled = true, GifPath = gif, Position = CornerPosition.BottomRight, Size = 100, Opacity = 30 },
                };

                // The runner's call, through the Core seam.
                CoreCornerGif.SessionActive = true;
                CoreCornerGif.ShowSession(gif, CornerPosition.TopLeft, 150, 40);
                await Settle();
                Assert.True(CornerGifOverlay.SessionActive);
                Assert.False(CornerGifOverlay.HasStandaloneOverlays);
                var w = CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot)!;
                var screen = shell.Screens.Primary ?? shell.Screens.All[0];
                Assert.Equal(screen.Bounds.X, w.Position.X);
                Assert.Equal(screen.Bounds.Y, w.Position.Y);
                Assert.Equal(0.4, w.Spiral.Opacity, 3);
                Assert.True(w.Topmost && !w.IsHitTestVisible);
                var first = w.Spiral.Source;
                CornerGifOverlay.Advance(CornerGifOverlay.SessionSlot);       // a gif plays as a gif, never a still
                Assert.NotSame(first, w.Spiral.Source);
                Assert.False(File.Exists(CornerGifOverlay.SentinelPath));     // a session's GIF never disables the user's slots next launch

                // The user's own slot is enabled, but the corner is the session's: a rebuild leaves it down.
                CornerGifOverlay.Refresh(shell);
                clock.Now += TimeSpan.FromSeconds(1).Ticks;
                CornerGifOverlay.Pump();
                await Settle();
                Assert.Equal(new[] { CornerGifOverlay.SessionSlot }, CornerGifOverlay.ShownSlots);

                // Terminal close: the corner goes back to the user's own slots.
                CoreCornerGif.SessionActive = false;
                CoreCornerGif.HideSession(true);
                await Settle();
                clock.Now += TimeSpan.FromSeconds(1).Ticks;
                CornerGifOverlay.Pump();
                await Settle();
                Assert.False(CornerGifOverlay.SessionActive);
                Assert.Equal(new[] { 0 }, CornerGifOverlay.ShownSlots);

                // Panic stops what is on screen, the session's included.
                CoreCornerGif.SessionActive = true;
                CornerGifOverlay.ShowSession(shell, gif, CornerPosition.TopLeft, 150, 40);
                clock.Now += TimeSpan.FromSeconds(1).Ticks;
                CornerGifOverlay.Pump();
                await Settle();
                Assert.True(CornerGifOverlay.SessionActive);
                CornerGifOverlay.StopAll();
                Assert.False(CornerGifOverlay.SessionActive);
                Assert.Empty(CornerGifOverlay.ShownSlots);
            }
            finally
            {
                CoreCornerGif.SessionActive = false;
                CornerGifOverlay.StopAll();
                CornerGifOverlay.SkipPlatformChecksForTests = false;
                CornerGifOverlay.ManualFramesForTests = false;
                CornerGifOverlay.Clock = TimeProvider.System;
                CoreCornerGif.RefreshHandler = handler;
                s.CornerGifOverlays = saved;
                shell.Close();
                Directory.Delete(dir, true);
            }
        });
    }

    private static async Task Settle()
    {
        // The dialog's 150 ms debounce is bypassed by a switch (ApplyLiveAll); the seam posts.
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            while (CornerGifOverlay.LastDecode is { IsCompleted: false } d) await d;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
