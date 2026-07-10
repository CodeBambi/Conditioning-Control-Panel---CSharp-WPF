using System;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// AttentionCheckLayer — the "eye is watching" gaze target (migrated from the standalone
/// click-through <c>Window</c> hosting <c>AttentionCheckControl</c> onto the compositor,
/// WS2/WP3 UCE migration; recipe in <c>docs/unified-compositor-engine-plan.md</c> Phase F,
/// behaviour reference = the WPF <c>AttentionCheckControl</c> source). It is the last live
/// window-based passive effect; migrating it completes the UCE window-migration lane.
///
/// Behaviour contract (the shipped behaviour of <c>AttentionCheckControl</c>, the contract
/// holder — the implementation underneath is free): an intrinsically 84×84 DIP composition —
/// a hot-pink progress ring around a glowing dot — shown at a chosen screen position:
/// - background ring Ø84 stroke 3 <c>#33FFFFFF</c>;
/// - foreground progress ring Ø84 stroke 4 <c>#FFFF69B4</c>, round cap, filling CLOCKWISE
///   from the top (−90°), sweep = progress × 360°;
/// - a soft glow Ø60 <c>#50FF69B4</c> behind a Ø44 radial-gradient dot
///   (<c>#FFFFB6E1</c>→<c>#FFFF69B4</c>@0.6→<c>#FFC71585</c>);
/// - the foreground ring gently pulses scale 1.0↔1.18 over an 840 ms full cycle
///   (WPF Animation SineEaseInOut 1.0↔1.18 / 420 ms AutoReverse) to signal "look here";
/// - dismissal fades the whole target opacity 1→0 over 180 ms (WPF window opacity fade).
///
/// The owning <c>AvaloniaAttentionCheckService</c> drives state (<see cref="Show"/> on fire,
/// <see cref="SetProgress"/> from the gaze-dwell tick, <see cref="Hide"/> on resolve); this
/// layer only renders it (UCE rule 7). Drawn on whichever monitor contains the target centre
/// (the service picks the primary screen). Geometry is PHYSICAL px — the 84 DIP art anchors
/// to the target rect and converts per monitor via <c>screen.Scaling</c> (the
/// ChaosWaveTimerLayer pattern). Z from <see cref="CompositorLayers"/> only (UCE rule 9);
/// capture-VISIBLE (main surface, <see cref="BaseLayer.ExcludeFromCapture"/> stays false).
///
/// Zero per-frame allocations: paints and the dot's radial-gradient shader are built once;
/// pulse and fade are derived from wall-clock timestamps read under the lock (no Update
/// dependency — the engine polls <see cref="IsActive"/> every frame, which latches the
/// layer off when the fade completes).
/// </summary>
public sealed class AttentionCheckLayer : BaseLayer
{
    private const double PulseFullCycleMs = 840.0; // WPF 420 ms AutoReverse = 840 ms full cycle
    private const double PulseAmplitude = 0.09;    // 1.0 .. 1.18
    private const float ArtHalf = 42f;             // 84 DIP composition, centred

    private static readonly SKColor BgRingColor = new(0xFF, 0xFF, 0xFF, 0x33);
    private static readonly SKColor GlowColor = new(0xFF, 0x69, 0xB4, 0x50);
    private static readonly SKColor FgRingColor = new(0xFF, 0x69, 0xB4, 0xFF);

    private readonly object _sync = new();
    private readonly SKPaint _bgRingPaint;
    private readonly SKPaint _glowPaint;
    private readonly SKPaint _dotPaint;
    private readonly SKPaint _fgArcPaint;
    private readonly SKPaint _fadePaint;
    private readonly SKRect _arcOval = new(-40f, -40f, 40f, 40f); // fg ring centreline r = (84-4)/2

    private bool _active;
    private double _tx, _ty, _tsize;   // target rect in PHYSICAL virtual-desktop px
    private double _progress;
    private DateTime _shownAt;
    private DateTime? _hideStartedAt;
    private double _fadeDurMs = 180.0;

    public AttentionCheckLayer()
    {
        _bgRingPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = BgRingColor };
        _glowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = GlowColor };
        _fgArcPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, StrokeCap = SKStrokeCap.Round, Color = FgRingColor };
        _fadePaint = new SKPaint();
        // Dot radial gradient in DIP-local space, centred at origin, r = 22 (Ø44). Built once.
        var dotShader = SKShader.CreateRadialGradient(
            new SKPoint(0f, 0f), 22f,
            new[] { new SKColor(0xFF, 0xB6, 0xE1), new SKColor(0xFF, 0x69, 0xB4), new SKColor(0xC7, 0x15, 0x85) },
            new[] { 0f, 0.6f, 1f },
            SKShaderTileMode.Clamp);
        _dotPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = dotShader };
    }

    public override int ZIndex => CompositorLayers.AttentionCheck;

    public override bool IsActive
    {
        get
        {
            lock (_sync)
            {
                if (!_active) return false;
                if (_hideStartedAt.HasValue && (DateTime.UtcNow - _hideStartedAt.Value).TotalMilliseconds >= _fadeDurMs)
                {
                    _active = false; // latch off once the fade completes
                    return false;
                }
                return true;
            }
        }
    }

    /// <summary>Show the gaze target at <paramref name="targetPhysical"/> (physical
    /// virtual-desktop px, 84 DIP square) with progress reset. Restarts pulse and clears any
    /// in-flight fade (WPF: new Window + StartPulse + SetProgress(0)).</summary>
    public void Show(ConditioningControlPanel.Core.Platform.PixelRect targetPhysical)
    {
        lock (_sync)
        {
            _tx = targetPhysical.X;
            _ty = targetPhysical.Y;
            _tsize = Math.Max(1.0, targetPhysical.Width);
            _progress = 0;
            _shownAt = DateTime.UtcNow;
            _hideStartedAt = null;
            _active = true;
        }
    }

    /// <summary>Set the progress-ring fill 0..1 (WPF SetProgress from the gaze-dwell tick).</summary>
    public void SetProgress(double progress)
    {
        lock (_sync) { _progress = Math.Clamp(progress, 0.0, 1.0); }
    }

    /// <summary>Dismiss with a <paramref name="fadeMs"/> opacity fade, then deactivate (WPF
    /// window opacity 1→0 over 180 ms then Close). fadeMs ≤ 0 hides immediately.</summary>
    public void Hide(int fadeMs = 180)
    {
        lock (_sync)
        {
            if (!_active) return;
            if (fadeMs <= 0) { _active = false; _hideStartedAt = null; return; }
            if (_hideStartedAt.HasValue) return; // already fading
            _fadeDurMs = fadeMs;
            _hideStartedAt = DateTime.UtcNow;
        }
    }

    public override void Update(TimeSpan deltaTime) { /* pulse/fade derive from timestamps in Render */ }

    public void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        double tx, ty, tsize;
        double progress, fadeDurMs;
        DateTime shownAt;
        DateTime? hideAt;
        lock (_sync)
        {
            if (!_active) return;
            tx = _tx; ty = _ty; tsize = _tsize;
            progress = _progress; fadeDurMs = _fadeDurMs;
            shownAt = _shownAt; hideAt = _hideStartedAt;
        }

        // Draw only on the monitor whose bounds contain the target centre (physical px).
        float cx = (float)(tx + tsize / 2.0);
        float cy = (float)(ty + tsize / 2.0);
        if (cx < bounds.X || cx >= bounds.X + bounds.Width || cy < bounds.Y || cy >= bounds.Y + bounds.Height)
            return;

        var now = DateTime.UtcNow;

        byte fadeAlpha = 255;
        if (hideAt.HasValue)
        {
            var e = (now - hideAt.Value).TotalMilliseconds;
            if (e >= fadeDurMs) return; // fully faded — IsActive latches off
            fadeAlpha = (byte)Math.Clamp((1.0 - e / fadeDurMs) * 255.0, 0, 255);
        }

        var pulseMs = (now - shownAt).TotalMilliseconds % PulseFullCycleMs;
        var pulse = 1.0 + PulseAmplitude * (1.0 - Math.Cos(2.0 * Math.PI * pulseMs / PulseFullCycleMs));

        var scaling = (float)(screen != null && screen.Scaling > 0 ? screen.Scaling : 1.0);

        int layerRestore = -1;
        if (fadeAlpha < 255)
        {
            _fadePaint.Color = SKColors.White.WithAlpha(fadeAlpha);
            layerRestore = canvas.SaveLayer(_fadePaint);
        }

        var save = canvas.Save();
        canvas.Translate(cx, cy);   // physical px centre; engine pre-transform maps to this monitor
        canvas.Scale(scaling);      // draw the 84 DIP art in DIP units

        canvas.DrawCircle(0f, 0f, 40.5f, _bgRingPaint); // background ring (Ø84 stroke 3)
        canvas.DrawCircle(0f, 0f, 30f, _glowPaint);     // soft glow (Ø60)
        canvas.DrawCircle(0f, 0f, 22f, _dotPaint);      // radial-gradient dot (Ø44)

        if (progress > 0.001)
        {
            var s2 = canvas.Save();
            canvas.Scale((float)pulse, (float)pulse);   // only the foreground ring pulses
            canvas.DrawArc(_arcOval, -90f, (float)(progress * 360.0), false, _fgArcPaint);
            canvas.RestoreToCount(s2);
        }

        canvas.RestoreToCount(save);
        if (layerRestore >= 0) canvas.RestoreToCount(layerRestore);
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);
}
