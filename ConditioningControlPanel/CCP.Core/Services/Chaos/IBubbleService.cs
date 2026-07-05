using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Public surface for the ambient bubble popping service.
/// Stage 1 covers ambient clickable bubbles; Chaos mode hooks are stubbed.
/// </summary>
public interface IBubbleService
{
    /// <summary>Raised when a bubble is popped by the player.</summary>
    event Action? OnBubblePopped;

    /// <summary>Raised when a bubble floats off screen without being popped.</summary>
    event Action? OnBubbleMissed;

    /// <summary>True when the ambient bubble loop is active.</summary>
    bool IsRunning { get; }

    /// <summary>True when the service is paused and cleared.</summary>
    bool IsPaused { get; }

    /// <summary>Number of bubbles currently alive.</summary>
    int ActiveBubbles { get; }

    /// <summary>Starts the ambient spawn loop.</summary>
    void Start();

    /// <summary>Stops the spawn loop and removes all bubbles.</summary>
    void Stop();

    /// <summary>Clears the field and pauses spawning without tearing down timers.</summary>
    void PauseAndClear();

    /// <summary>Resumes from a paused state.</summary>
    void Resume();

    /// <summary>Re-reads settings and restarts the spawn timer.</summary>
    void RefreshFrequency();

    /// <summary>Spawns a single ambient bubble immediately if the service is running.</summary>
    void SpawnOnce();

    /// <summary>Pops every currently alive ambient bubble.</summary>
    void PopAllBubbles();

    // ---- Chaos-mode stubs required by IAvaloniaBubbleService ----

    /// <summary>Tail-Plug trail seconds; Stage 1 always returns 0.</summary>
    double ChaosRabbitTrailSecNow { get; }

    /// <summary>Sets the Tail-Plug trail duration for the current chaos run.</summary>
    void SetRabbitTrailSec(double seconds);

    /// <summary>Pops bubbles intersecting the given DIP rectangle and returns how many were popped.</summary>
    int PopBubblesInRect(PixelRect rectDips);

    /// <summary>True if any darter intersects the rectangle; Stage 1 always returns false.</summary>
    bool AnyDarterIntersects(PixelRect rectDips);

    // ---- Stage 2a chaos mode hooks ----

    /// <summary>
    /// Enters chaos mode and wires callbacks for benign pops, defuses, detonations,
    /// and (Avalonia port) hold-to-defuse channel start/broken events.
    /// </summary>
    void BeginChaosMode(
        Action<ChaosBubbleSpec> onBenignPop,
        Action<ChaosBubbleSpec, double, bool> onDefuse,
        Action<ChaosBubbleSpec> onDetonate,
        Func<ChaosBubbleSpec, bool>? canChannel = null,
        Action<ChaosBubbleSpec>? onChannelStarted = null,
        Action<ChaosBubbleSpec, string>? onChannelBroken = null);

    /// <summary>
    /// Enters chaos mode with the FULL behavioral callback set the Core
    /// <see cref="BubbleEngine"/> supports (darter/freeze catches, chaperone shield,
    /// bound enrage, tease touch/denial, brittle shatter, treat expiry, spanker) — the
    /// seam the WPF 26-argument <c>BubbleService.BeginChaosMode</c> maps onto
    /// (WPF ChaosModeService.cs:361-381). Default implementation degrades to the
    /// 6-callback overload, dropping the behavioral callbacks, so existing fakes keep
    /// compiling and behaving.
    /// </summary>
    void BeginChaosMode(
        Action<ChaosBubbleSpec> onBenignPop,
        Action<ChaosBubbleSpec, double, bool> onDefuse,
        Action<ChaosBubbleSpec> onDetonate,
        Action<ChaosBubbleSpec, bool>? onDarterCaught = null,
        Action<ChaosBubbleSpec>? onFreezeCaught = null,
        Action<ChaosBubbleSpec, bool>? onChaperoneShieldBroken = null,
        Action<ChaosBubbleSpec>? onBoundEnraged = null,
        Action<ChaosBubbleSpec>? onTeaseTouched = null,
        Action<ChaosBubbleSpec>? onTeaseDenied = null,
        Action<ChaosBubbleSpec>? onBrittleShattered = null,
        Action<ChaosBubbleSpec>? onTreatExpired = null,
        Action<ChaosBubbleSpec, bool>? onDarterSpanked = null,
        double chainReachDip = 120.0,
        Func<ChaosBubbleSpec, bool>? canChannel = null,
        Action<ChaosBubbleSpec>? onChannelStarted = null,
        Action<ChaosBubbleSpec, string>? onChannelBroken = null,
        // Owner-authorized Q10b arc slice: the E-Stim discharge bolt callback. The default impl
        // below degrades to the 6-arg overload (dropping this, like the behavioral callbacks);
        // the real AvaloniaBubbleService impl forwards it to BubbleEngine.
        Action<IReadOnlyList<(Point From, Point To)>>? onEStimArc = null,
        // Owner-authorized: the darter/sweeper sparkle-trail callback. Also dropped by the default
        // impl below (degrades to the 6-arg overload); the real AvaloniaBubbleService forwards it.
        Action<Point, double, bool>? onDarterTrail = null)
        => BeginChaosMode(onBenignPop, onDefuse, onDetonate, canChannel, onChannelStarted, onChannelBroken);

    /// <summary>Leaves chaos mode and destroys all chaos bubbles.</summary>
    void EndChaosMode();

    /// <summary>Queues a chaos bubble for materialization.</summary>
    void SpawnChaosBubble(ChaosBubbleSpec spec);

    /// <summary>Spawns a Chaperone pair: the live is shielded while its escort orbits
    /// (WPF BubbleService.cs:1150 <c>SpawnChaosChaperone</c>). Default implementation
    /// degrades to two unlinked spawns so fakes keep compiling.</summary>
    void SpawnChaosChaperone(ChaosBubbleSpec live, ChaosBubbleSpec escort)
    {
        SpawnChaosBubble(live);
        SpawnChaosBubble(escort);
    }

    /// <summary>Spawns a tethered Bound pair — both halves must be defused quickly
    /// (WPF BubbleService <c>SpawnChaosBoundPair</c>, consumed WPF ChaosModeService.cs:1299).
    /// Default implementation degrades to two unlinked spawns.</summary>
    void SpawnChaosBoundPair(ChaosBubbleSpec a, ChaosBubbleSpec b)
    {
        SpawnChaosBubble(a);
        SpawnChaosBubble(b);
    }

    /// <summary>Freeze pickups currently on screen — the spawn director's
    /// FREEZE_MAX_ON_SCREEN re-pick reads this (WPF ChaosModeService.cs:1155-1162).
    /// Default 0 (fakes never hit the cap).</summary>
    int ActiveFreezeBubbles => 0;

    /// <summary>Live run knobs the chaos engine reads at use sites (WPF live-lambda
    /// equivalent, ChaosModeService.cs:361-381). The chaos service mutates these when
    /// upgrades/boons change mid-run. Default null (fakes/heads without a chaos engine).</summary>
    ChaosRunKnobs? ChaosKnobs => null;

    /// <summary>Engine-logical X of the last chaos pop — spawn-at-pop-point consumers pin here
    /// (WPF BubbleService.cs:120-122 <c>ChaosLastPopXPx</c>). Default 0.</summary>
    double ChaosLastPopX => 0;

    /// <summary>Engine-logical Y of the last chaos pop (WPF BubbleService.cs:120-122). Default 0.</summary>
    double ChaosLastPopY => 0;

    /// <summary>Pauses or resumes chaos bubble physics.</summary>
    void SetChaosFrozen(bool frozen);

    /// <summary>Adjusts the chaos simulation speed multiplier.</summary>
    void SetChaosTimeScale(double scale);

    /// <summary>Locks or unlocks chaos input handling.</summary>
    void SetChaosInputLocked(bool locked);

    // ---- Active-toy APIs (Avalonia parity) ----

    /// <summary>Enables or disables the VibePopping sweep mode. While active, left-clicks within
    /// the sweep radius pop nearby chaos bubbles instantly (live bubbles snap for full pay).</summary>
    void SetVibePop(bool active, bool hoverPops = false);

    /// <summary>Briefly vibrates all chaos bubble windows to telegraph a freeze.</summary>
    void VibrateAllForFreeze(int durationMs);

    /// <summary>Instantly defuses every live chaos bubble currently on screen.</summary>
    void DefuseAllLive();

    /// <summary>Pops every paid chaos bubble (treats + lives) currently on screen.</summary>
    void PopAllChaosPaid();

    /// <summary>Arms the E-Stim effect for the next N bubble clicks/detonations.</summary>
    void ArmEStim(int charges, bool chainReaction = false);

    /// <summary>Remaining E-Stim charges; 0 when unarmed.</summary>
    int EStimChargesLeft { get; }

    /// <summary>Casts a player ripple wave from the given physical-pixel centre.</summary>
    void TriggerPlayerRipple(Point centerPx, double radiusPx, double lifeMs);

    /// <summary>Plays a soft gold/loot chime (WPF parity). Stage 1 is a no-op.</summary>
    void PlayChime(float volumeScale = 0.3f);
}
