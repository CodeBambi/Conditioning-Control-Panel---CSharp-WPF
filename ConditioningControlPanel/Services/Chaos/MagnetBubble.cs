using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Bubbles v2, wave 2: the Magnet bubble. All of its decisions and all of its physics, none of
/// its WPF.
///
/// <para>Like <see cref="BrainDrainBubble"/> it is a dashboard TRIGGER bubble built in
/// BuildTriggerSpec rather than a row in <see cref="ChaosBubbleVariants.All"/>, so it never leaks
/// into the Rabbit Hole's pool or the chaos setup window. It joins the trigger roll as ONE more
/// equally-weighted id and therefore still sits behind the user's Trigger Bubbles switch and their
/// BubbleTriggerChance dial, on top of owning Bubbles v2 and leaving its own row switched on.</para>
///
/// <para>What it does differently: it has no payload at all. Instead of firing an effect on pop it
/// changes how the bubble MOVES - it steers toward the mouse cursor as it rises - and what the pop
/// is WORTH: catch it inside the first <see cref="EarlyWindowFrac"/> of its life and it pays double
/// the ordinary ambient-pop XP. Let it swim to you and it pays the ordinary amount. The whole
/// bargain is "it is coming to you anyway, so the points are for taking it now".</para>
///
/// <para>Steering is deliberately a capped TURN plus a capped SPEED, never a lerp toward the cursor
/// point: a lerp lets a bubble teleport across the screen when the cursor jumps, and it ends up
/// welded under the pointer where it cannot be missed. A bubble that can only bend its heading by
/// <see cref="MaxTurnRadPerFrame"/> a frame visibly hunts, overshoots, and comes round again.</para>
/// </summary>
public static class MagnetBubble
{
    /// <summary>Spec/sprite id. No art file ships for it, so it wears the tinted bubble.png.</summary>
    public const string VariantId = "magnet_pull";

    /// <summary>Magnetic steel blue, chosen to sit nowhere near the Brain Drain bubble's deep violet
    /// (0x6A22A8) or the bright-magenta "glitch" one. R,G,B kept as ints so this file stays free of
    /// System.Windows.Media.</summary>
    public const byte TintR = 0x2F, TintG = 0x6F, TintB = 0xBF;

    /// <summary>The halo colour, a lighter steel blue so the ring reads against the body.</summary>
    public const byte GlowR = 0x6F, GlowG = 0xA8, GlowB = 0xFF;

    /// <summary>
    /// The glyph worn on the body, since no sprite ships. U+2295, a circled plus: reads as an
    /// attract point, and deliberately NOT the chaos spiral variant's U+25CE or the Brain Drain
    /// bubble's U+25CD, which are the two nearest neighbours in the same visual family.
    /// </summary>
    public const string Label = "⊕";

    /// <summary>
    /// The ring's pulse: opacity swings between <c>1 - RingPulseDepth</c> and 1 at
    /// <c>RingPulseRateRadPerSec</c>. Faster and shallower than the Brain Drain breathe on purpose -
    /// that one is a slow "something is about to go quiet", this one is a quick "it has noticed you".
    /// Like that one it rides the opacity the frame already computes (the halo is a drop shadow on
    /// the same element, so it pulses with it), so neither render path gains an element.
    /// </summary>
    public const double RingPulseDepth = 0.12;
    public const double RingPulseRateRadPerSec = 4.4;

    /// <summary>Opacity multiplier for a bubble that has been alive <paramref name="secondsAlive"/>.</summary>
    public static double RingPulseAt(double secondsAlive) =>
        (1.0 - RingPulseDepth) + RingPulseDepth * (0.5 + 0.5 * Math.Sin(secondsAlive * RingPulseRateRadPerSec));

    // ---- the early-pop bargain ---------------------------------------------------------

    /// <summary>
    /// "Early" is the first 35% of the bubble's life. At the ambient trigger lifetime of 7000 ms
    /// that is the opening ~2.45 s: long enough that a user who is already looking at the field can
    /// take the shot, short enough that it is a shot and not the default. Past it the bubble has
    /// done the work of swimming over and the pop pays the ordinary rate.
    /// </summary>
    public const double EarlyWindowFrac = 0.35;

    /// <summary>XP multiplier an early pop earns. The pop still draws from the ordinary ambient
    /// bucket, so this doubles the rate, never the daily ceiling.</summary>
    public const int EarlyXpMultiplier = 2;

    /// <summary>True while <paramref name="ageMs"/> is inside the early window of a
    /// <paramref name="lifeMs"/>-long bubble. A non-positive life has no early window.</summary>
    public static bool IsEarlyPop(double ageMs, double lifeMs) =>
        lifeMs > 0 && ageMs >= 0 && ageMs < lifeMs * EarlyWindowFrac;

    /// <summary>
    /// What one real pop of this bubble is worth, as a multiple of the ordinary ambient-pop XP.
    ///
    /// <para>Motion Off pays the ordinary amount whatever the timing: with no attraction the bubble
    /// is a plain float, the user was never offered the trade, and paying double for it would make
    /// switching motion off the cheapest way to farm the prize.</para>
    /// </summary>
    public static int XpMultiplier(bool early, MotionLevel level) =>
        early && level != MotionLevel.Off ? EarlyXpMultiplier : 1;

    // ---- pool gating ------------------------------------------------------------------

    /// <summary>
    /// Owned AND switched on. Ownership is any Bubbles v2 grant (Rain or Spiral In) and is read
    /// from PrizeGrants, never from settings - a synced profile cannot grant itself the bubble.
    /// </summary>
    public static bool IsEligible(bool v2Owned, bool settingOn) => v2Owned && settingOn;

    /// <summary>
    /// The id list the trigger roll draws from: the ids handed in plus, when eligible, the Magnet
    /// bubble as one more equally-weighted entry. Returns the input list untouched when it is not
    /// eligible, so an unowned account can never roll it. Chains with
    /// <see cref="BrainDrainBubble.RollPool"/> - each v2 bubble adds at most its own id once.
    /// </summary>
    public static IReadOnlyList<string> RollPool(IReadOnlyList<string>? triggerIds, bool v2Owned, bool settingOn)
    {
        var baseIds = triggerIds ?? Array.Empty<string>();
        if (!IsEligible(v2Owned, settingOn)) return baseIds;
        var pool = new List<string>(baseIds.Count + 1);
        pool.AddRange(baseIds);
        if (!pool.Contains(VariantId)) pool.Add(VariantId);
        return pool;
    }

    // ---- attraction physics -----------------------------------------------------------
    //
    // Everything below is in DIPs and FRAMES. The bubble tick runs at ~30 fps and hands in the
    // field's time scale as `ts` (1 normally), exactly like SpiralInPath, so the per-frame numbers
    // here can be read straight off against the 1..2 DIP/frame a plain ambient bubble drifts at.

    /// <summary>Hardest the heading may bend in one frame at full strength: ~0.06 rad, so ~1.8 rad
    /// a second, a bit under a third of a turn. Enough to visibly hunt a moving cursor, far too
    /// little to snap onto one.</summary>
    public const double MaxTurnRadPerFrame = 0.06;

    /// <summary>How fast the bubble may work up to <see cref="MaxSpeedDipPerFrame"/> while it is
    /// steering. A plain ambient bubble drifts at 1..2 DIP/frame, so the ramp takes about a second.</summary>
    public const double AccelDipPerFrame = 0.12;

    /// <summary>Speed ceiling while homing. A SAFETY rail, not an attraction knob, so it does not
    /// scale with the motion level: 3.2 DIP/frame is ~96 DIP/s, faster than a float but still a
    /// thing you can put a cursor on.</summary>
    public const double MaxSpeedDipPerFrame = 3.2;

    /// <summary>Floor so a bubble that has been turned to a standstill still has a heading to bend.</summary>
    public const double MinSpeedDipPerFrame = 0.35;

    /// <summary>No steering inside this radius of the cursor: the bubble arrives, it does not sit
    /// under the pointer jittering. It keeps its last heading and drifts on through.</summary>
    public const double DeadZoneDip = 28;

    /// <summary>A cursor that has not moved for this long is treated as absent - the user is away,
    /// or reading, and a bubble homing on a parked pointer just piles up there.</summary>
    public const double CursorStaleSeconds = 2.0;

    /// <summary>Attraction strength at a motion level. Reduced is half, Off is none (the bubble is
    /// then a plain float, and <see cref="XpMultiplier"/> stops paying double to match).</summary>
    public static double StrengthFor(MotionLevel level) => level switch
    {
        MotionLevel.Full => 1.0,
        MotionLevel.Reduced => 0.5,
        _ => 0.0,
    };

    /// <summary>Whether this frame's cursor sample is live at all: on SOME monitor, and moved
    /// inside the last <see cref="CursorStaleSeconds"/>. Desktop-wide, so it says nothing about
    /// whether any particular bubble can reach it - that is <see cref="CursorInBounds"/>.</summary>
    public static bool CursorUsable(bool cursorOnScreen, double secondsSinceCursorMoved) =>
        cursorOnScreen && secondsSinceCursorMoved >= 0 && secondsSinceCursorMoved <= CursorStaleSeconds;

    /// <summary>
    /// Whether the cursor is somewhere THIS bubble could actually travel to.
    ///
    /// <para>The multi-monitor rule, and the reason it is not optional: a bubble lives on the
    /// screen it spawned on and is destroyed the moment it crosses that screen's bounds. With the
    /// pointer parked on monitor 2, a magnet on monitor 1 would otherwise beeline for the shared
    /// edge, cross it, and be quietly destroyed unpopped - every magnet on that screen, every
    /// time. Out of bounds it simply does not steer, and floats like any other bubble.</para>
    ///
    /// <para>Bounds are the bubble's own screen box in DIPs. <paramref name="right"/> is expected
    /// to already have the sprite size added back on, since the caller's <c>_screenRight</c> is
    /// the largest legal top-left X rather than the screen's right edge.</para>
    /// </summary>
    public static bool CursorInBounds(double cursorX, double cursorY,
                                      double left, double right, double top, double bottom) =>
        cursorX >= left && cursorX <= right && cursorY >= top && cursorY <= bottom;

    /// <summary>Both rules at once: a live sample, on a monitor, and inside this bubble's reach.
    /// The one question the per-frame steering branch asks.</summary>
    public static bool CanSteer(bool cursorOnScreen, double secondsSinceCursorMoved,
                                double cursorX, double cursorY,
                                double left, double right, double top, double bottom) =>
        CursorUsable(cursorOnScreen, secondsSinceCursorMoved)
        && CursorInBounds(cursorX, cursorY, left, right, top, bottom);

    /// <summary>A bubble's velocity in DIPs per frame.</summary>
    public readonly record struct Velocity(double Vx, double Vy)
    {
        public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy);
    }

    /// <summary>Signed smallest angle from <paramref name="from"/> to <paramref name="to"/>, in (-pi, pi].</summary>
    internal static double AngleDelta(double from, double to)
    {
        double d = (to - from) % (2.0 * Math.PI);
        if (d > Math.PI) d -= 2.0 * Math.PI;
        if (d <= -Math.PI) d += 2.0 * Math.PI;
        return d;
    }

    /// <summary>
    /// One frame of attraction: bend <paramref name="v"/> toward the cursor by at most
    /// <see cref="MaxTurnRadPerFrame"/> (times strength and <paramref name="ts"/>) and work the
    /// speed up toward <see cref="MaxSpeedDipPerFrame"/>. Returns <paramref name="v"/> untouched
    /// when there is no strength (Motion Off) or the cursor is inside the dead zone.
    /// </summary>
    /// <param name="v">This frame's incoming velocity, DIPs per frame.</param>
    /// <param name="bubbleCx">Bubble centre X, DIPs.</param>
    /// <param name="bubbleCy">Bubble centre Y, DIPs.</param>
    /// <param name="cursorCx">Cursor X, DIPs.</param>
    /// <param name="cursorCy">Cursor Y, DIPs.</param>
    /// <param name="strength">0..1 from <see cref="StrengthFor"/>.</param>
    /// <param name="ts">The field's time scale, 1 normally.</param>
    public static Velocity Steer(Velocity v, double bubbleCx, double bubbleCy,
                                 double cursorCx, double cursorCy, double strength, double ts)
    {
        if (strength <= 0) return v;
        double scale = Math.Clamp(strength, 0.0, 1.0) * Math.Max(ts, 0.0);
        if (scale <= 0) return v;

        double dx = cursorCx - bubbleCx, dy = cursorCy - bubbleCy;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist <= DeadZoneDip) return v;

        double want = Math.Atan2(dy, dx);
        double speed = v.Speed;

        // A bubble with no heading left cannot be turned - give it the smallest one that points
        // the right way and let the next frames build on it.
        double heading = speed < 1e-6 ? want : Math.Atan2(v.Vy, v.Vx);
        if (speed < MinSpeedDipPerFrame) speed = MinSpeedDipPerFrame;

        double turn = Math.Clamp(AngleDelta(heading, want), -MaxTurnRadPerFrame * scale, MaxTurnRadPerFrame * scale);
        heading += turn;

        speed = Math.Min(MaxSpeedDipPerFrame, speed + AccelDipPerFrame * scale);
        return new Velocity(Math.Cos(heading) * speed, Math.Sin(heading) * speed);
    }
}
