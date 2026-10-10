using System;
using System.Threading;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>
    /// Lane A: stills, host side (WPF Services/Media/Transfer/StillCompressLane.cs). The rules live
    /// here (long edge 1920, WebP q80 then JPEG q82, the 4096x4096 decode guard); the SkiaSharp
    /// decode and encode is the head's (<see cref="Encoder"/>, CCP.Avalonia Platform/TransferStillEncoder.cs),
    /// because Core carries no SkiaSharp. A full decode and re-encode drops EXIF / XMP / ICC and any
    /// GPS tag by construction, which is the point: these bytes are about to be sent to someone else.
    /// </summary>
    internal static class StillCompressLane
    {
        public const int MaxLongEdge = 1920;
        public const int WebpQuality = 80;
        public const int JpegQuality = 82;
        /// <summary>A decoded 8K x 8K is 256 MB of pixels.</summary>
        public const long MaxSourcePixels = 4096L * 4096L;

        public sealed record Result(string TmpPath, string Ext, string Codec, int Width, int Height, long Bytes);

        /// <summary>The head's encoder: (source, tmp out, cancel) -> result, null when the file cannot be
        /// decoded or both encoders refuse.</summary>
        public static Func<string, string, CancellationToken, Result?>? Encoder { get; set; }

        /// <summary>Compress one still into <paramref name="tmpPath"/>. Null = failed:encode-failed
        /// (also the answer when no head seeded an encoder). Blocking and CPU-heavy: workers only.</summary>
        public static Result? Compress(string srcPath, string tmpPath, CancellationToken ct = default)
        {
            var enc = Encoder;
            if (enc == null) { Serilog.Log.Debug("StillCompressLane: no encoder on this head"); return null; }
            return enc(srcPath, tmpPath, ct);
        }

        internal static (int W, int H) ScaledSize(int srcW, int srcH, int maxLongEdge)
        {
            int longest = Math.Max(srcW, srcH);
            if (longest <= maxLongEdge) return (srcW, srcH);
            double scale = maxLongEdge / (double)longest;
            return (Math.Max(1, (int)Math.Round(srcW * scale)), Math.Max(1, (int)Math.Round(srcH * scale)));
        }
    }
}
