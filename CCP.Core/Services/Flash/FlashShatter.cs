using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>
/// One piece of a shattered flash. The CUT is normalised (0..1 across the media box) so the pure
/// maths never has to know the picture's pixel size or the monitor's dpi; the MOTION is world px,
/// because gravity and "off the bottom of the screen" only mean anything in px.
/// </summary>
public sealed class FlashShard
{
    /// <summary>The sub-rect this shard was cut from, as fractions of the media box.</summary>
    public double U0, V0, U1, V1;

    /// <summary>How far the shard has travelled from where it was cut, world px.</summary>
    public double Dx, Dy;

    /// <summary>Velocity, world px per second.</summary>
    public double Vx, Vy;

    /// <summary>Tumble: current angle about the shard's own centre, and its rate (rad, rad/s).</summary>
    public double AngleRad, AngularVelRad;

    /// <summary>Fade, 1 at the break and 0 when the shard is gone.</summary>
    public double Alpha = 1.0;
}

/// <summary>
/// A flash mid-break. Hung off the compositor's flash item for the ~700 ms the shards take to
/// fall, then dropped with the item. The rect and the bounds are world px, the same space the
/// item's own X/Y/W/H lives in.
/// </summary>
public sealed class FlashShatterState
{
    /// <summary>The pieces, in reading order across the grid. Empty at MotionLevel.Off.</summary>
    public FlashShard[] Shards = Array.Empty<FlashShard>();

    /// <summary>
    /// Where the DRAWN picture was when it broke (world px). The shards fall from here. For a
    /// pendulum this is the media rect in pivot space, not the axis-aligned box the click and the
    /// gaze read, because the pieces have to line up with the picture the viewer was looking at.
    /// </summary>
    public double RectX, RectY, RectW, RectH;

    /// <summary>
    /// The tilt the picture was wearing at the break, radians, about <see cref="PivotX"/> /
    /// <see cref="PivotY"/> (world px). Zero for every style but the pendulum. It is FROZEN: the
    /// swing stops dead at the dismiss and the pieces fall away along the axis the picture had,
    /// so nothing snaps level under the cursor.
    /// </summary>
    public double FrozenAngleRad;
    public double PivotX, PivotY;

    /// <summary>The monitor the break happened on (world px): a shard past these edges is gone.</summary>
    public double BoundsX, BoundsY, BoundsW, BoundsH;

    /// <summary>How long the whole break lasts, seconds (halved at Reduced).</summary>
    public double DurationSec;

    /// <summary>Downward pull, world px per second squared.</summary>
    public double GravityPxPerSec2;

    /// <summary>Time since the break, seconds.</summary>
    public double ElapsedSec;

    /// <summary>True once the layer may drop the item: every shard is faded out or off screen.</summary>
    public bool Done;
}

/// <summary>
/// Flashes v2 wave 2: what a flash GIF does when a hand dismisses it. Instead of the plain cut the
/// picture breaks along a jittered grid and the pieces fall, tumble and fade.
///
/// No WPF, no Skia, no App - exactly like <see cref="FlashDrag"/>. FlashService mints one state at
/// the dismiss, the compositor's existing flash tick hands it delta time through
/// <see cref="Step"/>, and the renderer reads the shard transforms back out. The tests drive the
/// same two calls.
///
/// Motion level: Full breaks into 6 or 9 pieces over the full duration, Reduced into 4 over half
/// of it, and Off returns no shards at all, which is the signal to keep the plain cut.
/// </summary>
public static class FlashShatter
{
    /// <summary>How long a full-motion break lasts, seconds.</summary>
    public const double DurationSec = 0.70;

    /// <summary>Share of the duration a Reduced break gets.</summary>
    public const double ReducedDurationScale = 0.5;

    /// <summary>Downward pull on every shard, px per second squared.</summary>
    public const double GravityPxPerSec2 = 1400.0;

    /// <summary>Columns the picture is cut into. Full rolls 2 or 3 rows against this, Reduced uses its own grid.</summary>
    public const int FullColumns = 3;
    public const int FullRowsMin = 2, FullRowsMax = 3;

    /// <summary>Reduced motion breaks into a flat four.</summary>
    public const int ReducedColumns = 2, ReducedRows = 2;

    /// <summary>Each interior cut line wanders up to this share of a cell off the even split.</summary>
    public const double CutJitterFraction = 0.12;

    /// <summary>Outward kick away from the middle of the picture, px/s.</summary>
    public const double OutwardSpeedMin = 90.0, OutwardSpeedMax = 220.0;

    /// <summary>Extra downward shove on top of the outward kick, px/s (the picture drops, not explodes).</summary>
    public const double DownKickMin = 30.0, DownKickMax = 140.0;

    /// <summary>Tumble rate, rad/s either way. A shard turns a little; it does not spin like a coin.</summary>
    public const double SpinMaxRadPerSec = 2.4;

    /// <summary>The shards hold full opacity for this share of the duration, then fade out linearly.</summary>
    public const double HoldFraction = 0.30;

    /// <summary>How many pieces a break at this level makes. Zero means "keep the plain cut".</summary>
    public static int ShardCount(MotionLevel level, int fullRows) => level switch
    {
        MotionLevel.Full => FullColumns * Math.Clamp(fullRows, FullRowsMin, FullRowsMax),
        MotionLevel.Reduced => ReducedColumns * ReducedRows,
        _ => 0,
    };

    /// <summary>
    /// Break the picture at <paramref name="x"/>..<paramref name="h"/> (world px) on the monitor
    /// at <paramref name="bx"/>..<paramref name="bh"/>. At <see cref="MotionLevel.Off"/> the state
    /// comes back with no shards and <see cref="FlashShatterState.Done"/> already true, so the
    /// caller falls through to the plain cut without a second branch.
    /// </summary>
    public static FlashShatterState Create(double x, double y, double w, double h,
        double bx, double by, double bw, double bh, MotionLevel level, Random rng, bool ninePieces = false)
    {
        var s = new FlashShatterState
        {
            RectX = x, RectY = y, RectW = w, RectH = h,
            BoundsX = bx, BoundsY = by, BoundsW = bw, BoundsH = bh,
            GravityPxPerSec2 = GravityPxPerSec2,
        };

        if (level == MotionLevel.Off || w <= 0 || h <= 0)
        {
            s.Done = true;
            return s;
        }

        var reduced = level == MotionLevel.Reduced;
        s.DurationSec = reduced ? DurationSec * ReducedDurationScale : DurationSec;

        var cols = reduced ? ReducedColumns : FullColumns;
        var rows = reduced ? ReducedRows : ninePieces ? 3 : rng.Next(FullRowsMin, FullRowsMax + 1);

        // The cut lines: an even split with the interior ones nudged off it, so two breaks of the
        // same picture never produce the same pieces. The outer edges stay put - a shard that
        // started inside the frame is the whole point.
        var vCuts = CutLines(cols, rng);
        var hCuts = CutLines(rows, rng);

        var shards = new FlashShard[cols * rows];
        var n = 0;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var u0 = vCuts[c];
                var u1 = vCuts[c + 1];
                var v0 = hCuts[r];
                var v1 = hCuts[r + 1];

                // Push away from the middle of the picture, so the break opens outwards rather
                // than every piece sliding the same way. A shard sitting dead centre still gets a
                // direction (the fallback), it just has nowhere in particular to be.
                var ox = (u0 + u1) / 2.0 - 0.5;
                var oy = (v0 + v1) / 2.0 - 0.5;
                var len = Math.Sqrt(ox * ox + oy * oy);
                if (len < 1e-6) { ox = rng.NextDouble() - 0.5; oy = -0.25; len = Math.Sqrt(ox * ox + oy * oy); }
                if (len < 1e-6) { ox = 1; oy = 0; len = 1; }
                ox /= len;
                oy /= len;

                var speed = Lerp(OutwardSpeedMin, OutwardSpeedMax, rng.NextDouble());
                shards[n++] = new FlashShard
                {
                    U0 = u0, V0 = v0, U1 = u1, V1 = v1,
                    Vx = ox * speed,
                    Vy = oy * speed + Lerp(DownKickMin, DownKickMax, rng.NextDouble()),
                    AngularVelRad = (rng.NextDouble() * 2.0 - 1.0) * SpinMaxRadPerSec,
                    Alpha = 1.0,
                };
            }
        }
        s.Shards = shards;
        return s;
    }

    /// <summary>
    /// Take the break over the picture as it is actually DRAWN. Everything but a pendulum draws
    /// square in its own rect, so the plain overload is the whole story there; a pendulum hangs
    /// its media below a pivot and the whole rig is tilted, so the shards have to be cut from
    /// that rect at that angle or the picture jumps bigger and snaps level the moment it breaks.
    /// The angle is copied here and never stepped again: the swing is over.
    /// </summary>
    public static void TakeOverDrawnRect(FlashShatterState s, double x, double y, double w, double h)
    {
        s.RectX = x; s.RectY = y; s.RectW = w; s.RectH = h;
        s.FrozenAngleRad = 0;
        s.PivotX = 0; s.PivotY = 0;
    }

    /// <summary>
    /// The pendulum shape of <see cref="TakeOverDrawnRect"/>: the media hangs <paramref name="rope"/>
    /// below the pivot and the rig is tilted by <paramref name="angleRad"/> about it, exactly as
    /// FlashMotion.HangingBounds and the compositor's pendulum draw have it.
    /// </summary>
    public static void TakeOverHangingRect(FlashShatterState s, double pivotX, double pivotY,
        double rope, double angleRad, double mediaW, double mediaH)
    {
        s.RectX = pivotX - mediaW / 2.0;
        s.RectY = pivotY + rope - mediaH / 2.0;
        s.RectW = mediaW;
        s.RectH = mediaH;
        s.FrozenAngleRad = angleRad;
        s.PivotX = pivotX;
        s.PivotY = pivotY;
    }

    /// <summary>
    /// Advance the break by <paramref name="dt"/> seconds. True when anything moved, which is
    /// exactly when the compositor layer must go dirty. Sets <see cref="FlashShatterState.Done"/>
    /// once the shards have faded to nothing or all left the monitor, whichever comes first; a
    /// done state never moves again.
    /// </summary>
    public static bool Step(FlashShatterState s, double dt)
    {
        if (s.Done || dt <= 0 || s.Shards.Length == 0) return false;

        s.ElapsedSec += dt;
        var alpha = AlphaAt(s.ElapsedSec, s.DurationSec);
        var allGone = true;

        foreach (var shard in s.Shards)
        {
            shard.Vy += s.GravityPxPerSec2 * dt;
            shard.Dx += shard.Vx * dt;
            shard.Dy += shard.Vy * dt;
            shard.AngleRad += shard.AngularVelRad * dt;
            shard.Alpha = alpha;
            if (!OffScreen(s, shard)) allGone = false;
        }

        if (alpha <= 0 || allGone) s.Done = true;
        return true;
    }

    /// <summary>
    /// Fade curve: hold at full for the first <see cref="HoldFraction"/> of the break so the eye
    /// registers the pieces, then straight down to nothing at the end.
    /// </summary>
    public static double AlphaAt(double elapsedSec, double durationSec)
    {
        if (durationSec <= 0) return 0.0;
        var t = elapsedSec / durationSec;
        if (t <= HoldFraction) return 1.0;
        if (t >= 1.0) return 0.0;
        return 1.0 - (t - HoldFraction) / (1.0 - HoldFraction);
    }

    /// <summary>Has this shard's whole box left the monitor the break happened on?</summary>
    public static bool OffScreen(FlashShatterState s, FlashShard shard)
    {
        if (s.BoundsW <= 0 || s.BoundsH <= 0) return false;
        var left = s.RectX + shard.U0 * s.RectW + shard.Dx;
        var right = s.RectX + shard.U1 * s.RectW + shard.Dx;
        var top = s.RectY + shard.V0 * s.RectH + shard.Dy;
        var bottom = s.RectY + shard.V1 * s.RectH + shard.Dy;
        return top >= s.BoundsY + s.BoundsH
            || bottom <= s.BoundsY
            || left >= s.BoundsX + s.BoundsW
            || right <= s.BoundsX;
    }

    /// <summary>
    /// cols + 1 cut positions from 0 to 1: an even split with every interior line jittered inside
    /// its own cell, so the lines keep their order and no shard can come out inside-out.
    /// </summary>
    private static double[] CutLines(int cells, Random rng)
    {
        var cuts = new double[cells + 1];
        var step = 1.0 / cells;
        cuts[0] = 0.0;
        cuts[cells] = 1.0;
        for (var i = 1; i < cells; i++)
        {
            var jitter = (rng.NextDouble() * 2.0 - 1.0) * CutJitterFraction * step;
            cuts[i] = i * step + jitter;
        }
        return cuts;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
