using System.Windows.Media.Imaging;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the ambient "Bubble Pop" field AND the chaos "Down the Rabbit Hole"
/// bubbles. Like <see cref="FlashLayer"/> this is a pure DRAW LIST: BubbleService's existing
/// 31Hz animation tick keeps driving every bubble's physics, fuse, danger ramp and variant
/// animation through the (unshown) WPF <c>_grid</c> tree, then copies the COMPUTED visual
/// state into a <see cref="BubbleItem"/> once per frame (Bubble.SyncLayerItem). So motion,
/// lifetime, hydra, hit-testing and every variant's animation are identical by construction -
/// the shared-Canvas host (<see cref="ChaosBubbleHostOverlay"/>) rendered the same WPF tree;
/// this renders its state to Skia instead, killing the last per-effect layered surface.
///
/// Input is renderer-agnostic: the hook-pop path (BubbleService.OnSharedHostLeftDown /
/// ChaosClickDiscsSnapshot) keys off <c>_posX/_size/_dpiScale</c>, not the host, so layer
/// bubbles are popped by the SAME global hook with NO new hit-testing here (Bubble.UsesHost
/// reports true for layer bubbles so they enter the disc snapshot).
///
/// World-space layer (<see cref="WorldSpacePx"/>): item geometry is virtual-desktop DEVICE px
/// (center + dpi-scaled sizes), exactly the physical-px currency the hit discs already use, so
/// it stays correct on mixed-DPI multi-monitor setups without the Canvas host's single-scale
/// LayoutTransform compensation.
///
/// Threading: every member is UI-thread only (BubbleService spawn/animate/destroy and the
/// engine tick all run on the dispatcher), so items need no locking. Base sprites are shared,
/// immutable, never-freed SKImages (decode-once cache); per-bubble tease frames are owned by
/// the item and disposed on <see cref="Remove"/>.
/// </summary>
public sealed class BubbleLayer : BaseLayer
{
    // Decode-once cache for the shared base sprites (bubble.png + the variant sprites from
    // ChaosArt). Keyed by the frozen WPF BitmapSource (ChaosArt hands out one shared frozen
    // source per file), so a whole field of one variant shares a single SKImage. Never disposed
    // - immutable, app-lifetime handles, safe to draw from any tick (same never-freed invariant
    // as the Avalonia BubbleLayer's _bubbleImage; a mutable/disposed shared SKImage would be a
    // use-after-free heap-corruption class).
    private static readonly Dictionary<BitmapSource, SKImage> _spriteCache = new();

    public BubbleLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.Bubbles;
    public override bool WorldSpacePx => true;

    /// <summary>Convert a (frozen) sprite source to a cached, never-freed SKImage. Null for the
    /// DrawingImage fallback (no bitmap) -> the layer draws a soft circle instead.</summary>
    public static SKImage? ResolveSprite(BitmapSource? source)
    {
        if (source == null) return null;
        if (_spriteCache.TryGetValue(source, out var img)) return img;
        try { img = SkiaWpfInterop.ToSKImage(source); }
        catch { return null; }
        _spriteCache[source] = img;
        return img;
    }

    private readonly List<BubbleItem> _items = new();

    // Reused paints - no per-frame native churn. UI-thread single-writer (Render only).
    private readonly SKPaint _img = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _glow = new() { IsAntialias = true };
    private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
    // Primary label face. NOT necessarily the face we draw with: bug #615 - Skia does no font
    // fallback, and the variant labels are pointedly non-Latin (the clover/heart emoji, the
    // sparkle/cross dingbats), so anything Segoe UI lacks is routed through GlyphFallback per
    // item. The verb-hint pills are hard-coded English (ChaosBubbleHints), so those take the
    // helper's ASCII fast path and keep drawing in Segoe UI exactly as before.
    private static readonly SKTypeface _bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName(null, SKFontStyle.Bold)
        ?? SKTypeface.Default;

    // Shield dash effect cache, keyed on quantized DPI scale (the only input) - a handful of
    // entries max (one per monitor scale). Disposed on Clear; rebuilt lazily.
    private readonly Dictionary<float, SKPathEffect> _dashCache = new();

    private SKPathEffect GetDash(float scale)
    {
        float q = MathF.Round(scale * 4f) / 4f;
        if (!_dashCache.TryGetValue(q, out var fx))
        {
            fx = SKPathEffect.CreateDash(new[] { 3f * q, 2f * q }, 0);
            _dashCache[q] = fx;
        }
        return fx;
    }

    /// <summary>Rebuild a cached blur mask filter only when the quantized sigma moves - the
    /// FlashLayer.BlurCache pattern; churning native blur filters per bubble per frame is the
    /// expensive part, and every sigma here derives from spawn constants + DPI.</summary>
    private static SKMaskFilter GetCachedBlur(ref SKMaskFilter? cache, ref float cachedSigma, float sigma)
    {
        float q = MathF.Round(sigma * 2f) / 2f;
        if (cache == null || Math.Abs(q - cachedSigma) > 0.01f)
        {
            cache?.Dispose();
            cache = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, q));
            cachedSigma = q;
        }
        return cache;
    }

    public BubbleItem Add(BubbleItem item)
    {
        _items.Add(item);
        SetActive(true);
        return item;
    }

    public void Remove(BubbleItem item)
    {
        item.ReleaseTeaseFrames();
        item.ReleaseEffectCaches();
        _items.Remove(item);
        if (_items.Count == 0) SetActive(false);
    }

    public void Clear()
    {
        foreach (var it in _items)
        {
            it.ReleaseTeaseFrames();
            it.ReleaseEffectCaches();
        }
        _items.Clear();
        foreach (var fx in _dashCache.Values) { try { fx.Dispose(); } catch { } }
        _dashCache.Clear();
        SetActive(false);
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.Opacity <= 0.003f) continue;

            float s = item.DpiScale;
            float cx = (float)item.CenterXPx, cy = (float)item.CenterYPx;
            // Generous cull radius: body scaled up on pop + glow/telegraph reach.
            float reach = (Math.Max(item.SizeDip, item.HitSizeDip) * Math.Max(1f, item.Scale) * 0.75f
                           + item.GlowBlurDip + 20f) * s;
            if (cx + reach < boundsPx.Left || cx - reach > boundsPx.Right
                || cy + reach < boundsPx.Top || cy - reach > boundsPx.Bottom) continue;

            byte ga = (byte)Math.Clamp(item.Opacity * 255f, 0, 255);   // group alpha
            float half = item.SizeDip * 0.5f * s;                      // body radius (unscaled) in px

            // ---- Freeze aura (behind everything): icy-blue ring-glow radial ----
            if (item.FreezeAuraOpacity > 0.003f)
            {
                float ar = (item.SizeDip + 6f) * 0.5f * s;
                using var sh = SKShader.CreateRadialGradient(
                    new SKPoint(cx, cy), ar,
                    new[] { new SKColor(150, 210, 255, 0), new SKColor(150, 210, 255, 190), new SKColor(150, 210, 255, 0) },
                    new[] { 0.30f, 0.66f, 1f }, SKShaderTileMode.Clamp);
                _fill.Shader = sh;
                _fill.Color = SKColors.White.WithAlpha((byte)(item.FreezeAuraOpacity * item.Opacity * 255f));
                canvas.DrawCircle(cx, cy, ar, _fill);
                _fill.Shader = null;
            }

            // ---- Glow halo (DropShadow depth-0 equivalent): spotlight/golden/darter/brittle ----
            if (item.HasGlow)
            {
                float sigma = Math.Max(0.5f, item.GlowBlurDip * s / 3f);   // WPF blurRadius/3 convention
                _glow.MaskFilter = GetCachedBlur(ref item.GlowBlurCache, ref item.GlowBlurCacheSigma, sigma);
                _glow.Color = item.GlowColor.WithAlpha((byte)(item.GlowOpacity * item.Opacity * 255f));
                canvas.DrawCircle(cx, cy, half, _glow);
                _glow.MaskFilter = null;
            }

            // ---- Prism/brittle mimic ghost (revealed on pop; sits just under the sprite) ----
            if (item.PrismGhost != null && item.PrismOpacity > 0.003f)
            {
                float gr = item.SizeDip * 0.9f * 0.5f * s;
                float gy = cy + 14f * s;   // WPF Margin(0,14,0,0): peeks from under the burst
                var dst = new SKRect(cx - gr, gy - gr, cx + gr, gy + gr);
                _img.Color = SKColors.White.WithAlpha((byte)(item.PrismOpacity * item.Opacity * 255f));
                canvas.DrawImage(item.PrismGhost, Fit(item.PrismGhost, dst), _img);
            }

            // ---- Base sprite (scaled + rotated about center) ----
            int saved = canvas.Save();
            if (item.Scale != 1f || item.Angle != 0f)
            {
                canvas.Translate(cx, cy);
                if (item.Angle != 0f) canvas.RotateDegrees(item.Angle);
                if (item.Scale != 1f) canvas.Scale(item.Scale, item.Scale);
                canvas.Translate(-cx, -cy);
            }
            var body = new SKRect(cx - half, cy - half, cx + half, cy + half);
            if (item.Sprite != null)
            {
                _img.Color = SKColors.White.WithAlpha(ga);
                canvas.DrawImage(item.Sprite, Fit(item.Sprite, body), _img);
            }
            else
            {
                // No bitmap (DrawingImage fallback): soft tinted circle so the field is playable.
                var fb = item.HasTint ? item.Tint : new SKColor(0xE8, 0xE8, 0xF0);
                _fill.Color = fb.WithAlpha(ga);
                canvas.DrawCircle(cx, cy, half, _fill);
                _stroke.Color = SKColors.White.WithAlpha((byte)(ga * 0.8f));
                _stroke.StrokeWidth = 2f * s;
                canvas.DrawCircle(cx, cy, half, _stroke);
            }
            canvas.RestoreToCount(saved);

            // ---- Glassy specular shine (chaos plain bubbles) ----
            if (item.HasShine)
            {
                float sr = item.SizeDip * 0.32f * s;
                var sc = new SKPoint(cx + (0.34f - 0.5f) * item.SizeDip * s, cy + (0.27f - 0.5f) * item.SizeDip * s);
                using var sh = SKShader.CreateRadialGradient(sc, sr,
                    new[] { new SKColor(255, 255, 255, 190), new SKColor(255, 255, 255, 70), new SKColor(255, 255, 255, 0) },
                    new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
                _fill.Shader = sh;
                _fill.Color = SKColors.White.WithAlpha(ga);
                canvas.DrawCircle(sc.X, sc.Y, sr, _fill);
                _fill.Shader = null;
            }

            // ---- Tint overlay (radial; chaos non-variant bubbles) ----
            if (item.HasTint)
            {
                var tc = new SKPoint(cx + (0.35f - 0.5f) * item.SizeDip * s, cy + (0.30f - 0.5f) * item.SizeDip * s);
                using var sh = SKShader.CreateRadialGradient(tc, half,
                    new[] { item.Tint.WithAlpha(150), item.Tint.WithAlpha(90) },
                    new[] { 0f, 1f }, SKShaderTileMode.Clamp);
                _fill.Shader = sh;
                _fill.Color = SKColors.White.WithAlpha((byte)(0.55f * item.Opacity * 255f));
                canvas.DrawCircle(cx, cy, half, _fill);
                _fill.Shader = null;
            }

            // ---- Tease face (dark disc + clipped current frame + diagonal shine) ----
            if (item.IsTease)
            {
                float ir = item.TeaseInnerDip * 0.5f * s;
                int tsave = canvas.Save();
                canvas.ClipRoundRect(new SKRoundRect(new SKRect(cx - ir, cy - ir, cx + ir, cy + ir), ir, ir), antialias: true);
                _fill.Color = new SKColor(0x14, 0x07, 0x0C, ga);
                canvas.DrawCircle(cx, cy, ir, _fill);
                var frame = item.CurrentTeaseFrame();
                if (frame != null)
                {
                    _img.Color = SKColors.White.WithAlpha((byte)(0.92f * item.Opacity * 255f));
                    canvas.DrawImage(frame, FillFit(frame, new SKRect(cx - ir, cy - ir, cx + ir, cy + ir)), _img);
                }
                if (item.TeaseShineOpacity > 0.003f)
                {
                    // Diagonal white->transparent linear shine (35 deg).
                    float rad = 35f * (float)Math.PI / 180f;
                    var dir = new SKPoint((float)Math.Cos(rad), (float)Math.Sin(rad));
                    using var sh = SKShader.CreateLinearGradient(
                        new SKPoint(cx - ir * dir.X, cy - ir * dir.Y),
                        new SKPoint(cx + ir * dir.X, cy + ir * dir.Y),
                        new[] { new SKColor(255, 255, 255, 150), new SKColor(255, 255, 255, 0) },
                        SKShaderTileMode.Clamp);
                    _fill.Shader = sh;
                    _fill.Color = SKColors.White.WithAlpha((byte)(item.TeaseShineOpacity * item.Opacity * 255f));
                    canvas.DrawCircle(cx, cy, ir, _fill);
                    _fill.Shader = null;
                }
                canvas.RestoreToCount(tsave);
            }

            // ---- Label / emoji glyph ----
            if (!string.IsNullOrEmpty(item.Label))
            {
                var runs = item.LabelRuns(_bold);   // #615/#717
                _text.TextSize = item.LabelFontDip * s;
                // Soft shadow (BlurRadius 6 -> sigma 2) then the white glyph.
                _text.Color = SKColors.Black.WithAlpha((byte)(0.8f * item.Opacity * 255f));
                _text.MaskFilter = GetCachedBlur(ref item.LabelBlurCache, ref item.LabelBlurCacheSigma, 2f * s);
                float baseline = cy + _text.TextSize * 0.35f;
                GlyphFallback.DrawCentered(canvas, runs, cx, baseline, _text);
                _text.MaskFilter = null;
                _text.Color = SKColors.White.WithAlpha(ga);
                GlyphFallback.DrawCentered(canvas, runs, cx, baseline, _text);
            }

            // ---- Fuse ring (live): 3-phase colour, shrinking radius ----
            if (item.FuseOpacity > 0.003f)
            {
                _stroke.Color = item.FuseColor.WithAlpha((byte)(item.FuseOpacity * item.Opacity * 255f));
                _stroke.StrokeWidth = 5f * s;
                canvas.DrawCircle(cx, cy, half * item.FuseScale, _stroke);
            }

            // ---- Echo ghost ring (offset outline) ----
            if (item.IsEcho)
            {
                _stroke.Color = item.EchoColor.WithAlpha((byte)(item.EchoColor.Alpha * item.Opacity));
                _stroke.StrokeWidth = 3f * s;
                canvas.DrawCircle(cx + 5f * s, cy + 4f * s, half, _stroke);
            }

            // ---- Chaperone shield (dashed icy ring) ----
            if (item.ShieldOpacity > 0.003f)
            {
                float shr = (item.SizeDip + 12f) * 0.5f * s;
                _stroke.Color = new SKColor(0x9C, 0xE8, 0xFF, (byte)(item.ShieldOpacity * item.Opacity * 255f));
                _stroke.StrokeWidth = 3.5f * s;
                _stroke.PathEffect = GetDash(s);
                canvas.DrawCircle(cx, cy, shr, _stroke);
                _stroke.PathEffect = null;
            }

            // ---- Brittle cracks (jagged glass lines) ----
            if (item.Cracks != null && item.BrittleOpacity > 0.003f)
            {
                _stroke.Color = new SKColor(0xEC, 0xF7, 0xFF, (byte)(0xD8 / 255f * item.BrittleOpacity * item.Opacity * 255f));
                _stroke.StrokeWidth = 1.6f * s;
                float bx = cx - half, by = cy - half;   // crack pts are DIP in the 0.._size box
                foreach (var line in item.Cracks)
                {
                    using var path = new SKPath();
                    path.MoveTo(bx + line[0].X * s, by + line[0].Y * s);
                    for (int k = 1; k < line.Length; k++) path.LineTo(bx + line[k].X * s, by + line[k].Y * s);
                    canvas.DrawPath(path, _stroke);
                }
            }

            // ---- Darter telegraph ring (flares down to lock-on) ----
            if (item.TelegraphOpacity > 0.003f)
            {
                _stroke.Color = item.Tint.WithAlpha((byte)(item.TelegraphOpacity * item.Opacity * 255f));
                _stroke.StrokeWidth = 4f * s;
                canvas.DrawCircle(cx, cy, half * item.TelegraphScale, _stroke);
            }

            // ---- First-contact verb-hint pill (below the bubble) ----
            if (!string.IsNullOrEmpty(item.HintText) && item.HintOpacity > 0.003f)
                DrawHint(canvas, item, cx, cy, s);
        }
    }

    private void DrawHint(SKCanvas canvas, BubbleItem item, float cx, float cy, float s)
    {
        float alpha = item.HintOpacity * item.Opacity;
        // #615/#717: measure AND draw with the same resolved runs, or the pill would be sized for
        // one font and filled with another. English hints hit the ASCII fast path (a single Segoe
        // UI run, measured and drawn exactly as before).
        var runs = item.HintRuns(_bold);
        _text.TextSize = 12.5f * s;
        var runWidths = runs.Length > 1 ? new float[runs.Length] : null;
        float tw = GlyphFallback.Measure(runs, _text, runWidths, out _, out _);
        float padX = 8f * s, padY = 3f * s;
        float th = _text.TextSize;
        float py = cy + item.HintYOffDip * s;
        var pill = new SKRect(cx - tw / 2f - padX, py - th / 2f - padY, cx + tw / 2f + padX, py + th / 2f + padY);
        _fill.Color = new SKColor(0x12, 0x0A, 0x18, (byte)(0xA0 / 255f * alpha * 255f));
        canvas.DrawRoundRect(new SKRoundRect(pill, 9f * s, 9f * s), _fill);
        _text.Color = SKColors.Black.WithAlpha((byte)(0.9f * alpha * 255f));
        _text.MaskFilter = GetCachedBlur(ref item.HintBlurCache, ref item.HintBlurCacheSigma, 1.7f * s);
        float baseline = py + th * 0.35f;
        GlyphFallback.DrawCentered(canvas, runs, cx, baseline, _text, runWidths, tw);
        _text.MaskFilter = null;
        _text.Color = new SKColor(0xFF, 0xE2, 0xF2, (byte)(alpha * 255f));
        GlyphFallback.DrawCentered(canvas, runs, cx, baseline, _text, runWidths, tw);
    }

    /// <summary>Uniform (letterbox) fit of an image into a square dest - matches Stretch.Uniform.</summary>
    private static SKRect Fit(SKImage img, SKRect dest)
    {
        if (img.Width <= 0 || img.Height <= 0) return dest;
        float r = Math.Min(dest.Width / img.Width, dest.Height / img.Height);
        float w = img.Width * r, h = img.Height * r;
        float x = dest.MidX - w / 2f, y = dest.MidY - h / 2f;
        return new SKRect(x, y, x + w, y + h);
    }

    /// <summary>UniformToFill fit (cover) - the clip handles overflow. Matches Stretch.UniformToFill.</summary>
    private static SKRect FillFit(SKImage img, SKRect dest)
    {
        if (img.Width <= 0 || img.Height <= 0) return dest;
        float r = Math.Max(dest.Width / img.Width, dest.Height / img.Height);
        float w = img.Width * r, h = img.Height * r;
        float x = dest.MidX - w / 2f, y = dest.MidY - h / 2f;
        return new SKRect(x, y, x + w, y + h);
    }

    /// <summary>
    /// One live bubble's render state. Static fields are set once at spawn (Bubble ctor);
    /// dynamic fields are rewritten every frame by Bubble.SyncLayerItem from the (unshown) WPF
    /// tree's computed values, so the layer never re-derives animation - it just draws.
    /// </summary>
    public sealed class BubbleItem
    {
        // ---- static (spawn) ----
        public float DpiScale = 1f;
        public float SizeDip, HitSizeDip;
        public SKImage? Sprite;             // shared, cached, never disposed here
        public bool HasTint;
        public SKColor Tint;
        public string? Label;
        public float LabelFontDip;
        public bool HasShine;
        public bool IsEcho;
        public SKColor EchoColor;
        public bool IsTease;
        public float TeaseInnerDip;
        public bool HasGlow;
        public SKColor GlowColor;
        public float GlowBlurDip;
        public float GlowOpacity;
        public SKImage? PrismGhost;         // shared, cached, never disposed here
        public SKPoint[][]? Cracks;         // DIP points in the 0.._size box
        public string? HintText;
        public float HintYOffDip;

        // ---- dynamic (per frame) ----
        public double CenterXPx, CenterYPx;
        public float Scale = 1f;
        public float Angle;
        public float Opacity = 1f;
        public float FuseScale = 1f, FuseOpacity;
        public SKColor FuseColor = new(255, 210, 40);
        public float TelegraphScale = 1f, TelegraphOpacity;
        public float FreezeAuraOpacity;
        public float BrittleOpacity;
        public float TeaseShineOpacity;
        public float ShieldOpacity;
        public float PrismOpacity;
        public float HintOpacity;

        // Cached blur mask filters (FlashLayer.BlurCache pattern): each sigma derives from
        // spawn constants + DPI, so these build once per item instead of per frame. Owned by
        // the item; disposed via ReleaseEffectCaches on Remove/Clear (safe even in off-thread
        // mode - an in-flight SKPicture holds its own native ref on anything drawn into it).
        internal SKMaskFilter? GlowBlurCache;
        internal float GlowBlurCacheSigma = -1f;
        internal SKMaskFilter? LabelBlurCache;
        internal float LabelBlurCacheSigma = -1f;
        internal SKMaskFilter? HintBlurCache;
        internal float HintBlurCacheSigma = -1f;

        // Bugs #615 / #717: resolved draw runs for Label / HintText - one run per face, since no
        // single installed face need cover a whole string (a CJK phrase with a dingbat in it does
        // not). Both strings are fixed at spawn, so this resolves once per bubble and then costs a
        // null check per frame - GlyphFallback itself is memoised globally too, but it takes a
        // lock, and this runs per bubble per frame on the 31Hz tick. Never disposed here: the
        // faces are either the shared _bold or globally cached fallbacks, both app-lifetime (same
        // never-freed invariant as the sprites).
        private GlyphFallback.TextRun[]? _labelRuns;
        private GlyphFallback.TextRun[]? _hintRuns;

        internal GlyphFallback.TextRun[] LabelRuns(SKTypeface primary)
            => _labelRuns ??= GlyphFallback.Split(Label, primary, SKFontStyle.Bold);

        internal GlyphFallback.TextRun[] HintRuns(SKTypeface primary)
            => _hintRuns ??= GlyphFallback.Split(HintText, primary, SKFontStyle.Bold);

        internal void ReleaseEffectCaches()
        {
            GlowBlurCache?.Dispose(); GlowBlurCache = null; GlowBlurCacheSigma = -1f;
            LabelBlurCache?.Dispose(); LabelBlurCache = null; LabelBlurCacheSigma = -1f;
            HintBlurCache?.Dispose(); HintBlurCache = null; HintBlurCacheSigma = -1f;
        }

        // Tease frame: the WPF face Image's current Source (animated webp/gif frame or a still),
        // converted to an owned SKImage lazily and cached per source so repeated frames reuse.
        public BitmapSource? TeaseSource;
        private BitmapSource? _teaseKey;
        private SKImage? _teaseImg;
        private Dictionary<BitmapSource, SKImage>? _teaseFrames;

        public SKImage? CurrentTeaseFrame()
        {
            var src = TeaseSource;
            if (src == null) return _teaseImg;
            if (ReferenceEquals(src, _teaseKey)) return _teaseImg;
            _teaseKey = src;
            _teaseFrames ??= new Dictionary<BitmapSource, SKImage>();
            if (!_teaseFrames.TryGetValue(src, out var img))
            {
                try { img = SkiaWpfInterop.ToSKImage(src); }
                catch { img = null; }
                if (img != null)
                {
                    if (_teaseFrames.Count > 64)   // bound the per-bubble frame cache
                    {
                        foreach (var v in _teaseFrames.Values) { try { v.Dispose(); } catch { } }
                        _teaseFrames.Clear();
                    }
                    _teaseFrames[src] = img;
                }
            }
            _teaseImg = img;
            return img;
        }

        public void ReleaseTeaseFrames()
        {
            if (_teaseFrames != null)
            {
                foreach (var v in _teaseFrames.Values) { try { v.Dispose(); } catch { } }
                _teaseFrames.Clear();
            }
            _teaseImg = null;
            _teaseKey = null;
        }
    }
}
