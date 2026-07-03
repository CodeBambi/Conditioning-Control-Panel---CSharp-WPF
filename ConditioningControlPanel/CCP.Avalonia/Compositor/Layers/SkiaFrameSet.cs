using System;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// An immutable, ref-counted set of decoded SKImage frames (1 frame = static image,
/// N frames = animated GIF/WebP) plus per-frame display delays.
///
/// Lifetime contract (the UCE dispose-vs-render rule): an SKImage is a native Skia
/// handle; disposing it while the render thread may still draw it corrupts the native
/// heap (intermittent 0xC0000005 — see BubbleLayer's field comment and
/// AvaloniaUI/Avalonia#13521). Frames in a set are therefore NEVER disposed directly.
/// Owners take a reference (<see cref="AddRef"/>) and drop it (<see cref="Release"/>);
/// the frames are disposed only when the last reference is released. Layers that hold a
/// set MUST perform their final <see cref="Release"/> while holding the same lock their
/// Render method draws under, so no draw can be in flight when disposal happens.
/// </summary>
public sealed class SkiaFrameSet
{
    private int _refCount = 1; // creator's reference

    /// <summary>Decoded frames. Never empty.</summary>
    public SKImage[] Frames { get; }

    /// <summary>Display duration of each kept frame, in seconds (includes stepped-over frames).</summary>
    public double[] FrameDelaysSeconds { get; }

    /// <summary>Approximate retained raster bytes (w * h * 4 * frames).</summary>
    public long PixelBytes { get; }

    /// <summary>Decoded (possibly downscaled) frame width in pixels.</summary>
    public int Width { get; }

    /// <summary>Decoded (possibly downscaled) frame height in pixels.</summary>
    public int Height { get; }

    public bool IsAnimated => Frames.Length > 1;

    public SkiaFrameSet(SKImage[] frames, double[] frameDelaysSeconds, long pixelBytes, int width, int height)
    {
        if (frames == null || frames.Length == 0) throw new ArgumentException("Frame set requires at least one frame", nameof(frames));
        Frames = frames;
        FrameDelaysSeconds = frameDelaysSeconds ?? Array.Empty<double>();
        PixelBytes = pixelBytes;
        Width = width;
        Height = height;
    }

    /// <summary>Take an additional reference (e.g. a cache handing the set to a new consumer).</summary>
    public void AddRef() => Interlocked.Increment(ref _refCount);

    /// <summary>
    /// Drop one reference; disposes all frames when the count reaches zero.
    /// The caller must guarantee no render pass can still be drawing these frames when
    /// this is the last reference (see class remarks).
    /// </summary>
    public void Release()
    {
        if (Interlocked.Decrement(ref _refCount) == 0)
        {
            foreach (var frame in Frames) frame?.Dispose();
        }
    }
}

/// <summary>
/// Shared SKCodec-based decoder for compositor image layers (flash, spiral).
/// Decodes off the calling thread's context (callers run it inside Task.Run), applies the
/// WPF FlashService budgets (downscale-to-display-cap, frame cap, memory cap), reads real
/// per-frame delays from the codec metadata, and composites delta frames in O(n) by
/// reusing one bitmap with <see cref="SKCodecOptions.PriorFrame"/>.
/// </summary>
internal static class SkiaImageDecoder
{
    /// <summary>
    /// Decode <paramref name="path"/> into a frame set.
    /// </summary>
    /// <param name="path">Image file path.</param>
    /// <param name="maxFrames">Hard cap on kept frames (WPF: 60 for flash, 120 for spiral). Extra frames are stepped over evenly; their delays accumulate onto the kept frame so total loop duration is preserved.</param>
    /// <param name="decodeMaxDim">Longest-edge pixel cap for kept frames (0 = no downscale). Never upscales.</param>
    /// <param name="maxMemoryMb">Retained-raster budget in MB (0 = unlimited); shrinks the frame count like WPF LoadGifFrames' 30MB budget.</param>
    /// <param name="defaultFrameDelayMs">Delay used when a frame's metadata duration is missing/invalid (WPF: 100 flash, 50 spiral).</param>
    /// <param name="maxFrameDelayMs">Upper clamp on a single frame delay (0 = none; WPF spiral clamps &gt;500ms to the default).</param>
    /// <returns>The decoded set (caller owns one reference), or null on failure.</returns>
    public static SkiaFrameSet? Decode(string path, int maxFrames, int decodeMaxDim, double maxMemoryMb,
        int defaultFrameDelayMs, int maxFrameDelayMs)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec == null) return DecodeStaticFallback(path, decodeMaxDim);

            var srcW = codec.Info.Width;
            var srcH = codec.Info.Height;
            if (srcW <= 0 || srcH <= 0) return null;

            var (targetW, targetH) = ScaledSize(srcW, srcH, decodeMaxDim);
            var frameCount = Math.Max(1, codec.FrameCount);

            // Frame budget: memory cap first (like WPF LoadGifFrames), then the hard cap.
            var keepCount = frameCount;
            if (maxMemoryMb > 0 && frameCount > 1)
            {
                var bytesPerFrame = (long)targetW * targetH * 4;
                var estimatedMb = bytesPerFrame * frameCount / (1024.0 * 1024.0);
                if (estimatedMb > maxMemoryMb)
                    keepCount = Math.Max(10, (int)(frameCount * (maxMemoryMb / estimatedMb)));
            }
            keepCount = Math.Min(keepCount, Math.Max(1, maxFrames));
            // Ceil step: samples the WHOLE animation evenly (keeps loop continuity) instead
            // of truncating the tail.
            var step = (int)Math.Ceiling(frameCount / (double)keepCount);

            var info = new SKImageInfo(srcW, srcH);
            var frameInfos = frameCount > 1 ? codec.FrameInfo : Array.Empty<SKCodecFrameInfo>();
            var frames = new List<SKImage>(keepCount);
            var delays = new List<double>(keepCount);
            var needsScale = targetW != srcW || targetH != srcH;

            // One reusable bitmap: after decoding frame i it holds frame i's composited
            // pixels, which PriorFrame = i-1 lets the codec build frame i+1 from without
            // re-decoding the whole required-frame chain (O(n) instead of O(n^2)).
            using var bitmap = new SKBitmap(info);

            for (int i = 0; i < frameCount; i++)
            {
                SKCodecResult result;
                if (frameCount > 1)
                {
                    var opts = i > 0 ? new SKCodecOptions(i, i - 1) : new SKCodecOptions(i);
                    result = codec.GetPixels(info, bitmap.GetPixels(), opts);
                }
                else
                {
                    result = codec.GetPixels(info, bitmap.GetPixels());
                }

                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    if (frames.Count == 0) return null;
                    break; // keep what we decoded so far
                }

                if (i % step == 0 && frames.Count < keepCount)
                {
                    SKImage? image;
                    if (needsScale)
                    {
                        using var scaled = bitmap.Resize(new SKImageInfo(targetW, targetH, info.ColorType, info.AlphaType),
                            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                        image = scaled != null ? SKImage.FromBitmap(scaled) : null;
                    }
                    else
                    {
                        image = SKImage.FromBitmap(bitmap); // copies the mutable bitmap's pixels
                    }

                    if (image != null)
                    {
                        frames.Add(image);
                        delays.Add(0);
                    }
                }

                // Accumulate every source frame's duration onto the most recent kept frame
                // so stepping over frames slows playback of the kept frame instead of
                // speeding the whole animation up (WPF multiplies delay by step).
                if (delays.Count > 0 && frameCount > 1)
                {
                    var ms = i < frameInfos.Length ? frameInfos[i].Duration : defaultFrameDelayMs;
                    if (ms < 20 || (maxFrameDelayMs > 0 && ms > maxFrameDelayMs)) ms = defaultFrameDelayMs;
                    delays[delays.Count - 1] += ms / 1000.0;
                }
            }

            if (frames.Count == 0) return null;

            var pixelBytes = (long)targetW * targetH * 4 * frames.Count;
            return new SkiaFrameSet(frames.ToArray(), delays.ToArray(), pixelBytes, targetW, targetH);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Fallback for formats SKCodec cannot stream: full SKBitmap decode, single frame.</summary>
    private static SkiaFrameSet? DecodeStaticFallback(string path, int decodeMaxDim)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap == null) return null;

            var (targetW, targetH) = ScaledSize(bitmap.Width, bitmap.Height, decodeMaxDim);
            SKImage? image;
            if (targetW != bitmap.Width || targetH != bitmap.Height)
            {
                using var scaled = bitmap.Resize(new SKImageInfo(targetW, targetH, bitmap.ColorType, bitmap.AlphaType),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                image = scaled != null ? SKImage.FromBitmap(scaled) : null;
            }
            else
            {
                image = SKImage.FromBitmap(bitmap);
            }

            if (image == null) return null;
            return new SkiaFrameSet(new[] { image }, new[] { 0.0 }, (long)targetW * targetH * 4, targetW, targetH);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Aspect-preserving size so the longest edge is at most <paramref name="maxDim"/>
    /// (0 = uncapped). Never upscales. Mirrors WPF FlashService.ScaledSize.
    /// </summary>
    internal static (int w, int h) ScaledSize(int srcW, int srcH, int maxDim)
    {
        if (srcW <= 0 || srcH <= 0 || maxDim <= 0) return (srcW, srcH);
        var longest = Math.Max(srcW, srcH);
        if (longest <= maxDim) return (srcW, srcH);
        var ratio = (double)maxDim / longest;
        return (Math.Max(1, (int)Math.Round(srcW * ratio)),
                Math.Max(1, (int)Math.Round(srcH * ratio)));
    }
}
