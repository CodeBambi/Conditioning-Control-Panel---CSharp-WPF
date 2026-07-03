using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders flash image popups. The service owns decoding (via its LRU frame cache) and
/// spawning; this layer handles fade, GIF frame advance at the file's real frame delays,
/// and rendering.
///
/// Coordinate space (see <see cref="IAvaloniaLayer"/> contract): item geometry is PHYSICAL
/// virtual-desktop pixels. <see cref="HitTest"/> therefore takes raw WH_MOUSE_LL hook
/// coordinates (physical px) directly — no DIP conversion — and stays correct on
/// mixed-DPI multi-monitor setups.
///
/// Lifetime discipline (UCE rule 8): Render draws while holding _sync, and every
/// <see cref="SkiaFrameSet.Release"/> for an item's frames also happens under _sync
/// (expiry, click, Clear), so the render thread can never draw a disposed SKImage.
/// Snapshotting the reference and drawing outside the lock does NOT extend the native
/// handle's lifetime — that was the 0xC0000005 use-after-free class BubbleLayer documents
/// (AvaloniaUI/Avalonia#13521).
/// </summary>
public sealed class FlashLayer : BaseLayer
{
    private readonly List<FlashItem> _items = new();
    private readonly object _sync = new();
    // Reused paint: only touched inside Render under _sync. Never disposed (layer lives app-long).
    private readonly SKPaint _paint = new();
    private const double FADE_PER_SEC = 3.0;

    public override int ZIndex => CompositorLayers.Flash;

    public override bool IsActive
    {
        get
        {
            lock (_sync) { return _items.Count > 0; }
        }
    }

    /// <summary>
    /// Spawn a flash from a pre-decoded frame set. Takes ownership of one reference on
    /// <paramref name="frames"/>; it is released (under _sync) when the item is removed
    /// by expiry, click, or <see cref="Clear"/>. Returns the item ID for tracking.
    /// </summary>
    public Guid Spawn(SkiaFrameSet frames, double x, double y, double width, double height,
        double maxOpacity, int lifetimeMs, bool clickable)
    {
        var item = new FlashItem(Guid.NewGuid(), frames, x, y, width, height, maxOpacity, lifetimeMs, clickable);
        lock (_sync)
        {
            _items.Add(item);
        }
        return item.Id;
    }

    public void RemoveItem(Guid id)
    {
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].Id == id)
                {
                    _items[i].ReleaseFrames(); // under _sync: no render pass can be mid-draw
                    _items.RemoveAt(i);
                }
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            foreach (var item in _items) item.ReleaseFrames();
            _items.Clear();
        }
    }

    public void RemoveItem(FlashItem item) => RemoveItem(item.Id);

    public override void Update(TimeSpan deltaTime)
    {
        double dt = deltaTime.TotalSeconds;
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                item.Update(dt, FADE_PER_SEC);
                if (item.IsDone)
                {
                    item.ReleaseFrames(); // under _sync: safe deterministic disposal (no finalizer backlog)
                    _items.RemoveAt(i);
                }
            }
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        // Draw while holding _sync (see class doc). Contention is negligible: the UI thread
        // only takes this lock for list bookkeeping, and drawing records into the Skia
        // command stream rather than rasterizing inline.
        lock (_sync)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item.Opacity <= 0) continue;
                var image = item.CurrentFrame;
                if (image == null) continue;

                _paint.Color = new SKColor(255, 255, 255, (byte)(item.Opacity * 255));
                var dest = new SKRect((float)item.X, (float)item.Y,
                    (float)(item.X + item.Width), (float)(item.Y + item.Height));
                canvas.DrawImage(image, dest, _paint);
            }
        }
    }

    /// <summary>
    /// Hit-test in reverse order (topmost first). Returns the hit item or null.
    /// Coordinates are PHYSICAL virtual-desktop pixels (raw hook / gaze space).
    /// With <paramref name="requireClickable"/> true (mouse click-pop path) only items
    /// spawned clickable match; pass false for the gaze-pop path — WPF deliberately
    /// decouples gaze targeting from FlashClickable (FlashService.GetGazeTargets returns
    /// ALL live flashes; solid-mode host flashes are gaze-pop-only by design).
    /// </summary>
    public FlashItem? HitTest(double x, double y, bool requireClickable = true)
    {
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (requireClickable && !item.Clickable) continue;
                if (x >= item.X && x <= item.X + item.Width && y >= item.Y && y <= item.Y + item.Height)
                    return item;
            }
        }
        return null;
    }

    /// <summary>
    /// True when the given rect (physical px) intersects any live flash, clickable or not.
    /// Used by the spawn placement loop to avoid stacking flashes; one call replaces the old
    /// per-click-entry repeated center-point hit-tests.
    /// </summary>
    public bool IntersectsAny(double x, double y, double width, double height)
    {
        lock (_sync)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (x < item.X + item.Width && x + width > item.X
                    && y < item.Y + item.Height && y + height > item.Y)
                    return true;
            }
        }
        return false;
    }

    public sealed class FlashItem
    {
        public Guid Id { get; }
        private SkiaFrameSet? _frames;
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public double MaxOpacity { get; }
        public bool Clickable { get; }
        public double Opacity { get; private set; }
        public bool IsDone { get; private set; }
        public DateTime ExpiresAt { get; }
        public int OriginalLifetimeMs { get; }

        private bool _isFadingOut;
        private int _currentFrame;
        private double _frameTimer;

        /// <summary>Current frame, or null once the frames have been released.</summary>
        public SKImage? CurrentFrame
        {
            get
            {
                var frames = _frames;
                if (frames == null || frames.Frames.Length == 0) return null;
                return frames.Frames[_currentFrame % frames.Frames.Length];
            }
        }

        public FlashItem(Guid id, SkiaFrameSet frames, double x, double y, double width, double height,
            double maxOpacity, int lifetimeMs, bool clickable)
        {
            Id = id;
            _frames = frames;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            MaxOpacity = maxOpacity;
            Clickable = clickable;
            Opacity = 0;
            ExpiresAt = DateTime.Now.AddMilliseconds(lifetimeMs);
            OriginalLifetimeMs = lifetimeMs;
        }

        /// <summary>
        /// Drop this item's reference on its frame set. Must be called under the layer's
        /// _sync (all call sites are), so a final release can never race an in-flight draw.
        /// </summary>
        internal void ReleaseFrames()
        {
            var frames = _frames;
            _frames = null;
            frames?.Release();
        }

        public void Update(double dt, double fadePerSec)
        {
            var show = DateTime.Now < ExpiresAt && !_isFadingOut;
            var target = show ? MaxOpacity : 0.0;
            var step = fadePerSec * dt;

            if (Opacity < target)
                Opacity = Math.Min(target, Opacity + step);
            else if (Opacity > target)
            {
                Opacity = Math.Max(0.0, Opacity - step);
                if (Opacity <= 0) IsDone = true;
            }

            // Advance GIF frames at the file's real per-frame delays (from SKCodec.FrameInfo).
            var frames = _frames;
            if (frames != null && frames.IsAnimated)
            {
                var delays = frames.FrameDelaysSeconds;
                _frameTimer += dt;
                var guard = 0;
                while (guard++ < 1000)
                {
                    var delay = delays[_currentFrame % delays.Length];
                    if (delay <= 0.0005 || _frameTimer < delay) break;
                    _frameTimer -= delay;
                    _currentFrame = (_currentFrame + 1) % frames.Frames.Length;
                }
            }
        }

        public void BeginFadeOut() => _isFadingOut = true;
    }
}
