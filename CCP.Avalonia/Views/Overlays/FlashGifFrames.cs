using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Animated flash pictures (GIF and animated WebP): the head twin of WPF
    /// <c>AnimatedWebp.DecodeFrames</c> as <c>FlashService.LoadGifFrames</c> calls it
    /// (maxFrames 60, maxMemoryMb 30, decode ceiling 600, CEILING stride #683, restore-previous
    /// disposal kept by copy, average delay x stride clamped 20..2000 ms) and
    /// <c>FlashService.ScaleFrameDelay</c> (the user's GIF speed, 0.25..4, floored at 10 ms).
    /// Frames are decoded straight to the flash's display size, so each window holds only what
    /// it shows.
    /// </summary>
    internal static class FlashGifFrames
    {
        internal const int MaxFrames = 60;
        internal const double MaxMemoryMb = 30.0;
        internal const int DecodeCeiling = 600;
        internal const double MinFrameDelayMs = 10.0;

        // WPF _decodeGate: one animated decode at a time, so a burst of five gifs never holds five
        // full-size canvases at once.
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>WPF FlashService.ScaleFrameDelay, verbatim.</summary>
        internal static TimeSpan ScaleFrameDelay(TimeSpan sourceDelay, double multiplier)
        {
            var ms = sourceDelay.TotalMilliseconds;
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0) ms = 100.0;
            if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0) multiplier = 1.0;
            multiplier = Math.Clamp(multiplier, 0.25, 4.0);
            return TimeSpan.FromMilliseconds(Math.Max(MinFrameDelayMs, ms / multiplier));
        }

        /// <summary>WPF AnimatedWebp.FramePlan.Create: ceiling stride so the kept set spans the clip.</summary>
        internal static (int Step, int DecodeCount, int MaxKeep) Plan(int frameCount, int maxKeep)
        {
            int keep = Math.Max(1, maxKeep);
            int step = Math.Max(1, (int)Math.Ceiling(frameCount / (double)keep));
            return (step, Math.Clamp(frameCount, 0, DecodeCeiling), keep);
        }

        /// <summary>The composed frames at <paramref name="width"/> x <paramref name="height"/> and
        /// the per-frame delay, or null for a still or undecodable file (the caller keeps its still
        /// path). Heavy: call off the UI thread.</summary>
        internal static (List<Bitmap> Frames, TimeSpan FrameDelay)? Decode(string path, int width, int height)
        {
            if (width <= 0 || height <= 0) return null;
            Gate.Wait();
            try
            {
                using var codec = SKCodec.Create(path);
                return DecodeCore(codec, width, height);
            }
            catch { return null; }
            finally { Gate.Release(); }
        }

        private static (List<Bitmap> Frames, TimeSpan FrameDelay)? DecodeCore(SKCodec? codec, int tw, int th)
        {
            if (codec == null) return null;
            int frameCount = codec.FrameCount;
            if (frameCount <= 1) return null;
            int srcW = codec.Info.Width, srcH = codec.Info.Height;
            if (srcW <= 0 || srcH <= 0 || (long)srcW * srcH > 4096L * 4096L) return null;

            var bytesPerKept = (long)tw * th * 4;
            var estimatedMb = bytesPerKept * frameCount / (1024.0 * 1024.0);
            int maxKeep = frameCount;
            if (estimatedMb > MaxMemoryMb)
                maxKeep = Math.Max(10, (int)(frameCount * (MaxMemoryMb / estimatedMb)));
            maxKeep = Math.Min(maxKeep, MaxFrames);
            var (step, decodeCount, keep) = Plan(frameCount, maxKeep);

            var infos = codec.FrameInfo;
            var canvasInfo = new SKImageInfo(srcW, srcH, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKBitmap(canvasInfo);
            using var scaled = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul));
            IntPtr pixels = canvas.GetPixels();

            var frames = new List<Bitmap>();
            long totalMs = 0; int samples = 0;
            byte[]? restore = null;
            int restoreIndex = -1;
            try
            {
                for (int i = 0; i < decodeCount && frames.Count < keep; i++)
                {
                    int prior = -1;
                    if (i > 0 && infos[i].RequiredFrame >= 0)
                    {
                        if (infos[i - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious)
                            prior = i - 1;
                        else if (restore != null && restoreIndex >= infos[i].RequiredFrame)
                        {
                            Marshal.Copy(restore, 0, pixels, restore.Length);
                            prior = restoreIndex;
                        }
                    }
                    var opts = prior >= 0 ? new SKCodecOptions(i, prior) : new SKCodecOptions(i);
                    var res = codec.GetPixels(canvasInfo, pixels, opts);
                    if (res != SKCodecResult.Success && res != SKCodecResult.IncompleteInput) break;

                    if (i + 1 < decodeCount
                        && infos[i].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious
                        && infos[i + 1].DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious)
                    {
                        restore ??= new byte[canvas.ByteCount];
                        canvas.GetPixelSpan().CopyTo(restore);
                        restoreIndex = i;
                    }

                    if (i % step != 0) continue;
                    canvas.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    frames.Add(new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(),
                        new PixelSize(tw, th), new Vector(96, 96), scaled.RowBytes));
                    int d = infos[i].Duration;
                    if (d > 0) { totalMs += d; samples++; }
                }
            }
            catch
            {
                foreach (var f in frames) f.Dispose();
                return null;
            }
            if (frames.Count < 2)
            {
                foreach (var f in frames) f.Dispose();
                return null;
            }
            double avgMs = samples > 0 ? (double)totalMs / samples : 100;
            if (avgMs < 20) avgMs = 100;
            return (frames, TimeSpan.FromMilliseconds(Math.Clamp(avgMs * step, 20, 2000)));
        }
    }
}
