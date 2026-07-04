using System;
using System.Collections.Generic;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// The Porn DVD active skill AND the Intrusive Thoughts accessory (WS2/WP3 Phase F #6): a
/// DVD-screensaver text logo that drifts across the primary work area, bouncing off the
/// edges (hue shift on every bounce, as tradition demands) and popping every chaos bubble
/// it flies through. Was a pooled-window set; the pool machinery dies with the windows —
/// logos are plain render items now.
///
/// Behavior contract (WPF Chaos/ChaosDvdOverlay.cs):
/// - Launch(durationSec, speedMult, scale, count 1..2, text?, splitOnRabbit, splitBounces);
///   text marks an Intrusive Thoughts instance (alive cap 8); fontScale clamp 0.5..1.5;
/// - Segoe UI Bold 46*fontScale, OutlinedText stroke #0B0812 thickness 2.6 (pen = 2*2.6,
///   round join, UNDER the fill), pad = strokeThickness + 6 per side (WPF Build sizing);
/// - speed 230 DIP/s * clamp(speedMult, 0.3, 2.0), heading 20°..80° (never axis-flat),
///   random signs; random start inside the primary work area;
/// - fade-in 0 → 0.85 over 180ms (motion + life clock already running); life max(1,
///   durationSec); at expiry motion STOPS and the logo fades current → 0 over 240ms;
/// - bounce: clamp to the work-area edge, reflect velocity, hue palette advance, shared
///   boing throttle (one "dvd_bounce" @0.35 per 250ms across ALL logos);
/// - Casting Couch (splitBounces): a bounce peels off a diverged twin (±0.61 rad, same
///   speed/remaining life, one fewer split each), toy-logo alive cap 8, "dvd_launch" @0.35;
/// - Intrusive Thoughts capstone (splitOnRabbit): brushing a darter splits ONCE per
///   instance — self +2s, one diverged clone (which can split again), thought cap 8,
///   "dvd_bounce" @0.4;
/// - every step pops bubbles in the logo rect (treats pop with payloads, live ones snap);
/// - frame delta clamped to 0.1s (WPF composition-clock stall clamp).
///
/// The Spanker capstone smack-to-turn (WPF SpankerRedirect + clickable window) is DEAD in
/// the Avalonia head — nothing ever assigns SpankerRedirect (grep-verified) — so this layer
/// is purely passive; the clickable path arrives with the run-engine Spanker port (hook
/// hit-testing like FlashLayer.HitTest when it does).
///
/// Side effects (bubble pops, darter queries, sfx) are POLICY and stay with the owning
/// AvaloniaChaosService via delegates it assigns at construction (the announcer
/// LineCompleted precedent); they are invoked on the engine tick OUTSIDE the layer lock.
/// Geometry is PHYSICAL px: velocities convert DIP/s → px/s with the PRIMARY screen's
/// scale passed at Launch (WPF's single-DPI window space, per-monitor-correct because the
/// flight is confined to the primary work area). Blob/paints built at spawn, zero per-frame
/// allocations; group opacity via SaveLayer (stroke-under-fill must fade as ONE surface —
/// the pop-text precedent; the WPF window opacity is the analog).
///
/// Capture-VISIBLE (main surface). Z from CompositorLayers only (UCE rule 9) — the WPF
/// RaiseAboveVideo/RaiseTopmost churn and the shared-window-host mode (a WPF render-thread
/// workaround, ChaosDvdHostOverlay) have no layer equivalent.
/// </summary>
public sealed class ChaosDvdLayer : BaseLayer
{
    private const double BaseFontDip = 46;      // WPF BASE_FONT
    private const double BaseSpeedDips = 230;   // WPF BASE_SPEED (DIP/s)
    private const double PeakOpacity = 0.85;    // WPF PEAK_OPAC
    private const int MaxThoughts = 8;          // WPF MAX_THOUGHTS
    private const int MaxToyLogos = 8;          // WPF MAX_TOY_LOGOS
    private const double FadeInMs = 180;        // WPF Begin fade
    private const double FadeOutMs = 240;       // WPF FadeOutAndRetire
    private const double StrokeDip = 2.6;       // WPF OutlinedText.StrokeThickness
    private const double PadDip = StrokeDip + 6; // WPF OutlinedText.Build pad
    private const double BounceCueThrottleMs = 250; // shared across logos (WPF _lastBounceCue)

    // The classic logo palette, advanced one step per bounce (WPF Hues).
    private static readonly SKColor[] Hues =
    {
        new(0xFF, 0x4D, 0xC4), new(0x7A, 0xE0, 0xFF), new(0xFF, 0xD7, 0x00),
        new(0x9C, 0xE8, 0xA0), new(0xD2, 0x4D, 0xFF), new(0xFF, 0x8A, 0x5C),
    };
    private static readonly SKColor StrokeColor = new(0x0B, 0x08, 0x12);

    /// <summary>Pop every chaos bubble intersecting a logo rect (PHYSICAL px). Assigned by
    /// the owning service (policy stays in the service; invoked outside the layer lock).</summary>
    public Action<ConditioningControlPanel.Core.Platform.PixelRect>? PopBubblesInRect;

    /// <summary>True when a darter (white rabbit) intersects the rect (PHYSICAL px).</summary>
    public Func<ConditioningControlPanel.Core.Platform.PixelRect, bool>? DarterIntersects;

    /// <summary>Play a chaos sfx cue (name, volume).</summary>
    public Action<string, float>? PlaySfx;

    private readonly object _sync = new();
    private readonly List<Logo> _logos = new();
    private readonly Random _rng = new();
    private readonly SKTypeface _typeface;
    private readonly SKPaint _fillPaint;
    private readonly SKPaint _strokePaint;
    private readonly SKPaint _groupPaint;
    private ConditioningControlPanel.Core.Platform.PixelRect _workAreaPx = ConditioningControlPanel.Core.Platform.PixelRect.Empty;
    private double _scale = 1.0;
    private double _clockMs;           // monotonic tick clock for the shared bounce-cue throttle
    private double _lastBounceCueMs = double.MinValue;

    private sealed class Logo
    {
        public SKTextBlob Blob = null!;
        public string Text = "";
        public float BaselineOffsetPx;   // pad - ascent (top-anchored like WPF geometry at (pad, pad))
        public bool IsThought;
        public bool SplitOnRabbit;
        public bool SplitSpent;
        public int SplitBouncesLeft;
        public double FontScale = 1.0;
        public double X, Y, W, H;        // physical px
        public double Vx, Vy;            // physical px/s
        public double RemainingSec;
        public int HueIndex;
        public double FadeClockMs;
        public bool Retiring;
        public double RetireFromOpacity;
        public double Opacity;
        public float StrokeWidthPx;
        public float PadPx;
    }

    public ChaosDvdLayer()
    {
        _typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        _fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
            Color = StrokeColor,
        };
        _groupPaint = new SKPaint();
    }

    public override int ZIndex => CompositorLayers.ChaosDvd;

    public override bool IsActive
    {
        get { lock (_sync) { return _logos.Count > 0; } }
    }

    // ConsumeDirty stays the base always-dirty: every live logo moves/fades each frame,
    // and with no logos IsActive is false so the engine never ticks or renders this layer.

    /// <summary>True while a TOY-launched logo flies (WPF AnyToyActive). Intrusive Thoughts
    /// phrases don't count — counting them lit the Porn DVD button mid-cooldown.</summary>
    public bool AnyToyActive
    {
        get
        {
            lock (_sync)
            {
                foreach (var l in _logos) if (!l.IsThought) return true;
                return false;
            }
        }
    }

    /// <summary>Launch logos (WPF Launch contract — see class doc). <paramref name="workAreaPx"/>
    /// is the primary work area in PHYSICAL px and <paramref name="screenScale"/> its DPI
    /// scale (DIP→px for font/speed), both captured per launch by the owning service.</summary>
    public void Launch(double durationSec, double speedMult, double scale, int count,
        string? text, bool splitOnRabbit, int splitBounces,
        ConditioningControlPanel.Core.Platform.PixelRect workAreaPx, double screenScale)
    {
        if (workAreaPx.IsEmpty) return;
        lock (_sync)
        {
            _workAreaPx = workAreaPx;
            _scale = screenScale > 0 ? screenScale : 1.0;
            for (int i = 0; i < Math.Clamp(count, 1, 2); i++)
            {
                if (text != null && ThoughtCountLocked() >= MaxThoughts) break;
                SpawnLocked(durationSec, speedMult, scale, text, splitOnRabbit, null, null, null, null, splitBounces);
            }
        }
    }

    /// <summary>Run teardown: drop every logo immediately (WPF CloseActive + pool drain).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            foreach (var l in _logos) l.Blob.Dispose();   // under _sync: no draw can be in flight
            _logos.Clear();
        }
    }

    private int ThoughtCountLocked()
    {
        int n = 0;
        foreach (var l in _logos) if (l.IsThought) n++;
        return n;
    }

    private int ToyCountLocked()
    {
        int n = 0;
        foreach (var l in _logos) if (!l.IsThought) n++;
        return n;
    }

    /// <summary>Start one logo flight (WPF Begin). Callers hold _sync.</summary>
    private void SpawnLocked(double durationSec, double speedMult, double scale,
        string? text, bool splitOnRabbit,
        double? startX, double? startY, double? vxOverride, double? vyOverride,
        int splitBounces)
    {
        var logo = new Logo
        {
            RemainingSec = Math.Max(1, durationSec),
            IsThought = text != null,
            SplitOnRabbit = splitOnRabbit,
            FontScale = Math.Clamp(scale, 0.5, 1.5),
            SplitBouncesLeft = Math.Max(0, splitBounces),
            HueIndex = _rng.Next(Hues.Length),
            Text = text ?? "PORN",
        };

        // WPF OutlinedText.Build sizing in the primary screen's px space: font 46*fontScale
        // DIP, pad (2.6+6) DIP per side; height ≈ the font's line span (WPF ft.Height).
        var fontPx = (float)(BaseFontDip * logo.FontScale * _scale);
        using (var font = new SKFont(_typeface, fontPx) { Subpixel = true })
        {
            var blob = SKTextBlob.Create(logo.Text, font);
            if (blob == null) return;
            logo.Blob = blob;
            var metrics = font.Metrics;
            logo.PadPx = (float)(PadDip * _scale);
            logo.StrokeWidthPx = (float)(StrokeDip * 2 * _scale);   // WPF pen = StrokeThickness*2
            logo.BaselineOffsetPx = logo.PadPx - metrics.Ascent;    // top-anchored (ascent is negative)
            logo.W = font.MeasureText(logo.Text) + logo.PadPx * 2;
            logo.H = (metrics.Descent - metrics.Ascent) + logo.PadPx * 2;
        }

        // Random start inside the work area, random diagonal 20°..80° (WPF Begin).
        var speedPx = BaseSpeedDips * Math.Clamp(speedMult, 0.3, 2.0) * _scale;
        var angle = _rng.NextDouble() * Math.PI / 3 + Math.PI / 9;
        logo.Vx = vxOverride ?? speedPx * Math.Cos(angle) * (_rng.Next(2) == 0 ? 1 : -1);
        logo.Vy = vyOverride ?? speedPx * Math.Sin(angle) * (_rng.Next(2) == 0 ? 1 : -1);
        logo.X = startX ?? _workAreaPx.X + _rng.NextDouble() * Math.Max(1, _workAreaPx.Width - logo.W);
        logo.Y = startY ?? _workAreaPx.Y + _rng.NextDouble() * Math.Max(1, _workAreaPx.Height - logo.H);

        _logos.Add(logo);
    }

    public override void Update(TimeSpan deltaTime)
    {
        // WPF StepAll: composition-clock delta, clamped after a stall so no logo can jump.
        var dtSec = Math.Min(deltaTime.TotalSeconds, 0.1);
        var dtMs = deltaTime.TotalMilliseconds;
        if (dtSec <= 0) return;

        // Phase 1 (under _sync): physics + collect side-effect requests.
        List<(Logo logo, ConditioningControlPanel.Core.Platform.PixelRect rect, bool darterCheck)>? effects = null;
        var bounceCue = false;
        var splitLaunchCue = false;
        lock (_sync)
        {
            _clockMs += dtMs;
            for (int i = _logos.Count - 1; i >= 0; i--)
            {
                var logo = _logos[i];

                if (logo.Retiring)
                {
                    // Fading out — motion is done, the fade owns it (WPF FadeOutAndRetire).
                    logo.FadeClockMs += dtMs;
                    logo.Opacity = logo.RetireFromOpacity * Math.Max(0, 1 - logo.FadeClockMs / FadeOutMs);
                    if (logo.FadeClockMs >= FadeOutMs)
                    {
                        logo.Blob.Dispose();   // under _sync: safe deterministic disposal
                        _logos.RemoveAt(i);
                    }
                    continue;
                }

                logo.FadeClockMs += dtMs;
                logo.Opacity = PeakOpacity * Math.Min(1.0, logo.FadeClockMs / FadeInMs);

                logo.RemainingSec -= dtSec;
                if (logo.RemainingSec <= 0)
                {
                    logo.Retiring = true;
                    logo.RetireFromOpacity = logo.Opacity;
                    logo.FadeClockMs = 0;
                    continue;
                }

                var wa = _workAreaPx;
                var x = logo.X + logo.Vx * dtSec;
                var y = logo.Y + logo.Vy * dtSec;
                var bounced = false;
                if (x <= wa.X) { x = wa.X; logo.Vx = Math.Abs(logo.Vx); bounced = true; }
                else if (x + logo.W >= wa.Right) { x = wa.Right - logo.W; logo.Vx = -Math.Abs(logo.Vx); bounced = true; }
                if (y <= wa.Y) { y = wa.Y; logo.Vy = Math.Abs(logo.Vy); bounced = true; }
                else if (y + logo.H >= wa.Bottom) { y = wa.Bottom - logo.H; logo.Vy = -Math.Abs(logo.Vy); bounced = true; }
                logo.X = x;
                logo.Y = y;

                if (bounced)
                {
                    logo.HueIndex = (logo.HueIndex + 1) % Hues.Length;
                    // Soft retro boing, throttled so two logos hugging a corner can't machine-gun it.
                    if (_clockMs - _lastBounceCueMs >= BounceCueThrottleMs)
                    {
                        _lastBounceCueMs = _clockMs;
                        bounceCue = true;
                    }

                    // Casting Couch: the bounce splits the logo — a diverged twin peels off,
                    // both keep one fewer split (2 → two logos → four), capped (WPF StepOne).
                    if (logo.SplitBouncesLeft > 0 && !logo.IsThought)
                    {
                        logo.SplitBouncesLeft--;
                        if (ToyCountLocked() < MaxToyLogos)
                        {
                            var spd = Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy);
                            var baseAng = Math.Atan2(logo.Vy, logo.Vx);
                            var ang = baseAng + (_rng.Next(2) == 0 ? 0.61 : -0.61);   // ~35° divergence
                            SpawnLocked(logo.RemainingSec, 1.0, logo.FontScale, null, false,
                                logo.X, logo.Y, Math.Cos(ang) * spd, Math.Sin(ang) * spd,
                                logo.SplitBouncesLeft);
                            splitLaunchCue = true;
                        }
                    }
                }

                // The collider + the thought-split rabbit query run OUTSIDE the lock (phase 2).
                effects ??= new List<(Logo, ConditioningControlPanel.Core.Platform.PixelRect, bool)>();
                effects.Add((logo,
                    new ConditioningControlPanel.Core.Platform.PixelRect(logo.X, logo.Y, logo.W, logo.H),
                    logo.SplitOnRabbit && !logo.SplitSpent));
            }
        }

        // Phase 2 (no lock): service-owned side effects — bubble pops, darter queries, sfx.
        if (bounceCue) PlaySfx?.Invoke("dvd_bounce", 0.35f);
        if (splitLaunchCue) PlaySfx?.Invoke("dvd_launch", 0.35f);
        if (effects == null) return;
        foreach (var (logo, rect, darterCheck) in effects)
        {
            try { PopBubblesInRect?.Invoke(rect); } catch { }
            if (darterCheck && DarterIntersects?.Invoke(rect) == true)
                SplitThought(logo);
        }
    }

    /// <summary>Intrusive Thoughts capstone: brushing a rabbit splits the thought — self gets
    /// +2s, plus one diverged clone (which can split again). Cap enforced (WPF SplitInTwo).</summary>
    private void SplitThought(Logo logo)
    {
        lock (_sync)
        {
            if (logo.SplitSpent || logo.Retiring || !_logos.Contains(logo)) return;
            logo.SplitSpent = true;
            logo.RemainingSec += 2.0;   // WPF: the +2s lands even at the cap
            if (ThoughtCountLocked() >= MaxThoughts) return;   // no clone, no cue (WPF order)
            var spd = Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy);
            var baseAng = Math.Atan2(logo.Vy, logo.Vx);
            var ang = baseAng + (_rng.Next(2) == 0 ? 0.61 : -0.61);
            SpawnLocked(logo.RemainingSec, 1.0, logo.FontScale, logo.Text, true,
                logo.X, logo.Y, Math.Cos(ang) * spd, Math.Sin(ang) * spd, 0);
        }
        PlaySfx?.Invoke("dvd_bounce", 0.4f);
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        // Draw while holding _sync (FlashLayer discipline — blob disposal is under _sync too).
        lock (_sync)
        {
            foreach (var logo in _logos)
            {
                if (logo.Opacity <= 0) continue;

                // Whole logo (stroke UNDER fill) composited as one group, then faded via
                // SaveLayer alpha — per-paint alpha would let the stroke ghost through the
                // fill (the pop-text precedent; WPF fades the WINDOW's opacity).
                var rect = new SKRect((float)logo.X, (float)logo.Y,
                    (float)(logo.X + logo.W), (float)(logo.Y + logo.H));
                _groupPaint.Color = new SKColor(255, 255, 255,
                    (byte)Math.Clamp(logo.Opacity * 255, 0, 255));
                var save = canvas.SaveLayer(rect, _groupPaint);
                var tx = (float)logo.X + logo.PadPx;
                var ty = (float)logo.Y + logo.BaselineOffsetPx;
                _strokePaint.StrokeWidth = logo.StrokeWidthPx;
                canvas.DrawText(logo.Blob, tx, ty, _strokePaint);
                _fillPaint.Color = Hues[logo.HueIndex];
                canvas.DrawText(logo.Blob, tx, ty, _fillPaint);
                canvas.RestoreToCount(save);
            }
        }
    }
}
