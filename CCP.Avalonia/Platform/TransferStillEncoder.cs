using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services.Transfer;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The SkiaSharp half of the transfer cache's still lane (WPF Services/Media/Transfer/StillCompressLane.cs,
/// body for body; the rules and numbers are Core's <see cref="StillCompressLane"/>). Downscale the long
/// edge to 1920 and re-encode WebP q80, JPEG q82 if the WebP encoder hands back nothing. A full decode
/// and re-encode, so EXIF / XMP / ICC and any GPS tag are gone by construction.
/// </summary>
internal static class TransferStillEncoder
{
    /// <summary>Null when the file cannot be decoded or both encoders refuse (failed:encode-failed).
    /// Blocking and CPU-heavy: workers only.</summary>
    public static StillCompressLane.Result? Compress(string srcPath, string tmpPath, CancellationToken ct)
    {
        SKBitmap? src = null;
        SKBitmap? scaled = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            src = SKBitmap.Decode(srcPath);
            if (src == null || src.Width <= 0 || src.Height <= 0)
            {
                Log.Debug("StillCompressLane: undecodable {Path}", srcPath);
                return null;
            }
            if ((long)src.Width * src.Height > StillCompressLane.MaxSourcePixels)
            {
                Log.Debug("StillCompressLane: {Path} is {W}x{H}, past the decode guard", srcPath, src.Width, src.Height);
                return null;
            }

            var (tw, th) = StillCompressLane.ScaledSize(src.Width, src.Height, StillCompressLane.MaxLongEdge);
            var bmp = src;
            if (tw != src.Width || th != src.Height)
            {
                scaled = src.Resize(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (scaled != null) bmp = scaled;   // resize refused: ship full size rather than nothing
            }

            ct.ThrowIfCancellationRequested();
            var (data, ext, codec) = Encode(bmp);
            if (data == null)
            {
                Log.Debug("StillCompressLane: both encoders refused {Path}", srcPath);
                return null;
            }
            using (data)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tmpPath)!);
                using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                data.SaveTo(fs);
            }
            return new StillCompressLane.Result(tmpPath, ext, codec, bmp.Width, bmp.Height, new FileInfo(tmpPath).Length);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning("StillCompressLane({Path}) failed: {E}", srcPath, ex.Message);
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
            var webp = image?.Encode(SKEncodedImageFormat.Webp, StillCompressLane.WebpQuality);
            if (webp != null && webp.Size > 0) return (webp, "webp", "webp");
            webp?.Dispose();
        }
        catch (Exception ex) { Log.Debug("StillCompressLane: webp encode threw: {E}", ex.Message); }

        // JPEG has no alpha, so flatten first: otherwise transparent regions come out as whatever
        // happened to be in the premultiplied buffer.
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
            var jpeg = image?.Encode(SKEncodedImageFormat.Jpeg, StillCompressLane.JpegQuality);
            if (jpeg != null && jpeg.Size > 0) return (jpeg, "jpg", "jpeg");
            jpeg?.Dispose();
        }
        catch (Exception ex) { Log.Debug("StillCompressLane: jpeg fallback threw: {E}", ex.Message); }
        finally { flat?.Dispose(); }

        return (null, "", "");
    }

    /// <summary>WPF AssetCompressionPlanner's gif probe: the codec's frame count.</summary>
    public static int GifFrameCount(string path)
    {
        using var codec = SKCodec.Create(path);
        return codec?.FrameCount ?? 0;
    }
}
