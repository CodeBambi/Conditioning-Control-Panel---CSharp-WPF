using System;
using System.IO;
using System.Threading;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>
    /// Lane A: stills, host side, SkiaSharp only (the app already ships it and already decodes
    /// every image format the pool can contain through SKCodec — see
    /// <see cref="ConditioningControlPanel.Services.AnimatedWebp"/>).
    ///
    /// Downscale the long edge to 1920 and re-encode WebP q80; JPEG q82 if the WebP encoder hands
    /// back nothing (it can, for exotic colour types). Because this is a full decode → re-encode,
    /// EXIF/XMP/ICC and any GPS tag the user's phone stamped are gone by construction — which is
    /// the point: these bytes are about to be sent to a stranger.
    ///
    /// Writes to the tmp folder and hands the path back; hashing and the move into art/ belong to
    /// the service's single commit path.
    /// </summary>
    internal static class StillCompressLane
    {
        public const int MaxLongEdge = 1920;
        private const int WebpQuality = 80;
        private const int JpegQuality = 82;
        /// <summary>Same guard AnimatedWebp uses — a decoded 8K×8K is 256 MB of pixels.</summary>
        private const long MaxSourcePixels = 4096L * 4096L;

        public sealed record Result(string TmpPath, string Ext, string Codec, int Width, int Height, long Bytes);

        /// <summary>
        /// Compress one still into <paramref name="tmpPath"/>. Returns null when the file can't be
        /// decoded or the encoders both refuse — the caller records that as failed:encode-failed.
        /// Blocking and CPU-heavy: call from a worker, never the UI thread.
        /// </summary>
        public static Result? Compress(string srcPath, string tmpPath, CancellationToken ct = default)
        {
            SKBitmap? src = null;
            SKBitmap? scaled = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                src = SKBitmap.Decode(srcPath);
                if (src == null || src.Width <= 0 || src.Height <= 0)
                {
                    App.Logger?.Debug("StillCompressLane: undecodable {Path}", srcPath);
                    return null;
                }
                if ((long)src.Width * src.Height > MaxSourcePixels)
                {
                    App.Logger?.Debug("StillCompressLane: {Path} is {W}x{H}, past the decode guard",
                        srcPath, src.Width, src.Height);
                    return null;
                }

                var (tw, th) = ScaledSize(src.Width, src.Height, MaxLongEdge);
                var bmp = src;
                if (tw != src.Width || th != src.Height)
                {
                    scaled = src.Resize(
                        new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul),
                        SKFilterQuality.High);
                    if (scaled != null) bmp = scaled;
                    else { tw = src.Width; th = src.Height; }   // resize refused: ship full size rather than nothing
                }

                ct.ThrowIfCancellationRequested();
                var (data, ext, codec) = Encode(bmp);
                if (data == null)
                {
                    App.Logger?.Debug("StillCompressLane: both encoders refused {Path}", srcPath);
                    return null;
                }
                using (data)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(tmpPath)!);
                    using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    data.SaveTo(fs);
                }
                var len = new FileInfo(tmpPath).Length;
                return new Result(tmpPath, ext, codec, bmp.Width, bmp.Height, len);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Logger?.Warning("StillCompressLane({Path}) failed: {E}", srcPath, ex.Message);
                return null;
            }
            finally
            {
                scaled?.Dispose();
                src?.Dispose();
            }
        }

        private static (SKData? Data, string Ext, string Codec) Encode(SKBitmap bmp)
        {
            try
            {
                using var image = SKImage.FromBitmap(bmp);
                var webp = image?.Encode(SKEncodedImageFormat.Webp, WebpQuality);
                if (webp != null && webp.Size > 0) return (webp, "webp", "webp");
                webp?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("StillCompressLane: webp encode threw: {E}", ex.Message);
            }

            // JPEG has no alpha, so flatten first - otherwise transparent regions come out as
            // whatever happened to be in the premultiplied buffer.
            SKBitmap? flat = null;
            try
            {
                flat = new SKBitmap(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                using (var canvas = new SKCanvas(flat))
                {
                    canvas.Clear(SKColors.Black);
                    canvas.DrawBitmap(bmp, 0, 0);
                }
                using var image = SKImage.FromBitmap(flat);
                var jpeg = image?.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                if (jpeg != null && jpeg.Size > 0) return (jpeg, "jpg", "jpeg");
                jpeg?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("StillCompressLane: jpeg fallback threw: {E}", ex.Message);
            }
            finally { flat?.Dispose(); }

            return (null, "", "");
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
