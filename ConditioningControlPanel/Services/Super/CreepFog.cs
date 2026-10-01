using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super;

/// <summary>Creep speed: how long the fog takes to climb from nothing to the cap.</summary>
public enum CreepSpeed
{
    Slow,     // 15 minutes
    Normal,   // 10 minutes
    Fast,     // 5 minutes
}

/// <summary>One soft fog blob. Its home sits on one screen edge; the offset is the click push.</summary>
public sealed class CreepBlob
{
    public int Side;          // 0 top, 1 right, 2 bottom, 3 left
    public double U;          // 0..1 along the edge
    public double Depth;      // 0.15..1, how far in it reaches at full coverage
    public double Size;       // 0.22..0.52 of the short side, before the 2.6 spread
    public double Phase;      // drift phase, radians
    public double Ox, Oy;     // offset from home, px (the click push)
    public double Vx, Vy;     // push velocity, px/s
    public double Px, Py;     // last drawn centre, px (world space when the field is)
    public double DrawSize;   // last drawn diameter, px
}

/// <summary>A fog field for one monitor: 46 blobs around that monitor's edges.</summary>
public sealed class CreepField
{
    public double X, Y, W, H;
    public readonly CreepBlob[] Blobs;
    public double Short => Math.Min(W, H);

    public CreepField(double x, double y, double w, double h)
    {
        X = x; Y = y; W = w; H = h;
        Blobs = new CreepBlob[CreepFog.BlobCount];
        for (var i = 0; i < Blobs.Length; i++)
        {
            Blobs[i] = new CreepBlob
            {
                Side = i % 4,
                U = CreepFog.Seed(i * 3 + 1),
                Depth = 0.15 + CreepFog.Seed(i * 5 + 2) * 0.85,
                Size = 0.22 + CreepFog.Seed(i * 7 + 3) * 0.3,
                Phase = CreepFog.Seed(i) * Math.PI * 2,
            };
        }
    }
}

/// <summary>A click ring: where and how long ago.</summary>
public struct CreepRing
{
    public double X, Y, Age;
}

/// <summary>Coverage, clock and the live click rings. One per app; fields are per monitor.</summary>
public sealed class CreepState
{
    public double Coverage = CreepFog.StartCoverage;   // C, what is drawn
    public double Target = CreepFog.StartCoverage;     // Ct, where C is heading
    public double Time;                                // drift clock, s
    public readonly List<CreepRing> Rings = new(4);
}

/// <summary>
/// Super Creep: pink fog drifting in from the screen edges, a little more as time goes on; a click
/// anywhere makes it back off. Pure maths only (coverage ODE, drift, click push, rings); the
/// compositor layer draws what this computes. Constants come from <c>mkFog</c> in the mockup
/// (handoff-1001/super-effects-mockup.html), with the mockup's demo ramp (.11/s) replaced by the
/// real one (cap reached in 15/10/5 minutes).
/// </summary>
public static class CreepFog
{
    public const int BlobCount = 46;
    public const double StartCoverage = 0.04;
    public const double MaxCap = 0.9;            // the opacity cap can never go above this
    public const double MinCap = 0.2;
    public const double Floor = 0.02;            // a click never takes the fog below this
    public const double ClickDrop = 0.4;
    public const double FallRate = 6;            // C chases a lower Ct fast
    public const double RiseRate = 2;            // and a higher one slowly
    public const double PushStrength = 620;      // px/s at the click point
    public const double PushReach = 0.3;         // of the short side
    public const double PushDecay = 2;           // velocity bleeds off exp(-2 dt)
    public const double SpringBack = 0.9;        // offset returns exp(-0.9 dt)
    public const double RingSeconds = 0.7;
    public const double RingReach = 0.55;        // ring radius at the end, of the short side
    public const double DriftAmp = 0.05;         // of the short side
    public const double DriftX = 0.4, DriftY = 0.33, Breathe = 0.7;
    public const double EdgeOut = 0.05;          // blob homes sit this far OUTSIDE the edge
    public const double ReachIn = 0.75;          // full depth at C = 1, of the short side
    public const double Spread = 2.6;            // blob diameter = Size * short side * Spread
    public const double OffFadeSeconds = 0.12;   // Motion Off: a click recedes over 120 ms
    public const byte TintR = 255, TintG = 140, TintB = 205;

    /// <summary>
    /// The most any pixel of fog may cover, however many blobs stack there. Without it the 46
    /// overlapping blobs reach 99% at the cap and two thirds of a 1080p screen sits past 85%,
    /// which hides the panel and its panic controls. The pink filter itself stops at 50%; the fog
    /// is allowed thicker because it is patchy and a click clears it, but never opaque.
    /// </summary>
    public const double OpacityCeiling = 0.8;

    /// <summary>The fog is drawn at 1/4 resolution and stretched: it is all soft gradients, and a
    /// full-res pass is ~20 million blended pixels a frame on one 1080p monitor.</summary>
    public const int Downscale = 4;

    /// <summary>Radial alpha profile of one blob at normalised radius 0..1 (mockup sprite stops:
    /// .55 at the centre, .22 halfway, 0 at the rim).</summary>
    public static double SpriteAlpha(double r)
    {
        if (double.IsNaN(r) || r >= 1) return 0;
        if (r <= 0) return 0.55;
        return r < 0.5 ? 0.55 + (0.22 - 0.55) * (r / 0.5) : 0.22 * (1 - (r - 0.5) / 0.5);
    }

    /// <summary>
    /// Blob alpha inside the fog surface. The surface is drawn at <see cref="OpacityCeiling"/>, so
    /// the blobs are lifted by 1 / ceiling: a thin fog looks as it did in the mockup and only the
    /// thick stacks flatten out at the ceiling.
    /// </summary>
    public static double LayerAlpha(double coverage) => Math.Min(1, Alpha(coverage) / OpacityCeiling);

    /// <summary>Size of the fog surface for a monitor edge of <paramref name="px"/> device px.</summary>
    public static int SurfaceSize(int px) => Math.Max(1, (px + Downscale - 1) / Downscale);

    /// <summary>Minutes to climb from nothing to the 0.9 cap.</summary>
    public static double RampMinutes(CreepSpeed speed) => speed switch
    {
        CreepSpeed.Slow => 15,
        CreepSpeed.Fast => 5,
        _ => 10,
    };

    /// <summary>Coverage gained per second at this speed (0.9 over the ramp time).</summary>
    public static double RampRate(CreepSpeed speed) => MaxCap / (RampMinutes(speed) * 60.0);

    public static double ClampCap(double cap) =>
        double.IsNaN(cap) ? MaxCap : Math.Clamp(cap, MinCap, MaxCap);

    /// <summary>Fog alpha for a coverage, 0..1 (mockup: .3 + .75 C).</summary>
    public static double Alpha(double coverage) => Math.Clamp(0.3 + 0.75 * coverage, 0, 1);

    /// <summary>Motion scale: Full 1, Reduced half, Off none.</summary>
    public static double MotionScale(MotionLevel level) => level switch
    {
        MotionLevel.Off => 0,
        MotionLevel.Reduced => 0.5,
        _ => 1,
    };

    /// <summary>Deterministic 0..1 noise for blob layout (same field every run, every monitor).</summary>
    public static double Seed(int i)
    {
        var x = Math.Sin(i * 12.9898 + 78.233) * 43758.5453;
        return x - Math.Floor(x);
    }

    /// <summary>Back to a fresh fog: used on start and on every stop (panic included).</summary>
    public static void Reset(CreepState s, IEnumerable<CreepField>? fields = null)
    {
        s.Coverage = s.Target = StartCoverage;
        s.Time = 0;
        s.Rings.Clear();
        if (fields == null) return;
        foreach (var f in fields)
            foreach (var b in f.Blobs) { b.Ox = b.Oy = b.Vx = b.Vy = 0; }
    }

    /// <summary>
    /// Advance coverage and the rings by <paramref name="dt"/> seconds. Ct creeps up at the ramp
    /// rate to the cap; C eases toward Ct, fast down and slow up. Motion Off moves C linearly so a
    /// click's drop lands in 120 ms and nothing eases.
    /// </summary>
    public static void Step(CreepState s, double dt, CreepSpeed speed, double cap, MotionLevel level)
    {
        if (dt <= 0 || double.IsNaN(dt)) return;
        dt = Math.Min(dt, 0.25);   // a stalled tick must not jump the fog
        cap = ClampCap(cap);
        var scale = MotionScale(level);
        s.Time += dt * (scale == 0 ? 0 : scale);

        s.Target = Math.Min(cap, s.Target + dt * RampRate(speed));
        if (s.Target > cap) s.Target = cap;

        if (level == MotionLevel.Off)
        {
            var step = ClickDrop / OffFadeSeconds * dt;
            var d = s.Target - s.Coverage;
            s.Coverage = Math.Abs(d) <= step ? s.Target : s.Coverage + Math.Sign(d) * step;
        }
        else
        {
            var rate = s.Target < s.Coverage ? FallRate : RiseRate;
            s.Coverage += (s.Target - s.Coverage) * (1 - Math.Exp(-dt * rate));
        }

        for (var i = s.Rings.Count - 1; i >= 0; i--)
        {
            var r = s.Rings[i];
            r.Age += dt;
            if (r.Age >= RingSeconds) s.Rings.RemoveAt(i);
            else s.Rings[i] = r;
        }
    }

    /// <summary>
    /// A click anywhere: Ct drops by 0.4 (floor 0.02), every blob in every field is pushed away
    /// from the click (620 / (1 + d / (0.3 short side)) px/s, scaled by motion), and a ring starts
    /// unless motion is Off. Coordinates are in the same space as the fields.
    /// </summary>
    public static void Click(CreepState s, IEnumerable<CreepField> fields, double x, double y, MotionLevel level)
    {
        s.Target = Math.Max(Floor, s.Target - ClickDrop);
        var scale = MotionScale(level);
        if (scale <= 0) return;
        if (s.Rings.Count < 8) s.Rings.Add(new CreepRing { X = x, Y = y, Age = 0 });
        foreach (var f in fields)
        {
            var m = f.Short;
            if (m <= 0) continue;
            foreach (var b in f.Blobs)
            {
                var dx = b.Px - x; var dy = b.Py - y;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-6) { dx = 0; dy = -1; d = 1; }
                var push = PushStrength / (1 + d / (m * PushReach)) * scale;
                b.Vx += dx / d * push;
                b.Vy += dy / d * push;
            }
        }
    }

    /// <summary>
    /// Advance one field's blobs and work out where each draws: home on its edge, pushed in by
    /// coverage, plus drift and the click offset. Positions land in Px/Py, diameter in DrawSize.
    /// </summary>
    public static void StepField(CreepState s, CreepField f, double dt, MotionLevel level)
    {
        var m = f.Short;
        if (m <= 0) return;
        dt = Math.Clamp(double.IsNaN(dt) ? 0 : dt, 0, 0.25);
        var scale = MotionScale(level);
        var velKeep = Math.Exp(-dt * PushDecay);
        var offKeep = Math.Exp(-dt * SpringBack);
        var e = -m * EdgeOut;
        var t = s.Time;
        foreach (var b in f.Blobs)
        {
            if (scale <= 0) { b.Ox = b.Oy = b.Vx = b.Vy = 0; }
            else
            {
                b.Vx *= velKeep; b.Vy *= velKeep;
                b.Ox += b.Vx * dt; b.Oy += b.Vy * dt;
                b.Ox *= offKeep; b.Oy *= offKeep;
            }
            var dep = b.Depth * s.Coverage * m * ReachIn;
            double x, y;
            switch (b.Side)
            {
                case 0: x = b.U * f.W; y = e + dep; break;
                case 1: x = f.W - e - dep; y = b.U * f.H; break;
                case 2: x = b.U * f.W; y = f.H - e - dep; break;
                default: x = e + dep; y = b.U * f.H; break;
            }
            var amp = m * DriftAmp * scale;
            x += Math.Sin(t * DriftX + b.Phase) * amp + b.Ox;
            y += Math.Cos(t * DriftY + b.Phase) * amp + b.Oy;
            b.Px = f.X + x;
            b.Py = f.Y + y;
            b.DrawSize = b.Size * m * Spread * (1 + 0.15 * scale * Math.Sin(t * Breathe + b.Phase));
        }
    }

    /// <summary>Ring radius and alpha at its age (0.7 s, grows to 0.55 short side, fades out).</summary>
    public static (double radius, double alpha) RingAt(CreepRing r, double shortSide)
    {
        var u = Math.Clamp(r.Age / RingSeconds, 0, 1);
        return (u * shortSide * RingReach, 0.7 * (1 - u));
    }
}
