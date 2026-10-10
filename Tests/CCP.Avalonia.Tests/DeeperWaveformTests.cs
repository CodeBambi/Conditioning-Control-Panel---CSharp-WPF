using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace CCP.Avalonia.Tests;

/// <summary>Hunt IB8 (HB2): the Deeper waveform. WPF AudioWaveformCache's bucket rule and cache file, fed
/// from a WAV the shared LibVLC transcodes (faked here: the seam writes the WAV). Swaps process-wide seams,
/// so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DeeperWaveformTests
{
    /// <summary>Mono 16-bit PCM: silence for the first half, a square wave at <paramref name="level"/> after.</summary>
    private static byte[] Wav(double seconds, double level, int rate = DeeperWaveform.WavSampleRate, bool patchedHeader = true)
    {
        int frames = (int)(seconds * rate);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write("RIFF"u8.ToArray());
        bw.Write(patchedHeader ? 36 + frames * 2 : 0);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(rate);
        bw.Write(rate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data"u8.ToArray());
        bw.Write(patchedHeader ? frames * 2 : 0);
        short loud = (short)(level * 32767);
        for (int i = 0; i < frames; i++) bw.Write(i < frames / 2 ? (short)0 : (i % 2 == 0 ? loud : (short)-loud));
        bw.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void PeaksFromWav_BucketsSixASecond_AndFollowsTheLevel()
    {
        var r = DeeperWaveform.PeaksFromWav(new MemoryStream(Wav(20, 0.5)));
        Assert.Equal(20, r.DurationSeconds, 3);
        Assert.Equal(120, r.Peaks.Length);                       // 6 a second
        Assert.All(r.Peaks.Take(59), p => Assert.Equal(0f, p));
        Assert.All(r.Peaks.Skip(61), p => Assert.InRange(p, 0.49f, 0.51f));

        Assert.Equal(64, DeeperWaveform.PeaksFromWav(new MemoryStream(Wav(2, 1))).Peaks.Length);   // the floor
        // A muxer stopped before it patched the header: the data runs to the end of the file.
        var open = DeeperWaveform.PeaksFromWav(new MemoryStream(Wav(20, 0.5, patchedHeader: false)));
        Assert.Equal(120, open.Peaks.Length);
        Assert.InRange(open.Peaks[^1], 0.49f, 0.51f);
        Assert.Throws<InvalidDataException>(() => DeeperWaveform.PeaksFromWav(new MemoryStream(new byte[64])));
    }

    [Fact]
    public void Load_CachesByWholePathSizeAndTime_AndCancelLeavesNothingBehind()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-wave-").FullName;
        var (toWav, folder) = (DeeperWaveform.ToWav, DeeperWaveform.CacheFolderOverride);
        int decodes = 0;
        string? temp = null;
        try
        {
            DeeperWaveform.CacheFolderOverride = Path.Combine(dir, "cache");
            DeeperWaveform.ToWav = (_, dest, _) => { decodes++; temp = dest; File.WriteAllBytes(dest, Wav(20, 0.5)); };
            var a = Path.Combine(dir, "a", "clip.mp3");
            var b = Path.Combine(dir, "b", "clip.mp3");                // same name, another folder
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);
            File.WriteAllBytes(a, new byte[16]);
            File.WriteAllBytes(b, new byte[16]);

            var first = DeeperWaveform.Load(a, CancellationToken.None)!;
            Assert.Equal(120, first.Peaks.Length);
            Assert.False(File.Exists(temp));                           // the WAV never outlives the decode
            var again = DeeperWaveform.Load(a, CancellationToken.None)!;
            Assert.Equal(1, decodes);                                  // from the cache
            Assert.Equal(first.Peaks, again.Peaks);
            Assert.Equal(20, again.DurationSeconds, 3);

            DeeperWaveform.Load(b, CancellationToken.None);
            Assert.Equal(2, decodes);                                  // the whole path is the identity
            File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(5));
            DeeperWaveform.Load(a, CancellationToken.None);
            Assert.Equal(3, decodes);                                  // a changed file is decoded again

            Assert.Null(DeeperWaveform.Load(Path.Combine(dir, "gone.mp3"), CancellationToken.None));
            using var cts = new CancellationTokenSource();
            DeeperWaveform.ToWav = (_, dest, ct) => { temp = dest; File.WriteAllBytes(dest, Wav(20, 0.5)); cts.Cancel(); ct.ThrowIfCancellationRequested(); };
            File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddMinutes(9));
            Assert.Null(DeeperWaveform.LoadAsync(b, cts.Token).GetAwaiter().GetResult());
            Assert.False(File.Exists(temp));

            DeeperWaveform.ToWav = (_, _, _) => throw new InvalidDataException("will not decode");
            File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddMinutes(12));
            Assert.Null(DeeperWaveform.LoadAsync(b, CancellationToken.None).GetAwaiter().GetResult());   // quiet: a flat strip
        }
        finally
        {
            (DeeperWaveform.ToWav, DeeperWaveform.CacheFolderOverride) = (toWav, folder);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SoutChain_SurvivesAWindowsPath()
    {
        var sout = DeeperWaveform.SoutFor(@"C:\Temp\it's here\x.wav");
        Assert.Contains("samplerate=8000", sout);
        Assert.Contains("channels=1", sout);
        Assert.Contains(@"dst='C:/Temp/it\'s here/x.wav'", sout);
    }

    [Fact]
    public void Player_DrawsTheWaveOnce_AndACloseCancelsTheDecode() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-wave-ui-").FullName;
        var (toWav, folder, open) = (DeeperWaveform.ToWav, DeeperWaveform.CacheFolderOverride, DeeperLocalAudio.Open);
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        using var gate = new ManualResetEventSlim(false);
        bool cancelled = false;
        DeeperWaveform.CacheFolderOverride = Path.Combine(dir, "cache");
        DeeperWaveform.ToWav = (_, dest, _) => File.WriteAllBytes(dest, Wav(20, 0.5));
        DeeperLocalAudio.Open = _ => System.Threading.Tasks.Task.FromResult<IDeeperLocalAudio?>(null);
        var player = new EnhancementPlayerWindow(null, null);
        try
        {
            player.Show();
            player.OpenLocalMediaFile(path);
            for (int i = 0; i < 250 && player.WaveformPeaks == null; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(120, player.WaveformPeaks!.Length);
            var wave = player.FindControl<ShapePath>("WaveformPath")!;
            Assert.NotNull(wave.Data);
            Assert.IsType<BitmapCache>(wave.CacheMode);                // one bitmap, never per-frame geometry

            // A decode still running when the window closes is told to stop.
            DeeperWaveform.ToWav = (_, _, ct) =>
            {
                gate.Wait(5000);
                cancelled = ct.IsCancellationRequested;
                ct.ThrowIfCancellationRequested();
            };
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
            player.OpenLocalMediaFile(path);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(player.WaveformPeaks);
            player.Close();
            gate.Set();
            for (int i = 0; i < 250 && !cancelled; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
            Assert.True(cancelled);
            Assert.Null(player.WaveformPeaks);
        }
        finally
        {
            gate.Set();
            player.Close();
            (DeeperWaveform.ToWav, DeeperWaveform.CacheFolderOverride, DeeperLocalAudio.Open) = (toWav, folder, open);
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
