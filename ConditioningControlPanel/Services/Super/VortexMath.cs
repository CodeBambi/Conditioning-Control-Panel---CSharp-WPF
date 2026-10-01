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
public struct VortexMote { public double Angle, Radius, BoostUntil; public bool Alive; }

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

    public VortexSim(Random? rng = null) => _rng = rng ?? new Random();

    public double Radius => R0 * Size;

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

        Collapse = VortexMath.Collapse(_velocity);
        Rot += dt * VortexMath.RotationSpeed(Still, Collapse) * speed;
        Turns = VortexMath.Turns(Still, Collapse);
        ArmsAlpha = VortexMath.ArmsAlpha(Still, Collapse);
        FlashAlpha = prev > Size + 0.002 && Collapse > 0.3 ? 0.35 * Collapse : 0;
        BloomAlpha = VortexMath.BloomAlpha(Still);

        if (!still && Collapse > 0.15 && Time - _lastEcho > VortexMath.EchoEvery)
        {
            _lastEcho = Time;
            int slot = OldestEcho();
            Echoes[slot] = new VortexEcho { Size = Size, Rot = Rot, Turns = Turns, Born = Time, Alive = true };
        }
        for (int i = 0; i < Echoes.Length; i++)
            if (Echoes[i].Alive && Time - Echoes[i].Born >= VortexMath.EchoLife) Echoes[i].Alive = false;

        // Dust motes spiral inward; none at Off.
        double r = Radius;
        if (!still)
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
            m.Radius -= dt * speed * px * (40 + 120 * Still + (boosted ? 520 : 0));
            m.Angle += dt * speed * (1.2 + 2.5 * Still + (boosted ? 5 : 0));
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
            Motes[i] = new VortexMote { Angle = angle, Radius = radius, BoostUntil = boostUntil, Alive = true };
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
