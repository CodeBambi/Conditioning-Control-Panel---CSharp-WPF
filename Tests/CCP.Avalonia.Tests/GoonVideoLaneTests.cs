using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Transfer;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>IB4: Goon own-media videos (WPF VideoTranscodeLane). The engine is a seam, so nothing here
/// encodes a frame or touches the network: a fake transcoder stands in. A clip that needs shrinking is
/// transcoded off the caller's thread, the hold a closing window puts on the queue cancels it and
/// removes its temp file, and a head with no engine refuses it as no-decoder instead of offering it.</summary>
[Collection(RunsAloneCollection.Name)]   // process-wide transfer stores and the three engine seams
public sealed class GoonVideoLaneTests
{
    private sealed class Engine
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile string? TmpOut;
        public volatile int CallerThread;
        public int Previews;
    }

    private static async Task Library(bool withEngine, Func<Engine, TransferCompressionService, string, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-goonvideo-" + Guid.NewGuid().ToString("N"));
        var assets = Path.Combine(root, "assets");
        Directory.CreateDirectory(assets);
        var clip = Path.Combine(assets, "clip.mp4");
        using (var fs = new FileStream(clip, FileMode.Create)) fs.SetLength(AssetCompressionPlanner.ExemptMaxBytes + 4096);

        var userRoot = TransferPlatform.UserDataRoot;
        var pool = TransferPlatform.ActivePool;
        var (probe, transcode, preview) = (VideoTranscodeLane.Probe, VideoTranscodeLane.Transcode, VideoTranscodeLane.Preview);
        var s = CoreSettings.Current;
        bool auto = s.TransferCacheAutoCompress;
        var engine = new Engine();
        var svc = TransferCompressionService.Instance;

        TransferPlatform.UserDataRoot = () => root;
        TransferPlatform.ActivePool = () => new[] { (clip, "clip.mp4", new FileInfo(clip).Length, false) };
        s.TransferCacheAutoCompress = false;
        AssetCompressionPlanner.ClearProbeCache();
        if (withEngine)
        {
            VideoTranscodeLane.Probe = (_, _) => Task.FromResult<VideoTranscodeLane.VideoProbe?>(new(1920, 1080, 10_000, 8_000_000));
            VideoTranscodeLane.Transcode = async (src, tmp, p, progress, ct) =>
            {
                engine.CallerThread = Environment.CurrentManagedThreadId;
                engine.TmpOut = tmp;
                Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
                await File.WriteAllBytesAsync(tmp, new byte[2048], ct);
                progress?.Invoke(10);
                engine.Started.TrySetResult();
                try { await engine.Finish.Task.WaitAsync(ct); }
                catch (OperationCanceledException) { engine.Cancelled.TrySetResult(); throw; }
                return new VideoTranscodeLane.TranscodeResult(tmp, "mp4", "avc1", 1280, 720, p?.DurMs ?? 0, 2048);
            };
            VideoTranscodeLane.Preview = async (src, tmp, p, ct) =>
            {
                Interlocked.Increment(ref engine.Previews);
                await File.WriteAllBytesAsync(tmp, new byte[256], ct);
                return 256;
            };
        }
        else
        {
            VideoTranscodeLane.Probe = null;
            VideoTranscodeLane.Transcode = null;
            VideoTranscodeLane.Preview = null;
        }
        try
        {
            svc.Initialize();
            svc.CancelAll();
            TransferCacheStore.Instance.DeleteAll();   // the index is process-wide and loads once: start from nothing under the temp root
            svc.ReleaseHostHold();
            await svc.RefreshAsync();
            await body(engine, svc, root);
        }
        finally
        {
            svc.CancelAll();
            engine.Finish.TrySetCanceled();
            await Until(() => svc.GetState().Running == 0);
            try { TransferCacheStore.Instance.DeleteAll(); } catch { }   // while the root is still the temp one
            svc.ReleaseHostHold();
            VideoTranscodeLane.Probe = probe;
            VideoTranscodeLane.Transcode = transcode;
            VideoTranscodeLane.Preview = preview;
            TransferPlatform.ActivePool = pool;
            TransferPlatform.UserDataRoot = userRoot;
            s.TransferCacheAutoCompress = auto;
            AssetCompressionPlanner.ClearProbeCache();
            try { await svc.RefreshAsync(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static async Task Until(Func<bool> done, int ms = 10_000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done() && DateTime.UtcNow < until) await Task.Delay(20);
        Assert.True(done(), "timed out");
    }

    private static CacheItem Clip(TransferCompressionService svc) => svc.ListItems().Single(i => i.Rel == "clip.mp4");

    [Fact]
    public Task ABigClip_IsPlannedForTheHostEngine() => Library(true, (engine, svc, _) =>
    {
        Assert.True(VideoTranscodeLane.Available);
        Assert.Equal(TransferLanes.HostMt, Clip(svc).Lane);
        Assert.Equal(1, svc.GetState().NotReady);
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheTranscode_RunsOffTheCallersThread_AndCommitsWithItsPreview() => Library(true, async (engine, svc, root) =>
    {
        int caller = Environment.CurrentManagedThreadId;
        svc.CompressOne(Clip(svc).SrcKey);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, svc.GetState().Running);
        engine.Finish.TrySetResult();
        await Until(() => svc.GetState().Ready == 1);
        Assert.NotEqual(0, engine.CallerThread);
        Assert.Equal(1, engine.Previews);
        var done = Clip(svc);
        Assert.Equal("mp4", done.Ext);
        Assert.Equal("avc1", done.Codec);
        Assert.Null(done.Fail);
        Assert.False(File.Exists(engine.TmpOut!));   // moved into the cache, no temp left
        Assert.Empty(Directory.Exists(TransferCacheStore.Instance.TmpDir) ? Directory.GetFiles(TransferCacheStore.Instance.TmpDir) : Array.Empty<string>());
        _ = caller;
    });

    [Fact]
    public Task AClosingWindow_CancelsTheTranscode_CleansItsTemp_AndFailsNothing() => Library(true, async (engine, svc, _) =>
    {
        svc.CompressOne(Clip(svc).SrcKey);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(File.Exists(engine.TmpOut!));

        svc.HoldForHostClose();   // what GameWindow.CloseGoonTransfer calls: window close and panic
        await engine.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Until(() => svc.GetState().Running == 0);
        Assert.False(File.Exists(engine.TmpOut!));
        Assert.True(svc.IsPaused);
        Assert.Equal(0, svc.GetState().Failed);
        Assert.Null(Clip(svc).Fail);
        Assert.Equal(0, svc.GetState().Ready);
    });

    [Fact]
    public Task WhileHeld_NothingStarts_AndTheNextWindowRunsIt() => Library(true, async (engine, svc, _) =>
    {
        svc.HoldForHostClose();
        svc.CompressOne(Clip(svc).SrcKey);
        await Task.Delay(300);
        Assert.False(engine.Started.Task.IsCompleted);
        Assert.Equal(1, svc.GetState().Queued);
        svc.ReleaseHostHold();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(svc.IsPaused);
    });

    [Fact]
    public Task WithNoEngine_ABigClipIsRefusedAsNoDecoder_NeverOffered() => Library(false, async (engine, svc, _) =>
    {
        Assert.False(VideoTranscodeLane.Available);
        svc.CompressOne(Clip(svc).SrcKey);
        await Until(() => svc.GetState().Failed == 1);
        var refused = Clip(svc);
        Assert.Equal(TransferFailReasons.NoDecoder, refused.Fail);
        Assert.Equal(0, svc.GetState().Ready);
    });

    [Fact]
    public void ThePreviewWindow_IsTwoSecondsTwelvePercentIn_AndAlwaysFits()
    {
        Assert.Equal((12_000d, 14_000d), VideoTranscodeLane.PreviewWindow(100_000));
        Assert.Equal((0d, 1500d), VideoTranscodeLane.PreviewWindow(1500));   // shorter than the window: whole clip
        Assert.Equal((0d, 2000d), VideoTranscodeLane.PreviewWindow(0));      // unknown duration
        var (start, stop) = VideoTranscodeLane.PreviewWindow(2100);
        Assert.True(start >= 0 && stop <= 2100 && Math.Abs(stop - start - 2000) < 0.001);
    }

    [Fact]
    public void TheHeadSeedsTheWindowsEngine_OnlyOnWindows_AndNeverOverAFake()
    {
        var (probe, transcode, preview) = (VideoTranscodeLane.Probe, VideoTranscodeLane.Transcode, VideoTranscodeLane.Preview);
        try
        {
            VideoTranscodeLane.Probe = null; VideoTranscodeLane.Transcode = null; VideoTranscodeLane.Preview = null;
            GameWindow.SeedGoonVideoEngine();
            Assert.Equal(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041), VideoTranscodeLane.Available);

            Func<string, string, VideoTranscodeLane.VideoProbe?, Action<double>?, CancellationToken, Task<VideoTranscodeLane.TranscodeResult>> fake =
                (_, _, _, _, _) => throw new TranscodeUnsupportedException(TransferFailReasons.NoDecoder);
            VideoTranscodeLane.Transcode = fake;
            GameWindow.SeedGoonVideoEngine();
            Assert.Same(fake, VideoTranscodeLane.Transcode);
        }
        finally
        {
            VideoTranscodeLane.Probe = probe; VideoTranscodeLane.Transcode = transcode; VideoTranscodeLane.Preview = preview;
        }
    }
}
