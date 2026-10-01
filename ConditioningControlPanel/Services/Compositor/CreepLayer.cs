using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Super Creep: pink fog drifting in from the screen edges. A new effect beside the pink filter
/// (both may run). All the maths is in <see cref="CreepFog"/>; this layer only steps it and draws
/// it. Draws in world-space px so a click from the global mouse hook lands where the blobs are.
/// Sits under video and flashes (<see cref="CompositorLayers.Creep"/>) and is click-through like
/// every compositor layer.
/// </summary>
public sealed class CreepLayer : BaseLayer
{
    private const int SpriteSize = 128;
    private const int MotesPerField = 48;

    private readonly CreepState _state = new();
    private readonly Dictionary<(int, int, int, int), FieldDraw> _fields = new();
    private readonly List<CreepField> _fieldList = new();
    private readonly SKPaint _blobPaint = new() { FilterQuality = SKFilterQuality.Low };
    private readonly SKPaint _fogPaint = new()
    {
        FilterQuality = SKFilterQuality.Low,
        Color = new SKColor(255, 255, 255, (byte)Math.Round(CreepFog.OpacityCeiling * 255)),
    };
    private readonly SKPaint _motePaint = new() { IsAntialias = true };
    private readonly SKPaint _ringPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
    private readonly Random _rng = new();
    private SKImage? _sprite;
    private bool _dirty = true;
    private double _sinceFrame;
    private double _lastDrawnCoverage = -1;
    private double _sinceAccessCheck;

    private sealed class FieldDraw
    {
        public readonly CreepField Field;
        public readonly Mote[] Motes = new Mote[MotesPerField];
        /// <summary>The fog at 1/<see cref="CreepFog.Downscale"/> resolution, reused every frame.</summary>
        public SKSurface? Fog;
        public int FogW, FogH;
        public FieldDraw(CreepField f) { Field = f; }
    }

    private struct Mote { public bool Live; public double X, Y, Vx, Vy, Life; }

    public CreepLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.Creep;
    public override bool WorldSpacePx => true;

    /// <summary>Same monitor target as the pink filter it sits beside.</summary>
    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => App.ShouldRenderTargetOnScreen(
               App.Settings?.Current?.PinkFilterTargetMonitor ?? App.MonitorTargetFollowGlobal,
               screenBoundsPx);

    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    /// <summary>Start from a fresh, thin fog. UI thread.</summary>
    public void Show()
    {
        CreepFog.Reset(_state, _fieldList);
        ClearMotes();
        _dirty = true;
        SetActive(true);
    }

    /// <summary>Gone at once, and the next show starts thin again (panic never leaves a fog behind).</summary>
    public void Hide()
    {
        SetActive(false);
        CreepFog.Reset(_state, _fieldList);
        ClearMotes();
        _dirty = true;
    }

    /// <summary>A click anywhere, in physical virtual-desktop px. UI thread.</summary>
    public void Click(double x, double y)
    {
        if (!IsActive) return;
        CreepFog.Click(_state, _fieldList, x, y, MotionFx.Level);
        _dirty = true;
    }

    public override void Update(TimeSpan delta)
    {
        var s = App.Settings?.Current;
        var speed = s?.SuperCreepSpeed ?? CreepSpeed.Normal;
        var cap = s?.SuperCreepCap ?? CreepFog.MaxCap;
        var level = MotionFx.Level;
        var dt = delta.TotalSeconds;
        CreepFog.Step(_state, dt, speed, cap, level);
        foreach (var fd in _fields.Values)
        {
            CreepFog.StepField(_state, fd.Field, dt, level);
            StepMotes(fd, dt, level);
        }

        // Moving fog repaints at the ambient Skia rate for the tier; a still fog (Motion Off, or
        // the Performance tier) repaints only when its coverage visibly changes.
        var fps = PerformanceProfile.FxTargetFps(PerformanceProfile.CurrentTier);
        var moving = level != MotionLevel.Off && fps > 0;
        _sinceFrame += dt;
        if (moving && _sinceFrame >= 1.0 / fps) { _sinceFrame = 0; _dirty = true; }
        if (_state.Rings.Count > 0 && _sinceFrame >= 1.0 / 30) { _sinceFrame = 0; _dirty = true; }
        if (Math.Abs(_state.Coverage - _lastDrawnCoverage) > 0.003) _dirty = true;

        // Belt and braces for a lapsed tier or a switch flipped without a Changed event: once a
        // second, ask the seam again and let the controller take the fog down if it says no.
        _sinceAccessCheck += dt;
        if (_sinceAccessCheck >= 1)
        {
            _sinceAccessCheck = 0;
            if (!SuperAccess.IsOn(SuperEffect.Creep))
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(CreepController.Sync));
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        var key = (boundsPx.Left, boundsPx.Top, boundsPx.Width, boundsPx.Height);
        if (!_fields.TryGetValue(key, out var fd))
        {
            var f = new CreepField(boundsPx.Left, boundsPx.Top, boundsPx.Width, boundsPx.Height);
            fd = new FieldDraw(f);
            _fields[key] = fd;
            _fieldList.Add(f);
            CreepFog.StepField(_state, f, 0, MotionFx.Level);
        }

        var sprite = _sprite ??= BuildSprite();
        var a = (byte)Math.Clamp(CreepFog.LayerAlpha(_state.Coverage) * 255, 0, 255);
        _blobPaint.Color = new SKColor(255, 255, 255, a);

        // Blobs stack inside a small reused surface (alpha there tops out at 1), which is then
        // stretched onto the monitor at the opacity ceiling: no pixel of fog can pass it, and the
        // 46 big soft blobs cost 1/16 of the fill.
        var sw = CreepFog.SurfaceSize(boundsPx.Width);
        var sh = CreepFog.SurfaceSize(boundsPx.Height);
        if (fd.Fog == null || fd.FogW != sw || fd.FogH != sh)
        {
            fd.Fog?.Dispose();
            fd.Fog = SKSurface.Create(new SKImageInfo(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul));
            fd.FogW = sw; fd.FogH = sh;
        }
        var dest = new SKRect(boundsPx.Left, boundsPx.Top, boundsPx.Right, boundsPx.Bottom);
        canvas.Save();
        canvas.ClipRect(dest);
        if (fd.Fog != null)
        {
            var fc = fd.Fog.Canvas;
            fc.Clear(SKColors.Transparent);
            fc.Save();
            fc.Scale(sw / (float)Math.Max(1, boundsPx.Width), sh / (float)Math.Max(1, boundsPx.Height));
            fc.Translate(-boundsPx.Left, -boundsPx.Top);
            foreach (var b in fd.Field.Blobs)
            {
                var r = (float)(b.DrawSize / 2);
                if (r <= 0) continue;
                fc.DrawImage(sprite, new SKRect((float)b.Px - r, (float)b.Py - r, (float)b.Px + r, (float)b.Py + r), _blobPaint);
            }
            fc.Restore();
            // Snapshot shares the pixels; disposing it straight after the draw keeps the next
            // frame's Clear from copying them (the off-thread recorder holds its own ref).
            using var img = fd.Fog.Snapshot();
            canvas.DrawImage(img, dest, _fogPaint);
        }

        foreach (var m in fd.Motes)
        {
            if (!m.Live) continue;
            var al = 0.5 * Math.Sin(Math.Min(1, m.Life / 3) * Math.PI);
            _motePaint.Color = new SKColor(255, 200, 235, (byte)Math.Clamp(al * 255, 0, 255));
            canvas.DrawCircle((float)m.X, (float)m.Y, 1.5f * (float)dpiScale, _motePaint);
        }

        var shortSide = fd.Field.Short;
        foreach (var ring in _state.Rings)
        {
            var (radius, alpha) = CreepFog.RingAt(ring, shortSide);
            _ringPaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(alpha * 255, 0, 255));
            _ringPaint.StrokeWidth = 2f * (float)dpiScale;
            canvas.DrawCircle((float)ring.X, (float)ring.Y, (float)radius, _ringPaint);
        }
        canvas.Restore();
        _lastDrawnCoverage = _state.Coverage;
    }

    public override void OnDeactivated()
    {
        // Drop the per-monitor fields too, so a display change between runs rebuilds them.
        foreach (var fd in _fields.Values) { fd.Fog?.Dispose(); fd.Fog = null; }
        _fields.Clear();
        _fieldList.Clear();
    }

    private void StepMotes(FieldDraw fd, double dt, MotionLevel level)
    {
        var particles = level == MotionLevel.Full && MotionFx.AllowParticles;
        var f = fd.Field;
        for (var i = 0; i < fd.Motes.Length; i++)
        {
            ref var m = ref fd.Motes[i];
            if (!m.Live) continue;
            if (!particles) { m.Live = false; continue; }
            m.Life += dt; m.X += m.Vx * dt; m.Y += m.Vy * dt;
            if (m.Life >= 3) m.Live = false;
        }
        if (!particles || _rng.NextDouble() >= dt * 10 * _state.Coverage) return;
        for (var i = 0; i < fd.Motes.Length; i++)
        {
            ref var m = ref fd.Motes[i];
            if (m.Live) continue;
            m = new Mote
            {
                Live = true, Life = 0,
                X = f.X + _rng.NextDouble() * f.W, Y = f.Y + _rng.NextDouble() * f.H,
                Vx = (_rng.NextDouble() - 0.5) * 8, Vy = -6 - _rng.NextDouble() * 8,
            };
            return;
        }
    }

    private void ClearMotes()
    {
        foreach (var fd in _fields.Values)
            for (var i = 0; i < fd.Motes.Length; i++) fd.Motes[i].Live = false;
    }

    /// <summary>The one soft blob, rendered once (mockup: .55 / .22 / 0 alpha pink radial).</summary>
    private static SKImage BuildSprite()
    {
        var info = new SKImageInfo(SpriteSize, SpriteSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        var mid = SpriteSize / 2f;
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(mid, mid), mid,
            new[]
            {
                new SKColor(CreepFog.TintR, CreepFog.TintG, CreepFog.TintB, (byte)(0.55 * 255)),
                new SKColor(255, 120, 200, (byte)(0.22 * 255)),
                new SKColor(255, 120, 200, 0),
            },
            new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader, IsAntialias = true };
        c.DrawRect(0, 0, SpriteSize, SpriteSize, p);
        return surface.Snapshot();
    }
}
