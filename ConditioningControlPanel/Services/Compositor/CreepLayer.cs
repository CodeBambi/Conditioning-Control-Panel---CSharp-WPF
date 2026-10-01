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
    private readonly SKPaint _wispPaint = new() { FilterQuality = SKFilterQuality.Low };
    private readonly SKPaint _vignettePaint = new() { IsAntialias = false };
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
        /// <summary>The arrival pulse's edge gradient, built once per monitor (world px).</summary>
        public SKShader? Vignette;
        public FieldDraw(CreepField f) { Field = f; }
    }

    private struct Mote { public bool Live, Burst; public double X, Y, Vx, Vy, Life, Span, Seed; }

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

    /// <summary>Mid switch-off retreat: still drawing, on its way out.</summary>
    public bool IsRetreating => IsActive && _state.Retreating;

    /// <summary>Start from a fresh, thin fog that slides in from the edges (or come back from a
    /// retreat in progress without a pop). Idempotent while showing. UI thread.</summary>
    public void Show()
    {
        if (IsActive)
        {
            CreepFog.CancelRetreat(_state, MotionFx.Level);
            _dirty = true;
            return;
        }
        CreepFog.Reset(_state, _fieldList);
        CreepFog.Arrive(_state);
        ClearMotes();
        _dirty = true;
        SetActive(true);
    }

    /// <summary>Switch off with an eased retreat to the edges; the layer drops itself when done.
    /// Panic never comes here: it calls <see cref="Hide"/>. UI thread.</summary>
    public void Retreat()
    {
        if (!IsActive) return;
        CreepFog.BeginRetreat(_state);
        _dirty = true;
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
        if (!IsActive || _state.Retreating) return;
        var level = MotionFx.Level;
        CreepFog.Click(_state, _fieldList, x, y, level);
        if (MotionFx.AllowParticles) Burst(x, y, CreepFog.BurstCount(level));
        _dirty = true;
    }

    /// <summary>A small puff of motes thrown out from the click, into free mote slots only.</summary>
    private void Burst(double x, double y, int count)
    {
        foreach (var fd in _fields.Values)
        {
            var f = fd.Field;
            if (x < f.X || y < f.Y || x >= f.X + f.W || y >= f.Y + f.H) continue;
            for (var i = 0; i < fd.Motes.Length && count > 0; i++)
            {
                ref var m = ref fd.Motes[i];
                if (m.Live) continue;
                var ang = _rng.NextDouble() * Math.PI * 2;
                var sp = f.Short * (0.06 + 0.06 * _rng.NextDouble());
                m = new Mote
                {
                    Live = true, Burst = true, Life = 0, Span = CreepFog.BurstLife, Seed = _rng.NextDouble(),
                    X = x, Y = y, Vx = Math.Cos(ang) * sp, Vy = Math.Sin(ang) * sp,
                };
                count--;
            }
            return;
        }
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
        if (CreepFog.InTransition(_state)) _dirty = true;   // arrival and retreat are short: every frame

        if (CreepFog.RetreatDone(_state, level))
        {
            SetActive(false);   // the next Show resets and arrives fresh
            return;
        }

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
        var presence = Math.Clamp(_state.AlphaMul, 0, 1);
        var a = (byte)Math.Clamp(CreepFog.LayerAlpha(_state.Coverage) * presence * 255, 0, 255);
        _blobPaint.Color = new SKColor(255, 255, 255, a);
        _wispPaint.Color = new SKColor(255, 255, 255, (byte)(a * CreepFog.WispAlpha));

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
            // Everything lands inside the fog surface, so the arrival pulse and the wisps sit
            // under the same opacity ceiling as the blobs.
            var vig = _state.Vignette * presence / CreepFog.OpacityCeiling;
            if (vig > 0.003)
            {
                fd.Vignette ??= BuildVignette(fd.Field);
                _vignettePaint.Shader = fd.Vignette;
                _vignettePaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(vig * 255, 0, 255));
                fc.DrawRect(dest, _vignettePaint);
            }
            foreach (var w in fd.Field.Wisps)
            {
                if (w.Length <= 0 || w.Width <= 0) continue;
                fc.Save();
                fc.Translate((float)w.Px, (float)w.Py);
                fc.RotateRadians((float)w.Angle);
                var hl = (float)(w.Length / 2); var hw = (float)(w.Width / 2);
                fc.DrawImage(sprite, new SKRect(-hl, -hw, hl, hw), _wispPaint);
                fc.Restore();
            }
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
            var al = CreepFog.MoteAlpha(m.Life, m.Span, m.Seed, m.Burst) * presence;
            if (al <= 0.004) continue;
            _motePaint.Color = m.Burst
                ? new SKColor(255, 225, 245, (byte)Math.Clamp(al * 255, 0, 255))
                : new SKColor(255, 200, 235, (byte)Math.Clamp(al * 255, 0, 255));
            var mr = CreepFog.MoteRadius(m.Life, m.Span, m.Seed, m.Burst);
            canvas.DrawCircle((float)m.X, (float)m.Y, (float)(mr * dpiScale), _motePaint);
        }

        var shortSide = fd.Field.Short;
        foreach (var ring in _state.Rings)
        {
            var (radius, alpha) = CreepFog.RingAt(ring, shortSide);
            alpha *= presence;
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
        foreach (var fd in _fields.Values)
        {
            fd.Fog?.Dispose(); fd.Fog = null;
            fd.Vignette?.Dispose(); fd.Vignette = null;
        }
        _vignettePaint.Shader = null;
        _fields.Clear();
        _fieldList.Clear();
    }

    private void StepMotes(FieldDraw fd, double dt, MotionLevel level)
    {
        var particles = level != MotionLevel.Off && MotionFx.AllowParticles;
        var f = fd.Field;
        var drag = Math.Exp(-dt * CreepFog.MoteDrag);
        for (var i = 0; i < fd.Motes.Length; i++)
        {
            ref var m = ref fd.Motes[i];
            if (!m.Live) continue;
            if (!particles) { m.Live = false; continue; }
            m.Life += dt;
            if (m.Burst) { m.Vx *= drag; m.Vy *= drag; }
            // drifting motes sway on their own phase, so they float rather than slide
            var sway = m.Burst ? 0 : Math.Sin(m.Life * 1.1 + m.Seed * Math.PI * 2) * 6;
            m.X += (m.Vx + sway) * dt; m.Y += m.Vy * dt;
            if (m.Life >= m.Span) m.Live = false;
        }
        // New drifting motes are born inside the fog, near a blob, and never while it retreats.
        if (!particles || _state.Retreating
            || _rng.NextDouble() >= dt * 10 * _state.Coverage * _state.AlphaMul) return;
        var blob = f.Blobs[_rng.Next(f.Blobs.Length)];
        var spread = blob.DrawSize * 0.3;
        var x = Math.Clamp(blob.Px + (_rng.NextDouble() - 0.5) * 2 * spread, f.X, f.X + f.W);
        var y = Math.Clamp(blob.Py + (_rng.NextDouble() - 0.5) * 2 * spread, f.Y, f.Y + f.H);
        for (var i = 0; i < fd.Motes.Length; i++)
        {
            ref var m = ref fd.Motes[i];
            if (m.Live) continue;
            m = new Mote
            {
                Live = true, Life = 0, Span = CreepFog.MoteLife, Seed = _rng.NextDouble(),
                X = x, Y = y,
                Vx = (_rng.NextDouble() - 0.5) * 10, Vy = -4 - _rng.NextDouble() * 6,
            };
            return;
        }
    }

    /// <summary>Edge gradient for the arrival pulse: clear in the middle, tint at the rim.</summary>
    private static SKShader BuildVignette(CreepField f)
    {
        var cx = (float)(f.X + f.W / 2); var cy = (float)(f.Y + f.H / 2);
        var r = (float)(Math.Sqrt(f.W * f.W + f.H * f.H) / 2);
        return SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), Math.Max(1, r),
            new[]
            {
                new SKColor(CreepFog.TintR, CreepFog.TintG, CreepFog.TintB, 0),
                new SKColor(CreepFog.TintR, CreepFog.TintG, CreepFog.TintB, 0),
                new SKColor(255, 120, 200, 255),
            },
            new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp);
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
