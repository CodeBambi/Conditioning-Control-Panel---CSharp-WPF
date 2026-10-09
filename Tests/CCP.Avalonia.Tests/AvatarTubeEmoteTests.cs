using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The animated avatar (AvatarTubeWindow.Emotes.cs, WPF CirceEmotes): CCP Default plays the
/// avatar0 cel clips through the SkiaSharp GIF player, the frame advances on the clock, and a map
/// that names a missing clip never blanks the tube.
/// </summary>
public sealed class AvatarTubeEmoteTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
    }

    [Fact]
    public Task TheDecoderReadsAnAvatar0ClipIntoTimedFrames() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var s = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar0_emotes/idle.gif"));
        using var clip = GifClip.Open(s);
        Assert.NotNull(clip);
        Assert.True(clip!.FrameCount > 1, $"idle.gif decoded {clip.FrameCount} frame(s)");
        foreach (var d in clip.FrameDurationsMs) Assert.True(d > 0);
        Assert.True(clip.TotalMs > 1000, $"idle.gif runs {clip.TotalMs} ms");

        // Frame 5 has real pixels (not a transparent buffer).
        using var player = new GifPlayer("idle", GifClip.Open(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar0_emotes/idle.gif")))!);
        for (int i = 0; i < 5; i++) player.Advance(TimeSpan.FromMilliseconds(clip.FrameDurationsMs[i]));
        Assert.Equal(5, player.FrameIndex);
        using var fb = player.Bitmap.Lock();
        var row = new byte[fb.RowBytes];
        int opaque = 0;
        for (int y = 0; y < fb.Size.Height; y += 8)
        {
            System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes, row, 0, row.Length);
            for (int x = 3; x < fb.Size.Width * 4; x += 4) if (row[x] > 200) opaque++;
        }
        Assert.True(opaque > 100, $"frame 5 is near empty ({opaque} opaque samples)");
        return Task.CompletedTask;
    });

    [Fact]
    public Task CcpDefaultPlaysAnAvatar0ClipAndItsFramesAdvance() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Assert.True(CoreMods.IsCCPDefault);
        var tube = new AvatarTubeWindow(null);
        try
        {
            Assert.True(tube.EmoteModeActive, "CCP Default did not engage the avatar0 emote set");
            var clip = tube.EmoteCurrentClip;
            Assert.Contains(clip, new[] { "idle", "idle3", "idle4", "idle5", "tender", "shy", "adoring", "blowkiss", "celebrate", "entrancing" });
            var layer = tube.EmoteActiveLayer!;
            Assert.True(layer.IsVisible);
            Assert.IsType<WriteableBitmap>(layer.Source);
            Assert.False(tube.FindControl<global::Avalonia.Controls.Image>("ImgAvatar")!.IsVisible);

            int f0 = tube.EmoteActiveFrameIndex;
            for (int i = 0; i < 10; i++) tube.StepEmotes(TimeSpan.FromMilliseconds(70));
            Assert.True(tube.EmoteActiveFrameIndex > f0, $"frame stuck at {tube.EmoteActiveFrameIndex}");

            // Run the clip out: the rotation crossfades to another idle clip, never to nothing.
            for (int i = 0; i < 400 && tube.EmoteCurrentClip == clip; i++) tube.StepEmotes(TimeSpan.FromMilliseconds(70));
            Assert.NotEqual(clip, tube.EmoteCurrentClip);
            Assert.NotNull(tube.EmoteActiveLayer?.Source);
        }
        finally { tube.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AMissingClipIsSkippedWithoutBlankingTheTube() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        // A mod-style set whose map names a clip that does not ship, beside one that does.
        var dir = Path.Combine(Path.GetTempPath(), "ccp-emote-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "emotes.json"),
                "{ \"fadeMs\": 300, \"idleRotation\": [ { \"clip\": \"ghost\", \"weight\": 50 }, { \"clip\": \"idle\", \"weight\": 1 } ], \"clickEmotes\": [\"ghost\"] }");
            using (var src = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar0_emotes/idle.gif")))
            using (var dst = File.Create(Path.Combine(dir, "idle.gif")))
                src.CopyTo(dst);
            File.WriteAllText(Path.Combine(dir, "broken.gif"), "not a gif");

            var tube = new AvatarTubeWindow(null);
            try
            {
                Assert.True(tube.EnterEmoteMode(dir) || tube.EmoteModeActive);
                Assert.Equal("idle", tube.EmoteCurrentClip);           // "ghost" dropped from the rotation
                Assert.False(tube.EmoteClick());                        // the only click clip is missing

                Assert.False(tube.DoEmoteCrossfade("broken"));          // undecodable: refused
                Assert.False(tube.DoEmoteCrossfade("nosuchclip"));      // missing: refused
                Assert.Equal("idle", tube.EmoteCurrentClip);
                Assert.True(tube.EmoteActiveLayer!.IsVisible);
                Assert.NotNull(tube.EmoteActiveLayer.Source);
                Assert.True(tube.EmoteActiveLayer.Opacity > 0.5);

                // The clip ends with nothing else to play: it replays instead of leaving a blank tube.
                for (int i = 0; i < 400; i++) tube.StepEmotes(TimeSpan.FromMilliseconds(70));
                Assert.Equal("idle", tube.EmoteCurrentClip);
                Assert.NotNull(tube.EmoteActiveLayer!.Source);
            }
            finally { tube.Close(); }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return Task.CompletedTask;
    });
}
