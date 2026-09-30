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
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The mandatory-video overlay headless with a real clip on the shared LibVLC: Trigger
/// opens a topmost full-screen window after the pre-roll and frames arrive; strict vetoes a close
/// and the panic key; Esc dismisses a non-strict one and credits the watch; engine Stop (panic)
/// closes it. Skips where ffmpeg (to make the clip) or libvlc is missing.</summary>
public sealed class MandatoryVideoOverlayTests
{
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
                    $"-v error -f lavfi -i testsrc=d=10:s=320x240:r=25 -pix_fmt yuv420p \"{clip}\"") { UseShellExecute = false })!;
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
            (s.PanicKeyEnabled, s.PanicKey) = (true, "F12");
            var o = MandatoryVideoOverlay.Instance;
            var v = new ConditioningControlPanel.Services.MandatoryVideoScheduler(o, library: () => new[] { clip });
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
                        Assert.Same(o.Sink.Bitmap, ((Image)w.Content!).Source);
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
                    await Task.Delay(1200);
                    w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                    Assert.False(v.IsPlaying);
                    Assert.Empty(o.Windows);
                    Assert.True(o.Sink == null, "frame buffer freed");
                    Assert.True(credited >= 1, $"credited {credited}s");
                });
            }
            finally
            {
                v.Stop();
                o.Scheduler = real;
                MandatoryVideoOverlay.PanicListenerLive = listener;
                CoreProgression.TrackVideoWatchedProvider = null;
                (s.PanicKeyEnabled, s.PanicKey) = panic;
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
