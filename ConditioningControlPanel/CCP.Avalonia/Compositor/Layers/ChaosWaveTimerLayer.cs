using System;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// ChaosWaveTimerLayer — the small click-through pill pinned to the primary monitor's
/// top-right corner showing the current wave, the time left in it, and the run score
/// (migrated from the standalone <c>ChaosWaveTimerOverlay</c> onto the compositor, WS2/WP3
/// UCE migration; recipe in <c>docs/unified-compositor-engine-plan.md</c> Phase F).
///
/// Behaviour contract (the current shipped behaviour of <c>ChaosWaveTimerOverlay</c>, which
/// is the contract holder — the implementation underneath is free):
/// - a rounded pill (corner 12, dark fill #120E1E α170, pink border #E84393 α160, pad 14/5)
///   pinned to the primary work-area top-right (right inset 14, top inset 10);
/// - line 1: "WAVE x/y" (or "LAST WAVE" on the final wave) in pink #E89BC8 13, a 10-gap,
///   then the clock "m:ss" in 17 bold — white normally, DANGER red #FF5A5A when ≤10s left;
/// - line 2: the score right-aligned in gold #FFD700 13;
/// - "final rush" (≤10s AND the last wave): the clock breathes, opacity 0.25↔1.0 over an
///   840ms full cycle (WPF DoubleAnimation 1.0↔0.25 / 420ms AutoReverse).
///
/// The owning <c>AvaloniaChaosService</c> drives state (SetValues from the run tick, Hide
/// when the draft table is out, Clear at run teardown); this layer only renders it (UCE
/// rule 7). Primary-monitor only (WPF was a single primary-screen window): rendered on the
/// compositor window whose bounds origin is the virtual-desktop origin. Geometry is PHYSICAL
/// px — the pill anchors to <c>screen.WorkingArea</c> and the 13/17 DIP glyphs convert per
/// monitor via <c>screen.Scaling</c> (the ChaosPopTextLayer pattern). Z from
/// <see cref="CompositorLayers"/> only (UCE rule 9); capture-VISIBLE (main surface,
/// <see cref="BaseLayer.ExcludeFromCapture"/> stays false).
///
/// Zero per-frame allocations: fonts/paints built once; each text <see cref="SKTextBlob"/>
/// rebuilt only when its string changes (the clock ~1/s, wave/score rarely), never per frame.
/// </summary>
public sealed class ChaosWaveTimerLayer : BaseLayer
{
    private const float PadH = 14f;
    private const float PadV = 5f;
    private const float Corner = 12f;
    private const float LabelGap = 10f;
    private const float ScoreMarginTop = 2f;
    private const float RightInset = 14f;   // WPF: right edge at wa.Right - 14*scale
    private const float TopInset = 10f;     // WPF: top at wa.Y + 10*scale
    private const double PulseFullCycleMs = 840.0;   // WPF 420ms AutoReverse

    private static readonly SKColor PillFill = new(0x12, 0x0E, 0x1E, 170);
    private static readonly SKColor PillBorder = new(0xE8, 0x43, 0x93, 160);
    private static readonly SKColor WaveColor = new(0xE8, 0x9B, 0xC8);
    private static readonly SKColor ClockNormal = new(0xFF, 0xFF, 0xFF);
    private static readonly SKColor ClockUrgent = new(0xFF, 0x5A, 0x5A);
    private static readonly SKColor ScoreColor = new(0xFF, 0xD7, 0x00);

    private readonly object _sync = new();
    private readonly SKFont _smallFont;   // wave label + score (13)
    private readonly SKFont _clockFont;   // clock (17)
    private readonly SKPaint _pillFillPaint;
    private readonly SKPaint _pillStrokePaint;
    private readonly SKPaint _textPaint;

    private bool _visible;
    private bool _urgent;
    private bool _finalRush;
    private double _pulseClockMs;

    private string _waveStr = "";
    private string _clockStr = "";
    private string _scoreStr = "";
    private SKTextBlob? _waveBlob, _clockBlob, _scoreBlob;
    private float _waveW, _clockW, _scoreW;

    public ChaosWaveTimerLayer()
    {
        _smallFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 13f) { Subpixel = true };
        _clockFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 17f) { Subpixel = true };
        _pillFillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = PillFill };
        _pillStrokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = PillBorder };
        _textPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
    }

    public override int ZIndex => CompositorLayers.ChaosWaveTimer;

    public override bool IsActive
    {
        get { lock (_sync) { return _visible; } }
    }

    /// <summary>Show + update the pill (WPF Update). <paramref name="secLeftInWave"/> is the
    /// seconds remaining in the current wave.</summary>
    public void SetValues(int wave, int waveCount, double secLeftInWave, double score)
    {
        bool last = wave >= waveCount;
        var waveStr = last ? "LAST WAVE" : $"WAVE {wave}/{waveCount}";
        int s = (int)Math.Max(0, Math.Ceiling(secLeftInWave));
        var clockStr = $"{s / 60}:{s % 60:00}";
        var scoreStr = $"{(int)score:N0}";
        bool urgent = secLeftInWave <= 10;
        bool finalRush = urgent && last;

        lock (_sync)
        {
            if (waveStr != _waveStr) { _waveStr = waveStr; RebuildLocked(ref _waveBlob, ref _waveW, waveStr, _smallFont); }
            if (clockStr != _clockStr) { _clockStr = clockStr; RebuildLocked(ref _clockBlob, ref _clockW, clockStr, _clockFont); }
            if (scoreStr != _scoreStr) { _scoreStr = scoreStr; RebuildLocked(ref _scoreBlob, ref _scoreW, scoreStr, _smallFont); }
            _urgent = urgent;
            if (finalRush && !_finalRush) _pulseClockMs = 0;   // fresh breath when the final rush starts
            _finalRush = finalRush;
            _visible = true;
        }
    }

    /// <summary>Hide the pill but keep the run alive (WPF Clear — the watch blanks while the
    /// draft table is out); a subsequent SetValues re-shows it.</summary>
    public void Hide()
    {
        lock (_sync) { _visible = false; }
    }

    /// <summary>Run teardown (WPF CloseActive): hide and reset the animation state.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _visible = false;
            _urgent = false;
            _finalRush = false;
            _pulseClockMs = 0;
        }
    }

    private static void RebuildLocked(ref SKTextBlob? blob, ref float width, string text, SKFont font)
    {
        blob?.Dispose();
        blob = SKTextBlob.Create(text, font);
        width = font.MeasureText(text);
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            if (_visible && _finalRush) _pulseClockMs += deltaTime.TotalMilliseconds;
        }
    }

    public void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        // Primary-monitor only: draw on the compositor window whose region is the
        // virtual-desktop origin (Windows always puts the primary at (0,0)).
        if (Math.Abs(bounds.X) > 1 || Math.Abs(bounds.Y) > 1) return;

        bool visible, urgent, finalRush;
        double pulseClockMs;
        SKTextBlob? waveBlob, clockBlob, scoreBlob;
        float waveW, clockW, scoreW;
        lock (_sync)
        {
            visible = _visible;
            urgent = _urgent;
            finalRush = _finalRush;
            pulseClockMs = _pulseClockMs;
            waveBlob = _waveBlob; clockBlob = _clockBlob; scoreBlob = _scoreBlob;
            waveW = _waveW; clockW = _clockW; scoreW = _scoreW;
        }
        if (!visible || clockBlob == null) return;

        var scaling = (float)(screen != null && screen.Scaling > 0 ? screen.Scaling : 1.0);
        var wa = screen?.WorkingArea ?? bounds;

        // Line metrics (DIP).
        var cm = _clockFont.Metrics;
        var sm = _smallFont.Metrics;
        var rowLineH = cm.Descent - cm.Ascent;
        var scoreLineH = sm.Descent - sm.Ascent;

        var rowW = waveW + LabelGap + clockW;
        var contentW = Math.Max(rowW, scoreW);
        var pillW = contentW + 2 * PadH;
        var pillH = PadV + rowLineH + ScoreMarginTop + scoreLineH + PadV;

        // Anchor the pill's top-left in PHYSICAL px (right edge at wa.Right - RightInset).
        var rightPx = (float)(wa.X + wa.Width) - RightInset * scaling;
        var topPx = (float)wa.Y + TopInset * scaling;
        var leftPx = rightPx - pillW * scaling;

        var save = canvas.Save();
        canvas.Translate(leftPx, topPx);
        canvas.Scale(scaling);   // draw the rest in DIP units

        // Pill.
        var pillRect = new SKRect(0, 0, pillW, pillH);
        canvas.DrawRoundRect(pillRect, Corner, Corner, _pillFillPaint);
        canvas.DrawRoundRect(pillRect, Corner, Corner, _pillStrokePaint);

        // Line 1: wave label (vertically centred in the row) + clock.
        var rowBaseline = PadV - cm.Ascent;
        var waveBaseline = PadV + (rowLineH - (sm.Descent - sm.Ascent)) / 2f - sm.Ascent;
        if (waveBlob != null)
        {
            _textPaint.Color = WaveColor;
            canvas.DrawText(waveBlob, PadH, waveBaseline, _textPaint);
        }
        // Clock alpha breathes during the final rush (linear triangle 0.25↔1.0 over 840ms).
        byte clockAlpha = 255;
        if (finalRush)
        {
            var t = (pulseClockMs % PulseFullCycleMs) / (PulseFullCycleMs / 2.0); // 0..2
            var tri = t <= 1 ? t : 2 - t;                                          // 0..1..0
            clockAlpha = (byte)Math.Clamp((0.25 + 0.75 * tri) * 255.0, 0, 255);
        }
        _textPaint.Color = (urgent ? ClockUrgent : ClockNormal).WithAlpha(clockAlpha);
        canvas.DrawText(clockBlob, PadH + waveW + LabelGap, rowBaseline, _textPaint);

        // Line 2: score, right-aligned to the content edge.
        if (scoreBlob != null)
        {
            var scoreBaseline = PadV + rowLineH + ScoreMarginTop - sm.Ascent;
            _textPaint.Color = ScoreColor;
            canvas.DrawText(scoreBlob, PadH + contentW - scoreW, scoreBaseline, _textPaint);
        }

        canvas.RestoreToCount(save);
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);
}
