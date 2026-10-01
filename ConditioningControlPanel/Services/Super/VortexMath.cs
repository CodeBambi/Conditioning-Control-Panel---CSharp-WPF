using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super;

/// <summary>
/// Super Vortex, the pure rules: a second small spiral that follows the cursor on top of the base
/// spiral overlay. It grows while the pointer sits still and, on any click, swells for a blink and
/// then collapses on an underdamped spring (overshoots below its new size and settles). Constants
/// are the owner-approved mockup's (handoff-1001/super-effects-mockup.html, mkSpiral).
///
/// WPF-free, Skia-free, App-free: the layer feeds delta time, the cursor and clicks, and only
/// draws what <see cref="VortexSim"/> holds. Sizes are fractions of the "full" radius R0
/// (0.64 of the shorter side of the monitor the cursor is on).
/// </summary>
public static class VortexMath
{
    public const double StartSize = 0.16;       // first size, fraction of full
    public const double MinTarget = 0.12;       // a click never squeezes the target below this
    public const double MinSize = 0.06;         // the spring never draws smaller than this
    public const double GrowRate = 0.16;        // target growth per second at stillness 0.5
    public const double SpringK = 90;
    public const double SpringDamping = 9;
    public const double ClickKick = 0.9;        // swell first, then the squeeze
    public const double CollapseGain = 2.6;     // collapse = clamp(-velocity * gain)
    public const double StillSpeedPx = 25;      // px/s below which the cursor counts as still
    public const double StillRise = 0.45;       // per second while still
    public const double StillFall = 1.8;        // per second while moving
    public const double FollowRate = 3;         // lag factor 1 - exp(-rate * dt)
    public const double EchoEvery = 0.045, EchoLife = 0.28, EchoAlpha = 0.22;
    public const double RingLife = 0.35;
    public const int ClickMotes = 30;
    public const double ClickMoteBoost = 0.5;
    public const double GifChance = 0.10;       // 1 in 10 clicks
    public const double GifLife = 0.9, GifCycleSec = 0.35, GifFps = 24, GifGlitchSec = 0.2;
    public const double SoftFadeSec = 0.4;      // photosafe / reduced motion replacement
    public const double BloomFrom = 0.85;
    public const double MockupR0 = 192;         // the mockup's full radius in px; motes scale off it

    // Juice: the vortex gathers, opens with a small overshoot, breathes, and leaves by being sucked in.
    public const double GatherSec = 0.16;       // anticipation: a tight seed while the motes pull in
    public const double OpenSec = 0.42;         // then it opens on an ease-out-back
    public const double SeedScale = 0.34;       // the seed's size, fraction of the drawn size
    public const double GatherDip = 0.08;       // the seed draws in a little more before it opens
    public const double BackFull = 1.70158;     // peak about 6.6 percent past full
    public const double BackReduced = 1.15;     // peak about 3.2 percent: half the overshoot
    public const int GatherMotes = 20;
    public const double CloseSec = 0.34;
    public const double CloseSwell = 0.04;      // a breath out before it is sucked in
    public const double CloseSpin = 5;          // extra spin multiplier at the end of the close
    public const double OffFadeSec = 0.12;      // Motion off: a plain fade, no scale
    public const double BreathPeriod = 4.2;     // 0.24 Hz, far under the 3 Hz photosafe line
    public const double BreathScale = 0.025, BreathGlow = 0.06, CoreGlow = 0.10;
    public const double MoteFadeIn = 0.12;
    public const double TrailSec = 0.05;        // a mote's tail is where it was 50 ms ago
    public const double GifPopSec = 0.2, GifRiseSec = 0.07, GifPopFrom = 0.72;

    public static double EaseOutCubic(double t) { t = Clamp01(t); double u = 1 - t; return 1 - u * u * u; }
    public static double EaseInQuad(double t) { t = Clamp01(t); return t * t; }
    public static double Smoothstep(double t) { t = Clamp01(t); return t * t * (3 - 2 * t); }

    /// <summary>Ease-out-back: overshoots past 1 and settles. <paramref name="c1"/> sets the overshoot.</summary>
    public static double EaseOutBack(double t, double c1)
    {
        t = Clamp01(t);
        double c3 = c1 + 1, x = t - 1;
        return 1 + c3 * x * x * x + c1 * x * x;
    }

    /// <summary>
    /// The opening. Full/Reduced: a seed that draws in a touch while the motes gather, then it opens
    /// on an ease-out-back (Reduced at half the overshoot) while the arms fade up, and a core glow
    /// peaks at the moment it opens. Off: a 120 ms fade, no scale. False once it is fully open.
    /// </summary>
    public static bool Opening(double age, MotionLevel level, out double scale, out double alpha, out double glow)
    {
        scale = 1; alpha = 1; glow = 0;
        if (age < 0) age = 0;
        if (level == MotionLevel.Off)
        {
            if (age >= OffFadeSec) return false;
            alpha = age / OffFadeSec;
            return true;
        }
        if (age >= GatherSec + OpenSec) return false;
        if (age < GatherSec)
        {
            double g = age / GatherSec;
            scale = SeedScale * (1 - GatherDip * EaseInQuad(g));
            alpha = 0.35 * EaseInQuad(g);
            glow = 0.5 * EaseInQuad(g);
            return true;
        }
        double u = (age - GatherSec) / OpenSec;
        double from = SeedScale * (1 - GatherDip);
        scale = from + (1 - from) * EaseOutBack(u, level == MotionLevel.Reduced ? BackReduced : BackFull);
        alpha = 0.35 + 0.65 * EaseOutCubic(u / 0.6);
        glow = 0.5 * (1 - EaseOutCubic(u));
        return true;
    }

    /// <summary>
    /// The exit. Full/Reduced: a small swell out (Reduced half), then an ease-in suck to nothing while
    /// it spins up and the arms fade over the back two thirds. Off: a 120 ms fade, no scale, no spin.
    /// False once it is gone.
    /// </summary>
    public static bool Closing(double age, MotionLevel level, out double scale, out double alpha, out double spin)
    {
        scale = 1; alpha = 1; spin = 1;
        if (age < 0) age = 0;
        if (level == MotionLevel.Off)
        {
            if (age >= OffFadeSec) { alpha = 0; return false; }
            alpha = 1 - age / OffFadeSec;
            return true;
        }
        if (age >= CloseSec) { scale = 0; alpha = 0; return false; }
        double amp = level == MotionLevel.Reduced ? 0.5 : 1;
        double u = age / CloseSec;
        const double swellEnd = 0.18;
        scale = u < swellEnd
            ? 1 + CloseSwell * amp * EaseOutCubic(u / swellEnd)
            : (1 + CloseSwell * amp) * (1 - EaseInQuad((u - swellEnd) / (1 - swellEnd)));
        alpha = 1 - Smoothstep((u - 0.35) / 0.65);
        spin = 1 + CloseSpin * amp * u;
        return true;
    }

    /// <summary>Idle breath, -1..1 (Reduced: half amplitude at half speed, Off: 0); phased per vortex.</summary>
    public static double Breath(double time, double phase, MotionLevel level)
    {
        if (level == MotionLevel.Off) return 0;
        double slow = level == MotionLevel.Reduced ? 0.5 : 1;
        return slow * Math.Sin(2 * Math.PI * time * slow / BreathPeriod + phase);
    }

    /// <summary>A new mote fades in over 120 ms instead of popping in.</summary>
    public static double MoteFade(double age) => EaseOutCubic(age / MoteFadeIn);

    /// <summary>
    /// The gif grows out of the vortex: a 70 ms alpha rise and an ease-out-back from 0.72 (Reduced
    /// from 0.86). Soft mode (photosafe or not Full motion) keeps its own fade: no rise, no scale.
    /// </summary>
    public static void GifPop(double age, bool flicker, MotionLevel level, out double scale, out double alphaMul)
    {
        scale = 1; alphaMul = 1;
        if (!flicker || level == MotionLevel.Off || age < 0) return;
        alphaMul = EaseOutCubic(age / GifRiseSec);
        double from = level == MotionLevel.Reduced ? 1 - (1 - GifPopFrom) / 2 : GifPopFrom;
        scale = from + (1 - from) * EaseOutBack(age / GifPopSec, level == MotionLevel.Reduced ? BackReduced : BackFull);
    }

    public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    /// <summary>Stillness climbs while the cursor moves slower than 25 px/s, falls fast otherwise.</summary>
    public static double StepStillness(double still, double speedPxPerSec, double dt)
        => Clamp01(still + (speedPxPerSec < StillSpeedPx ? dt * StillRise : -dt * StillFall));

    /// <summary>The target grows toward full, faster the stiller the cursor.</summary>
    public static double GrowTarget(double target, double still, double dt)
        => Math.Min(1, target + dt * GrowRate * (0.5 + still));

    /// <summary>A click halves the target (never below <see cref="MinTarget"/>); it never cuts the size itself.</summary>
    public static double ClickTarget(double target) => Math.Max(MinTarget, target * 0.5);

    /// <summary>How far the cursor-follow closes the gap this frame.</summary>
    public static double FollowFactor(double dt, double rate = FollowRate) => 1 - Math.Exp(-rate * dt);

    /// <summary>
    /// One underdamped spring step (K 90, damping 9), sub-stepped at 1/240 s so a long engine frame
    /// (up to the engine's 100 ms clamp) cannot blow it up. Returns the new size; velocity in/out.
    /// </summary>
    public static double SpringStep(double size, ref double velocity, double target, double dt)
    {
        if (dt <= 0) return size;
        int n = Math.Max(1, (int)Math.Ceiling(dt * 240));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            velocity += ((target - size) * SpringK - velocity * SpringDamping) * h;
            size = Math.Max(MinSize, size + velocity * h);
        }
        return size;
    }

    public static double Collapse(double velocity) => Clamp01(-velocity * CollapseGain);
    public static double Turns(double still, double collapse) => 2.4 + 4 * still + 3 * collapse;
    public static double RotationSpeed(double still, double collapse) => 1 + 3.2 * still + 16 * collapse;
    public static double ArmsAlpha(double still, double collapse) => Math.Min(1, 0.45 + 0.35 * still + 0.35 * collapse);
    public static double DustRate(double still) => 40 * (0.4 + still);
    public static double BloomAlpha(double still) => still > BloomFrom ? (still - BloomFrom) * 2.4 : 0;
    public static double EchoFade(double age) => age >= EchoLife ? 0 : EchoAlpha * (1 - age / EchoLife);

    /// <summary>Does this click flicker a gif. <paramref name="roll"/> is a uniform 0..1 draw.</summary>
    public static bool RollsGif(double roll) => roll < GifChance;

    /// <summary>
    /// The gif flash timeline. Flicker: 3 frames cycled at 24 fps for 0.35 s then held, red/cyan
    /// glitch for the first 0.2 s, alpha 1 then a linear fade out by 0.9 s. Soft (photosafe or not
    /// Full motion): frame 0 only, one soft 0.4 s fade in and out, no glitch. False once it is over.
    /// </summary>
    public static bool GifFrame(double age, bool flicker, out int frame, out double alpha, out bool glitch)
    {
        frame = 0; alpha = 0; glitch = false;
        if (age < 0) return false;
        if (!flicker)
        {
            if (age >= SoftFadeSec) return false;
            alpha = 0.8 * Math.Sin(Math.PI * age / SoftFadeSec);
            return true;
        }
        if (age >= GifLife) return false;
        double cycle = Math.Min(age, GifCycleSec);
        frame = (int)Math.Floor(cycle * GifFps) % 3;
        double q = age / GifLife;
        alpha = q < 0.2 ? 1 : 1 - (q - 0.2) / 0.8;
        glitch = age < GifGlitchSec;
        return true;
    }

    /// <summary>The implosion ring: radius shrinks from 1.25 R to 0, alpha 0.6 fading. False when over.</summary>
    public static bool Ring(double age, out double radiusScale, out double alpha)
    {
        radiusScale = 0; alpha = 0;
        if (age < 0 || age >= RingLife) return false;
        double u = age / RingLife;
        radiusScale = 1.25 * (1 - u) * (1 - u * 0.2);
        alpha = 0.6 * (1 - u);
        return true;
    }

    /// <summary>
    /// Repaint gate: Quality repaints every engine frame; Balanced and Performance cap the vortex
    /// at 30 fps, because one always-moving layer re-rasters the whole shared surface it sits on.
    /// </summary>
    public static bool ShouldRepaint(double sinceLastPaint, PerformanceTier tier)
        => tier == PerformanceTier.Quality || sinceLastPaint >= 1.0 / 30 - 1e-6;

    /// <summary>Motion levels: Reduced halves speed and amplitude; Off is still.</summary>
    public static double SpeedScale(MotionLevel level) => level switch
    {
        MotionLevel.Off => 0,
        MotionLevel.Reduced => 0.5,
        _ => 1,
    };
}

public struct VortexEcho { public double Size, Rot, Turns, Born; public bool Alive; }
/// <summary>A dust mote. Vr (px/s inward) and Va (rad/s) are this frame's speeds, for its tail.</summary>
public struct VortexMote { public double Angle, Radius, BoostUntil, Born, Vr, Va; public bool Alive; }

/// <summary>
/// The vortex state machine. Pure: time, cursor, randomness and the motion level come in, the
/// fields the layer draws come out. Fixed pools, no allocation per step.
/// </summary>
public sealed class VortexSim
{
    public const int MaxMotes = 160, MaxEchoes = 8, MaxRings = 4;

    private readonly Random _rng;
    private bool _placed;
    private double _px, _py, _lastEcho = double.NegativeInfinity, _dustAcc, _velocity;

    public double Time { get; private set; }
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Size { get; private set; } = VortexMath.StartSize;
    public double Target { get; private set; } = VortexMath.StartSize;
    public double Velocity => _velocity;
    public double Still { get; private set; }
    public double Collapse { get; private set; }
    public double Rot { get; private set; }
    public double Turns { get; private set; } = VortexMath.Turns(0, 0);
    public double ArmsAlpha { get; private set; } = VortexMath.ArmsAlpha(0, 0);
    public double FlashAlpha { get; private set; }
    public double BloomAlpha { get; private set; }
    /// <summary>Full radius in px, set by the layer from the cursor's monitor.</summary>
    public double R0 { get; set; } = VortexMath.MockupR0;

    public readonly VortexEcho[] Echoes = new VortexEcho[MaxEchoes];
    public readonly VortexMote[] Motes = new VortexMote[MaxMotes];
    public readonly double[] RingBorn = { -1e9, -1e9, -1e9, -1e9 };
    public readonly double[] RingRadius = new double[MaxRings];

    public double GifBorn { get; private set; } = -1e9;
    public double GifX { get; private set; }
    public double GifY { get; private set; }

    /// <summary>This vortex's breath phase, so two vortices never breathe in step.</summary>
    public double Phase { get; }
    /// <summary>Open/close/breath product: what the layer multiplies the radius by.</summary>
    public double DrawScale { get; private set; } = 1;
    /// <summary>Open/close fade: what the layer multiplies the arms, echoes and glows by.</summary>
    public double DrawAlpha { get; private set; } = 1;
    /// <summary>The soft core glow: the open's flare plus the idle breath.</summary>
    public double CoreGlow { get; private set; }
    public bool IsOpening { get; private set; }
    public bool IsClosing => _closeAt > -1e8;
    /// <summary>The exit has run its course; the layer deactivates.</summary>
    public bool Closed { get; private set; }

    private double _openAt = -1e9, _closeAt = -1e9;

    public VortexSim(Random? rng = null)
    {
        _rng = rng ?? new Random();
        Phase = _rng.NextDouble() * Math.PI * 2;
    }

    public double Radius => R0 * Size;

    /// <summary>
    /// Start the opening at the cursor: the seed, and (above Motion off) a ring of motes far out that
    /// get pulled in hard, so the vortex gathers before it opens.
    /// </summary>
    public void Open(MotionLevel level, double cursorX, double cursorY)
    {
        _closeAt = -1e9; Closed = false;
        _openAt = Time; IsOpening = true;
        if (!_placed) { X = _px = cursorX; Y = _py = cursorY; _placed = true; }
        if (level == MotionLevel.Off) return;
        double boost = Time + VortexMath.GatherSec + 0.1;
        for (int i = 0; i < VortexMath.GatherMotes; i++)
            SpawnMote(_rng.NextDouble() * Math.PI * 2, Radius * (2.2 + _rng.NextDouble()), boost);
    }

    /// <summary>Begin the exit: no new dust, every mote is sucked in, the arms close. Idempotent.</summary>
    public void Close()
    {
        if (IsClosing) return;
        _closeAt = Time; IsOpening = false;
        for (int i = 0; i < Motes.Length; i++)
            if (Motes[i].Alive) Motes[i].BoostUntil = Time + VortexMath.CloseSec;
    }

    /// <summary>Advance one frame. <paramref name="level"/> is the effective MotionFx level.</summary>
    public void Step(double dt, double cursorX, double cursorY, MotionLevel level)
    {
        if (dt < 0) dt = 0;
        Time += dt;
        double speed = VortexMath.SpeedScale(level);
        bool still = level == MotionLevel.Off;
        double px = R0 / VortexMath.MockupR0;

        if (!_placed) { X = _px = cursorX; Y = _py = cursorY; _placed = true; }
        double cursorSpeed = dt > 0 ? Math.Sqrt((cursorX - _px) * (cursorX - _px) + (cursorY - _py) * (cursorY - _py)) / dt : 0;
        _px = cursorX; _py = cursorY;
        Still = VortexMath.StepStillness(Still, cursorSpeed, dt);

        if (still) { X = cursorX; Y = cursorY; }
        else
        {
            double f = VortexMath.FollowFactor(dt, VortexMath.FollowRate * speed);
            X += (cursorX - X) * f; Y += (cursorY - Y) * f;
        }

        // Off is still: the vortex keeps the size it has instead of swelling.
        if (!still) Target = VortexMath.GrowTarget(Target, Still, dt);
        double prev = Size;
        if (still) { Size = Target; _velocity = 0; }
        else Size = VortexMath.SpringStep(Size, ref _velocity, Target, dt);

        // In and out: the opening (gather, then open) and the exit (swell, suck in, fade).
        double scale = 1, alpha = 1, glow = 0, spin = 1;
        if (IsClosing)
        {
            if (!VortexMath.Closing(Time - _closeAt, level, out scale, out alpha, out spin)) Closed = true;
        }
        else if (IsOpening && !VortexMath.Opening(Time - _openAt, level, out scale, out alpha, out glow))
            IsOpening = false;
        double breath = VortexMath.Breath(Time, Phase, level);
        DrawScale = scale * (1 + VortexMath.BreathScale * breath);
        DrawAlpha = alpha;
        CoreGlow = Math.Max(glow, (VortexMath.CoreGlow + VortexMath.BreathGlow * breath) * alpha);

        Collapse = VortexMath.Collapse(_velocity);
        Rot += dt * VortexMath.RotationSpeed(Still, Collapse) * speed * spin;
        Turns = VortexMath.Turns(Still, Collapse);
        ArmsAlpha = VortexMath.ArmsAlpha(Still, Collapse);
        FlashAlpha = prev > Size + 0.002 && Collapse > 0.3 ? 0.35 * Collapse : 0;
        BloomAlpha = VortexMath.BloomAlpha(Still);

        if (!still && !IsClosing && Collapse > 0.15 && Time - _lastEcho > VortexMath.EchoEvery)
        {
            _lastEcho = Time;
            int slot = OldestEcho();
            Echoes[slot] = new VortexEcho { Size = Size, Rot = Rot, Turns = Turns, Born = Time, Alive = true };
        }
        for (int i = 0; i < Echoes.Length; i++)
            if (Echoes[i].Alive && Time - Echoes[i].Born >= VortexMath.EchoLife) Echoes[i].Alive = false;

        // Dust motes spiral inward; none at Off.
        double r = Radius;
        if (!still && !IsClosing)
        {
            _dustAcc += dt * VortexMath.DustRate(Still) * speed;
            while (_dustAcc >= 1)
            {
                _dustAcc -= 1;
                SpawnMote(_rng.NextDouble() * Math.PI * 2, r * (0.6 + _rng.NextDouble() * 0.5), 0);
            }
        }
        else _dustAcc = 0;
        for (int i = 0; i < Motes.Length; i++)
        {
            ref var m = ref Motes[i];
            if (!m.Alive) continue;
            bool boosted = Time < m.BoostUntil;
            m.Vr = speed * px * (40 + 120 * Still + (boosted ? 520 : 0));
            m.Va = speed * (1.2 + 2.5 * Still + (boosted ? 5 : 0));
            m.Radius -= dt * m.Vr;
            m.Angle += dt * m.Va;
            if (m.Radius <= 4 * px || still) m.Alive = false;
        }
    }

    /// <summary>
    /// A global mouse button went down. Halves the target and kicks the spring (swell, then the
    /// squeeze), starts the ring and the hard-pulled dust. Returns true when this click should
    /// flicker a gif (the layer still skips it when no frames are ready).
    /// </summary>
    public bool Click(MotionLevel level, double gifRoll)
    {
        // Off is still: a click neither jumps nor collapses it (the gif may still show, as one
        // soft fade, because the layer never cycles frames below Full motion).
        if (IsClosing) return false;   // on its way out: a click no longer feeds it
        if (level != MotionLevel.Off)
        {
            Target = VortexMath.ClickTarget(Target);
            _velocity += VortexMath.ClickKick * VortexMath.SpeedScale(level);
            int slot = 0;
            for (int i = 1; i < RingBorn.Length; i++) if (RingBorn[i] < RingBorn[slot]) slot = i;
            RingBorn[slot] = Time;
            RingRadius[slot] = Radius;
            double boost = Time + VortexMath.ClickMoteBoost;
            for (int i = 0; i < VortexMath.ClickMotes; i++)
                SpawnMote(_rng.NextDouble() * Math.PI * 2, Radius * (0.7 + _rng.NextDouble() * 0.6), boost);
        }
        if (!VortexMath.RollsGif(gifRoll)) return false;
        GifBorn = Time; GifX = X; GifY = Y;
        return true;
    }

    private void SpawnMote(double angle, double radius, double boostUntil)
    {
        for (int i = 0; i < Motes.Length; i++)
        {
            if (Motes[i].Alive) continue;
            Motes[i] = new VortexMote { Angle = angle, Radius = radius, BoostUntil = boostUntil, Born = Time, Alive = true };
            return;
        }
    }

    private int OldestEcho()
    {
        int slot = 0;
        for (int i = 0; i < Echoes.Length; i++)
        {
            if (!Echoes[i].Alive) return i;
            if (Echoes[i].Born < Echoes[slot].Born) slot = i;
        }
        return slot;
    }
}
