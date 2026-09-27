using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>
/// Per-item motion state for Flashes v2. Owned by a FlashLayer item and stepped by
/// <see cref="FlashMotion.Step"/> on the compositor tick. World px throughout. X/Y/W/H is the
/// media's CURRENT axis-aligned bounds (for a pendulum, the bounds of the rotated picture), which
/// is what gaze dwell, the click hit-test and the overlap check read.
/// </summary>
public sealed class FlashMotionState
{
    public FlashMotionStyle Style;

    /// <summary>The monitor the flash spawned on (world px). Drift bounces off these edges.</summary>
    public double BoundsX, BoundsY, BoundsW, BoundsH;

    /// <summary>Unrotated media size (the bookkeeping rect at spawn, glow padding included).</summary>
    public double MediaW, MediaH;

    /// <summary>Current axis-aligned bounds.</summary>
    public double X, Y, W, H;

    // Drift and Bounce: velocity in px/s.
    public double Vx, Vy;

    // Pendulum: pivot at the top centre of the monitor, rope down to the media centre,
    // angle = AmpRad * sin(Omega * Elapsed + Phase).
    public double PivotX, PivotY, Rope, AmpRad, Omega, Phase, AngleRad;
    public double Elapsed;

    /// <summary>
    /// Wave 2: non-null while a hand is on this flash, and for as long as the fling that follows
    /// lasts. It overrides <see cref="Style"/> for its whole life - see <see cref="FlashDrag"/>.
    /// </summary>
    public FlashDragState? Drag;
}

/// <summary>
/// The pure maths behind Flashes v2 (Drift and Bounce, Pendulum). No WPF, no Skia: the layer
/// calls <see cref="Create"/> once per spawn and <see cref="Step"/> once per tick, and the tests
/// drive the same two calls.
/// </summary>
public static class FlashMotion
{
    /// <summary>Drift: a flash crosses its monitor's width in this many seconds (uniform roll).</summary>
    public const double CrossSecondsMin = 6.0, CrossSecondsMax = 8.0;
    /// <summary>Drift heading, degrees off the horizontal: never purely horizontal or vertical.</summary>
    public const double DriftAngleMinDeg = 20.0, DriftAngleMaxDeg = 70.0;
    /// <summary>Pendulum amplitude, degrees either side of straight down.</summary>
    public const double PendulumAmpMinDeg = 12.0, PendulumAmpMaxDeg = 18.0;
    /// <summary>Pendulum full period in seconds.</summary>
    public const double PendulumPeriodMinSec = 2.5, PendulumPeriodMaxSec = 3.5;

    /// <summary>Full = as designed, Reduced = half speed and half amplitude, Off = no motion.</summary>
    public static double LevelScale(MotionLevel level) => level switch
    {
        MotionLevel.Full => 1.0,
        MotionLevel.Reduced => 0.5,
        _ => 0.0,
    };

    /// <summary>
    /// The style one flash actually plays. An unowned pick degrades to Still (a synced profile can
    /// carry a style this account never bought); Mix rolls among Still plus the OWNED styles;
    /// MotionLevel.Off wins over everything.
    /// </summary>
    public static FlashMotionStyle Resolve(FlashMotionStyle picked, bool ownsDriftBounce, bool ownsPendulum,
        MotionLevel level, Random rng)
    {
        if (level == MotionLevel.Off) return FlashMotionStyle.Still;
        switch (picked)
        {
            case FlashMotionStyle.DriftBounce:
                return ownsDriftBounce ? FlashMotionStyle.DriftBounce : FlashMotionStyle.Still;
            case FlashMotionStyle.Pendulum:
                return ownsPendulum ? FlashMotionStyle.Pendulum : FlashMotionStyle.Still;
            case FlashMotionStyle.Mix:
                var pool = new List<FlashMotionStyle>(3) { FlashMotionStyle.Still };
                if (ownsDriftBounce) pool.Add(FlashMotionStyle.DriftBounce);
                if (ownsPendulum) pool.Add(FlashMotionStyle.Pendulum);
                return pool[rng.Next(pool.Count)];
            default:
                return FlashMotionStyle.Still;
        }
    }

    /// <summary>
    /// Build the motion for one flash. <paramref name="x"/>..<paramref name="h"/> is the spawn rect,
    /// <paramref name="bx"/>..<paramref name="bh"/> the spawn monitor, both world px. A Still result
    /// (Still picked, or level Off) keeps the spawn rect and never moves.
    /// </summary>
    /// <param name="neighbours">
    /// The pendulums already hanging on this monitor, or null when there are none. A pendulum hangs
    /// away from them and picks its phase off the nearest one; without it every pendulum in a burst
    /// hung from the same nail and the swings stacked. Ignored by every other style.
    /// </param>
    public static FlashMotionState Create(FlashMotionStyle style, double x, double y, double w, double h,
        double bx, double by, double bw, double bh, MotionLevel level, Random rng,
        IReadOnlyList<PendulumNeighbour>? neighbours = null)
    {
        var s = new FlashMotionState
        {
            Style = FlashMotionStyle.Still,
            X = x, Y = y, W = w, H = h, MediaW = w, MediaH = h,
            BoundsX = bx, BoundsY = by, BoundsW = bw, BoundsH = bh,
        };
        var scale = LevelScale(level);
        if (scale <= 0 || bw <= 0 || bh <= 0) return s;

        if (style == FlashMotionStyle.DriftBounce)
        {
            s.Style = FlashMotionStyle.DriftBounce;
            var secs = Lerp(CrossSecondsMin, CrossSecondsMax, rng.NextDouble());
            var speed = bw / secs * scale;
            var ang = Lerp(DriftAngleMinDeg, DriftAngleMaxDeg, rng.NextDouble()) * Math.PI / 180.0;
            s.Vx = speed * Math.Cos(ang) * (rng.Next(2) == 0 ? 1 : -1);
            s.Vy = speed * Math.Sin(ang) * (rng.Next(2) == 0 ? 1 : -1);
        }
        else if (style == FlashMotionStyle.Pendulum)
        {
            s.Style = FlashMotionStyle.Pendulum;
            s.PivotY = by;
            s.AmpRad = Lerp(PendulumAmpMinDeg, PendulumAmpMaxDeg, rng.NextDouble()) * Math.PI / 180.0 * scale;
            s.Omega = 2.0 * Math.PI / Lerp(PendulumPeriodMinSec, PendulumPeriodMaxSec, rng.NextDouble());
            // The spawn Y decides the rope; clamp so the swinging picture stays on the monitor.
            // Measured against the CENTRE pivot, which is the roomiest one there is, so the band
            // below is the slack this rope leaves over - never slack it does not have.
            var (ropeMin, ropeMax) = RopeRange(bw, bh, w, h, s.AmpRad);
            var wanted = (y + h / 2.0) - s.PivotY;
            s.Rope = ropeMax >= ropeMin ? Math.Clamp(wanted, ropeMin, ropeMax) : ropeMin;
            // ...and the rope decides how far off centre this one may hang. A lone pendulum still
            // takes the top centre exactly as it always did; the second one on the monitor moves
            // as far away as the band allows, and if the picture is too wide for a band at all,
            // the phase is what keeps the two apart. See FlashPendulumRig.
            var span = FlashPendulumRig.PivotSpan(bw, w, h, s.Rope, s.AmpRad);
            s.PivotX = FlashPendulumRig.ChoosePivot(bx + bw / 2.0, span, neighbours, rng);
            s.Phase = neighbours == null || neighbours.Count == 0
                ? rng.NextDouble() * 2.0 * Math.PI
                : FlashPendulumRig.ChoosePhase(s.PivotX, w, neighbours, rng);
            s.AngleRad = s.AmpRad * Math.Sin(s.Phase);
            ApplyHang(s);
        }
        return s;
    }

    /// <summary>
    /// Advance by <paramref name="dt"/> seconds. Returns true when X/Y/W/H (or the angle) changed,
    /// which is exactly when the layer must go dirty; a Still item always returns false.
    /// </summary>
    public static bool Step(FlashMotionState s, double dt)
    {
        if (dt <= 0) return false;
        // Wave 2: a held or flying flash answers to the hand, not to its style. FlashDrag drops
        // the state once the flight settles, and the style below takes over again from there.
        if (s.Drag != null) return FlashDrag.Step(s, dt);
        switch (s.Style)
        {
            case FlashMotionStyle.DriftBounce:
            {
                var (nx, vx, _) = Reflect(s.X + s.Vx * dt, s.W, s.BoundsX, s.BoundsW, s.Vx);
                var (ny, vy, _) = Reflect(s.Y + s.Vy * dt, s.H, s.BoundsY, s.BoundsH, s.Vy);
                bool moved = nx != s.X || ny != s.Y;
                s.X = nx; s.Y = ny; s.Vx = vx; s.Vy = vy;
                return moved;
            }
            case FlashMotionStyle.Pendulum:
            {
                if (s.AmpRad <= 0) return false;
                s.Elapsed += dt;
                s.AngleRad = s.AmpRad * Math.Sin(s.Omega * s.Elapsed + s.Phase);
                ApplyHang(s);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Axis-aligned bounds of a <paramref name="mw"/> x <paramref name="mh"/> picture hanging
    /// <paramref name="rope"/> px below the pivot, the whole rig rotated by <paramref name="angle"/>
    /// about the pivot. Positive angle is clockwise in screen space, matching SKCanvas.Rotate, so
    /// the media centre lands at (pivotX - rope sin a, pivotY + rope cos a).
    /// </summary>
    public static (double X, double Y, double W, double H) HangingBounds(double pivotX, double pivotY,
        double rope, double angle, double mw, double mh)
    {
        var sin = Math.Sin(angle);
        var cos = Math.Cos(angle);
        var cx = pivotX - rope * sin;
        var cy = pivotY + rope * cos;
        var hw = (mw * Math.Abs(cos) + mh * Math.Abs(sin)) / 2.0;
        var hh = (mw * Math.Abs(sin) + mh * Math.Abs(cos)) / 2.0;
        return (cx - hw, cy - hh, hw * 2.0, hh * 2.0);
    }

    /// <summary>
    /// Legal rope lengths for a picture swinging up to <paramref name="ampRad"/>: long enough that
    /// the rotated picture hangs fully below the pivot, short enough that it clears the bottom and
    /// both sides at the extreme angle. Max may come out below min for a picture too big to swing.
    /// </summary>
    public static (double Min, double Max) RopeRange(double bw, double bh, double mw, double mh, double ampRad)
    {
        var sin = Math.Sin(ampRad);
        var cos = Math.Cos(ampRad);
        var hw = (mw * cos + mh * sin) / 2.0;
        var hh = (mw * sin + mh * cos) / 2.0;
        var min = cos > 1e-6 ? hh / cos : hh;
        var max = cos > 1e-6 ? (bh - hh) / cos : bh - hh;
        if (sin > 1e-6) max = Math.Min(max, (bw / 2.0 - hw) / sin);
        return (min, max);
    }

    private static void ApplyHang(FlashMotionState s)
    {
        (s.X, s.Y, s.W, s.H) = HangingBounds(s.PivotX, s.PivotY, s.Rope, s.AngleRad, s.MediaW, s.MediaH);
    }

    /// <summary>
    /// One axis of the bounce: mirror the overshoot back inside and flip the velocity. Bounced
    /// says a wall was actually hit, which a drift ignores (it is perfectly elastic) and a fling
    /// reads to take its 25% off the speed.
    /// </summary>
    internal static (double Pos, double V, bool Bounced) Reflect(double pos, double size, double min, double extent, double v)
    {
        var max = min + extent - size;
        if (max <= min) return (min, 0, false);        // wider than the monitor: park it
        var bounced = false;
        if (pos < min) { pos = min + (min - pos); v = Math.Abs(v); bounced = true; }
        else if (pos > max) { pos = max - (pos - max); v = -Math.Abs(v); bounced = true; }
        return (Math.Clamp(pos, min, max), v, bounced);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
