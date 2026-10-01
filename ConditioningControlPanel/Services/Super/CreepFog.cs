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

/// <summary>One tendril reaching in from an edge: a stretched blob that sways on its own phase.</summary>
public sealed class CreepWisp
{
    public int Side;          // 0 top, 1 right, 2 bottom, 3 left
    public double U;          // 0..1 along the edge
    public double Len;        // 0.5..1, how far it reaches relative to the fog front
    public double Phase;      // sway phase, radians
    public double Px, Py;     // drawn centre, px
    public double Length, Width, Angle;   // drawn ellipse, px and radians
}

/// <summary>A fog field for one monitor: 46 blobs around that monitor's edges.</summary>
public sealed class CreepField
{
    public double X, Y, W, H;
    public readonly CreepBlob[] Blobs;
    public readonly CreepWisp[] Wisps;
    public double Short => Math.Min(W, H);

    public CreepField(double x, double y, double w, double h)
    {
        X = x; Y = y; W = w; H = h;
        Wisps = new CreepWisp[CreepFog.WispCount];
        for (var i = 0; i < Wisps.Length; i++)
        {
            Wisps[i] = new CreepWisp
            {
                Side = (i + 2) % 4,
                U = 0.08 + CreepFog.Seed(i * 11 + 101) * 0.84,
                Len = 0.5 + CreepFog.Seed(i * 13 + 103) * 0.5,
                Phase = CreepFog.Seed(i * 17 + 107) * Math.PI * 2,
            };
        }
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

    // Presence (juice): how far the fog has arrived or retreated. A bare state is settled, so
    // the maths reads as it always did; the layer calls CreepFog.Arrive on show.
    public double ShownAge = CreepFog.SettledAge;      // real seconds since the fog arrived
    public double RetreatAge = -1;                     // real seconds into the retreat, -1 none
    public double AlphaMul = 1;                        // fades the whole fog in and out
    public double ReachMul = 1;                        // slides the front in from and back to the edges
    public double Vignette;                            // the arrival pulse at the edges, 0..1
    public bool Pulsed;                                // a come-back from a retreat skips the pulse
    public bool Retreating => RetreatAge >= 0;
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

    // ---- Juice: arrival, front surges, wisps, motes, retreat. Feel only; the ramp, the cap,
    // the click and the ceiling above are untouched. ----
    public const double SettledAge = 1e6;
    public const double ArriveSeconds = 1.1;      // front slides in from the edges
    public const double ArriveKick = 0.2;         // shapes a ~6% overshoot before it settles
    public const double RetreatSeconds = 0.65;    // switch off: gather, then recede to the edges
    public const double RetreatGather = 0.04;     // the front swells 4% before it pulls back
    public const double VignettePeak = 0.22, VignettePeakAt = 0.35, VignetteSeconds = 1.6;
    public const double SurgePeriod = 9, SurgeRise = 0.15, SurgeAmp = 0.1;   // of the reach
    public const double FrontBreathAmp = 0.04, FrontBreathe = 0.22;           // rad/s
    public const int WispCount = 14;
    public const double WispSway = 0.45, WispSwayAngle = 0.22, WispWidth = 0.07, WispAlpha = 0.7;
    public const double MoteLife = 3, BurstLife = 1.1;
    public const double MoteTwinkle = 2.4;        // rad/s, 0.38 Hz: far under the 3 Hz photosafe line
    public const int BurstMotes = 10;
    public const double MoteDrag = 2.2;           // burst motes slow down exp(-2.2 dt)

    public static double EaseOutCubic(double x) { x = Math.Clamp(x, 0, 1); return 1 - (1 - x) * (1 - x) * (1 - x); }
    public static double Smoothstep(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    /// <summary>Arrival: (alpha, reach) multipliers at <paramref name="age"/> s after show. The fog
    /// fades in a little ahead of the front, and the front lands with a small overshoot. Reduced
    /// travels half as far at half speed; Off is a plain 120 ms fade.</summary>
    public static (double alpha, double reach) ArriveEnvelope(double age, MotionLevel level)
    {
        if (double.IsNaN(age)) age = SettledAge;
        if (level == MotionLevel.Off) return (Math.Clamp(age / OffFadeSeconds, 0, 1), 1);
        var scale = MotionScale(level);
        var d = ArriveSeconds / scale;
        var x = Math.Clamp(age / d, 0, 1);
        var alpha = EaseOutCubic(age / (d * 0.6));
        var travel = EaseOutCubic(x) + ArriveKick * Math.Sin(Math.PI * x) * x * x;
        return (alpha, 1 - scale + scale * travel);
    }

    /// <summary>The one edge pulse when the fog first arrives: up in 350 ms, gone by 1.6 s. One
    /// cycle, so nothing flickers. Reduced is half as strong, Off has none.</summary>
    public static double VignetteAt(double age, MotionLevel level)
    {
        var scale = MotionScale(level);
        if (scale <= 0 || double.IsNaN(age) || age < 0 || age >= VignetteSeconds) return 0;
        var peak = VignettePeak * scale;
        if (age < VignettePeakAt) { var u = age / VignettePeakAt; return peak * (1 - (1 - u) * (1 - u)); }
        return peak * (1 - Smoothstep((age - VignettePeakAt) / (VignetteSeconds - VignettePeakAt)));
    }

    /// <summary>How long a switch-off retreat takes at this motion level.</summary>
    public static double RetreatDuration(MotionLevel level)
    {
        var scale = MotionScale(level);
        return scale <= 0 ? OffFadeSeconds : RetreatSeconds / scale;
    }

    /// <summary>Retreat: (alpha, reach) multipliers. A brief gather, then the front pulls back to the
    /// edges (ease-in, it leaves under its own power) while the fog fades behind it. Reduced
    /// pulls back half as far; Off only fades.</summary>
    public static (double alpha, double reach) RetreatEnvelope(double age, MotionLevel level)
    {
        var scale = MotionScale(level);
        var u = Math.Clamp(age / RetreatDuration(level), 0, 1);
        if (scale <= 0) return (1 - u, 1);
        var gather = u < 0.2 ? RetreatGather * Math.Sin(Math.PI * u / 0.2) : 0;
        var reach = 1 + scale * (gather - u * u);
        var alpha = 1 - Smoothstep((u - 0.25) / 0.75);
        return (alpha, reach);
    }

    /// <summary>The fog has arrived now (from nothing). UI thread, before the first frame.</summary>
    public static void Arrive(CreepState s)
    {
        s.ShownAge = 0; s.RetreatAge = -1; s.Pulsed = false;
        s.AlphaMul = 0; s.ReachMul = 0; s.Vignette = 0;
    }

    /// <summary>Start the eased retreat (switch off). Idempotent while it runs.</summary>
    public static void BeginRetreat(CreepState s)
    {
        if (s.RetreatAge < 0) s.RetreatAge = 0;
    }

    /// <summary>Switched back on mid-retreat: come back in from where the fog is now, no pop.</summary>
    public static void CancelRetreat(CreepState s, MotionLevel level)
    {
        if (s.RetreatAge < 0) return;
        s.RetreatAge = -1;
        var a = Math.Clamp(s.AlphaMul, 0, 1);
        // invert the arrival fade so the fog resumes at its current strength
        var d = level == MotionLevel.Off ? OffFadeSeconds : ArriveSeconds / MotionScale(level) * 0.6;
        var x = level == MotionLevel.Off ? a : 1 - Math.Cbrt(1 - a);
        s.ShownAge = x * d;
        s.Pulsed = true;   // the edge pulse plays once per real arrival, never on a come-back
    }

    public static bool RetreatDone(CreepState s, MotionLevel level) =>
        s.RetreatAge >= 0 && s.RetreatAge >= RetreatDuration(level);

    /// <summary>Still moving between states: the layer repaints every frame while this holds.</summary>
    public static bool InTransition(CreepState s) => s.RetreatAge >= 0 || s.ShownAge < Math.Max(VignetteSeconds, ArriveSeconds * 2);

    /// <summary>
    /// The fog front's own life, as a fraction of its reach: a slow breath plus an eased surge
    /// every <see cref="SurgePeriod"/> s (in quick, back slow). Each side surges on its own beat
    /// and the surge rolls along the edge, so it reads as a front, not a pulse.
    /// </summary>
    public static double FrontSwell(double t, int side, double u, double scale)
    {
        if (scale <= 0 || double.IsNaN(t)) return 0;
        var lag = side * SurgePeriod * 0.25 + u * 0.9;
        var c = (t - lag) / SurgePeriod;
        c -= Math.Floor(c);
        var surge = c < SurgeRise ? EaseOutCubic(c / SurgeRise) : 1 - Smoothstep((c - SurgeRise) / (1 - SurgeRise));
        var breath = Math.Sin(t * FrontBreathe + side * 1.7 + u * 3);
        return scale * (SurgeAmp * surge + FrontBreathAmp * breath);
    }

    /// <summary>Mote alpha over its life. Drifting motes rise and fall on a sine with a slow
    /// twinkle; burst motes (a click) flash in over 60 ms and fade out fast.</summary>
    public static double MoteAlpha(double life, double span, double seed, bool burst)
    {
        if (span <= 0 || life < 0 || life >= span) return 0;
        if (burst) { var k = 1 - life / span; return 0.85 * Math.Min(1, life / 0.06) * k * k; }
        return 0.5 * Math.Sin(Math.PI * life / span) * (0.8 + 0.2 * Math.Sin(life * MoteTwinkle + seed * Math.PI * 2));
    }

    /// <summary>Mote radius in px at dpi 1: drifting motes vary per mote, burst motes shrink.</summary>
    public static double MoteRadius(double life, double span, double seed, bool burst) =>
        burst ? 2.2 * (1 - 0.5 * Math.Clamp(life / span, 0, 1)) : 1.1 + 1.3 * seed;

    /// <summary>Motes thrown out by one click: Full 10, Reduced 5, Off none.</summary>
    public static int BurstCount(MotionLevel level) => (int)Math.Round(BurstMotes * MotionScale(level));

    private static void UpdatePresence(CreepState s, MotionLevel level)
    {
        var (a, r) = ArriveEnvelope(s.ShownAge, level);
        var v = s.Pulsed ? 0 : VignetteAt(s.ShownAge, level);
        if (s.RetreatAge >= 0)
        {
            var (ra, rr) = RetreatEnvelope(s.RetreatAge, level);
            a *= ra; r *= rr; v *= ra;
        }
        s.AlphaMul = a; s.ReachMul = r; s.Vignette = v;
    }

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
        s.ShownAge = SettledAge; s.RetreatAge = -1; s.Pulsed = false;
        s.AlphaMul = 1; s.ReachMul = 1; s.Vignette = 0;
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
        if (s.ShownAge < SettledAge) s.ShownAge += dt;
        if (s.RetreatAge >= 0) s.RetreatAge += dt;
        UpdatePresence(s, level);

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
            var dep = b.Depth * s.Coverage * m * ReachIn * s.ReachMul * (1 + FrontSwell(t, b.Side, b.U, scale));
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
        StepWisps(s, f, scale);
    }

    /// <summary>Tendrils: each one roots on its edge, reaches in with the front and sways on its own
    /// phase while its root slides slowly along the edge. Motion Off holds them straight and still.</summary>
    private static void StepWisps(CreepState s, CreepField f, double scale)
    {
        var m = f.Short;
        var t = s.Time;
        foreach (var w in f.Wisps)
        {
            var u = Math.Clamp(w.U + Math.Sin(t * 0.11 + w.Phase * 1.3) * 0.03 * scale, 0, 1);
            var reach = w.Len * s.Coverage * m * ReachIn * s.ReachMul * (1 + FrontSwell(t, w.Side, u, scale));
            double bx, by, inward;
            switch (w.Side)
            {
                case 0: bx = u * f.W; by = 0; inward = Math.PI / 2; break;
                case 1: bx = f.W; by = u * f.H; inward = Math.PI; break;
                case 2: bx = u * f.W; by = f.H; inward = -Math.PI / 2; break;
                default: bx = 0; by = u * f.H; inward = 0; break;
            }
            w.Angle = inward + Math.Sin(t * WispSway + w.Phase) * WispSwayAngle * scale;
            w.Length = reach + m * 0.1;
            w.Width = m * WispWidth * (1 + 0.15 * scale * Math.Sin(t * 0.6 + w.Phase));
            var c = w.Length / 2 - m * 0.05;   // root sits just past the edge
            w.Px = f.X + bx + Math.Cos(w.Angle) * c;
            w.Py = f.Y + by + Math.Sin(w.Angle) * c;
        }
    }

    /// <summary>Ring radius and alpha at its age (0.7 s, grows to 0.55 short side, fades out). The
    /// radius eases out (a shove, then it coasts) and the alpha drops off quadratically.</summary>
    public static (double radius, double alpha) RingAt(CreepRing r, double shortSide)
    {
        var u = Math.Clamp(r.Age / RingSeconds, 0, 1);
        return (EaseOutCubic(u) * shortSide * RingReach, 0.7 * (1 - u) * (1 - u));
    }
}
