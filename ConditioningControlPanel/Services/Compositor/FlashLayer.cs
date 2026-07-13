using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the flash image popups. Unlike the Avalonia port's FlashLayer (which
/// owns fade + GIF advance), this layer is a pure DRAW LIST: FlashService's existing heartbeat
/// keeps driving every item's opacity, frame index and gaze-dwell scale through the FlashWindow
/// state bag - exactly the split solid mode already established - so fade speed, lifetime,
/// hydra, gaze and XP behavior are identical by construction. Flag the split when coordinating
/// with the Avalonia head.
///
/// Threading: every member is UI-thread only (spawn/remove happen on the dispatcher, and the
/// engine ticks Update/Render there too), so items need no locking. Items OWN their SKImage
/// frames; <see cref="Remove"/>/<see cref="Clear"/> dispose them deterministically - never
/// dispose an item's frames from outside the layer.
///
/// Renders on the MAIN surface (flashes stay visible in recordings, like every non-braindrain
/// effect). Mouse clicks can't reach the click-through host: FlashService runs a global-hook
/// hit-test over these items (like the shared-host bubbles) for clickable flashes.
/// </summary>
public sealed class FlashLayer : BaseLayer
{
    /// <summary>One live flash. FlashService's heartbeat writes the mutable fields each tick.</summary>
    public sealed class FlashItem
    {
        /// <summary>Decoded frames, owned by the layer. Null after removal.</summary>
        internal SKImage[]? Frames;

        // Bookkeeping rect in world (virtual-desktop) px - INCLUDES the glow padding,
        // mirroring host mode's expanded rect (gaze + overlap read the same values in DIP
        // from the FlashWindow state bag).
        public float X, Y, W, H;
        /// <summary>Glow inset: the image draws PaddingPx inside the rect (legacy Border.Padding).</summary>
        public float PaddingPx;
        public float CornerRadiusPx;

        /// <summary>Fade alpha 0..1 - written by FlashService's heartbeat (VisualOpacity).</summary>
        public double Opacity;
        /// <summary>Current GIF frame - written by FlashService's heartbeat.</summary>
        public int FrameIndex;
        /// <summary>Gaze-dwell inflate about center, 1.0..1.1 (SetGazeDwellProgress parity).</summary>
        public double DwellScale = 1.0;

        // Glow (lucky / sparkle-boost tiers). Sigma is the WPF DropShadow blur radius / 3
        // (same conversion as the brain-drain layer). LuckyPulse replicates the 400ms
        // auto-reverse radius x1.6 / opacity 0.7->1.0 forever-animation.
        internal bool HasGlow;
        internal SKColor GlowColor;
        internal float GlowSigmaPx;
        internal double GlowOpacity;
        internal bool LuckyPulse;

        internal double ElapsedSec;          // pulse clock, advanced by Update
        internal SKMaskFilter? BlurCache;    // rebuilt only when the quantized sigma changes
        internal float BlurCacheSigma = -1f;

        internal void ReleaseFrames()
        {
            var frames = Frames;
            Frames = null;
            if (frames == null) return;
            foreach (var f in frames)
            {
                try { f.Dispose(); } catch { }
            }
            BlurCache?.Dispose();
            BlurCache = null;
        }
    }

    private readonly List<FlashItem> _items = new();
    // Reused paints (no per-frame allocations).
    private readonly SKPaint _imagePaint = new() { FilterQuality = SKFilterQuality.Low };
    private readonly SKPaint _fillPaint = new();

    public FlashLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.Flash;

    public override bool WorldSpacePx => true;

    /// <summary>
    /// Add a flash. The layer takes ownership of <paramref name="frames"/>. Geometry is world
    /// px; the image draws <paramref name="paddingPx"/> inside the rect (glow inset, 0 without
    /// glow). <paramref name="glowSigmaPx"/> is the DropShadow radius/3 in px; 0 = no glow.
    /// </summary>
    public FlashItem Spawn(SKImage[] frames, float x, float y, float w, float h,
        float paddingPx, float cornerRadiusPx,
        SKColor glowColor, float glowSigmaPx, double glowOpacity, bool luckyPulse)
    {
        var item = new FlashItem
        {
            Frames = frames,
            X = x, Y = y, W = w, H = h,
            PaddingPx = paddingPx,
            CornerRadiusPx = cornerRadiusPx,
            HasGlow = glowSigmaPx > 0,
            GlowColor = glowColor,
            GlowSigmaPx = glowSigmaPx,
            GlowOpacity = glowOpacity,
            LuckyPulse = luckyPulse
        };
        _items.Add(item);
        SetActive(true);
        return item;
    }

    /// <summary>Remove an item and dispose its frames. Idempotent.</summary>
    public void Remove(FlashItem item)
    {
        item.ReleaseFrames();
        _items.Remove(item);
        if (_items.Count == 0) SetActive(false);
    }

    public void Clear()
    {
        foreach (var item in _items) item.ReleaseFrames();
        _items.Clear();
        SetActive(false);
    }

    public override void Update(TimeSpan delta)
    {
        for (int i = 0; i < _items.Count; i++)
            _items[i].ElapsedSec += delta.TotalSeconds;
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var frames = item.Frames;
            if (frames == null || frames.Length == 0 || item.Opacity <= 0) continue;

            var rect = new SKRect(item.X, item.Y, item.X + item.W, item.Y + item.H);
            if (!rect.IntersectsWith(boundsPx)) continue;   // cull to this monitor

            var alpha = (byte)Math.Clamp(item.Opacity * 255, 0, 255);
            var image = frames[Math.Clamp(item.FrameIndex, 0, frames.Length - 1)];

            int saves = canvas.Save();
            // Gaze-dwell inflate about the rect center (RenderTransform ScaleTransform parity).
            if (item.DwellScale > 1.001)
            {
                var s = (float)item.DwellScale;
                canvas.Translate(rect.MidX, rect.MidY);
                canvas.Scale(s, s);
                canvas.Translate(-rect.MidX, -rect.MidY);
            }

            // The image sits PaddingPx inside the bookkeeping rect (glow inset), letterboxed
            // uniform like Stretch.Uniform - geometry preserves aspect, so fit is a no-op in
            // practice, but the 50px minimum clamp can distort slightly on tiny images.
            var inner = new SKRect(rect.Left + item.PaddingPx, rect.Top + item.PaddingPx,
                rect.Right - item.PaddingPx, rect.Bottom - item.PaddingPx);
            var fit = UniformFit(image.Width, image.Height, inner);

            if (item.HasGlow)
            {
                // Halo: blurred round-rect behind the image (DropShadow depth-0 equivalent).
                var sigma = item.GlowSigmaPx;
                var glowAlpha = item.GlowOpacity;
                if (item.LuckyPulse)
                {
                    // 400ms auto-reverse: radius base->x1.6, opacity 0.7->1.0 (legacy anim).
                    var tri = Math.Abs(item.ElapsedSec % 0.8 / 0.4 - 1.0);   // 1..0..1 triangle
                    tri = 1.0 - tri;                                          // 0..1..0
                    sigma *= (float)(1.0 + 0.6 * tri);
                    glowAlpha = 0.7 + 0.3 * tri;
                }
                // Rebuild the mask filter only when the quantized sigma moves (blur filters
                // are expensive to churn per frame; lucky pulses quantize to 0.5px steps).
                var q = MathF.Round(sigma * 2f) / 2f;
                if (item.BlurCache == null || Math.Abs(q - item.BlurCacheSigma) > 0.01f)
                {
                    item.BlurCache?.Dispose();
                    item.BlurCache = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, q));
                    item.BlurCacheSigma = q;
                }
                _fillPaint.MaskFilter = item.BlurCache;
                _fillPaint.Color = item.GlowColor.WithAlpha(
                    (byte)Math.Clamp(glowAlpha * item.Opacity * 255, 0, 255));
                canvas.DrawRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), _fillPaint);
                _fillPaint.MaskFilter = null;

                // Rounded clip so the image corners match the glow card (legacy clip Border).
                canvas.ClipRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), antialias: true);
            }
            else
            {
                // Legacy non-glow content: black backing behind the (letterboxed) image.
                _fillPaint.Color = new SKColor(0, 0, 0, alpha);
                canvas.DrawRect(inner, _fillPaint);
            }

            _imagePaint.Color = new SKColor(255, 255, 255, alpha);
            canvas.DrawImage(image, fit, _imagePaint);
            canvas.RestoreToCount(saves);
        }
    }

    private static SKRect UniformFit(int srcW, int srcH, SKRect dest)
    {
        if (srcW <= 0 || srcH <= 0 || dest.Width <= 0 || dest.Height <= 0) return dest;
        var ratio = Math.Min(dest.Width / srcW, dest.Height / srcH);
        float w = srcW * ratio, h = srcH * ratio;
        var x = dest.Left + (dest.Width - w) / 2f;
        var y = dest.Top + (dest.Height - h) / 2f;
        return new SKRect(x, y, x + w, y + h);
    }
}
