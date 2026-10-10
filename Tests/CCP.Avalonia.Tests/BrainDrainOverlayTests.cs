using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Brain Drain haze (X1): the pump blurs and hands over a frame, the window draws it
/// at the dial's alpha, and the overlay follows the four settings live.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class BrainDrainOverlayTests
{
    /// <summary>A painted buffer standing in for the desktop: left half pure blue, right half pure red,
    /// with the unused fourth byte left at ZERO (the #960 driver shape).</summary>
    private sealed class FakeGrab : BrainDrainCapturePump.IGrab
    {
        private readonly int _w, _h;
        public IntPtr Bits { get; private set; }
        public int Captures;
        public FakeGrab(int w, int h) { _w = w; _h = h; Bits = Marshal.AllocHGlobal(w * h * 4); }
        public bool Capture()
        {
            Captures++;
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                    // memory order B, G, R, X
                    Marshal.WriteInt32(Bits, (y * _w + x) * 4, x < _w / 2 ? 0x000000FF : 0x00FF0000);
            return true;
        }
        public void Dispose() { if (Bits != IntPtr.Zero) { Marshal.FreeHGlobal(Bits); Bits = IntPtr.Zero; } }
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void The_pump_publishes_an_opaque_frame_with_the_channels_the_right_way_round()
    {
        var old = BrainDrainCapturePump.GrabFactory;
        BrainDrainCapturePump.GrabFactory = (_, w, h) => new FakeGrab(w, h);
        var bounds = new PixelRect(0, 0, 800, 400);
        var pump = new BrainDrainCapturePump(4, TimeSpan.FromMilliseconds(33), melt: false,
            new[] { bounds }, sigma: 0f, meltAmplitude: 0f, start: false);
        try
        {
            pump.RunOnceForTest(0f);
            Assert.Equal(1, pump.SlotCount);
            Assert.Equal(1, pump.FramesPublished);
            Assert.True(pump.TryTakeFrame(bounds, out var frame));
            Assert.NotNull(frame);
            using (frame)
            {
                Assert.Equal((200, 100), (frame!.Width, frame.Height));
                using var bmp = SKBitmap.FromImage(frame);
                var left = bmp.GetPixel(10, 50);
                var right = bmp.GetPixel(190, 50);
                Assert.Equal((0, 0, 255, 255), (left.Red, left.Green, left.Blue, left.Alpha));     // blue stays blue, opaque though X was 0
                Assert.Equal((255, 0, 0, 255), (right.Red, right.Green, right.Blue, right.Alpha));
            }
            // Taken once: the slot is empty until the next capture.
            Assert.True(pump.TryTakeFrame(bounds, out var again));
            Assert.Null(again);
            Assert.False(pump.TryTakeFrame(new PixelRect(5, 5, 1, 1), out _));
        }
        finally
        {
            pump.ReleaseForTest();
            BrainDrainCapturePump.GrabFactory = old;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Blur_and_melt_soften_the_edge_and_keep_the_frame_opaque(bool melt)
    {
        var old = BrainDrainCapturePump.GrabFactory;
        BrainDrainCapturePump.GrabFactory = (_, w, h) => new FakeGrab(w, h);
        var bounds = new PixelRect(0, 0, 800, 400);
        var pump = new BrainDrainCapturePump(4, TimeSpan.FromMilliseconds(33), melt, new[] { bounds },
            BrainDrainLayerRules.SigmaFor(100) * 4, BrainDrainLayerRules.MeltAmplitudeFor(100), start: false);
        try
        {
            pump.RunOnceForTest(1.5f);
            Assert.True(pump.TryTakeFrame(bounds, out var frame));
            Assert.NotNull(frame);
            using (frame)
            using (var bmp = SKBitmap.FromImage(frame!))
            {
                var seam = bmp.GetPixel(100, 50);        // on the blue / red seam: a mix of both now
                Assert.InRange(seam.Red, 20, 235);
                Assert.InRange(seam.Blue, 20, 235);
                var mid = bmp.GetPixel(100, 50);
                Assert.Equal(255, mid.Alpha);
                Assert.Equal(255, bmp.GetPixel(2, 2).Alpha);   // melt: the displacement gutter is off-frame
                Assert.Equal(255, bmp.GetPixel(197, 97).Alpha);
            }
        }
        finally
        {
            pump.ReleaseForTest();
            BrainDrainCapturePump.GrabFactory = old;
        }
    }

    [Fact]
    public void Capture_is_excluded_unless_the_user_opts_in()
    {
        Assert.Equal(0x11u, BrainDrainOverlay.AffinityFor(allowCapture: false));
        Assert.Equal(0u, BrainDrainOverlay.AffinityFor(allowCapture: true));
    }

    [Fact]
    public async Task The_window_draws_the_pump_frame_at_the_dial_alpha_and_only_when_something_changed()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var old = BrainDrainCapturePump.GrabFactory;
            BrainDrainCapturePump.GrabFactory = (_, w, h) => new FakeGrab(w, h);
            var bounds = new PixelRect(0, 0, 800, 400);
            var pump = new BrainDrainCapturePump(4, TimeSpan.FromMilliseconds(33), false, new[] { bounds }, 0f, 0f, start: false);
            var w = new BrainDrainOverlayWindow(pump, bounds, TimeSpan.FromMilliseconds(33)) { Width = 800, Height = 400 };
            try
            {
                w.Show();
                w.SetDrawAlpha(BrainDrainLayerRules.AlphaFor(50));
                w.Step();
                Assert.Equal(0, w.FramesDrawn);          // the pump has produced nothing yet: nothing is drawn

                pump.RunOnceForTest(0f);
                w.Step();
                Assert.Equal(1, w.FramesDrawn);
                w.Step();
                Assert.Equal(1, w.FramesDrawn);          // same frame, same dial: not a repaint (#853)

                using (var bmp = w.Surface.CopyBacking())
                {
                    Assert.NotNull(bmp);
                    var p = bmp!.GetPixel(bmp.Width / 4, bmp.Height / 2);
                    Assert.InRange(p.Alpha, 140, 150);   // 0.57 x 255: the real screen ghosts through
                }

                w.SetDrawAlpha(BrainDrainLayerRules.AlphaFor(100));
                w.Step();
                Assert.Equal(2, w.FramesDrawn);          // the dial moved: repaint with the frame it holds
            }
            finally
            {
                w.Close();
                pump.ReleaseForTest();
                BrainDrainCapturePump.GrabFactory = old;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_overlay_follows_the_settings_live_and_goes_down_with_the_feature()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (enabled, strength, melt) = (s.BrainDrainEnabled, s.BrainDrainBlurStrength, s.BrainDrainMeltEnabled);
            var oldGrab = BrainDrainCapturePump.GrabFactory;
            var oldRunning = CoreSession.IsEngineRunningProvider;
            BrainDrainCapturePump.GrabFactory = (_, w, h) => new FakeGrab(w, h);
            CoreSession.IsEngineRunningProvider = () => true;
            BrainDrainOverlay.SkipPlatformChecksForTest = true;
            var host = new Window { Width = 300, Height = 200 };
            try
            {
                host.Show();
                s.BrainDrainEnabled = true;
                s.BrainDrainBlurStrength = 40;
                s.BrainDrainMeltEnabled = false;
                BrainDrainOverlay.Refresh(host);
                if (host.Screens.All.Count == 0) { Assert.False(BrainDrainOverlay.IsShowing); return Task.CompletedTask; }
                Assert.True(BrainDrainOverlay.IsShowing);
                Assert.Equal(40, BrainDrainOverlay.CurrentIntensity);

                s.BrainDrainBlurStrength = 80;                 // no Refresh call: the overlay follows the setting
                Assert.True(BrainDrainOverlay.IsShowing);
                Assert.Equal(80, BrainDrainOverlay.CurrentIntensity);

                s.BrainDrainMeltEnabled = true;                // a new run with the warp
                Assert.True(BrainDrainOverlay.IsShowing);

                s.BrainDrainBlurStrength = 0;                  // 0 = no picture at all
                Assert.False(BrainDrainOverlay.IsShowing);

                s.BrainDrainBlurStrength = 30;
                Assert.True(BrainDrainOverlay.IsShowing);
                CoreSession.IsEngineRunningProvider = () => false;   // engine stopped
                BrainDrainOverlay.Refresh(host);
                Assert.False(BrainDrainOverlay.IsShowing);

                CoreSession.IsEngineRunningProvider = () => true;
                BrainDrainOverlay.Refresh(host);
                Assert.True(BrainDrainOverlay.IsShowing);
                global::ConditioningControlPanel.Avalonia.App.StopDesktopOverlays(final: false);   // the panic / stop path
                Assert.False(BrainDrainOverlay.IsShowing);
            }
            finally
            {
                BrainDrainOverlay.CloseAll();
                BrainDrainOverlay.SkipPlatformChecksForTest = false;
                BrainDrainCapturePump.GrabFactory = oldGrab;
                CoreSession.IsEngineRunningProvider = oldRunning;
                s.BrainDrainEnabled = enabled; s.BrainDrainBlurStrength = strength; s.BrainDrainMeltEnabled = melt;
                host.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_card_blur_dial_goes_down_to_zero_and_writes_the_setting()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var strength = s.BrainDrainBlurStrength;
            try
            {
                var card = new BrainDrainFeatureControl();
                var blur = card.FindControl<Slider>("SliderBlurStrength")!;
                Assert.Equal(0, blur.Minimum);
                if (blur.IsEnabled)
                {
                    blur.Value = 0;
                    Assert.Equal(0, s.BrainDrainBlurStrength);
                    blur.Value = 64;
                    Assert.Equal(64, s.BrainDrainBlurStrength);
                }
            }
            finally { s.BrainDrainBlurStrength = strength; CoreSettings.SaveImmediate(); }
            return Task.CompletedTask;
        });
    }
}

/// <summary>X2: the Brain Drain volume slider turns a clip that is already playing (WPF RefreshVolume).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class BrainDrainLiveVolumeTests
{
    private sealed class FakeVoice : ConditioningControlPanel.Avalonia.Platform.MindWipePlayer.IVoice
    {
        public double Last = -1;
        public bool Disposed;
        public double Volume { set => Last = value; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void The_playing_clip_takes_the_new_volume_and_an_idle_player_is_left_alone()
    {
        var s = CoreSettings.Current;
        var (enabled, master, volume) = (s.BrainDrainEnabled, s.MasterVolume, s.BrainDrainVolume);
        var oldProvider = CoreBrainDrain.RefreshVolumeProvider;
        var voice = new FakeVoice();
        double startedAt = -1;
        var player = new ConditioningControlPanel.Avalonia.Platform.BrainDrainPlayer(
            (clip, vol, loop, done) => { startedAt = vol; return voice; }, roll: () => 0.0);
        try
        {
            player.RefreshVolume();                       // nothing playing: nothing to turn
            Assert.Equal(-1, voice.Last);

            s.BrainDrainEnabled = true; s.MasterVolume = 100; s.BrainDrainVolume = 80;
            player.Start();
            typeof(ConditioningControlPanel.Avalonia.Platform.BrainDrainPlayer)
                .GetField("_clips", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(player, new[] { "clip.mp3" });
            player.Intensity = 100;
            for (var i = 0; i < 2000 && startedAt < 0; i++) player.Tick();
            Assert.Equal(0.8, startedAt, 3);

            CoreBrainDrain.RefreshVolumeProvider = player.RefreshVolume;
            s.BrainDrainVolume = 25;
            CoreBrainDrain.RefreshVolume();               // what the card's slider calls
            Assert.Equal(0.25, voice.Last, 3);

            s.MasterVolume = 50;
            CoreBrainDrain.RefreshVolume();
            Assert.Equal(0.125, voice.Last, 3);
        }
        finally
        {
            player.Stop();
            Assert.True(voice.Disposed || startedAt < 0);
            CoreBrainDrain.RefreshVolumeProvider = oldProvider;
            s.BrainDrainEnabled = enabled; s.MasterVolume = master; s.BrainDrainVolume = volume;
            CoreSettings.SaveImmediate();
        }
    }
}
