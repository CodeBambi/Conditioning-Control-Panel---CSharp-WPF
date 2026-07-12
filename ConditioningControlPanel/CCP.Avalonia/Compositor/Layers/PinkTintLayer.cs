using System;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders a full-screen themed tint overlay. The service owns all state: it resolves the
/// active mod's <c>FilterColor</c> (green under Dronification, pink under CCP Default, etc.)
/// and pushes both color and opacity here. This layer only renders what the service tells
/// it to. The class name is historical — the tint is not literally pink.
/// </summary>
public sealed class PinkTintLayer : BaseLayer
{
    // Reused paint (UCE rule: no per-frame allocations). Only touched inside Render.
    private readonly SKPaint _paint = new();
    private Color _color = Colors.Transparent;
    private double _opacity = 0;
    private bool _dirty;

    public override int ZIndex => CompositorLayers.PinkTint;

    /// <summary>
    /// AMBIENT click-through (per-region rule 2026-07-09): the theme color filter is tinted
    /// glass the user works THROUGH, so its painted region is excluded from the capture mask
    /// and clicks pass to the app behind. Rendered semi-transparent (opacity &lt; 1) by the
    /// service so the desktop stays visible beneath it.
    /// </summary>
    public override bool IsAmbientClickThrough => true;

    public override bool IsActive => _opacity > 0;

    /// <summary>Current tint color (read-only; test/diagnostic only — the service owns all state).</summary>
    public Color CurrentColor => _color;

    public void SetColor(Color color, double opacity)
    {
        var clamped = Math.Clamp(opacity, 0, 1);
        if (_color != color || Math.Abs(clamped - _opacity) > 0.0005)
            _dirty = true;
        _color = color;
        _opacity = clamped;
    }

    public override void Update(TimeSpan deltaTime) { }

    // Static fill: repaint only when the service actually changed color/opacity, so a
    // pink-only frame lets the engine skip the full-screen re-render entirely.
    public override bool ConsumeDirty()
    {
        var dirty = _dirty;
        _dirty = false;
        return dirty;
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        if (_opacity <= 0) return;
        _paint.Color = new SKColor(_color.R, _color.G, _color.B, (byte)(_opacity * 255));
        canvas.DrawRect(ToSkRect(bounds), _paint);
    }
}
