using System;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Rabbit Caller "whistle held" telegraph: a soft pink-gold halo that rides the cursor from
/// the moment the toy is armed until the next click summons the rabbits there. First chaos
/// overlay migrated onto the compositor (WS2/WP3 template migration — the recipe lives in
/// docs/unified-compositor-engine-plan.md Phase F).
///
/// Behavior contract (WPF Chaos/ChaosCursorGlowOverlay.cs — the plain-window path; the WPF
/// consolidated ChaosSkiaFxOverlay bloom variant is a separate queue item):
/// - 76-DIP circle, radial gradient FFD700 alpha 0 @0.18 -> FF8FC8 alpha 150 @0.55 ->
///   FF4DC4 alpha 0 @1.0 (center-origin, spans the full circle);
/// - "slow breath" scale pulse 0.85..1.12, SineEase in-out, 620ms per leg with AutoReverse
///   = 1240ms full cycle. NOTE: the legacy Avalonia window passed 620ms as ScalePulse's FULL
///   cycle (ScalePulse period = up+down; see the ChaosEStimGlow 160->320 doubling), i.e. it
///   breathed 2x too fast vs WPF — this layer restores the WPF timing;
/// - armed => visible riding the cursor; disarmed => hidden; parked off-screen until the
///   first MoveTo (WPF parks at -2*SIZE).
///
/// The owning AvaloniaChaosService drives state (Arm/Disarm/MoveTo from the Rabbit Caller
/// aim loop); this layer only renders it (UCE rule 7). Geometry is PHYSICAL virtual-desktop
/// px per the IAvaloniaLayer coordinate contract; the 76-DIP diameter converts to physical
/// px per monitor via the screen-aware Render overload's Scaling (a halo straddling a
/// mixed-DPI seam draws each half at that monitor's scale — the WPF single-window overlay
/// had one DPI, an accepted seam-only difference). Z-order comes from CompositorLayers only
/// (UCE rule 9): no RaiseAboveVideo equivalent is needed — the chaos band sits above the
/// video layers by constant.
///
/// Capture affinity: capture-VISIBLE (main surface, ExcludeFromCapture stays false). No WPF
/// chaos window calls SetWindowDisplayAffinity (verified by grep 2026-07-04; only keyword
/// highlight, brain drain and subliminal touch affinity in the WPF head).
/// </summary>
public sealed class ChaosCursorGlowLayer : BaseLayer
{
    private const double DiameterDip = 76.0;        // WPF SIZE
    private const double PulseMin = 0.85;           // WPF DoubleAnimation from
    private const double PulseMax = 1.12;           // WPF DoubleAnimation to
    private const double PulseFullCycleMs = 1240.0; // WPF: 620ms per leg, AutoReverse

    private readonly object _sync = new();
    private bool _armed;
    // Parked far off-screen until the first MoveTo (WPF parks the window at -2*SIZE).
    private double _x = -10000;
    private double _y = -10000;
    private double _pulseClockMs;

    // Reused paint with a unit-radius gradient shader built ONCE (UCE rule: no per-frame
    // allocations). The pulse is applied with canvas transforms, so breathing costs no
    // shader rebuilds. Never disposed (layer lives app-long, same as BubbleLayer's paints).
    private readonly SKPaint _paint;

    public ChaosCursorGlowLayer()
    {
        _paint = new SKPaint { IsAntialias = true };
        _paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            1f,
            new[]
            {
                new SKColor(0xFF, 0xD7, 0x00, 0),
                new SKColor(0xFF, 0x8F, 0xC8, 150),
                new SKColor(0xFF, 0x4D, 0xC4, 0),
            },
            new[] { 0.18f, 0.55f, 1.0f },
            SKShaderTileMode.Clamp);
    }

    public override int ZIndex => CompositorLayers.ChaosCursorGlow;

    public override bool IsActive
    {
        get { lock (_sync) { return _armed; } }
    }

    // ConsumeDirty stays the base always-dirty: the breath animates every frame while armed,
    // and while disarmed IsActive is false so the engine never ticks or renders this layer.

    /// <summary>Show the halo (armed). WPF Arm(): halo appears at its last position until the
    /// aim loop's next MoveTo (~16ms later).</summary>
    public void Arm()
    {
        lock (_sync)
        {
            if (!_armed) _pulseClockMs = 0; // fresh breath phase per arm (legacy Avalonia parity)
            _armed = true;
        }
    }

    /// <summary>Hide the halo (the whistle was answered or cancelled).</summary>
    public void Disarm()
    {
        lock (_sync) { _armed = false; }
    }

    /// <summary>Center the halo on the given PHYSICAL virtual-desktop px (raw
    /// IPointerState/hook coordinates — the layer's native space; no DPI conversion).</summary>
    public void MoveTo(double pxX, double pxY)
    {
        lock (_sync)
        {
            _x = pxX;
            _y = pxY;
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            if (_armed) _pulseClockMs += deltaTime.TotalMilliseconds;
        }
    }

    /// <summary>Screen-aware render: converts the 76-DIP halo to physical px with the
    /// composited monitor's scaling.</summary>
    public void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        bool armed;
        double x, y, clockMs;
        lock (_sync)
        {
            armed = _armed;
            x = _x;
            y = _y;
            clockMs = _pulseClockMs;
        }
        if (!armed) return;

        // WPF DoubleAnimation with AutoReverse + SineEase(EaseInOut): triangle phase 0..1..0
        // over the full cycle, sine-eased — identical math to the shared ScalePulse helper,
        // just clocked by the engine tick instead of a private DispatcherTimer.
        var phase = (clockMs % PulseFullCycleMs) / (PulseFullCycleMs / 2.0); // 0..2
        var tri = phase <= 1 ? phase : 2 - phase;                            // 0..1..0
        var eased = (1 - Math.Cos(tri * Math.PI)) / 2.0;
        var pulse = PulseMin + (PulseMax - PulseMin) * eased;

        var scaling = screen != null && screen.Scaling > 0 ? screen.Scaling : 1.0;
        var radiusPx = (float)(DiameterDip / 2.0 * scaling * pulse);
        if (radiusPx <= 0) return;

        // Unit-radius shader mapped onto the pulsating halo via transforms (zero per-frame alloc).
        var save = canvas.Save();
        canvas.Translate((float)x, (float)y);
        canvas.Scale(radiusPx);
        canvas.DrawCircle(0, 0, 1f, _paint);
        canvas.RestoreToCount(save);
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);
}
