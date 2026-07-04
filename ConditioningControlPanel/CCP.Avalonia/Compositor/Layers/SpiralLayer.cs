using System;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders the spiral overlay. Animated GIFs play their real decoded frames at the file's
/// own frame delays (WPF parity: OverlayService plays cached GIF frames on a timer, with
/// no rotation transform); static images keep the port's gentle rotation so a still image
/// is not a frozen picture (WPF has no static-image spiral path at all).
///
/// Decode/caching contract (WPF parity: OverlayService._spiralFramesCache):
/// - Frames are decoded ONCE per path, off the UI thread, and cached until the path
///   changes (single-entry, path-keyed, like WPF). Stop/start never re-decodes.
/// - Frame count is capped at 120 with even stepping (WPF LoadSpiralGifFrames), plus a
///   decode-dimension/memory budget the WPF head lacks: WPF caches the stock 2400x1600x32
///   spiral at full resolution (~490MB); at the spiral's &lt;=5% on-screen alpha a 1024px
///   decode is visually identical and keeps the working set inside the benchmark bar.
///
/// Lifetime discipline (UCE rule 8): Render draws while holding _sync, and the retired
/// frame set is Release()d under the same lock, so the render thread can never draw a
/// disposed SKImage (snapshotting a reference does NOT extend the native handle's
/// lifetime — see BubbleLayer's field comment, 0xC0000005 / AvaloniaUI/Avalonia#13521).
/// </summary>
public sealed class SpiralLayer : BaseLayer, IDisposable
{
    private const int MaxFrames = 120;          // WPF: Math.Min(frameCount, 120)
    private const int DecodeMaxDim = 1024;      // deviation from WPF full-res, see class doc
    private const double MaxMemoryMb = 96.0;    // safety budget for pathological GIFs
    private const int DefaultFrameDelayMs = 50; // WPF spiral default
    private const int MaxFrameDelayMs = 500;    // WPF spiral clamps >500ms to default
    private const double RotationSpeed = 30.0;  // degrees per second (static images only)

    private readonly object _sync = new();
    // Reused paint. Only touched inside Render/Dispose under _sync; never allocated per frame.
    private readonly SKPaint _paint = new();

    private SkiaFrameSet? _frames;      // cached decode for _cachedPath; this layer owns one ref
    private string? _cachedPath;
    private string? _pendingPath;       // path currently decoding on a background thread
    private int _decodeGeneration;      // discards superseded decodes
    private bool _visible;
    private double _opacity;
    private int _frameIndex;
    private SKColor _tintColor = new SKColor(255, 255, 255); // white = identity; the service pushes the active mod's themed color
    private double _frameClock;         // seconds accumulated toward the current frame's delay
    private double _rotationAngle;      // degrees (static-image fallback only)
    private bool _disposed;

    public override int ZIndex => CompositorLayers.Spiral;

    public override bool IsActive
    {
        get { lock (_sync) { return _visible && _opacity > 0 && (_frames != null || _pendingPath != null); } }
    }

    /// <summary>
    /// True once a frame set has been decoded and cached (diagnostics / --verify-spiral
    /// harness; IsActive alone cannot distinguish "decoded" from "decode still pending").
    /// </summary>
    public bool HasDecodedSource
    {
        get { lock (_sync) { return _frames != null; } }
    }

    /// <summary>
    /// Monotonic animation progress sample for the --verify-spiral harness: the current GIF
    /// frame index for animated sources, or the rotation angle (whole degrees) for static
    /// images. Two samples ~700ms apart differing proves the 60Hz engine tick is advancing
    /// the animation.
    /// </summary>
    public int AnimationProgress
    {
        get { lock (_sync) { return _frames?.IsAnimated == true ? _frameIndex : (int)_rotationAngle; } }
    }

    /// <summary>
    /// Show the spiral from <paramref name="path"/> at the given opacity (0..1, applied 1:1
    /// as paint alpha — the service owns the WPF 0.1 "very subtle" reduction). A repeat call
    /// with the cached path only updates opacity: no I/O, no decode, animation keeps running.
    /// </summary>
    public void SetSource(string? path, double opacity)
    {
        var newOpacity = Math.Clamp(opacity, 0, 1);

        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            lock (_sync)
            {
                _visible = false;
                _opacity = newOpacity;
            }
            return;
        }

        lock (_sync)
        {
            if (_disposed) return;
            _visible = true;
            _opacity = newOpacity;
            if (string.Equals(path, _cachedPath, StringComparison.OrdinalIgnoreCase) && _frames != null)
                return; // cache hit — instant re-show (WPF _spiralFramesCache parity)
            BeginDecode(path);
        }
    }

    /// <summary>
    /// Pre-decode <paramref name="path"/> into the cache off the UI thread without showing
    /// anything (WPF WarmSpiralCache parity). Note the cache is single-entry: preloading a
    /// new path replaces the cached frames, which is correct because the caller always warms
    /// the path the next StartSpiral will use.
    /// </summary>
    public void Preload(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        lock (_sync)
        {
            if (_disposed) return;
            if (string.Equals(path, _cachedPath, StringComparison.OrdinalIgnoreCase) && _frames != null) return;
            BeginDecode(path);
        }
    }

    /// <summary>
    /// Set the spiral tint color (theme-driven: the service resolves the active mod's
    /// FilterColor and pushes it here; drone => green #00FF41, CCP Default => pink, etc.).
    /// White (the default) is identity — the image renders at its native colors. The tint
    /// is applied as a per-channel multiply during Render.
    /// </summary>
    public void SetColor(SKColor color)
    {
        lock (_sync) { _tintColor = color; }
    }

    /// <summary>Current spiral tint color (read-only; test/diagnostic only).</summary>
    public SKColor CurrentColor { get { lock (_sync) { return _tintColor; } } }

    /// <summary>Hide the spiral. The decoded frames stay cached for instant re-show.</summary>
    public void ClearSource()
    {
        lock (_sync)
        {
            _visible = false;
            _frameIndex = 0;
            _frameClock = 0;
            _rotationAngle = 0;
        }
    }

    /// <summary>Kick a background decode for <paramref name="path"/>. Caller must hold _sync.</summary>
    private void BeginDecode(string path)
    {
        if (string.Equals(path, _pendingPath, StringComparison.OrdinalIgnoreCase)) return; // already decoding
        _pendingPath = path;
        var generation = ++_decodeGeneration;

        _ = Task.Run(() =>
        {
            // Decode entirely off the UI thread (the stock spiral.gif is ~8.6MB; WPF's
            // UI-thread decode froze the screen for ~1s, hence its warm-cache design).
            var set = SkiaImageDecoder.Decode(path, MaxFrames, DecodeMaxDim, MaxMemoryMb,
                DefaultFrameDelayMs, MaxFrameDelayMs);

            lock (_sync)
            {
                if (generation != _decodeGeneration || _disposed)
                {
                    // Superseded (newer SetSource/Preload) or torn down. The set never
                    // entered layer state, so no render pass can reference it.
                    set?.Release();
                    return;
                }

                _pendingPath = null;
                var old = _frames;
                _frames = set;
                _cachedPath = set != null ? path : null;
                _frameIndex = 0;
                _frameClock = 0;
                _rotationAngle = 0;
                // Safe final release: Render draws under this same lock, so no draw is in flight.
                old?.Release();
            }
        });
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            if (!_visible || _opacity <= 0 || _frames == null) return;

            if (_frames.IsAnimated)
            {
                // Advance through the real GIF frames at their metadata delays (WPF parity).
                var delays = _frames.FrameDelaysSeconds;
                _frameClock += deltaTime.TotalSeconds;
                var guard = 0;
                while (guard++ < 1000)
                {
                    var delay = delays[_frameIndex % delays.Length];
                    if (delay <= 0.0005 || _frameClock < delay) break;
                    _frameClock -= delay;
                    _frameIndex = (_frameIndex + 1) % _frames.Frames.Length;
                }
            }
            else
            {
                _rotationAngle += RotationSpeed * deltaTime.TotalSeconds;
                if (_rotationAngle >= 360) _rotationAngle -= 360;
            }
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        // Draw while holding _sync: the frame set's final Release also runs under _sync,
        // so a disposed SKImage can never reach DrawImage (dispose-vs-render race).
        lock (_sync)
        {
            if (!_visible || _opacity <= 0 || _frames == null) return;

            var frames = _frames.Frames;
            var image = frames[_frameIndex % frames.Length];
            var imgW = (float)image.Width;
            var imgH = (float)image.Height;
            if (imgW <= 0 || imgH <= 0) return;

            var boundsW = (float)bounds.Width;
            var boundsH = (float)bounds.Height;
            _paint.Color = new SKColor(_tintColor.Red, _tintColor.Green, _tintColor.Blue, (byte)(_opacity * 255));

            if (_frames.IsAnimated)
            {
                // WPF parity: Stretch.UniformToFill — cover the monitor, centred, no rotation.
                var scale = Math.Max(boundsW / imgW, boundsH / imgH);
                var destW = imgW * scale;
                var destH = imgH * scale;
                var destX = (float)bounds.X + (boundsW - destW) / 2f;
                var destY = (float)bounds.Y + (boundsH - destH) / 2f;
                canvas.DrawImage(image, new SKRect(destX, destY, destX + destW, destY + destH), _paint);
            }
            else
            {
                // Static image (no WPF equivalent): rotate about the centre; size to the
                // screen diagonal so the corners stay covered at every rotation angle.
                var diagonal = MathF.Sqrt(boundsW * boundsW + boundsH * boundsH);
                var scale = diagonal / Math.Min(imgW, imgH);
                var destW = imgW * scale;
                var destH = imgH * scale;
                var centerX = (float)bounds.X + boundsW / 2f;
                var centerY = (float)bounds.Y + boundsH / 2f;
                var destX = centerX - destW / 2f;
                var destY = centerY - destH / 2f;

                canvas.Save();
                canvas.Translate(centerX, centerY);
                canvas.RotateDegrees((float)_rotationAngle);
                canvas.Translate(-centerX, -centerY);
                canvas.DrawImage(image, new SKRect(destX, destY, destX + destW, destY + destH), _paint);
                canvas.Restore();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _decodeGeneration++; // invalidate any in-flight decode
            _pendingPath = null;
            _visible = false;
            var old = _frames;
            _frames = null;
            _cachedPath = null;
            old?.Release(); // safe: Render holds _sync and bails on _frames == null
            _paint.Dispose();
        }
    }
}
