using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The mandatory-video overlay headless with a real clip on the shared LibVLC: Trigger
/// opens a topmost full-screen window after the pre-roll and frames arrive; strict vetoes a close
/// and the panic key; Esc dismisses a non-strict one and credits the watch; engine Stop (panic)
/// closes it. Skips where ffmpeg (to make the clip) or libvlc is missing.</summary>
public sealed class MandatoryVideoOverlayTests
{
    [Fact]
    public void NoVideosDialogBlamesTheLengthFilterWhenItEmptiedTheLibrary()
    {
        // WPF #1352 (854ac954a): files exist, the length filter kept none -> name the filter, not "add files".
        Assert.NotNull(MandatoryVideoOverlay.Instance.Scheduler.DurationOf);   // the real overlay filters by length
        var s = CoreSettings.Current;
        var (min, max) = (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds);
        try
        {
            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (52, 170);
            var v = new ConditioningControlPanel.Services.MandatoryVideoScheduler(MandatoryVideoOverlay.Instance,
                library: () => new[] { "/v/a.mp4", "/v/b.mp4" }) { DurationOf = _ => 20 };
            Assert.False(v.Trigger());
            Assert.Equal(Loc.GetF("video_length_filter_emptied", 2, "52s - 2m 50s"), MandatoryVideoOverlay.NoVideosMessage(v));

            var empty = new ConditioningControlPanel.Services.MandatoryVideoScheduler(MandatoryVideoOverlay.Instance,
                library: Array.Empty<string>) { DurationOf = _ => 20 };
            Assert.False(empty.Trigger());
            Assert.EndsWith(Loc.Get("video_add_files_hint"), MandatoryVideoOverlay.NoVideosMessage(empty));
        }
        finally { (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (min, max); }
    }

    [Fact]
    public async Task OpensPlaysHonoursStrictAndClosesOnEscAndEngineStop()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-mvideo-").FullName;
        var clip = Path.Combine(dir, "clip.mp4");
        try
        {
            try
            {
                using var ff = Process.Start(new ProcessStartInfo("ffmpeg",
                    $"-v error -f lavfi -i testsrc=d=10:s=320x180:r=25 -pix_fmt yuv420p \"{clip}\"") { UseShellExecute = false })!;
                ff.WaitForExit();
            }
            catch (Exception) { }
            if (!File.Exists(clip)) Assert.Skip("ffmpeg not available to generate a test clip");
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }

            double credited = 0;
            CoreProgression.TrackVideoWatchedProvider = s => credited += s;
            var s = CoreSettings.Current;
            var panic = (s.PanicKeyEnabled, s.PanicKey);
            var maxLen = s.VideoMaxDurationSeconds;
            var overrides = s.PanicOverridesAll;
            var engineVideo = CoreEngine.Video;
            (s.PanicKeyEnabled, s.PanicKey) = (true, "F12");
            var saved = (s.AudioDuckingEnabled, s.VideoBlurredBackgroundEnabled, s.AttentionChecksEnabled);
            (s.AudioDuckingEnabled, s.VideoBlurredBackgroundEnabled, s.AttentionChecksEnabled) = (true, true, false);
            int ducks = 0, unducks = 0;
            var (duck, unduck) = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider);
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider) = (_ => ducks++, _ => unducks++);
            var xp = 0.0;
            CoreProgression.AddXPProvider = (x, _) => xp += x;
            var o = MandatoryVideoOverlay.Instance;
            var v = new ConditioningControlPanel.Services.MandatoryVideoScheduler(o, library: () => new[] { clip });
            CoreEngine.Video = v;
            var real = o.Scheduler;
            var listener = MandatoryVideoOverlay.PanicListenerLive;
            o.Scheduler = v;
            try
            {
                await AvaloniaTestDispatcher.RunAsync(async () =>
                {
                    if (Application.Current is null)
                        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                            .UseSkia()
                            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                            .SetupWithoutStarting();

                    async Task<Window> Open(bool strict)
                    {
                        Assert.True(v.Trigger(strict));
                        Assert.Empty(o.Windows);                          // the 1.3 s pre-roll
                        for (var i = 0; i < 100 && (o.Windows.Count == 0 || (o.Sink?.FrameCount ?? 0) < 3); i++) await Task.Delay(50);
                        Assert.Single(o.Windows);
                        Assert.True(o.Sink!.FrameCount >= 3, "frames decoded");
                        var w = o.Windows[0];
                        Assert.True(w.Topmost && w.WindowState == WindowState.FullScreen && w.IsVisible);
                        Assert.Same(o.Sink.Bitmap, o.Surfaces[0].Video.Source);
                        return w;
                    }

                    // Strict with a live global panic listener: a close and the panic key are
                    // refused here (the global path stops it).
                    MandatoryVideoOverlay.PanicListenerLive = () => true;
                    var w = await Open(strict: true);
                    w.Close();
                    w.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, "");
                    Assert.True(v.IsPlaying && w.IsVisible, "strict window survives close + panic key");
                    v.Stop();                                           // engine stop / panic
                    Assert.Empty(o.Windows);
                    Assert.False(w.IsVisible);

                    // Strict with NO live listener (#875): the panic key must escape, not trap.
                    MandatoryVideoOverlay.PanicListenerLive = () => false;
                    w = await Open(strict: true);
                    w.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, "");
                    Assert.False(v.IsPlaying, "no listener: the panic key force-stops a strict video");
                    Assert.Empty(o.Windows);

                    // Non-strict: Esc dismisses and the watched seconds are credited.
                    credited = 0;
                    w = await Open(strict: false);
                    Assert.True((ducks, unducks) == (3, 2), $"one duck ref per open, released by each close ({ducks}/{unducks})");

                    // WPF blurred fill: the 16:9 clip leaves bars on the 4:3 headless window, so the fill shows a
                    // small, non-black copy of the frame under the scrim.
                    var sf = o.Surfaces[0];
                    Assert.True(sf.Fill.IsVisible && sf.Scrim.IsVisible, $"blur fill on a {w.Bounds} window");
                    var fill = (global::Avalonia.Media.Imaging.WriteableBitmap)sf.Fill.Source!;
                    Assert.Equal(MandatoryVideoOverlay.FillWidth, fill.PixelSize.Width);
                    using (var fb = fill.Lock())
                    {
                        var lit = 0;
                        for (var i = 0; i < fb.Size.Width * fb.Size.Height; i++)
                            if ((System.Runtime.InteropServices.Marshal.ReadInt32(fb.Address + i * 4) & 0xFFFFFF) != 0) lit++;
                        Assert.True(lit > fb.Size.Width, $"fill carries the picture ({lit} lit px)");
                    }

                    // Attention targets: a spawn puts one target per screen; clicking it scores once
                    // (+15 XP) and clears it; an unclicked one expires after AttentionLifespan.
                    o.Spawn(0);
                    Assert.Equal(1, o.LiveTargets);
                    var t = (Border)sf.Layer.Children[^1];
                    global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var at = new Point(Canvas.GetLeft(t) + t.Width / 2, Canvas.GetTop(t) + t.Height / 2);
                    w.MouseDown(at, MouseButton.Left);
                    w.MouseUp(at, MouseButton.Left);
                    Assert.Equal((1, 1, 0), (v.AttentionSpawned, v.AttentionHits, o.LiveTargets));
                    Assert.Equal(15, xp);
                    o.Spawn(0);
                    o.Step(s.AttentionLifespan + 0.01, 0);
                    Assert.Equal(0, o.LiveTargets);
                    Assert.Equal(1, v.AttentionHits);
                    await Task.Delay(1200);

                    // WPF #735 grace pause: the clip's first Esc pauses it behind the card (guards asleep);
                    // Resume plays on, and the spent pause makes the next Esc a dismiss.
                    var clock = new SteppedClock();
                    o.Time = clock;
                    w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                    Assert.True(v.IsPlaying && o.GracePaused && sf.Grace.IsVisible && !sf.Layer.IsHitTestVisible, "first Esc = grace pause");
                    Assert.Contains("60", sf.Countdown.Text);
                    s.VideoMaxDurationSeconds = 1;
                    o.GuardTick();
                    Assert.True(v.IsPlaying, "the clip guards sleep through a grace pause");
                    s.VideoMaxDurationSeconds = 0;
                    clock.Step(1);
                    o.GraceTick();
                    Assert.Equal(Loc.GetF("video_grace_auto_resume_in", 59), sf.Countdown.Text);
                    // WPF's Resume is a Button: Tab reaches it and Enter presses it (P17).
                    var resume = System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<Button>(
                        global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(sf.Grace)));
                    w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
                    Assert.True(resume.IsFocused, "Tab reaches Resume");
                    w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                    Assert.True(v.IsPlaying && !o.GracePaused && !sf.Grace.IsVisible && sf.Layer.IsHitTestVisible, "Enter on Resume resumes");
                    clock.Step(0.3);   // past the 200 ms same-keystroke dedup
                    w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                    Assert.False(v.IsPlaying);
                    Assert.Empty(o.Windows);
                    Assert.True(o.Sink == null, "frame buffer freed");
                    Assert.True(credited >= 1, $"credited {credited}s");
                    Assert.True((ducks, unducks) == (3, 3), $"every close releases its duck ({ducks}/{unducks})");

                    // The pink pill by mouse (0.85 hover, 0.7 pressed, click resumes), then the 60 s auto-resume
                    // on the next clip; neither stops the clip.
                    w = await Open(strict: false);
                    sf = o.Surfaces[0];
                    clock.Step(1);
                    w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                    Assert.True(o.GracePaused && sf.Grace.IsVisible, "grace on the second clip");
                    resume = System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<Button>(
                        global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(sf.Grace)));
                    global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var pill = resume.TranslatePoint(new Point(resume.Bounds.Width / 2, resume.Bounds.Height / 2), w)!.Value;
                    w.MouseMove(pill);
                    Assert.Equal(0.85, resume.Opacity);
                    w.MouseDown(pill, MouseButton.Left);
                    Assert.Equal(0.7, resume.Opacity);
                    w.MouseUp(pill, MouseButton.Left);
                    Assert.True(v.IsPlaying && !o.GracePaused && !sf.Grace.IsVisible, "Resume click resumes");
                    v.End();
                    w = await Open(strict: false);
                    sf = o.Surfaces[0];
                    clock.Step(1);
                    w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                    clock.Step(59.5);
                    o.GraceTick();
                    Assert.True(o.GracePaused && sf.Countdown.Text == Loc.GetF("video_grace_auto_resume_in", 1), "still paused at 59.5 s");
                    clock.Step(0.5);
                    o.GraceTick();
                    Assert.True(v.IsPlaying && !o.GracePaused && !sf.Grace.IsVisible, "auto-resume at 60 s");
                    v.End();
                    // The panic key bound to Enter beats a focused Resume: it stops the clip, never resumes it.
                    w = await Open(strict: false);
                    sf = o.Surfaces[0];
                    clock.Step(1);
                    w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                    resume = System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<Button>(
                        global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(sf.Grace)));
                    w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
                    Assert.True(o.GracePaused && resume.IsFocused, "paused, Resume focused");
                    clock.Step(1);
                    s.PanicKey = Key.Enter.ToString();
                    w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                    s.PanicKey = "F12";
                    Assert.False(v.IsPlaying, "panic key Enter stops the clip instead of resuming");
                    Assert.Empty(o.Windows);
                    o.Time = TimeProvider.System;

                    // WPF vout heal: output lost for 5 s replays the same clip once; lost again, it ends.
                    w = await Open(strict: false);
                    o.FrameTs -= System.Diagnostics.Stopwatch.Frequency * 6;
                    o.GuardTick();
                    Assert.True(v.IsPlaying && o.Windows.Count == 1 && o.Windows[0] != w, "lost output replays the clip once");
                    for (var i = 0; i < 100 && o.FirstFrameMs < 0; i++) await Task.Delay(50);
                    o.FrameTs -= System.Diagnostics.Stopwatch.Frequency * 6;
                    o.GuardTick();
                    Assert.False(v.IsPlaying, "lost again after the replay: the clip ends");
                    Assert.Empty(o.Windows);

                    // The global panic press (WPF HandlePanicKeyPress): with PanicOverridesAll off the first
                    // press grace-pauses the clip and the next one stops it; with it on, panic wins at once.
                    var shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
                    shell.Show();
                    var t0 = DateTime.Now;
                    s.PanicOverridesAll = false;
                    w = await Open(strict: false);
                    shell.HandlePanicKeyPress(t0);
                    Assert.True(v.IsPlaying && o.GracePaused, "override off: the global panic press grace-pauses");
                    await Task.Delay(300);
                    shell.HandlePanicKeyPress(t0.AddSeconds(10));
                    Assert.False(v.IsPlaying, "override off: the second press stops it");
                    // A game owns the screen (WPF MainWindow.xaml.cs:1710: closed before the #735 rung):
                    // the press ends the game and stops the clip, never a grace pause.
                    var surfaces = global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All;
                    global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Append(surfaces,
                        new global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.Surface("test-game", _ => { }, () => true)));
                    try
                    {
                        w = await Open(strict: false);
                        shell.HandlePanicKeyPress(t0.AddSeconds(15));
                        Assert.False(v.IsPlaying || o.GracePaused, "override off, game on screen: no grace pause");
                    }
                    finally { global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All = surfaces; }
                    s.PanicOverridesAll = true;
                    w = await Open(strict: false);
                    shell.HandlePanicKeyPress(t0.AddSeconds(20));
                    Assert.False(v.IsPlaying || o.GracePaused, "override on: panic wins");
                    shell.Close();

                    // The max-length cap (VideoMaxDurationSeconds = 1) ends the next clip.
                    s.VideoMaxDurationSeconds = 1;
                    w = await Open(strict: false);
                    await Task.Delay(1100);
                    o.GuardTick();
                    Assert.False(v.IsPlaying, "max-length cap ends the clip");
                    Assert.Empty(o.Windows);
                });
            }
            finally
            {
                v.Stop();
                o.Time = TimeProvider.System;
                o.Scheduler = real;
                MandatoryVideoOverlay.PanicListenerLive = listener;
                CoreProgression.TrackVideoWatchedProvider = null;
                CoreProgression.AddXPProvider = null;
                (CoreAudio.DuckProvider, CoreAudio.UnduckProvider) = (duck, unduck);
                (s.AudioDuckingEnabled, s.VideoBlurredBackgroundEnabled, s.AttentionChecksEnabled) = saved;
                (s.PanicKeyEnabled, s.PanicKey) = panic;
                s.VideoMaxDurationSeconds = maxLen;
                s.PanicOverridesAll = overrides;
                CoreEngine.Video = engineVideo;
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class SteppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public void Step(double seconds) => _now = _now.AddSeconds(seconds);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
