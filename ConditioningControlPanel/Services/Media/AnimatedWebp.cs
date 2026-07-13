using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using SkiaSharp;
using Image = System.Windows.Controls.Image;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Animated .webp support. Every display path in the app treated webp as a still: WPF's WIC
/// codec only ever hands over the first frame, GDI+ (System.Drawing) can't open webp at all,
/// and XamlAnimatedGif is GIF-only — so animated webps in the user's image pool played frozen
/// (report 2026-07-02). SkiaSharp's SKCodec decodes the full animation INCLUDING frame
/// composition (per-frame required-frame/blend handling that raw WIC frames don't give you),
/// with no dependency on the OS "WebP Image Extensions" codec being installed.
///
/// Despite the name, SKCodec is format-agnostic: since the #486 native-OOM fix these same
/// entry points also decode animated GIFs (FlashService.LoadGifFrames, chaos wash, tease
/// bubbles), replacing GDI+/XamlAnimatedGif's uncapped native-resolution decodes. Note GIF's
/// rare "restore previous" disposal isn't modeled (WebP has none) — acceptable for effects.
///
/// Two consumption shapes:
///  - <see cref="DecodeFrames"/> → frozen frame list + delay for FlashService's heartbeat
///    frame-stepper (mirrors its LoadGifFrames memory budget).
///  - <see cref="AttachAnimation"/>/<see cref="Detach"/> → a looping Image.Source keyframe
///    animation for the XamlAnimatedGif-style call sites (chaos wash/cascade, tease bubbles).
///    Detach is LOAD-BEARING: a RepeatBehavior.Forever clock keeps its target pinned by the
///    app-global timing manager until cleared with BeginAnimation(prop, null) — same leak the
///    flash glow effects hit (see FlashService.SafeCloseFlashWindow).
/// </summary>
internal static class AnimatedWebp
{
    /// <summary>
    /// Cheap 21-byte header probe: RIFF/WEBP container with a VP8X extended header whose
    /// animation flag (0x02, per the WebP container spec / libwebp ANIMATION_FLAG) is set.
    /// Still webps (VP8/VP8L simple format, or VP8X without the flag) return false. Safe to
    /// call on the UI thread — callers already do FileInfo.Length probes at the same sites.
    /// </summary>
    public static bool IsAnimated(string path)
    {
        try
        {
            Span<byte> h = stackalloc byte[21];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Read(h) < h.Length) return false;
            return h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
                && h[8] == (byte)'W' && h[9] == (byte)'E' && h[10] == (byte)'B' && h[11] == (byte)'P'
                && h[12] == (byte)'V' && h[13] == (byte)'P' && h[14] == (byte)'8' && h[15] == (byte)'X'
                && (h[20] & 0x02) != 0;
        }
        catch { return false; }
    }

    // Global gate on concurrent Skia decodes, across ALL callers (flash bursts, chaos
    // wash/cascade, tease bubbles). The per-decode memory budget below is only per-decode:
    // a flash burst used to fire N unthrottled Task.Run decodes at once, each holding a
    // srcW×srcH×4 composition canvas (up to 64MB at the 4096² guard) + kept frames — a
    // 15-wide GIF burst spiked private bytes +1.1GB in ~2s and died with 0xc0000374 native
    // heap corruption inside SkiaSharp (no crash.log; WER dump 28788 shows two threads
    // concurrently in DecodeFrames, one mid GetPixels, one freeing an SKFileStream).
    // Two permits bounds the aggregate at ~190MB worst-case; the same burst config that
    // crashed at 228s survived a 15-min soak with burst width capped to 2 (A/B, 2026-07-12).
    private static readonly SemaphoreSlim _decodeGate = new(2, 2);

    /// <summary>
    /// Decode an animated webp into fully-composed, frozen BGRA frames, downscaled so the
    /// longest edge is at most <paramref name="maxDim"/> and subsampled (evenly, GIF-loader
    /// style) so the kept set fits <paramref name="maxMemoryMb"/> / <paramref name="maxFrames"/>.
    /// Returns null for still/undecodable files so callers can fall back to their static path.
    /// Heavy — call off the UI thread (also blocks on the global decode gate).
    /// </summary>
    public static (List<BitmapSource> Frames, TimeSpan FrameDelay)? DecodeFrames(
        string path, int maxDim, int maxFrames, double maxMemoryMb)
    {
        _decodeGate.Wait();
        try
        {
            using var codec = SKCodec.Create(path);
            return DecodeFramesCore(codec, maxDim, maxFrames, maxMemoryMb);
        }
        finally { _decodeGate.Release(); }
    }

    /// <summary>
    /// Stream variant of <see cref="DecodeFrames(string,int,int,double)"/> for sources that are
    /// not on-disk files (pack:// application resources, mod archives). Same global decode gate.
    /// </summary>
    public static (List<BitmapSource> Frames, TimeSpan FrameDelay)? DecodeFrames(
        Stream stream, int maxDim, int maxFrames, double maxMemoryMb)
    {
        _decodeGate.Wait();
        try
        {
            using var data = SKData.Create(stream);
            if (data == null) return null;
            using var codec = SKCodec.Create(data);
            return DecodeFramesCore(codec, maxDim, maxFrames, maxMemoryMb);
        }
        finally { _decodeGate.Release(); }
    }

    private static (List<BitmapSource> Frames, TimeSpan FrameDelay)? DecodeFramesCore(
        SKCodec? codec, int maxDim, int maxFrames, double maxMemoryMb)
    {
        if (codec == null) return null;
        int frameCount = codec.FrameCount;
        if (frameCount <= 1) return null;

        int srcW = codec.Info.Width, srcH = codec.Info.Height;
        if (srcW <= 0 || srcH <= 0 || (long)srcW * srcH > 4096L * 4096L) return null;
        var (tw, th) = ScaledSize(srcW, srcH, maxDim);

        // Frame budget mirrors FlashService.LoadGifFrames: estimate kept-frame memory, then
        // subsample evenly rather than truncating (keeps the loop's full motion arc).
        var bytesPerKept = (long)tw * th * 4;
        var estimatedMemoryMB = (bytesPerKept * frameCount) / (1024.0 * 1024.0);
        int maxKeep = frameCount;
        if (estimatedMemoryMB > maxMemoryMb)
            maxKeep = Math.Max(10, (int)(frameCount * (maxMemoryMb / estimatedMemoryMB)));
        maxKeep = Math.Min(maxKeep, maxFrames);
        int step = frameCount > maxKeep ? frameCount / maxKeep : 1;

        // Delta frames must be composed in order, so every frame up to the hard ceiling is
        // DECODED sequentially into one reusable canvas; only every step-th is KEPT.
        var frameInfos = codec.FrameInfo;
        int decodeCount = Math.Min(frameCount, 600);   // CPU ceiling for pathological files
        var canvasInfo = new SKImageInfo(srcW, srcH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKBitmap(canvasInfo);
        IntPtr pixels = canvas.GetPixels();

        var frames = new List<BitmapSource>();
        long durationTotalMs = 0; int durationSamples = 0;
        for (int i = 0; i < decodeCount && frames.Count < maxKeep; i++)
        {
            // WebP has no restore-previous disposal, so the canvas (holding frame i-1) always
            // satisfies a dependent frame's RequiredFrame; independent frames decode standalone.
            var opts = i > 0 && frameInfos[i].RequiredFrame >= 0
                ? new SKCodecOptions(i, i - 1)
                : new SKCodecOptions(i);
            var res = codec.GetPixels(canvasInfo, pixels, opts);
            if (res != SKCodecResult.Success && res != SKCodecResult.IncompleteInput) break;

            if (i % step != 0) continue;
            frames.Add(ToFrozenBitmapSource(canvas, tw, th));
            int d = frameInfos[i].Duration;
            if (d > 0) { durationTotalMs += d; durationSamples++; }
        }
        if (frames.Count < 2) return null;

        double avgMs = durationSamples > 0 ? (double)durationTotalMs / durationSamples : 100;
        if (avgMs < 20) avgMs = 100;   // bogus/zero delays → 100ms, same as the GIF loader
        return (frames, TimeSpan.FromMilliseconds(Math.Clamp(avgMs * step, 20, 2000)));
    }

    // ---- Image.Source keyframe playback (the XamlAnimatedGif-style call sites) ----

    // Per-Image attach token: a newer Attach/Detach orphans any decode still in flight,
    // so a superseded wash/spawn can never stamp its frames onto a reused Image.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, object> _live = new();

    // Tiny LRU so the chaos wash/cascade re-showing the same pool clip doesn't re-decode it
    // every time. Frames are frozen (shareable); FlashService does NOT ride this — it has its
    // own decode cache.
    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, (List<BitmapSource> Frames, TimeSpan Delay, DateTime Touched)> _cache = new();
    private const int CACHE_MAX_ENTRIES = 4;

    /// <summary>
    /// Decode <paramref name="path"/> off-thread and start a looping frame animation on
    /// <paramref name="img"/> (falls back to a display-size still if the animated decode
    /// fails). Pair with <see cref="Detach"/> at teardown — the Forever clock pins the Image.
    /// UI thread only.
    /// </summary>
    public static void AttachAnimation(Image img, string path, int maxDim, int maxFrames = 48)
    {
        var token = new object();
        _live.Remove(img);
        _live.Add(img, token);
        int dim = Math.Clamp(maxDim, 64, 1280);

        Task.Run(() =>
        {
            (List<BitmapSource> Frames, TimeSpan Delay)? decoded = null;
            BitmapSource? still = null;
            try { decoded = DecodeCached(path, dim, maxFrames); }
            catch (Exception ex) { App.Logger?.Debug("AnimatedWebp decode {Path}: {E}", path, ex.Message); }
            if (decoded == null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = dim;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    if (bmp.CanFreeze) bmp.Freeze();
                    still = bmp;
                }
                catch { }
            }

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (!_live.TryGetValue(img, out var current) || !ReferenceEquals(current, token)) return;
                    if (decoded is { } d)
                    {
                        var anim = new ObjectAnimationUsingKeyFrames
                        {
                            Duration = new Duration(TimeSpan.FromMilliseconds(d.Delay.TotalMilliseconds * d.Frames.Count)),
                            RepeatBehavior = RepeatBehavior.Forever,
                        };
                        for (int i = 0; i < d.Frames.Count; i++)
                            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(d.Frames[i],
                                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(d.Delay.TotalMilliseconds * i))));
                        anim.Freeze();
                        img.Source = d.Frames[0];
                        img.BeginAnimation(Image.SourceProperty, anim);
                    }
                    else if (still != null)
                    {
                        img.Source = still;
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("AnimatedWebp attach: {E}", ex.Message); }
            });
        });
    }

    /// <summary>
    /// Stop a frame animation started by <see cref="AttachAnimation"/> and release the
    /// Forever clock's pin on the Image. Safe (and cheap) on Images that never attached.
    /// UI thread only.
    /// </summary>
    public static void Detach(Image img)
    {
        try
        {
            _live.Remove(img);
            img.BeginAnimation(Image.SourceProperty, null);
        }
        catch { }
    }

    private static (List<BitmapSource> Frames, TimeSpan Delay)? DecodeCached(string path, int maxDim, int maxFrames)
    {
        string key = path + "|" + maxDim;
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                _cache[key] = (hit.Frames, hit.Delay, DateTime.UtcNow);
                return (hit.Frames, hit.Delay);
            }
        }

        // Attach sites are budget-capped upstream (MAX_ANIMATED / TEASE_MAX_ANIMATED), so a
        // per-clip 24MB frame budget keeps worst-case cache residency under ~100MB.
        var decoded = DecodeFrames(path, maxDim, maxFrames, maxMemoryMb: 24.0);
        if (decoded is not { } d) return null;

        lock (_cacheLock)
        {
            while (_cache.Count >= CACHE_MAX_ENTRIES)
            {
                string? oldest = null; var oldestTime = DateTime.MaxValue;
                foreach (var kvp in _cache)
                    if (kvp.Value.Touched < oldestTime) { oldestTime = kvp.Value.Touched; oldest = kvp.Key; }
                if (oldest == null) break;
                _cache.Remove(oldest);
            }
            _cache[key] = (d.Frames, d.FrameDelay, DateTime.UtcNow);
        }
        return (d.Frames, d.FrameDelay);
    }

    private static BitmapSource ToFrozenBitmapSource(SKBitmap src, int tw, int th)
    {
        SKBitmap? resized = null;
        try
        {
            var bmp = src;
            if (tw != src.Width || th != src.Height)
            {
                resized = src.Resize(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.Medium);
                if (resized != null) bmp = resized;
            }
            // BitmapSource.Create COPIES the pixels, so the composition canvas can be reused.
            var bs = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Pbgra32, null,
                bmp.GetPixels(), bmp.ByteCount, bmp.RowBytes);
            bs.Freeze();
            return bs;
        }
        finally { resized?.Dispose(); }
    }

    private static (int w, int h) ScaledSize(int srcW, int srcH, int maxDim)
    {
        int longest = Math.Max(srcW, srcH);
        if (longest <= maxDim) return (srcW, srcH);
        double scale = maxDim / (double)longest;
        return (Math.Max(1, (int)Math.Round(srcW * scale)), Math.Max(1, (int)Math.Round(srcH * scale)));
    }
}
