using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>
/// One triangle of a Flicker Deck break. The cut is normalised (0..1 across the picture box);
/// the motion is world px relative to where the shard's centroid sat in the picture.
/// </summary>
public sealed class FlickerShard
{
    /// <summary>Triangle corners, normalised over the picture box.</summary>
    public double U0, V0, U1, V1, U2, V2;
    /// <summary>Centroid offset from the picture centre at the break, px.</summary>
    public double Cx, Cy;
    public double X, Y, Vx, Vy;
    public double AngleRad, SpinRadPerSec;
}

/// <summary>One spark of the break. White or pink, damped like the mockup's particle system.</summary>
public sealed class FlickerSpark
{
    public double X, Y, Vx, Vy, LifeSec, T;
    public bool Pink;
    /// <summary>Juice: a dust mote from a crack, drawn dim and small, not additive. Falls by <see cref="Gravity"/>.</summary>
    public bool Mote;
    public double Gravity;
    public double Alpha => LifeSec <= 0 ? 0 : Math.Max(0, 1 - T / LifeSec);
}

/// <summary>A Flicker Deck flash mid-break, hung off the compositor item until it is done.</summary>
public sealed class FlickerShatterState
{
    public FlickerShard[] Shards = Array.Empty<FlickerShard>();
    public FlickerSpark[] Sparks = Array.Empty<FlickerSpark>();
    /// <summary>The picture box at the break, world px (centre and size) and its tilt then.</summary>
    public double CenterX, CenterY, W, H, Rot0;
    public double ElapsedSec;
    public bool Done;
}

/// <summary>
/// Super: Flicker Deck's own break, the final click's dismissal. NOT the Back Room prize Shatter
/// (<see cref="FlashShatter"/>): about forty TRIANGLES cut from a 5x4 jittered grid, thrown out from
/// the middle and pulled down, with a burst of white and pink sparks. Pure maths, the layer draws.
/// Off motion makes nothing (the plain cut); Reduced halves every speed and spin.
/// </summary>
public static class FlickerShatter
{
    public const int Cols = 5, Rows = 4;
    public const int ShardCount = Cols * Rows * 2;
    public const double JitterX = 0.12, JitterY = 0.14;
    public const double OutwardScale = 3.2, DownScale = 2.0;
    public const double SideRandomPx = 90.0, UpKickPx = 90.0, UpRandomPx = 150.0;
    public const double SpinRandom = 9.0;            // (r - .5) * 9 = +-4.5 rad/s
    public const double GravityPxPerSec2 = 520.0;
    public const double FadeStartSec = 0.5, FadeSec = 0.9, LifeSec = 1.4;
    public const int WhiteSparks = 26, PinkSparks = 18;
    public const double WhiteSparkSpeed = 260.0, WhiteSparkLife = 0.8;
    public const double PinkSparkSpeed = 200.0, PinkSparkLife = 0.9;

    /// <summary>Break the picture box centred at (cx, cy), w x h world px, tilted rot0.</summary>
    public static FlickerShatterState Create(double cx, double cy, double w, double h, double rot0,
        MotionLevel level, Random rng)
    {
        var s = new FlickerShatterState { CenterX = cx, CenterY = cy, W = w, H = h, Rot0 = rot0 };
        if (level == MotionLevel.Off || w <= 0 || h <= 0)
        {
            s.Done = true;
            return s;
        }
        var speed = level == MotionLevel.Reduced ? 0.5 : 1.0;

        // The grid: outer points stay on the edge, interior ones wander.
        var pu = new double[Rows + 1, Cols + 1];
        var pv = new double[Rows + 1, Cols + 1];
        for (var r = 0; r <= Rows; r++)
            for (var q = 0; q <= Cols; q++)
            {
                var jx = q > 0 && q < Cols ? (rng.NextDouble() - 0.5) * JitterX : 0;
                var jy = r > 0 && r < Rows ? (rng.NextDouble() - 0.5) * JitterY : 0;
                pu[r, q] = (double)q / Cols + jx;
                pv[r, q] = (double)r / Rows + jy;
            }

        var shards = new FlickerShard[ShardCount];
        var n = 0;
        for (var r = 0; r < Rows; r++)
            for (var q = 0; q < Cols; q++)
            {
                // a b / d e: two triangles per cell, (a b d) and (b e d).
                shards[n++] = Tri(pu[r, q], pv[r, q], pu[r, q + 1], pv[r, q + 1], pu[r + 1, q], pv[r + 1, q], w, h, speed, rng);
                shards[n++] = Tri(pu[r, q + 1], pv[r, q + 1], pu[r + 1, q + 1], pv[r + 1, q + 1], pu[r + 1, q], pv[r + 1, q], w, h, speed, rng);
            }
        s.Shards = shards;

        var sparks = new FlickerSpark[WhiteSparks + PinkSparks];
        for (var i = 0; i < sparks.Length; i++)
        {
            var pink = i >= WhiteSparks;
            var a = rng.NextDouble() * Math.PI * 2;
            var sp = (pink ? PinkSparkSpeed : WhiteSparkSpeed) * (0.3 + rng.NextDouble() * 0.7) * speed;
            sparks[i] = new FlickerSpark
            {
                Pink = pink,
                Vx = Math.Cos(a) * sp,
                Vy = Math.Sin(a) * sp,
                LifeSec = (pink ? PinkSparkLife : WhiteSparkLife) * (0.6 + rng.NextDouble() * 0.6),
            };
        }
        s.Sparks = sparks;
        return s;
    }

    /// <summary>Advance the break. True while anything is still on screen.</summary>
    public static bool Step(FlickerShatterState s, double dt)
    {
        if (s.Done || dt <= 0) return false;
        s.ElapsedSec += dt;
        foreach (var sh in s.Shards)
        {
            sh.X += sh.Vx * dt;
            sh.Vy += GravityPxPerSec2 * dt;
            sh.Y += sh.Vy * dt;
            sh.AngleRad += sh.SpinRadPerSec * dt;
        }
        StepSparks(s.Sparks, dt);
        if (s.ElapsedSec >= LifeSec) s.Done = true;
        return true;
    }

    /// <summary>The small white puff at a flip's edge as the new picture lands (mockup: 10 sparks, 120 px/s, 0.5 s).</summary>
    public const int SwapSparkCount = 10;
    public const double SwapSparkSpeed = 120.0, SwapSparkLife = 0.5;

    /// <summary>
    /// The puff for one flip. Empty under Off motion; Reduced halves the speed. One small array
    /// per flip, never per frame; the layer steps it with <see cref="StepSparks"/>.
    /// </summary>
    public static FlickerSpark[] SwapSparks(MotionLevel level, Random rng)
    {
        if (level == MotionLevel.Off) return Array.Empty<FlickerSpark>();
        var speed = level == MotionLevel.Reduced ? 0.5 : 1.0;
        var sparks = new FlickerSpark[SwapSparkCount];
        for (var i = 0; i < sparks.Length; i++)
        {
            var a = rng.NextDouble() * Math.PI * 2;
            var sp = SwapSparkSpeed * (0.3 + rng.NextDouble() * 0.7) * speed;
            sparks[i] = new FlickerSpark
            {
                Vx = Math.Cos(a) * sp,
                Vy = Math.Sin(a) * sp,
                LifeSec = SwapSparkLife * (0.6 + rng.NextDouble() * 0.6),
            };
        }
        return sparks;
    }

    /// <summary>Crack dust: slow, mostly downward, pulled by gravity, gone inside a second.</summary>
    public const double MoteSpeed = 45.0, MoteGravity = 160.0, MoteLife = 0.8;

    /// <summary>
    /// The swap puff plus <paramref name="motes"/> dust motes shed at (<paramref name="ox"/>,
    /// <paramref name="oy"/>) px from the card centre, the crack's impact point. One array per
    /// flip, never per frame. Off makes nothing; Reduced halves the speed (the count is the
    /// caller's, see <see cref="FlickerDeck.CrackMotes"/>).
    /// </summary>
    public static FlickerSpark[] SwapSparks(MotionLevel level, Random rng, int motes, double ox, double oy)
    {
        var puff = SwapSparks(level, rng);
        if (level == MotionLevel.Off || motes <= 0) return puff;
        var speed = level == MotionLevel.Reduced ? 0.5 : 1.0;
        var all = new FlickerSpark[puff.Length + motes];
        Array.Copy(puff, all, puff.Length);
        for (var i = 0; i < motes; i++)
        {
            // A lower half-fan: the dust falls out of the crack, it is not thrown at the viewer.
            var a = Math.PI * (0.1 + rng.NextDouble() * 0.8);
            var sp = MoteSpeed * (0.4 + rng.NextDouble() * 0.6) * speed;
            all[puff.Length + i] = new FlickerSpark
            {
                Mote = true,
                X = ox + (rng.NextDouble() - 0.5) * 6,
                Y = oy + (rng.NextDouble() - 0.5) * 6,
                Vx = Math.Cos(a) * sp,
                Vy = Math.Sin(a) * sp * 0.5,
                Gravity = MoteGravity * speed,
                LifeSec = MoteLife * (0.7 + rng.NextDouble() * 0.4),
            };
        }
        return all;
    }

    /// <summary>Advance a spark set (damped like the mockup's particles). True while any is still visible.</summary>
    public static bool StepSparks(FlickerSpark[] sparks, double dt)
    {
        if (dt <= 0) return false;
        var damp = Math.Max(0, 1 - dt * 2);
        var alive = false;
        foreach (var sp in sparks)
        {
            sp.T += dt;
            sp.Vy += sp.Gravity * dt;
            sp.X += sp.Vx * dt;
            sp.Y += sp.Vy * dt;
            sp.Vx *= damp;
            sp.Vy *= damp;
            if (sp.T < sp.LifeSec) alive = true;
        }
        return alive;
    }

    /// <summary>Shards hold for half a second, then fade over the next 0.9 s, eased (smoothstep) so they neither start nor end on a hard edge.</summary>
    public static double ShardAlpha(double elapsedSec)
    {
        var u = Math.Clamp(1 - (elapsedSec - FadeStartSec) / FadeSec, 0, 1);
        return u * u * (3 - 2 * u);
    }

    private static FlickerShard Tri(double u0, double v0, double u1, double v1, double u2, double v2,
        double w, double h, double speed, Random rng)
    {
        var cx = ((u0 + u1 + u2) / 3 - 0.5) * w;
        var cy = ((v0 + v1 + v2) / 3 - 0.5) * h;
        return new FlickerShard
        {
            U0 = u0, V0 = v0, U1 = u1, V1 = v1, U2 = u2, V2 = v2,
            Cx = cx, Cy = cy,
            Vx = (cx * OutwardScale + (rng.NextDouble() - 0.5) * SideRandomPx) * speed,
            Vy = (cy * DownScale - UpKickPx - rng.NextDouble() * UpRandomPx) * speed,
            SpinRadPerSec = (rng.NextDouble() - 0.5) * SpinRandom * speed,
        };
    }
}
