using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>MiniPlayerWindow plays a real clip headless through the shared LibVLC: frames change,
/// seek moves, pause freezes, close frees the player (VideoCheck.Check, shared with --video-check).
/// Skips where ffmpeg (to make the clip) or libvlc is missing.</summary>
public sealed class MiniPlayerVideoTests
{
    [Fact]
    public async Task PlaysSeeksPausesAndFreesOnClose()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-video-").FullName;
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

            var log = new System.Text.StringBuilder();
            var fails = -1;
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                fails = await VideoCheck.Check(clip, l => log.AppendLine(l));
            });
            Assert.True(fails == 0, log.ToString());
        }
        finally { Directory.Delete(dir, true); }
    }
}
