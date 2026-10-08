using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>
/// Flashes v2 wave 2: what one flash is doing while a hand is on it. Hung off a
/// <see cref="FlashMotionState"/> for as long as the drag and the fling that may follow it last,
/// then dropped so the item settles where it landed.
///
/// Threading: the global mouse hook writes the pointer samples and the compositor tick reads them,
/// so every sample field sits behind <see cref="Sync"/>. Nothing else here is shared.
/// </summary>
public sealed class FlashDragState
{
    /// <summary>Guards the pointer samples and the travel tally.</summary>
    public readonly object Sync = new();

    /// <summary>True while the button is still down.</summary>
    public bool Held = true;

    /// <summary>True once the button is off and the flash is flying on its own.</summary>
    public bool Flinging;

    /// <summary>Pointer to rect top-left at the grab, so the picture never jumps under the cursor.</summary>
    public double GrabDx, GrabDy;

    /// <summary>Where and when the press started (the tap test measures against this).</summary>
    public double StartX, StartY, StartMs;

    /// <summary>Latest pointer sample, and the one before it: the throw is measured across those two.</summary>
    public double LastX, LastY, LastMs;
    public double PrevX, PrevY, PrevMs;
    public bool HasPrev;

    /// <summary>
    /// Furthest the pointer has been from the grab point. A press that never leaves the 6 px
    /// circle is a click no matter how it wandered back to the middle before letting go.
    /// </summary>
    public double MaxTravelPx;
}

/// <summary>How a press ended.</summary>
public enum FlashDragOutcome
{
    /// <summary>Barely moved, barely held: the ordinary pop.</summary>
    Tap,
    /// <summary>Let go without a throw (or with motion off): it stays where the hand left it.</summary>
    Place,
    /// <summary>Thrown: it keeps the velocity, bounces off the work area and slows to a stop.</summary>
    Fling,
}

/// <summary>
/// The physics behind dragging and flinging a flash. No WPF, no Skia, no App: the mouse hook feeds
/// it pointer samples, <see cref="FlashMotion.Step"/> hands it the compositor's delta time, and the
/// tests drive the same calls.
///
/// The throw is measured the way the sort room's swipe is
/// (Resources/web/arcademy/games/sort/swipe.js): from the LAST move only, zeroed when that sample
/// has gone stale, over a flat px/ms threshold. A pointer parked for a beat before release is a
/// placement, not a throw, and must not inherit the speed it happened to arrive with.
/// </summary>
public static class FlashDrag
{
    /// <summary>A press that stays inside this many px of the grab point can still be a tap.</summary>
    public const double TapMovePx = 6.0;
    /// <summary>...and only if it was this short.</summary>
    public const double TapMs = 250.0;
    /// <summary>A pointer sample older than this contributes no velocity.</summary>
    public const double StaleMs = 120.0;
    /// <summary>Below this the release is a placement, not a throw.</summary>
    public const double FlingThresholdPxPerMs = 0.5;
    /// <summary>Share of the speed that survives a bounce (25% goes into the wall).</summary>
    public const double BounceKeep = 0.75;
    /// <summary>Share of the speed friction leaves after one second of flight.</summary>
    public const double FrictionKeepPerSec = 0.30;
    /// <summary>Below this the flight is over and the flash parks.</summary>
    public const double StopSpeedPxPerSec = 40.0;

    /// <summary>
    /// Take hold of a flash at <paramref name="pointerX"/>, <paramref name="pointerY"/> (world px).
    /// The drag owns the rect for as long as it lasts, so the state converts to the rect-driven
    /// Drift and Bounce shape at rest: a pendulum's rope maths would fight the pointer, and the
    /// renderer must stop rotating a picture the hand is holding. It does not swing again
    /// afterwards - you put it somewhere, and there it stays.
    /// </summary>
    public static FlashDragState Begin(FlashMotionState s, double pointerX, double pointerY, double nowMs)
    {
        s.MediaW = s.W;
        s.MediaH = s.H;
        s.Style = FlashMotionStyle.DriftBounce;
        s.Vx = 0;
        s.Vy = 0;
        var d = new FlashDragState
        {
            Held = true,
            GrabDx = s.X - pointerX,
            GrabDy = s.Y - pointerY,
            StartX = pointerX, StartY = pointerY, StartMs = nowMs,
            LastX = pointerX, LastY = pointerY, LastMs = nowMs,
        };
        s.Drag = d;
        return d;
    }

    /// <summary>
    /// Record one pointer position. Two samples inside the same millisecond keep the older
    /// predecessor rather than dividing by a zero interval.
    /// </summary>
    public static void Sample(FlashDragState d, double x, double y, double nowMs)
    {
        lock (d.Sync)
        {
            if (!d.Held) return;
            if (nowMs > d.LastMs)
            {
                d.PrevX = d.LastX; d.PrevY = d.LastY; d.PrevMs = d.LastMs;
                d.HasPrev = true;
            }
            d.LastX = x; d.LastY = y; d.LastMs = nowMs;
            var travel = Length(x - d.StartX, y - d.StartY);
            if (travel > d.MaxTravelPx) d.MaxTravelPx = travel;
        }
    }

    /// <summary>A press this small and this short is a click, not a drag.</summary>
    public static bool IsTap(double travelPx, double elapsedMs)
        => travelPx < TapMovePx && elapsedMs < TapMs;

    /// <summary>Velocity in px/ms across the last move, or zero when that move has gone stale.</summary>
    public static (double Vx, double Vy) SampleVelocity(FlashDragState d, double nowMs)
    {
        lock (d.Sync)
        {
            if (!d.HasPrev) return (0, 0);
            if (nowMs - d.LastMs > StaleMs) return (0, 0);
            var dt = d.LastMs - d.PrevMs;
            if (dt <= 0) return (0, 0);
            return ((d.LastX - d.PrevX) / dt, (d.LastY - d.PrevY) / dt);
        }
    }

    /// <summary>Does this sampled speed (px/ms) clear the throw threshold?</summary>
    public static bool ShouldFling(double vxPerMs, double vyPerMs)
        => Length(vxPerMs, vyPerMs) >= FlingThresholdPxPerMs;

    /// <summary>
    /// Let go at <paramref name="nowMs"/>. A tap and a placement both end the drag then and there;
    /// a fling converts the sampled px/ms into the state's px/s velocity, scaled by
    /// <paramref name="level"/> - Reduced halves the throw, and Off refuses to throw at all, so the
    /// flash simply stays where it was put (dragging itself stays allowed at every level).
    /// </summary>
    public static FlashDragOutcome Release(FlashMotionState s, double nowMs, MotionLevel level)
    {
        var d = s.Drag;
        if (d == null) return FlashDragOutcome.Place;

        double travel, elapsed;
        lock (d.Sync)
        {
            d.Held = false;
            travel = d.MaxTravelPx;
            elapsed = nowMs - d.StartMs;
        }

        if (IsTap(travel, elapsed))
        {
            Settle(s);
            return FlashDragOutcome.Tap;
        }

        var (vx, vy) = SampleVelocity(d, nowMs);
        var scale = FlashMotion.LevelScale(level);
        if (scale <= 0 || !ShouldFling(vx, vy))
        {
            Settle(s);
            return FlashDragOutcome.Place;
        }

        s.Vx = vx * 1000.0 * scale;   // px/ms to px/s
        s.Vy = vy * 1000.0 * scale;
        d.Flinging = true;
        return FlashDragOutcome.Fling;
    }

    /// <summary>
    /// One tick of a drag or a fling, <paramref name="dt"/> seconds. True when the rect moved,
    /// which is exactly when the compositor layer must go dirty. Once the flight has slowed below
    /// <see cref="StopSpeedPxPerSec"/> the drag state is dropped and the flash holds its spot for
    /// the rest of its dwell.
    /// </summary>
    public static bool Step(FlashMotionState s, double dt)
    {
        var d = s.Drag;
        if (d == null || dt <= 0) return false;

        if (d.Held)
        {
            double px, py;
            lock (d.Sync) { px = d.LastX; py = d.LastY; }
            var hx = px + d.GrabDx;
            var hy = py + d.GrabDy;
            if (hx == s.X && hy == s.Y) return false;
            s.X = hx;
            s.Y = hy;
            return true;
        }

        var (x, vx, bouncedX) = FlashMotion.Reflect(s.X + s.Vx * dt, s.W, s.BoundsX, s.BoundsW, s.Vx);
        var (y, vy, bouncedY) = FlashMotion.Reflect(s.Y + s.Vy * dt, s.H, s.BoundsY, s.BoundsH, s.Vy);
        if (bouncedX) vx *= BounceKeep;
        if (bouncedY) vy *= BounceKeep;

        var keep = Math.Pow(FrictionKeepPerSec, dt);
        vx *= keep;
        vy *= keep;

        var moved = x != s.X || y != s.Y;
        s.X = x; s.Y = y; s.Vx = vx; s.Vy = vy;

        if (Length(vx, vy) < StopSpeedPxPerSec) Settle(s);
        return moved;
    }

    /// <summary>The hand is off and nothing is flying: park the item where it is.</summary>
    private static void Settle(FlashMotionState s)
    {
        s.Vx = 0;
        s.Vy = 0;
        s.Drag = null;
    }

    private static double Length(double x, double y) => Math.Sqrt(x * x + y * y);
}
