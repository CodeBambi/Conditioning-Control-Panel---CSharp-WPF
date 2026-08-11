using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>
    /// Thrown when Windows has no decoder for a source (PrepareFileTranscodeAsync says
    /// CanTranscode:false). The entry becomes failed:no-decoder — the honest answer, and the reason
    /// there is no LibVLC lane: the spike proved WebM VP8 AND VP9 both transcode here, so a refusal
    /// is a genuinely exotic file, not a gap worth a second engine.
    /// </summary>
    internal sealed class TranscodeUnsupportedException : Exception
    {
        public string Reason { get; }
        public TranscodeUnsupportedException(string reason)
            : base("transcode unsupported: " + reason) => Reason = reason;
    }

    /// <summary>
    /// Lane C: video, host side, WinRT <see cref="MediaTranscoder"/> (disk → disk, hardware
    /// accelerated, real Progress, real Cancel). Measured ~30 MB/s on the spike box: a 90 MB 1080p
    /// clip became 18 MB of 720p in 3.1 s.
    ///
    /// Also mints the 426×240 micro-preview via a second pass with TrimStartTime/TrimStopTime and
    /// <c>profile.Audio = null</c> (2 s ≈ 96 KB).
    ///
    /// Everything here is off-UI-thread by construction; nothing touches the Dispatcher.
    /// </summary>
    internal static class VideoTranscodeLane
    {
        public const long MinTargetBitrate = 900_000;
        public const long MaxTargetBitrate = 2_000_000;
        public const long PreferredTargetBitrate = 1_800_000;
        public const int MaxWidth = 1280;
        public const int MaxHeight = 720;

        public const int PreviewWidth = 426;
        public const int PreviewHeight = 240;
        public const int PreviewMs = 2000;
        public const long PreviewBitrate = 350_000;
        public const int PreviewFps = 15;
        /// <summary>Preview starts here into the clip — far enough past titles/fades to be representative.</summary>
        private const double PreviewStartFraction = 0.12;

        public sealed record VideoProbe(int Width, int Height, int DurMs, long Bitrate);

        public sealed record TranscodeResult(string TmpPath, string Ext, string Codec, int Width, int Height, int DurMs, long Bytes);

        /// <summary>
        /// Width/height/duration/bitrate straight off the Windows property handler. Null when the
        /// shell can't describe the file — callers must treat that as "probe unknown", never as
        /// "unsupported": the transcoder is the only thing allowed to say no.
        /// </summary>
        public static async Task<VideoProbe?> ProbeAsync(string path, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct).ConfigureAwait(false);
                var props = await file.Properties.GetVideoPropertiesAsync().AsTask(ct).ConfigureAwait(false);
                int w = (int)props.Width, h = (int)props.Height;
                int durMs = (int)Math.Min(int.MaxValue, props.Duration.TotalMilliseconds);
                long bitrate = props.Bitrate;
                // A zero-everything probe is the shell shrugging, not a real answer.
                if (w <= 0 && h <= 0 && durMs <= 0 && bitrate <= 0) return null;
                return new VideoProbe(w, h, durMs, bitrate);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoTranscodeLane.ProbeAsync({Path}): {E}", path, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Target bitrate for a source: 85% of what it already spends, capped at 1.8 Mbps and never
        /// below 0.9 Mbps. An unknown source bitrate takes the preferred target.
        /// </summary>
        public static long TargetBitrateFor(long srcBitrate)
        {
            long wanted = srcBitrate > 0
                ? Math.Min((long)(srcBitrate * 0.85), PreferredTargetBitrate)
                : PreferredTargetBitrate;
            return Math.Clamp(wanted, MinTargetBitrate, MaxTargetBitrate);
        }

        /// <summary>
        /// Transcode <paramref name="srcPath"/> to H.264/AAC MP4 at <paramref name="tmpOut"/>.
        /// <paramref name="onProgress"/> receives 0-100 on the transcoder's own thread.
        /// Throws <see cref="TranscodeUnsupportedException"/> when Windows can't read the source.
        /// </summary>
        public static async Task<TranscodeResult> TranscodeAsync(
            string srcPath, string tmpOut, VideoProbe? probe,
            Action<double>? onProgress = null, CancellationToken ct = default)
        {
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            profile.Video.Bitrate = (uint)TargetBitrateFor(probe?.Bitrate ?? 0);
            if (probe is { Width: > 0, Height: > 0 } p)
            {
                // Fit inside 1280x720 on the long side, even dimensions (H.264 chroma), and NEVER
                // upscale: the profile's own 1280x720 default would blow a 640x480 clip up.
                var (w, h) = FitEven(p.Width, p.Height, MaxWidth, MaxHeight);
                profile.Video.Width = w;
                profile.Video.Height = h;
            }

            await RunAsync(srcPath, tmpOut, profile, null, null, onProgress, ct).ConfigureAwait(false);

            long bytes = new FileInfo(tmpOut).Length;
            return new TranscodeResult(tmpOut, "mp4", "avc1",
                (int)profile.Video.Width, (int)profile.Video.Height, probe?.DurMs ?? 0, bytes);
        }

        /// <summary>
        /// 2-second, 426×240, audio-free micro-preview starting 12% into the clip (clamped so the
        /// window always fits inside the duration). Used by the assets grid and, later, by the
        /// offer card — never by playback.
        /// </summary>
        public static async Task<long> MakePreviewAsync(
            string srcPath, string tmpOut, VideoProbe? probe, CancellationToken ct = default)
        {
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
            profile.Audio = null;                       // a preview with sound is a jump-scare
            profile.Video.Bitrate = (uint)PreviewBitrate;
            var (w, h) = probe is { Width: > 0, Height: > 0 } p
                ? FitEven(p.Width, p.Height, PreviewWidth, PreviewHeight)
                : ((uint)PreviewWidth, (uint)PreviewHeight);
            profile.Video.Width = w;
            profile.Video.Height = h;
            try
            {
                if (profile.Video.FrameRate != null)
                {
                    profile.Video.FrameRate.Numerator = PreviewFps;
                    profile.Video.FrameRate.Denominator = 1;
                }
            }
            catch { /* some profiles refuse a framerate override; 30fps of 2s is survivable */ }

            var durMs = probe?.DurMs ?? 0;
            TimeSpan start = TimeSpan.Zero, stop;
            if (durMs > PreviewMs)
            {
                double startMs = durMs * PreviewStartFraction;
                startMs = Math.Clamp(startMs, 0, durMs - PreviewMs);
                start = TimeSpan.FromMilliseconds(startMs);
                stop = TimeSpan.FromMilliseconds(startMs + PreviewMs);
            }
            else
            {
                // Shorter than the preview window: take the whole thing.
                stop = durMs > 0 ? TimeSpan.FromMilliseconds(durMs) : TimeSpan.FromMilliseconds(PreviewMs);
            }

            await RunAsync(srcPath, tmpOut, profile, start, stop, null, ct).ConfigureAwait(false);
            return new FileInfo(tmpOut).Length;
        }

        private static async Task RunAsync(
            string srcPath, string tmpOut, MediaEncodingProfile profile,
            TimeSpan? trimStart, TimeSpan? trimStop,
            Action<double>? onProgress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(tmpOut)!);

            StorageFile src, dst;
            try
            {
                src = await StorageFile.GetFileFromPathAsync(srcPath).AsTask(ct).ConfigureAwait(false);
                var dstDir = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(tmpOut)!)
                    .AsTask(ct).ConfigureAwait(false);
                dst = await dstDir.CreateFileAsync(Path.GetFileName(tmpOut), CreationCollisionOption.ReplaceExisting)
                    .AsTask(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new TranscodeUnsupportedException("io: " + ex.Message);
            }

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            if (trimStart.HasValue) transcoder.TrimStartTime = trimStart.Value;
            if (trimStop.HasValue) transcoder.TrimStopTime = trimStop.Value;

            var prep = await transcoder.PrepareFileTranscodeAsync(src, dst, profile).AsTask(ct).ConfigureAwait(false);
            if (!prep.CanTranscode)
            {
                TryDelete(tmpOut);
                App.Logger?.Information("VideoTranscodeLane: cannot transcode {Path} ({Reason})",
                    srcPath, prep.FailureReason);
                throw new TranscodeUnsupportedException(TransferFailReasons.NoDecoder);
            }

            var op = prep.TranscodeAsync();
            if (onProgress != null)
            {
                op.Progress = (_, percent) =>
                {
                    try { onProgress(percent); }
                    catch { /* a UI subscriber must never kill an encode */ }
                };
            }
            using var reg = ct.Register(() => { try { op.Cancel(); } catch { } });
            try
            {
                await op.AsTask().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                TryDelete(tmpOut);
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex)
            {
                TryDelete(tmpOut);
                App.Logger?.Warning("VideoTranscodeLane: transcode of {Path} failed: {E}", srcPath, ex.Message);
                throw;
            }
        }

        /// <summary>Fit inside a box preserving aspect, never upscaling, rounding to even dimensions.</summary>
        internal static (uint W, uint H) FitEven(int srcW, int srcH, int maxW, int maxH)
        {
            if (srcW <= 0 || srcH <= 0) return ((uint)Even(maxW), (uint)Even(maxH));
            double scale = Math.Min(maxW / (double)srcW, maxH / (double)srcH);
            if (scale >= 1.0) return ((uint)Even(srcW), (uint)Even(srcH));
            return ((uint)Even((int)Math.Round(srcW * scale)), (uint)Even((int)Math.Round(srcH * scale)));
        }

        private static int Even(int v) => Math.Max(2, v - (v % 2));

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
