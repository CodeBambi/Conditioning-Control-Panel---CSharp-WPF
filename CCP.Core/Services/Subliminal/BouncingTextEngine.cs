using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>Everything one bouncing logo did in a single frame, reported back to the head so it can
/// run the parts that are not arithmetic (the achievement, the haptic pulse, the burst particles,
/// the window repaint). Nothing here is a request - the engine has already applied its own state.</summary>
/// <param name="ColorBeforeBounce">The logo's colour as the frame began. The corner burst is drawn
/// in it deliberately: WPF spawned the particles before the bounce re-rolled the colour, so passing
/// the new one would change what Windows draws.</param>
public readonly record struct BounceStep(
    bool Bounced,
    bool BouncedX,
    bool CornerHit,
    bool TextChanged,
    (byte R, byte G, byte B) ColorBeforeBounce);

/// <summary>
/// The portable half of <c>BouncingTextService</c>: DVD-screensaver motion, wall and corner
/// detection, the three colour modes, the per-frame effect transforms and the XP rate limit.
///
/// <para><b>Why an extraction and not a move.</b> There is no file to <c>git mv</c> here: the maths
/// was interleaved line-by-line with <c>App.Achievements</c>, <c>App.Haptics</c>, <c>App.Video</c>,
/// <c>CompositionTarget.Rendering</c> and calls into the layered <c>BouncingTextWindow</c>, all
/// inside one 800-line class. So the arithmetic was lifted out method by method and the head now
/// delegates to it; the drawing half is untouched.</para>
///
/// <para><b>What stays in the head.</b> Screen bounds (a monitor enumeration), glyph metrics (a
/// font rasteriser) and the surface itself. The engine takes bounds through
/// <see cref="SetBounds"/> and metrics through <see cref="Measure"/>, so a second head supplies two
/// small facts rather than reimplementing bounce physics that would then drift from Windows.</para>
///
/// <para>Settings are passed in per call, exactly as the WPF methods took them, so this class never
/// reads <c>CoreSettings</c> and a test can drive it against a throwaway <see cref="AppSettings"/>.</para>
/// </summary>
public sealed class BouncingTextEngine
{
    /// <summary>The 100%-size font size. Settings scale this by 50-300%.</summary>
    public const int BaseFontSize = 72;

    /// <summary>Corners are hard to hit exactly, so a single-axis bounce this close to one counts.</summary>
    private const double CornerTolerance = 15.0;

    // Anti-exploit: XP rate limiting for bounces, shared across logos so a second text does not
    // double the XP income. Short cooldown so a corner hit is not counted twice.
    private static readonly TimeSpan BounceXpCooldown = TimeSpan.FromSeconds(2);
    private const int MaxBounceXpPerMinute = 150;

    /// <summary>Per-logo bounce state. One or two of these exist while running.</summary>
    public sealed class Logo
    {
        public string Text = "";
        public double PosX, PosY;
        public double VelX, VelY;
        public double TextWidth = 200, TextHeight = 60;
        public (byte R, byte G, byte B) Color;
        public int HueIndex;            // rainbow-cycle position
        public double Phase;            // per-logo offset so two logos never breathe/wobble in sync
        public double SpinAngle;        // accumulated continuous-spin angle (deg)
        public double Tilt;             // smoothed velocity-lean angle (deg)
        public double SquashTimer = -1; // seconds since last wall hit; <0 = idle
        public bool SquashAxisX;        // true = hit a vertical wall (X velocity reversed)
        public double BurstTimer = -1;  // seconds since last corner hit; <0 = idle
    }

    private readonly Random _random;
    private readonly List<Logo> _logos = new();
    private List<string>? _poolOverride;
    private double _fxTime;   // shared effects clock (seconds)
    private double _lastDt;   // dt of the current frame, so the effect timers need not thread it

    private DateTime _lastBounceXpTime = DateTime.MinValue;
    private int _bounceXpThisMinute;
    private DateTime _bounceXpMinuteStart = DateTime.MinValue;

    /// <summary>Ordered hue wheel for the rainbow-cycle colour mode (advances one step per bounce).</summary>
    private static readonly (byte R, byte G, byte B)[] RainbowWheel = BuildRainbowWheel();

    /// <param name="rng">Injected so a test can make the motion deterministic. Null takes a fresh one.</param>
    public BouncingTextEngine(Random? rng = null) => _random = rng ?? new Random();

    /// <summary>
    /// Head-supplied glyph metrics for a string at a font size, in DIPs. Left null the engine falls
    /// back to the same crude estimate the WPF measurement's own catch block used, so the logo still
    /// bounces off plausible edges rather than off a zero-width box.
    /// </summary>
    public Func<string, int, (double Width, double Height)>? Measure;

    public IReadOnlyList<Logo> Logos => _logos;

    /// <summary>The AI/session-supplied pool, kept so a restart re-rolls from the same override.</summary>
    public IReadOnlyList<string>? PoolOverride => _poolOverride;

    /// <summary>Font size the logos were measured at (base size scaled by the size setting).</summary>
    public int FontSize { get; private set; } = BaseFontSize;

    /// <summary>Family name the running logos were measured with, so a mid-run change is spottable.</summary>
    public string Font { get; private set; } = "";

    public double MinX { get; private set; }
    public double MinY { get; private set; }
    public double MaxX { get; private set; }
    public double MaxY { get; private set; }

    /// <summary>The virtual-desktop rectangle to bounce inside, in DIPs. The head computes it from
    /// its monitor enumeration; call before <see cref="Start"/> or the logos start in a zero box.</summary>
    public void SetBounds(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
    }

    /// <summary>Build one or two logos at random positions, velocities and colours.</summary>
    public void Start(AppSettings settings, IReadOnlyList<string>? pool = null)
    {
        _poolOverride = pool != null && pool.Count > 0 ? pool.ToList() : null;
        _fxTime = 0;
        _lastDt = 0;

        FontSize = (int)(BaseFontSize * settings.BouncingTextSize / 100.0);
        Font = settings.BouncingTextFont ?? "Segoe UI";

        _logos.Clear();
        int logoCount = settings.BouncingTextSecondText ? 2 : 1;
        for (int i = 0; i < logoCount; i++)
        {
            var logo = new Logo
            {
                Phase = _random.NextDouble() * Math.PI * 2,
                HueIndex = _random.Next(RainbowWheel.Length),
            };
            logo.Text = SelectRandomText(settings);
            MeasureInto(logo);

            // Random starting position (ensure text starts fully within bounds)
            logo.PosX = MinX + _random.NextDouble() * Math.Max(1, (MaxX - MinX - logo.TextWidth));
            logo.PosY = MinY + _random.NextDouble() * Math.Max(1, (MaxY - MinY - logo.TextHeight));

            // Random velocity in DIP/second (speed based on setting). The base is the old
            // 3-5 px/tick value scaled by 60 so the on-screen feel is unchanged at 60 FPS, but
            // motion is delta-time driven so it stays correct at any rate.
            var speed = settings.BouncingTextSpeed / 10.0; // 1-10 maps to 0.1-1.0 multiplier
            var baseSpeed = (3.0 + _random.NextDouble() * 2.0) * 60.0; // 180-300 DIP/sec
            logo.VelX = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);
            logo.VelY = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);

            logo.Color = NextColor(logo, settings);
            _logos.Add(logo);
        }
    }

    public void Stop()
    {
        _logos.Clear();
        _poolOverride = null;
    }

    /// <summary>Advance the shared effects clock. Once per frame, before stepping the logos.</summary>
    public void Tick(double dt)
    {
        _fxTime += dt;
        _lastDt = dt;
    }

    public string SelectRandomText(AppSettings settings)
    {
        var enabledTexts = _poolOverride != null && _poolOverride.Count > 0
            ? _poolOverride
            : settings.BouncingTextPool
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

        if (enabledTexts.Count == 0)
            return "GOOD GIRL";
        return enabledTexts[_random.Next(enabledTexts.Count)];
    }

    /// <summary>
    /// Advance one logo's motion and handle wall/corner bounces: reflect, re-colour, arm the squash
    /// and burst timers, award the rate-limited XP and re-roll the text 10% of the time. The head
    /// reads the returned flags for the parts it still owns.
    /// </summary>
    public BounceStep Step(Logo l, double dt, AppSettings settings)
    {
        var colorBefore = l.Color;

        // Move (delta-time based; velocities are DIP/second)
        l.PosX += l.VelX * dt;
        l.PosY += l.VelY * dt;

        bool bouncedX = false;
        bool bouncedY = false;

        // The RIGHT and BOTTOM edges of the text
        double textRight = l.PosX + l.TextWidth;
        double textBottom = l.PosY + l.TextHeight;

        if (l.PosX <= MinX)
        {
            l.PosX = MinX;
            l.VelX = Math.Abs(l.VelX);
            bouncedX = true;
        }
        else if (textRight >= MaxX)
        {
            l.PosX = MaxX - l.TextWidth;
            l.VelX = -Math.Abs(l.VelX);
            bouncedX = true;
        }

        if (l.PosY <= MinY)
        {
            l.PosY = MinY;
            l.VelY = Math.Abs(l.VelY);
            bouncedY = true;
        }
        else if (textBottom >= MaxY)
        {
            l.PosY = MaxY - l.TextHeight;
            l.VelY = -Math.Abs(l.VelY);
            bouncedY = true;
        }

        bool bounced = bouncedX || bouncedY;
        // A true corner is both axes at once; a single-axis bounce within tolerance counts too.
        bool cornerHit = (bouncedX && bouncedY)
                         || (bounced && IsNearCorner(l.PosX, l.PosY, textRight, textBottom));

        if (cornerHit && settings.BouncingTextFxCornerBurst)
            l.BurstTimer = 0;

        bool textChanged = false;
        if (bounced)
        {
            l.Color = NextColor(l, settings);
            if (settings.BouncingTextFxSquashStretch)
            {
                l.SquashTimer = 0;
                l.SquashAxisX = bouncedX;
            }

            AwardBounceXp();

            // 10% chance to change text on bounce
            if (_random.NextDouble() < 0.1)
            {
                l.Text = SelectRandomText(settings);
                MeasureInto(l); // re-measure when the text changes
                textChanged = true;
            }
        }

        return new BounceStep(bounced, bouncedX, cornerHit, textChanged, colorBefore);
    }

    /// <summary>15 XP a bounce, at most one award per 2s and 150 XP a minute across every logo.
    /// Unseeded <see cref="CoreProgression"/> drops the award silently, which is the honest answer
    /// on a head with no XP service - but the rate limit still runs, so the counters cannot be
    /// primed on one head and cashed in on another.</summary>
    private void AwardBounceXp()
    {
        var now = DateTime.UtcNow;

        // Reset per-minute counter if a new minute has started
        if ((now - _bounceXpMinuteStart).TotalSeconds >= 60)
        {
            _bounceXpThisMinute = 0;
            _bounceXpMinuteStart = now;
        }

        if (now - _lastBounceXpTime >= BounceXpCooldown && _bounceXpThisMinute < MaxBounceXpPerMinute)
        {
            CoreProgression.AddXP(15, "BouncingText");
            _lastBounceXpTime = now;
            _bounceXpThisMinute += 15;
        }
    }

    /// <summary>Combine the enabled per-frame effects into one scale/rotation set for a logo.</summary>
    public (double sx, double sy, double angle) ComputeEffectTransform(Logo l, AppSettings settings)
    {
        double sx = 1.0, sy = 1.0, angle = 0.0;

        // Breathing: slow hypnotic scale pulse
        if (settings.BouncingTextFxBreathing)
        {
            double breath = 1.0 + 0.08 * Math.Sin(_fxTime * (Math.PI * 2 / 3.2) + l.Phase);
            sx *= breath;
            sy *= breath;
        }

        // Wobble: gentle autoreversing tilt
        if (settings.BouncingTextFxWobble)
            angle += 6.0 * Math.Sin(_fxTime * (Math.PI * 2 / 2.3) + l.Phase);

        // Continuous spin
        if (settings.BouncingTextFxSpin)
        {
            l.SpinAngle = (l.SpinAngle + 40.0 * _lastDt) % 360.0;
            angle += l.SpinAngle;
        }

        // Velocity tilt: lean into the direction of travel (mirrored so the text never flips
        // upside down when moving left)
        if (settings.BouncingTextFxVelocityTilt)
        {
            double target = Math.Atan2(l.VelY * Math.Sign(l.VelX == 0 ? 1 : l.VelX), Math.Abs(l.VelX))
                            * (180.0 / Math.PI) * 0.25;
            target = Math.Clamp(target, -12.0, 12.0);
            l.Tilt += (target - l.Tilt) * Math.Min(1.0, _lastDt * 6.0);
            angle += l.Tilt;
        }
        else
        {
            l.Tilt = 0;
        }

        // Squash & stretch: damped compress-then-overshoot on the axis that hit the wall
        if (l.SquashTimer >= 0)
        {
            l.SquashTimer += _lastDt;
            if (l.SquashTimer > 0.45)
            {
                l.SquashTimer = -1;
            }
            else
            {
                double t = l.SquashTimer;
                double deform = 0.35 * Math.Exp(-6.0 * t) * Math.Cos(24.0 * t);
                if (l.SquashAxisX) { sx *= 1.0 - deform; sy *= 1.0 + deform * 0.7; }
                else               { sy *= 1.0 - deform; sx *= 1.0 + deform * 0.7; }
            }
        }

        // Corner burst: quick celebratory pop on top of everything else
        if (l.BurstTimer >= 0)
        {
            l.BurstTimer += _lastDt;
            if (l.BurstTimer > 0.5)
            {
                l.BurstTimer = -1;
            }
            else
            {
                double pop = 1.0 + 0.4 * Math.Exp(-l.BurstTimer / 0.15);
                sx *= pop;
                sy *= pop;
            }
        }

        return (sx, sy, angle);
    }

    /// <summary>Re-roll every logo's speed to the current setting, keeping its direction.</summary>
    public void RefreshSpeed(AppSettings settings)
    {
        var speed = settings.BouncingTextSpeed / 10.0;
        foreach (var l in _logos)
        {
            var currentSpeed = Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY);
            var targetSpeed = (3.0 + _random.NextDouble() * 2.0) * 60.0 * speed; // DIP/sec
            var scale = targetSpeed / Math.Max(0.1, currentSpeed);
            l.VelX *= scale;
            l.VelY *= scale;
        }
    }

    /// <summary>True when the size setting moved: <see cref="FontSize"/> is updated and every logo
    /// re-measured, and the head must push the new size into its visuals.</summary>
    public bool RefreshFontSize(AppSettings settings)
    {
        var newFontSize = (int)(BaseFontSize * settings.BouncingTextSize / 100.0);
        if (newFontSize == FontSize) return false;
        FontSize = newFontSize;
        foreach (var l in _logos) MeasureInto(l);
        return true;
    }

    /// <summary>True when the family setting moved: every logo is re-measured (glyph widths change)
    /// and the head must push the new family into its visuals. No restart - the element is the same.</summary>
    public bool RefreshFont(AppSettings settings)
    {
        var newFont = settings.BouncingTextFont ?? "Segoe UI";
        if (string.Equals(newFont, Font, StringComparison.OrdinalIgnoreCase)) return false;
        Font = newFont;
        foreach (var l in _logos) MeasureInto(l);
        return true;
    }

    /// <summary>Switching to Fixed applies immediately (Random/Rainbow apply on the next bounce).</summary>
    public void ApplyFixedColor(AppSettings settings)
    {
        var fixedColor = GetFixedColor(settings);
        foreach (var l in _logos) l.Color = fixedColor;
    }

    private void MeasureInto(Logo logo)
    {
        if (Measure is { } measure)
        {
            try
            {
                var (w, h) = measure(logo.Text, FontSize);
                if (w > 0 && h > 0) { logo.TextWidth = w; logo.TextHeight = h; return; }
            }
            catch { /* fall through to the estimate, as the WPF measurement's own catch did */ }
        }
        logo.TextWidth = FontSize * logo.Text.Length * 0.6;
        logo.TextHeight = FontSize * 1.2;
    }

    /// <summary>Is the text within tolerance of any corner of the bounds?</summary>
    private bool IsNearCorner(double left, double top, double right, double bottom)
    {
        bool nearTopLeft     = left <= MinX + CornerTolerance && top <= MinY + CornerTolerance;
        bool nearTopRight    = right >= MaxX - CornerTolerance && top <= MinY + CornerTolerance;
        bool nearBottomLeft  = left <= MinX + CornerTolerance && bottom >= MaxY - CornerTolerance;
        bool nearBottomRight = right >= MaxX - CornerTolerance && bottom >= MaxY - CornerTolerance;

        return nearTopLeft || nearTopRight || nearBottomLeft || nearBottomRight;
    }

    /// <summary>Pick the next colour for a logo according to the colour-mode setting.</summary>
    private (byte R, byte G, byte B) NextColor(Logo logo, AppSettings settings)
    {
        switch (settings.BouncingTextColorMode)
        {
            case 1: // Fixed: the user's chosen colour, no re-roll
                return GetFixedColor(settings);
            case 2: // Rainbow cycle: walk the ordered hue wheel one step per bounce
                logo.HueIndex = (logo.HueIndex + 1) % RainbowWheel.Length;
                return RainbowWheel[logo.HueIndex];
            default:
                return GetRandomColor();
        }
    }

    /// <summary>The colour Fixed mode renders: the user's <c>#RRGGBB</c> pick, else hot pink.
    /// <see cref="CoreMods.TryParseHexColor"/> is the same six-hex parse with the same hot-pink
    /// fallback the WPF copy had, so no head keeps a third.</summary>
    public static (byte R, byte G, byte B) GetFixedColor(AppSettings settings)
    {
        CoreMods.TryParseHexColor(settings.BouncingTextFixedColor, out var rgb);
        return rgb;
    }

    private (byte R, byte G, byte B) GetRandomColor()
    {
        // Bright, vibrant colours
        var colors = new (byte, byte, byte)[]
        {
            (255, 105, 180), // Hot Pink
            (255, 20, 147),  // Deep Pink
            (138, 43, 226),  // Blue Violet
            (255, 0, 255),   // Magenta
            (0, 255, 255),   // Cyan
            (255, 255, 0),   // Yellow
            (0, 255, 0),     // Lime
            (255, 165, 0),   // Orange
            (255, 69, 0),    // Red Orange
            (50, 205, 50),   // Lime Green
        };
        return colors[_random.Next(colors.Length)];
    }

    /// <summary>Ordered 12-step hue wheel (full saturation/value) for the rainbow-cycle mode.</summary>
    private static (byte R, byte G, byte B)[] BuildRainbowWheel()
    {
        var wheel = new (byte, byte, byte)[12];
        for (int i = 0; i < wheel.Length; i++)
        {
            double h = i * 360.0 / wheel.Length;
            wheel[i] = HsvToRgb(h, 1.0, 1.0);
        }
        return wheel;
    }

    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = ((int)(h / 60.0) % 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
