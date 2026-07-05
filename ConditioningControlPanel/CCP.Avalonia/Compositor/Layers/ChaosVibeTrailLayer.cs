using System;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Vibe-pop cursor trail on the compositor: a warm buzzing glow that follows the cursor plus a
/// short fading sparkle trail, shown while the <c>vibe_popping</c> toy's buzz runs. Ports the
/// Avalonia <c>ChaosVibeTrailOverlay</c> window (itself the WPF ChaosVibeTrailOverlay port) onto a
/// UCE layer so the effect renders through the single Skia compositor instead of a per-effect
/// window (replaces the unwired window; the vibe-pop toy lifecycle is already live in the head).
///
/// Behavior contract (<c>ChaosVibeTrailOverlay.axaml.cs</c>):
/// - warm glow GLOW_SIZE=58 DIP (radial <c>#FFE9A0@0.12(0a) -> #FFB03A@0.5(160a) -> #FF69B4@1.0(0a)</c>),
///   buzz-pulse scale 0.9&lt;-&gt;1.12 over 360ms (SineEase, Forever) centred on the cursor;
/// - trail: up to 14 recycled dots DOT_SIZE=20 DIP (radial <c>#FFD76A(190a) -> #FFB03A@0.55(110a) ->
///   #FF8A3A@1.0(0a)</c>), a new dot emitted every EMIT_DIST=9 of cursor travel, each fading opacity
///   0.5-&gt;0 and shrinking scale 1.0-&gt;0.35 over FADE_MS=340ms.
///
/// Coordinate contract: cursor positions are pushed as PHYSICAL virtual-desktop px (the head polls
/// <c>IPointerState.GetCursorPosition</c> on a 16ms timer, the same source the Rabbit-Caller aim
/// loop feeds <c>ChaosCursorGlowLayer</c>). DIP sizes scale by the composited monitor's Scaling at
/// render (at 100% scale exact; HiDPI a seam-only difference, the ChaosCursorGlowLayer precedent);
/// the 9px emit gate treats the DIP constant as px (monitor-agnostic, computed in Push). Normal
/// alpha blend (SrcOver) \u2014 the window used alpha-blended Ellipses, not additive.
///
/// The owning <c>AvaloniaChaosService</c> drives <see cref="Start"/>/<see cref="Stop"/> (the vibe
/// toy lifecycle) and <see cref="Push"/> (cursor feed). Capture-VISIBLE (chaos FX; no
/// <c>WDA_EXCLUDEFROMCAPTURE</c>).
/// </summary>
public sealed class ChaosVibeTrailLayer : BaseLayer
{
    private const float GlowSizeDip = 58f;
    private const float DotSizeDip = 20f;
    private const int TrailDots = 14;
    private const float EmitDistPx = 9f;    // 9 DIP treated as px (monitor-agnostic emit gate)
    private const float FadeMs = 340f;
    private const float BuzzPeriodMs = 360f;

    private struct Dot { public float X, Y, AgeMs; public bool Live; }

    private readonly object _sync = new();
    private readonly Dot[] _dots = new Dot[TrailDots];
    private int _dotIndex;
    private bool _started;
    private float _cx, _cy;
    private bool _haveCursor;
    private float _lastEmitX, _lastEmitY;
    private bool _haveEmit;
    private float _buzzMs;

    private static SKImage? _dotSprite;
    private static SKImage? _glowSprite;
    // Built once (UCE rule: no per-frame alloc); never disposed (layer lives app-long).
    private readonly SKPaint _paint = new() { IsAntialias = true, BlendMode = SKBlendMode.SrcOver };

    public override int ZIndex => CompositorLayers.ChaosVibeTrail;

    public override bool IsActive
    {
        get
        {
            lock (_sync)
            {
                if (_started) return true;
                foreach (var d in _dots) if (d.Live) return true;
                return false;
            }
        }
    }

    /// <summary>Begin the buzz (vibe_popping toy activated) \u2014 the glow shows and the trail emits.</summary>
    public void Start()
    {
        lock (_sync) { _started = true; _haveEmit = false; _buzzMs = 0f; }
    }

    /// <summary>End the buzz \u2014 hide the glow and clear the trail (WPF EndFollow hides all dots).</summary>
    public void Stop()
    {
        lock (_sync)
        {
            _started = false;
            _haveCursor = false;
            for (int i = 0; i < _dots.Length; i++) _dots[i].Live = false;
        }
    }

    /// <summary>Feed a PHYSICAL virtual-desktop px cursor position (head 16ms poll while buzzing).</summary>
    public void Push(double pxX, double pxY)
    {
        lock (_sync)
        {
            if (!_started) return;
            _cx = (float)pxX; _cy = (float)pxY; _haveCursor = true;
            if (!_haveEmit)
            {
                _lastEmitX = _cx; _lastEmitY = _cy; _haveEmit = true;
                EmitDot(_cx, _cy);
                return;
            }
            float dx = _cx - _lastEmitX, dy = _cy - _lastEmitY;
            if (dx * dx + dy * dy >= EmitDistPx * EmitDistPx)
            {
                _lastEmitX = _cx; _lastEmitY = _cy;
                EmitDot(_cx, _cy);
            }
        }
    }

    private void EmitDot(float x, float y)
    {
        _dots[_dotIndex] = new Dot { X = x, Y = y, AgeMs = 0f, Live = true };
        _dotIndex = (_dotIndex + 1) % TrailDots;
    }

    public override void Update(TimeSpan deltaTime)
    {
        float dtMs = (float)deltaTime.TotalMilliseconds;
        if (dtMs <= 0) return;
        if (dtMs > 100f) dtMs = 100f;
        lock (_sync)
        {
            if (_started) _buzzMs += dtMs;
            for (int i = 0; i < _dots.Length; i++)
            {
                if (!_dots[i].Live) continue;
                _dots[i].AgeMs += dtMs;
                if (_dots[i].AgeMs >= FadeMs) _dots[i].Live = false;
            }
        }
    }

    public void Render(SKCanvas canvas, PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        float scale = (float)(screen != null && screen.Scaling > 0 ? screen.Scaling : 1.0);
        var dot = DotSprite();
        var glow = GlowSprite();
        lock (_sync)
        {
            // Trail dots first (behind the glow), oldest-to-newest doesn't matter (all additive-free).
            for (int i = 0; i < _dots.Length; i++)
            {
                if (!_dots[i].Live) continue;
                float t = _dots[i].AgeMs / FadeMs;
                float op = 0.5f * (1f - t);             // opacity 0.5 -> 0
                float ds = 1.0f - 0.65f * t;            // scale 1.0 -> 0.35
                float r = (DotSizeDip * 0.5f) * ds * scale;
                DrawSprite(canvas, dot, _dots[i].X, _dots[i].Y, r, op);
            }
            // Warm buzzing glow at the cursor (only while active).
            if (_started && _haveCursor)
            {
                float breath = 1.01f + 0.11f * MathF.Sin(2f * MathF.PI * (_buzzMs / BuzzPeriodMs)); // 0.9..1.12
                float r = (GlowSizeDip * 0.5f) * breath * scale;
                DrawSprite(canvas, glow, _cx, _cy, r, 1f);
            }
        }
    }

    public override void Render(SKCanvas canvas, PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);

    private void DrawSprite(SKCanvas canvas, SKImage img, float cx, float cy, float radius, float opacity)
    {
        if (radius <= 0.2f || opacity <= 0.002f) return;
        // Modulate scales the pre-coloured sprite's alpha by `opacity` (RGB x1, A x opacity).
        _paint.ColorFilter = opacity >= 0.999f
            ? null
            : SKColorFilter.CreateBlendMode(SKColors.White.WithAlpha((byte)(255 * opacity)), SKBlendMode.Modulate);
        var dest = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        canvas.DrawImage(img, dest, _paint);
    }

    private static SKImage DotSprite()
    {
        return _dotSprite ??= BuildRadial(
            new[]
            {
                new SKColor(0xFF, 0xD7, 0x6A, 190),
                new SKColor(0xFF, 0xB0, 0x3A, 110),
                new SKColor(0xFF, 0x8A, 0x3A, 0),
            },
            new[] { 0f, 0.55f, 1f });
    }

    private static SKImage GlowSprite()
    {
        return _glowSprite ??= BuildRadial(
            new[]
            {
                new SKColor(0xFF, 0xE9, 0xA0, 0),
                new SKColor(0xFF, 0xB0, 0x3A, 160),
                new SKColor(0xFF, 0x69, 0xB4, 0),
            },
            new[] { 0.12f, 0.5f, 1f });
    }

    private static SKImage BuildRadial(SKColor[] colors, float[] stops)
    {
        const int s = 128;
        var info = new SKImageInfo(s, s, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surf = SKSurface.Create(info);
        var c = surf.Canvas;
        c.Clear(SKColors.Transparent);
        float r = s / 2f;
        using var shader = SKShader.CreateRadialGradient(new SKPoint(r, r), r, colors, stops, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader, IsAntialias = true };
        c.DrawCircle(r, r, r, p);
        return surf.Snapshot();
    }
}
