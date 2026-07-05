using System;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// ChaosFxLayer — the full-screen coloured-vignette "impact juice" (migrated from the
/// standalone <c>ChaosFxWindow</c> onto the compositor, WS2/WP3 UCE migration; recipe in
/// <c>docs/unified-compositor-engine-plan.md</c> Phase F).
///
/// Behaviour contract (WPF <c>Chaos/ChaosFxWindow.cs</c> <see cref="Pulse"/>, verbatim):
/// a coloured edge-vignette flashes on an impact (red = detonation/malus, green = defuse,
/// gold = combo milestone, blue = shield save), snapping up over 40 ms and fading out by
/// 300 ms. The vignette is an elliptical radial gradient centred on the screen —
/// transparent core, opaque colour at the edge (WPF RadiusX 0.9 / RadiusY 1.0; stops
/// alpha 0 @0.0, 0 @0.45, 255 @1.0) — so the centre stays clear and the screen edges tint.
///
/// SCOPE (honest): the WPF window also exposed <c>BeginEdgeHold</c>/<c>EndEdgeHold</c>,
/// <c>SetHeatTint</c>/<c>EndHeatTint</c> and <c>FreezeBurst</c> on two extra held surfaces,
/// but those had ZERO callers in the Avalonia head (the run-engine cues that drive them are
/// unwired), so the window only ever rendered <see cref="Pulse"/>. Only <see cref="Pulse"/>
/// is ported here; if the held/freeze cues are wired later, add sibling surfaces then.
///
/// The owning <c>AvaloniaChaosService</c> drives state (Pulse from the run's impact palette,
/// gated by <c>ColorFlashesEnabled</c>); this layer only renders it (UCE rule 7). Geometry is
/// PHYSICAL virtual-desktop px per the <see cref="IAvaloniaLayer"/> coordinate contract; the
/// vignette is centred on the composited monitor's <c>bounds</c>, so — unlike the WPF
/// single primary-screen window — each effect screen gets its own vignette (identical to WPF
/// when DualMonitor is off, since only the primary is then an effect screen; a documented
/// dual-monitor improvement, consistent with the sibling full-screen chaos layers). Z-order
/// comes from <see cref="CompositorLayers"/> only (UCE rule 9); capture-VISIBLE (main
/// surface, <see cref="BaseLayer.ExcludeFromCapture"/> stays false — no WPF chaos window sets
/// <c>SetWindowDisplayAffinity</c>).
/// </summary>
public sealed class ChaosFxLayer : BaseLayer
{
    // WPF Pulse gradient ellipse radii (fraction of the screen w/h) and stop offsets.
    private const float RadiusXRel = 0.9f;   // WPF RadialGradientBrush.RadiusX
    private const float RadiusYRel = 1.0f;   // WPF RadialGradientBrush.RadiusY
    private const double RiseMs = 40.0;      // WPF LinearDoubleKeyFrame @40ms → peak
    private const double TotalMs = 300.0;    // WPF LinearDoubleKeyFrame @300ms → 0

    private readonly object _sync = new();
    private readonly SKPaint _paint = new() { IsAntialias = false };

    private SKColor _tint = SKColors.White;      // last pulse colour (for shader reuse)
    private bool _hasShader;
    private double _peak;                        // clamped peak opacity for the active pulse
    private double _elapsedMs;                   // pulse clock; >= TotalMs => idle
    private bool _active;

    public override int ZIndex => CompositorLayers.ChaosFx;

    public override bool IsActive
    {
        get { lock (_sync) { return _active; } }
    }

    // Always-dirty while active (the pulse animates every frame); IsActive gates the engine
    // so an idle layer never ticks or renders (BaseLayer.ConsumeDirty default is fine).

    /// <summary>Flash a coloured edge-vignette. <paramref name="strength"/> 0..1 scales the
    /// peak opacity (WPF clamp 0.22 + strength*0.5 → 0.15..0.72). Restarts if one is in flight.</summary>
    public void Pulse(SKColor tint, double strength)
    {
        lock (_sync)
        {
            _peak = Math.Clamp(0.22 + strength * 0.5, 0.15, 0.72);
            _elapsedMs = 0;
            _active = true;
            if (!_hasShader || !_tint.Equals(tint))
            {
                _tint = tint;
                RebuildShaderLocked();
            }
        }
    }

    /// <summary>Reset the vignette (run teardown — WPF closed the FX window in EndRun/CleanupAfterRun).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _active = false;
            _elapsedMs = TotalMs;
        }
    }

    private void RebuildShaderLocked()
    {
        // Unit-radius radial gradient (transparent core → opaque tint edge); scaled to the
        // screen ellipse by the canvas transform in Render (zero per-frame allocation — the
        // shader is rebuilt only when the pulse colour changes, not every frame).
        var rgb0 = new SKColor(_tint.Red, _tint.Green, _tint.Blue, 0);
        var rgbFull = new SKColor(_tint.Red, _tint.Green, _tint.Blue, 255);
        _paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            1f,
            new[] { rgb0, rgb0, rgbFull },
            new[] { 0.0f, 0.45f, 1.0f },
            SKShaderTileMode.Clamp);
        _hasShader = true;
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            if (!_active) return;
            _elapsedMs += deltaTime.TotalMilliseconds;
            if (_elapsedMs >= TotalMs) _active = false;
        }
    }

    public void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        double opacity;
        lock (_sync)
        {
            if (!_active || !_hasShader) return;
            // WPF DoubleAnimationUsingKeyFrames: linear rise to peak over RiseMs, linear
            // fall to 0 over the remainder (reads as an impact).
            opacity = _elapsedMs <= RiseMs
                ? _peak * (_elapsedMs / RiseMs)
                : _peak * (1.0 - (_elapsedMs - RiseMs) / (TotalMs - RiseMs));
            if (opacity <= 0) return;
            _paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255.0, 0, 255));
        }

        var w = bounds.Width;
        var h = bounds.Height;
        if (w <= 0 || h <= 0) return;

        var cx = (float)(bounds.X + w / 2.0);
        var cy = (float)(bounds.Y + h / 2.0);
        var sx = (float)(RadiusXRel * w);   // ellipse horizontal radius (px)
        var sy = (float)(RadiusYRel * h);   // ellipse vertical radius (px)
        if (sx <= 0 || sy <= 0) return;

        // Draw the unit-shader over a rect that covers the whole screen in the transformed
        // space (screen half-extent normalised by the ellipse radii).
        var lx = (float)(w / 2.0) / sx;
        var ly = (float)(h / 2.0) / sy;

        var save = canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(sx, sy);
        canvas.DrawRect(new SKRect(-lx, -ly, lx, ly), _paint);
        canvas.RestoreToCount(save);
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);
}
