using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The pure half of ChaosModeService's run loop: the 4 Hz clock, the end/Relapse rule, wave
/// scheduling and the payout scalars. Every effect (announces, barks, overlays, drafts, the
/// save) stays in the head, which calls these in the same order RunTick always ran them.
/// No timers here: the caller steps the clock, so a test drives a whole run in a loop.
/// </summary>
public static class ChaosRunEngine
{
    /// <summary>RunTick fires every 250 ms; each tick advances the run clock by this much.</summary>
    public const double TICK_SEC = 0.25;
    /// <summary>Lust (heat) cools by this much per tick.</summary>
    public const double HEAT_DECAY_PER_TICK = 0.0015;
    /// <summary>"the hole is closing…" fires once when this little run time is left.</summary>
    public const double ENDING_SOON_SEC = 10;
    /// <summary>A loop (wave) per this many in the act: DEPTH I = loops 1-5, II = 6-10, …</summary>
    public const int WAVES_PER_ACT = 5;

    public enum EndCheck { Running, Relapse, RunOver }

    /// <summary>One clock tick: elapsed += dt, lust cools. Returns the new elapsed.</summary>
    public static double Advance(ChaosRunState s, double dt = TICK_SEC)
    {
        double elapsed = s.ElapsedSec + dt;
        s.ElapsedSec = elapsed;
        s.Heat = Math.Max(0, s.Heat - HEAT_DECAY_PER_TICK);
        return elapsed;
    }

    public static bool IsEndingSoon(ChaosRunState s, double elapsed) => s.RunDurationSec - elapsed <= ENDING_SOON_SEC;

    /// <summary>At the run's end: an armed, unspent Relapse buys one more loop (extends the
    /// run now), otherwise the run is over.</summary>
    public static EndCheck CheckEnd(ChaosRunState s, double elapsed)
    {
        if (elapsed < s.RunDurationSec) return EndCheck.Running;
        if (s.RelapseLoopArmed && !s.RelapseLoopActive) { s.ExtendOneLoop(); return EndCheck.Relapse; }
        return EndCheck.RunOver;
    }

    /// <summary>The wave the clock is in (1-based, capped at WaveCount) and the wave length;
    /// writes the in-wave progress the HUD bar shows.</summary>
    public static (int Wave, double WaveLen) WaveAt(ChaosRunState s, double elapsed)
    {
        double waveLen = (double)s.RunDurationSec / s.WaveCount;
        s.WaveProgress = (elapsed % waveLen) / waveLen;
        return (Math.Min(s.WaveCount, 1 + (int)(elapsed / waveLen)), waveLen);
    }

    public static int ActFor(int wave) => 1 + (wave - 1) / WAVES_PER_ACT;

    // ---- payout scalars ----
    /// <summary>A pop's base points by bubble strength: 40..200.</summary>
    public static double BasePoints(int strength) => 40 + strength * 1.6;
    /// <summary>Relapse's bonus loop pays double gold — every gold bank routes through here.</summary>
    public static int GoldScaled(ChaosRunState? s, int gold) => s?.RelapseLoopActive == true ? gold * 2 : gold;
    /// <summary>Drip-feed drops per pop, doubled in the Relapse loop.</summary>
    public static int DropsPerPop(ChaosRunState? s) => (s?.DropPerPop ?? 0) * (s?.RelapseLoopActive == true ? 2 : 1);
}
