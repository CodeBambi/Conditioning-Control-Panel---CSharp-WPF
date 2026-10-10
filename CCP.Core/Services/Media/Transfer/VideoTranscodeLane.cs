using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>Thrown when the platform has no decoder for a source, or no transcoder at all. The
    /// entry becomes failed:no-decoder, the honest answer.</summary>
    internal sealed class TranscodeUnsupportedException : Exception
    {
        public string Reason { get; }
        public TranscodeUnsupportedException(string reason)
            : base("transcode unsupported: " + reason) => Reason = reason;
    }

    /// <summary>
    /// Lane C: video, host side (WPF Services/Media/Transfer/VideoTranscodeLane.cs). The targets live
    /// here (720p H.264, 85% of the source bitrate clamped 0.9 to 2.0 Mbps, the 426x240 two-second
    /// silent preview). The engine in WPF is WinRT MediaTranscoder, which Core cannot reference, so it
    /// is three seams. The Windows head seeds them with the same WinRT engine
    /// (CCP.Avalonia/Platform/WinRtVideoTranscoder.cs); Linux has no engine yet. With no seam seeded a
    /// probe is "unknown" (never "unsupported") and a transcode is refused as no-decoder, so a video
    /// over 5 MB shows as failed in the page's library and is never offered; videos of 5 MB or less
    /// travel as they are.
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

        public sealed record VideoProbe(int Width, int Height, int DurMs, long Bitrate);

        public sealed record TranscodeResult(string TmpPath, string Ext, string Codec, int Width, int Height, int DurMs, long Bytes);

        /// <summary>The platform engine; null on a head without one.</summary>
        public static Func<string, CancellationToken, Task<VideoProbe?>>? Probe { get; set; }
        public static Func<string, string, VideoProbe?, Action<double>?, CancellationToken, Task<TranscodeResult>>? Transcode { get; set; }
        public static Func<string, string, VideoProbe?, CancellationToken, Task<long>>? Preview { get; set; }

        /// <summary>True when this head can re-encode a big video.</summary>
        public static bool Available => Transcode != null;

        /// <summary>Null = "probe unknown", never "unsupported": the transcoder is the only thing allowed to say no.</summary>
        public static async Task<VideoProbe?> ProbeAsync(string path, CancellationToken ct = default)
        {
            var probe = Probe;
            if (probe == null) return null;
            try
            {
                ct.ThrowIfCancellationRequested();
                return await probe(path, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Serilog.Log.Debug("VideoTranscodeLane.ProbeAsync({Path}): {E}", path, ex.Message);
                return null;
            }
        }

        /// <summary>85% of what the source already spends, capped at 1.8 Mbps and never below 0.9 Mbps.
        /// An unknown source bitrate takes the preferred target.</summary>
        public static long TargetBitrateFor(long srcBitrate)
        {
            long wanted = srcBitrate > 0
                ? Math.Min((long)(srcBitrate * 0.85), PreferredTargetBitrate)
                : PreferredTargetBitrate;
            return Math.Clamp(wanted, MinTargetBitrate, MaxTargetBitrate);
        }

        public static Task<TranscodeResult> TranscodeAsync(
            string srcPath, string tmpOut, VideoProbe? probe,
            Action<double>? onProgress = null, CancellationToken ct = default)
        {
            var run = Transcode;
            if (run == null) throw new TranscodeUnsupportedException(TransferFailReasons.NoDecoder);
            return run(srcPath, tmpOut, probe, onProgress, ct);
        }

        public static Task<long> MakePreviewAsync(
            string srcPath, string tmpOut, VideoProbe? probe, CancellationToken ct = default)
        {
            var run = Preview;
            if (run == null) throw new TranscodeUnsupportedException(TransferFailReasons.NoDecoder);
            return run(srcPath, tmpOut, probe, ct);
        }

        /// <summary>WPF PreviewStartFraction: the preview starts 12% into the clip.</summary>
        public const double PreviewStartFraction = 0.12;

        /// <summary>The preview's trim window in ms (WPF MakePreviewAsync): two seconds starting 12% in,
        /// clamped so the window always fits; a clip shorter than the window is taken whole, and an
        /// unknown duration takes the first two seconds.</summary>
        public static (double StartMs, double StopMs) PreviewWindow(int durMs)
        {
            if (durMs > PreviewMs)
            {
                double start = Math.Clamp(durMs * PreviewStartFraction, 0, durMs - PreviewMs);
                return (start, start + PreviewMs);
            }
            return (0, durMs > 0 ? durMs : PreviewMs);
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
    }
}
