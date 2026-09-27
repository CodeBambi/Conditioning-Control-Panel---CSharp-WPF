using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
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
///
/// Flashes v2: an item may carry a <see cref="FlashMotionState"/>. The layer steps it in Update
/// (the one place with delta time) and keeps the item's X/Y/W/H at the media's current
/// axis-aligned bounds, so the click snapshot and the heartbeat's state-bag mirror read the live
/// rect. A moving item dirties the layer only when the maths actually moved it.
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
        internal System.Windows.Point? BubbleOriginPx;
        internal double BubbleDiameterPx;
        internal MotionLevel EntranceMotion;
        internal System.Windows.Rect? EntranceTarget;
        internal double EntranceAlpha = 1;
        internal double EntranceProgress = 1;

        /// <summary>Flashes v2 motion, null or Still for a classic held flash. Stepped by Update.</summary>
        public FlashMotionState? Motion;

        /// <summary>
        /// Flashes v2 wave 2: non-null once a hand has dismissed this flash and it is breaking
        /// apart. FlashService is already finished with the item by then (the window is out of the
        /// active list, XP and hydra have run), so the layer owns the rest of its life: Update
        /// steps the shards and drops the item the moment they are done.
        /// </summary>
        public FlashShatterState? Shatter;

        // Glow (lucky / sparkle-boost tiers). Sigma is the WPF DropShadow blur radius / 3
        // (same conversion as the brain-drain layer). LuckyPulse replicates the 400ms
        // auto-reverse radius x1.6 / opacity 0.7->1.0 forever-animation.
        internal bool HasGlow;
        internal SKColor GlowColor;
        internal float GlowSigmaPx;
        internal double GlowOpacity;
        /// <summary>Natasha's favourite: a short red blink across the picture now and then.</summary>
        internal bool NatashaCue;
        internal bool LuckyPulse;

        internal double ElapsedSec;          // pulse clock, advanced by Update
        internal SKMaskFilter? BlurCache;    // rebuilt only when the quantized sigma changes
        internal float BlurCacheSigma = -1f;

        // #853 dirty tracking: the values this item was last DRAWN from. A flash holding at full
        // opacity on a still image has none of them rewritten by the heartbeat, so the layer can
        // report clean and stop re-rastering the whole shared surface behind it.
        internal double LastOpacity = double.NaN;
        internal int LastFrameIndex = -1;
        internal double LastDwellScale = double.NaN;

        internal void SetClipFrame(System.Windows.Media.Imaging.BitmapSource source)
        {
            if (Shatter != null || Frames == null) return;
            var next = SkiaWpfInterop.ToSKImage(source);
            foreach (var old in Frames) old.Dispose();
            Frames = new[] { next };
            FrameIndex = 0;
            LastFrameIndex = -1;
        }

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
        SKColor glowColor, float glowSigmaPx, double glowOpacity, bool luckyPulse,
        FlashMotionState? motion = null)
    {
        var item = new FlashItem
        {
            Frames = frames,
            X = x, Y = y, W = w, H = h,
            Motion = motion,
            PaddingPx = paddingPx,
            CornerRadiusPx = cornerRadiusPx,
            HasGlow = glowSigmaPx > 0,
            GlowColor = glowColor,
            GlowSigmaPx = glowSigmaPx,
            GlowOpacity = glowOpacity,
            LuckyPulse = luckyPulse
        };
        // A pendulum re-homes the picture under its pivot at spawn: start from the motion's rect.
        if (motion != null && motion.Style != FlashMotionStyle.Still)
            SyncRect(item, motion);
        _items.Add(item);
        _dirty = true;
        SetActive(true);
        return item;
    }

    /// <summary>
    /// Flashes v2 wave 2: hand an item over to the shatter instead of removing it. The layer keeps
    /// the frames alive for the length of the break and disposes them when the last shard is gone,
    /// so FlashService can tear its window down at the dismiss exactly as it always did. A state
    /// with no shards (MotionLevel.Off) removes the item on the spot: that is the plain cut.
    /// </summary>
    public void BeginShatter(FlashItem item, FlashShatterState shatter)
    {
        if (item.Frames is not { Length: > 0 } || shatter.Done || shatter.Shards.Length == 0)
        {
            Remove(item);
            return;
        }
        item.Shatter = shatter;
        // The shards fall from where the picture is actually DRAWN, not from where it spawned and
        // not from the axis-aligned box the click reads. A pendulum's box is the bounds of the
        // ROTATED media, some 15-18% larger than the picture and never tilted, so cutting from it
        // would make the flash jump bigger and snap level the instant it broke; the pivot-space
        // rect plus the frozen angle is the same geometry the pendulum draw itself uses.
        if (item.Motion is { Style: FlashMotionStyle.Pendulum } pend)
            FlashShatter.TakeOverHangingRect(shatter, pend.PivotX, pend.PivotY, pend.Rope,
                pend.AngleRad, pend.MediaW, pend.MediaH);
        else
            FlashShatter.TakeOverDrawnRect(shatter, item.X, item.Y, item.W, item.H);
        _dirty = true;
        SetActive(true);
    }

    /// <summary>Remove an item and dispose its frames. Idempotent.</summary>
    public void Remove(FlashItem item)
    {
        item.ReleaseFrames();
        _items.Remove(item);
        _dirty = true;      // the survivors must be repainted without this one
        if (_items.Count == 0) SetActive(false);
    }

    public void Clear()
    {
        foreach (var item in _items) item.ReleaseFrames();
        _items.Clear();
        _dirty = true;
        SetActive(false);
    }

    // #853: honest dirt. FlashService's heartbeat only WRITES these fields while a flash is fading
    // or stepping GIF frames - a still image holding at full opacity writes nothing, yet the layer
    // used to force a full re-raster of the shared surface every frame it was up.
    // UI thread only, like every other member (see the class Threading note).
    private bool _dirty = true;

    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    public override void Update(TimeSpan delta)
    {
        // Backwards: a finished shatter drops its item right here, on the existing flash tick, so
        // the break needs no timer of its own.
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.ElapsedSec += delta.TotalSeconds;
            if (item.Shatter == null && item.BubbleOriginPx is { } origin)
            {
                item.EntranceTarget ??= new System.Windows.Rect(item.X, item.Y, item.W, item.H);
                var sample = FlashDelivery.Sample(origin, item.BubbleDiameterPx,
                    item.EntranceTarget.Value, item.ElapsedSec, MotionFx.Level == MotionLevel.Off
                        ? MotionLevel.Off : item.EntranceMotion);
                item.X = (float)sample.Rect.X; item.Y = (float)sample.Rect.Y;
                item.W = (float)sample.Rect.Width; item.H = (float)sample.Rect.Height;
                item.EntranceAlpha = sample.Alpha;
                item.EntranceProgress = sample.Progress;
                _dirty = true;
                if (sample.Progress >= 1 && sample.Alpha >= 1) item.BubbleOriginPx = null;
            }

            if (item.Shatter is { } shatter)
            {
                if (FlashShatter.Step(shatter, delta.TotalSeconds)) _dirty = true;
                if (shatter.Done)
                {
                    item.Shatter = null;
                    item.ReleaseFrames();
                    _items.RemoveAt(i);
                    _dirty = true;
                    if (_items.Count == 0) SetActive(false);
                }
                // A breaking flash answers to the shards and to nothing else: no drift, no fade
                // ramp, no GIF advance (its FlashWindow is already gone and writes nothing).
                continue;
            }

            // Flashes v2: step the motion here (the one place with delta time). Step answers
            // false for a Still item and for a zero delta, so a held flash stays clean.
            if (item.Motion != null && FlashMotion.Step(item.Motion, delta.TotalSeconds))
            {
                SyncRect(item, item.Motion);
                _dirty = true;
            }

            // Compare against what was last drawn instead of having FlashService announce its
            // writes: a missed call site there would be a STUCK-CLEAN (visually frozen) flash,
            // whereas a state compare self-heals on the next tick. A lucky pulse animates its
            // glow off ElapsedSec every frame, so it is legitimately dirty throughout.
            if ((item.HasGlow && item.LuckyPulse)
                || item.Opacity != item.LastOpacity
                || item.FrameIndex != item.LastFrameIndex
                || item.DwellScale != item.LastDwellScale)
            {
                item.LastOpacity = item.Opacity;
                item.LastFrameIndex = item.FrameIndex;
                item.LastDwellScale = item.DwellScale;
                _dirty = true;
            }
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var frames = item.Frames;
            if (frames == null || frames.Length == 0 || item.Opacity <= 0) continue;

            var rect = new SKRect(item.X, item.Y, item.X + item.W, item.Y + item.H);

            // Flashes v2 wave 2: a dismissed flash draws as falling pieces of its LAST frame and
            // nothing else - no glow card, no dwell inflate, no pendulum rotation. The shards
            // leave the item's own rect, so this runs before the AABB cull and culls per shard.
            if (item.Shatter is { } shatter)
            {
                DrawShards(canvas, item, shatter, frames[Math.Clamp(item.FrameIndex, 0, frames.Length - 1)],
                    boundsPx);
                continue;
            }

            if (!rect.IntersectsWith(boundsPx)) continue;   // cull to this monitor (the AABB)

            var alpha = (byte)Math.Clamp(item.Opacity * item.EntranceAlpha * 255, 0, 255);
            var image = frames[Math.Clamp(item.FrameIndex, 0, frames.Length - 1)];

            int saves = canvas.Save();
            // Pendulum: rotate the canvas about the pivot and draw the media hanging straight
            // down the rope in that frame. item.X..H stays the axis-aligned bounds of the rotated
            // picture (for gaze/click); the draw rect is the unrotated media in pivot space.
            if (item.Motion is { Style: FlashMotionStyle.Pendulum } pend)
            {
                canvas.RotateDegrees((float)(pend.AngleRad * 180.0 / Math.PI), (float)pend.PivotX, (float)pend.PivotY);
                var cx = (float)pend.PivotX;
                var cy = (float)(pend.PivotY + pend.Rope);
                var hw = (float)(pend.MediaW / 2.0);
                var hh = (float)(pend.MediaH / 2.0);
                rect = new SKRect(cx - hw, cy - hh, cx + hw, cy + hh);
            }
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
            var padding = item.PaddingPx * (float)item.EntranceProgress;
            var inner = new SKRect(rect.Left + padding, rect.Top + padding,
                rect.Right - padding, rect.Bottom - padding);
            var fit = UniformFit(image.Width, image.Height, inner);
            if (item.EntranceProgress < 1)
            {
                // Start with the same circle and center crop used by the bubble face.
                var radius = (float)(Math.Min(inner.Width, inner.Height) * .5 * (1 - item.EntranceProgress)
                    + item.CornerRadiusPx * item.EntranceProgress);
                canvas.ClipRoundRect(new SKRoundRect(inner, radius), antialias: true);
                var scale = Math.Max(inner.Width / image.Width, inner.Height / image.Height);
                var fw = image.Width * scale; var fh = image.Height * scale;
                fit = new SKRect(inner.MidX - fw / 2, inner.MidY - fh / 2,
                    inner.MidX + fw / 2, inner.MidY + fh / 2);
                _imagePaint.Color = new SKColor(255, 255, 255, alpha);
                canvas.DrawImage(image, fit, _imagePaint);
                canvas.RestoreToCount(saves);
                continue;
            }

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
                    (byte)Math.Clamp(glowAlpha * item.Opacity * item.EntranceAlpha * 255, 0, 255));
                canvas.DrawRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), _fillPaint);
                _fillPaint.MaskFilter = null;

                // Rounded clip so the image corners match the glow card (legacy clip Border).
                canvas.ClipRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), antialias: true);
            }
            else
            {
                // Legacy non-glow content: black backing behind the (letterboxed) image.
                // Wave 2: with rounded corners on, the backing rounds too and the image is clipped
                // to the same round-rect, so no square black shoulder survives behind a corner.
                _fillPaint.Color = new SKColor(0, 0, 0, alpha);
                if (item.CornerRadiusPx > 0)
                {
                    canvas.DrawRoundRect(new SKRoundRect(inner, item.CornerRadiusPx), _fillPaint);
                    canvas.ClipRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), antialias: true);
                }
                else
                {
                    canvas.DrawRect(inner, _fillPaint);
                }
            }

            _imagePaint.Color = new SKColor(255, 255, 255, alpha);
            canvas.DrawImage(image, fit, _imagePaint);
            if (item.NatashaCue)
            {
                var wash = Chaster.NatashasFavourite.WashAlphaAt(item.ElapsedSec);
                if (wash > 0.003)
                {
                    _fillPaint.MaskFilter = null;
                    _fillPaint.Color = new SKColor(Chaster.NatashasFavourite.R, Chaster.NatashasFavourite.G, Chaster.NatashasFavourite.B,
                        (byte)Math.Clamp(wash * alpha, 0, 255));
                    canvas.DrawRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), _fillPaint);
                }
            }
            canvas.RestoreToCount(saves);
        }
    }

    /// <summary>
    /// Flashes v2 wave 2: draw one broken flash. Every shard is a sub-rect of the same frame the
    /// flash was showing, blitted at the shard's offset, turned about its own centre and faded
    /// with the rest of them.
    ///
    /// The geometry mirrors the classic draw above it so nothing jumps at the break: a pendulum
    /// rotates the canvas about its pivot by the angle FROZEN at the dismiss and cuts from the
    /// media rect in pivot space, a gaze-dwell pop keeps the inflate it was wearing, and the cut
    /// is taken over the LETTERBOXED image box (glow padding removed). A non-glow flash keeps its
    /// black backing, now per shard, so a GIF with transparency in it does not turn to ghosts
    /// halfway down the screen.
    ///
    /// What it deliberately does NOT carry over: the glow halo (a shattered flash is no longer a
    /// card, so there is no card to light) and the corner radius (a break exposes hard edges, and
    /// rounding every shard would read as a bag of lozenges).
    /// </summary>
    private void DrawShards(SKCanvas canvas, FlashItem item, FlashShatterState shatter, SKImage image,
        SKRectI boundsPx)
    {
        var rect = new SKRect((float)shatter.RectX, (float)shatter.RectY,
            (float)(shatter.RectX + shatter.RectW), (float)(shatter.RectY + shatter.RectH));

        int saves = canvas.Save();
        // Pendulum: the same rotate-about-the-pivot the classic path does, at the frozen angle.
        // The pieces then fall down the picture's own axis rather than the screen's, which is
        // what a thing coming apart mid-swing does.
        var tilted = shatter.FrozenAngleRad != 0;
        if (tilted)
            canvas.RotateDegrees((float)(shatter.FrozenAngleRad * 180.0 / Math.PI),
                (float)shatter.PivotX, (float)shatter.PivotY);

        // Gaze-dwell inflate about the rect center, as the classic path has it: a stare-to-pop
        // must not start the break by shrinking the picture 10%.
        if (item.DwellScale > 1.001)
        {
            var ds = (float)item.DwellScale;
            canvas.Translate(rect.MidX, rect.MidY);
            canvas.Scale(ds, ds);
            canvas.Translate(-rect.MidX, -rect.MidY);
        }

        var inner = new SKRect(rect.Left + item.PaddingPx, rect.Top + item.PaddingPx,
            rect.Right - item.PaddingPx, rect.Bottom - item.PaddingPx);
        var fit = UniformFit(image.Width, image.Height, inner);
        if (fit.Width <= 0 || fit.Height <= 0) { canvas.RestoreToCount(saves); return; }

        foreach (var shard in shatter.Shards)
        {
            var a = shard.Alpha * item.Opacity;
            if (a <= 0) continue;

            var dx = (float)shard.Dx;
            var dy = (float)shard.Dy;
            var dest = new SKRect(
                fit.Left + (float)(shard.U0 * fit.Width) + dx,
                fit.Top + (float)(shard.V0 * fit.Height) + dy,
                fit.Left + (float)(shard.U1 * fit.Width) + dx,
                fit.Top + (float)(shard.V1 * fit.Height) + dy);
            if (dest.Width <= 0 || dest.Height <= 0) continue;

            // Cull generously: the tumble can push a shard's corners a little past its own box.
            // A tilted rig is not culled at all - these rects are in pivot space, so testing them
            // against a screen rect would throw away pieces that are plainly on the monitor, and
            // a pendulum break is a handful of shards for 0.7 s.
            if (!tilted)
            {
                var slack = Math.Max(dest.Width, dest.Height);
                if (!SKRect.Create(dest.Left - slack, dest.Top - slack,
                        dest.Width + 2 * slack, dest.Height + 2 * slack).IntersectsWith(boundsPx))
                    continue;
            }

            var src = new SKRect(
                (float)(shard.U0 * image.Width), (float)(shard.V0 * image.Height),
                (float)(shard.U1 * image.Width), (float)(shard.V1 * image.Height));

            var shardAlpha = (byte)Math.Clamp(a * 255, 0, 255);
            int shardSaves = canvas.Save();
            canvas.RotateDegrees((float)(shard.AngleRad * 180.0 / Math.PI), dest.MidX, dest.MidY);
            if (!item.HasGlow)
            {
                // The legacy black backing, cut up and falling with the picture it was behind.
                _fillPaint.Color = new SKColor(0, 0, 0, shardAlpha);
                canvas.DrawRect(dest, _fillPaint);
            }
            _imagePaint.Color = new SKColor(255, 255, 255, shardAlpha);
            canvas.DrawImage(image, src, dest, _imagePaint);
            canvas.RestoreToCount(shardSaves);
        }
        canvas.RestoreToCount(saves);
    }

    /// <summary>Copy the motion's current axis-aligned bounds onto the item's bookkeeping rect.</summary>
    private static void SyncRect(FlashItem item, FlashMotionState m)
    {
        item.X = (float)m.X; item.Y = (float)m.Y; item.W = (float)m.W; item.H = (float)m.H;
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
