#if CCP_WINRT
// The Windows engine behind Core's VideoTranscodeLane seams (WPF Services/Media/Transfer/
// VideoTranscodeLane.cs, body for body): Windows.Media.Transcoding.MediaTranscoder, the same OS
// component the WPF head uses, so a clip a player could send from WPF is a clip they can send here.
// The targets and the numbers are Core's; this file only drives the encoder. Every call runs on the
// caller's worker (the compression service never calls it from the UI thread), honours the token, and
// leaves no partial file behind.

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Transfer;
using Serilog;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace ConditioningControlPanel.Avalonia.Platform;

[SupportedOSPlatform("windows10.0.19041.0")]
internal static class WinRtVideoTranscoder
{
    /// <summary>Null = "probe unknown", never "unsupported": the transcoder is the only thing allowed to say no.</summary>
    public static async Task<VideoTranscodeLane.VideoProbe?> ProbeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct).ConfigureAwait(false);
        var props = await file.Properties.GetVideoPropertiesAsync().AsTask(ct).ConfigureAwait(false);
        int w = (int)props.Width, h = (int)props.Height;
        int durMs = (int)Math.Min(int.MaxValue, props.Duration.TotalMilliseconds);
        long bitrate = props.Bitrate;
        // A zero-everything probe is the shell shrugging, not a real answer.
        if (w <= 0 && h <= 0 && durMs <= 0 && bitrate <= 0) return null;
        return new VideoTranscodeLane.VideoProbe(w, h, durMs, bitrate);
    }

    /// <summary>H.264/AAC MP4, fit inside 1280x720 with even dimensions, never upscaled.</summary>
    public static async Task<VideoTranscodeLane.TranscodeResult> TranscodeAsync(
        string srcPath, string tmpOut, VideoTranscodeLane.VideoProbe? probe, Action<double>? onProgress, CancellationToken ct)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Bitrate = (uint)VideoTranscodeLane.TargetBitrateFor(probe?.Bitrate ?? 0);
        if (probe is { Width: > 0, Height: > 0 } p)
        {
            var (w, h) = VideoTranscodeLane.FitEven(p.Width, p.Height, VideoTranscodeLane.MaxWidth, VideoTranscodeLane.MaxHeight);
            profile.Video.Width = w;
            profile.Video.Height = h;
        }

        await RunAsync(srcPath, tmpOut, profile, null, null, onProgress, ct).ConfigureAwait(false);

        long bytes = new FileInfo(tmpOut).Length;
        return new VideoTranscodeLane.TranscodeResult(tmpOut, "mp4", "avc1",
            (int)profile.Video.Width, (int)profile.Video.Height, probe?.DurMs ?? 0, bytes);
    }

    /// <summary>The two-second, 426x240, silent micro-preview, 12% into the clip.</summary>
    public static async Task<long> PreviewAsync(
        string srcPath, string tmpOut, VideoTranscodeLane.VideoProbe? probe, CancellationToken ct)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
        profile.Audio = null;   // a preview with sound is a jump-scare
        profile.Video.Bitrate = (uint)VideoTranscodeLane.PreviewBitrate;
        var (w, h) = probe is { Width: > 0, Height: > 0 } p
            ? VideoTranscodeLane.FitEven(p.Width, p.Height, VideoTranscodeLane.PreviewWidth, VideoTranscodeLane.PreviewHeight)
            : ((uint)VideoTranscodeLane.PreviewWidth, (uint)VideoTranscodeLane.PreviewHeight);
        profile.Video.Width = w;
        profile.Video.Height = h;
        try
        {
            if (profile.Video.FrameRate != null)
            {
                profile.Video.FrameRate.Numerator = VideoTranscodeLane.PreviewFps;
                profile.Video.FrameRate.Denominator = 1;
            }
        }
        catch { /* some profiles refuse a framerate override; 30 fps of 2 s is survivable */ }

        var (startMs, stopMs) = VideoTranscodeLane.PreviewWindow(probe?.DurMs ?? 0);
        await RunAsync(srcPath, tmpOut, profile, TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(stopMs), null, ct)
            .ConfigureAwait(false);
        return new FileInfo(tmpOut).Length;
    }

    private static async Task RunAsync(
        string srcPath, string tmpOut, MediaEncodingProfile profile,
        TimeSpan? trimStart, TimeSpan? trimStop, Action<double>? onProgress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(tmpOut)!);

        StorageFile src, dst;
        try
        {
            src = await StorageFile.GetFileFromPathAsync(srcPath).AsTask(ct).ConfigureAwait(false);
            var dstDir = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(tmpOut)!).AsTask(ct).ConfigureAwait(false);
            dst = await dstDir.CreateFileAsync(Path.GetFileName(tmpOut), CreationCollisionOption.ReplaceExisting).AsTask(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { TryDelete(tmpOut); throw; }
        catch (Exception ex) { TryDelete(tmpOut); throw new TranscodeUnsupportedException("io: " + ex.Message); }

        try
        {
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            if (trimStart is { } start && start > TimeSpan.Zero) transcoder.TrimStartTime = start;
            if (trimStop is { } stop) transcoder.TrimStopTime = stop;

            var prep = await transcoder.PrepareFileTranscodeAsync(src, dst, profile).AsTask(ct).ConfigureAwait(false);
            if (!prep.CanTranscode)
            {
                Log.Information("VideoTranscodeLane: cannot transcode {Path} ({Reason})", srcPath, prep.FailureReason);
                throw new TranscodeUnsupportedException(TransferFailReasons.NoDecoder);
            }

            var op = prep.TranscodeAsync();
            if (onProgress != null)
            {
                op.Progress = (_, percent) =>
                {
                    try { onProgress(percent); }
                    catch { /* a subscriber must never kill an encode */ }
                };
            }
            using var reg = ct.Register(() => { try { op.Cancel(); } catch { } });
            try { await op.AsTask().ConfigureAwait(false); }
            catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        }
        catch (OperationCanceledException) { TryDelete(tmpOut); throw; }
        catch (TranscodeUnsupportedException) { TryDelete(tmpOut); throw; }
        catch (Exception ex)
        {
            TryDelete(tmpOut);
            Log.Warning("VideoTranscodeLane: transcode of {Path} failed: {E}", srcPath, ex.Message);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
#endif
