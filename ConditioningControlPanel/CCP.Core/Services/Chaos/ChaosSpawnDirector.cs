namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Pure spawn-director math extracted verbatim from the WPF chaos engine's SpawnTick
/// (WPF ChaosModeService.cs:1103-1230) so the cadence, density and gating formulas are
/// Core-testable and shared by any head. The service composes these pieces exactly like
/// the WPF tick does; NO formula may be inlined back into service code
/// (contract: docs/chaos-run-engine-contracts/spawn-system.md §1).
/// All state (intensity, difficulty, knobs) arrives as parameters — nothing here reads
/// run state or randomness of its own.
/// </summary>
public static class ChaosSpawnDirector
{
    /// <summary>Chaos motion/fuse speed while darter slow-mo is active; also stretches the
    /// spawn cadence (lower = stronger slow). WPF keeps this as a private service const
    /// (WPF ChaosModeService.cs:2323 <c>SLOWMO_FACTOR = 0.12</c>).</summary>
    public const double SLOWMO_FACTOR = 0.12;

    /// <summary>Real-time length of the darter slow-mo window
    /// (WPF ChaosModeService.cs:2324 <c>SLOWMO_DURATION_SEC = 6.0</c>).</summary>
    public const double SLOWMO_DURATION_SEC = 6.0;

    /// <summary>Hard floor on the refill cadence (WPF ChaosModeService.cs:1227).</summary>
    public const double SPAWN_INTERVAL_FLOOR_MS = 280;

    /// <summary>No video bubble when the loop has less than this left — the bubble's fuse
    /// plus the 15s video slice would not fit (WPF ChaosModeService.cs:1131-1132).</summary>
    public const double VIDEO_STRIP_WAVE_LEFT_SEC = 14;

    /// <summary>No video bubble when the run has less than this left (WPF ChaosModeService.cs:1132).</summary>
    public const double VIDEO_STRIP_RUN_LEFT_SEC = 18;

    /// <summary>
    /// Effective intensity for picks/strength/behavioral rolls: raw run intensity plus a
    /// flat difficulty bias — <c>clamp(intensity + (difficultyMult - 1.0) * 0.15, 0, 1)</c>
    /// (WPF ChaosModeService.cs:1111). Easy +0.0, Medium +0.045, Hard +0.105, Extreme +0.18.
    /// Density and cadence keep using the RAW intensity (WPF ChaosModeService.cs:1107-1109).
    /// </summary>
    public static double EffIntensity(double intensity, double difficultyMult) =>
        Math.Clamp(intensity + (difficultyMult - 1.0) * 0.15, 0, 1);

    /// <summary>
    /// Field density cap: <c>round((6 + intensity*10) * sqrt(difficultyMult))</c>
    /// (WPF ChaosModeService.cs:1117). Easy 6→16, Extreme 9→24. Gates behavioral + ordinary
    /// spawns only; darters and golden/prism/brittle riders are NOT capped
    /// (WPF ChaosModeService.cs:1120-1122, 1170-1214).
    /// </summary>
    public static int MaxConcurrent(double intensity, double difficultyMult) =>
        (int)Math.Round((6 + intensity * 10) * Math.Sqrt(difficultyMult));

    /// <summary>
    /// Self-retuning refill cadence in milliseconds (WPF ChaosModeService.cs:1219-1227):
    /// <c>(1000 - intensity*680) / difficultyMult</c>, divided by
    /// <c>clamp(spawnRateMult, 0.1, 10)</c> (more spawns = shorter gap), divided by
    /// <see cref="SLOWMO_FACTOR"/> while slow-mo runs (cadence stretches ~8x), then floored
    /// at 280ms. The WPF tick multiplies the floored value by its perf-governor backoff
    /// (<c>* _perfBackoff</c>, WPF ChaosModeService.cs:1227) — heads that have a governor
    /// pass it via <paramref name="perfBackoff"/>; heads without one pass 1.0.
    /// </summary>
    public static double SpawnIntervalMs(double intensity, double difficultyMult,
        double spawnRateMult, bool slowMoActive, double perfBackoff = 1.0)
    {
        double interval = (1000 - intensity * 680) / difficultyMult;
        interval /= Math.Clamp(spawnRateMult, 0.1, 10.0);
        if (slowMoActive) interval /= SLOWMO_FACTOR;
        return Math.Max(SPAWN_INTERVAL_FLOOR_MS, interval) * perfBackoff;
    }

    /// <summary>
    /// End-of-loop video strip predicate (WPF ChaosModeService.cs:1127-1134): strip the
    /// <c>video</c> variant from the enabled pool while a heavy effect (video/gif cascade)
    /// runs, or when the loop/run is too close to its end for the bubble's fuse plus the
    /// 15s video slice to fit. A null pool (= all variants) is never stripped in WPF — the
    /// check requires <c>enabled != null &amp;&amp; enabled.Contains("video")</c>.
    /// </summary>
    public static bool ShouldStripVideo(IReadOnlyCollection<string>? enabled,
        bool heavyEffectActive, double waveLeftSec, double runLeftSec) =>
        enabled != null && enabled.Contains("video")
        && (heavyEffectActive || waveLeftSec < VIDEO_STRIP_WAVE_LEFT_SEC || runLeftSec < VIDEO_STRIP_RUN_LEFT_SEC);

    /// <summary>
    /// Gentle stays gentle by HALVING every behavioral/brittle roll instead of forbidding the
    /// menagerie outright — <c>Easy ? 0.5 : 1.0</c> (WPF ChaosModeService.cs:1247 and the
    /// brittle rider WPF ChaosModeService.cs:1189-1190).
    /// </summary>
    public static double GentleMult(bool easyDifficulty) => easyDifficulty ? 0.5 : 1.0;

    /// <summary>
    /// Per-tick chance for one behavioral bubble roll: the tuning-table base chance times
    /// the gentle multiplier (WPF ChaosModeService.cs:1249-1307 — every roll is
    /// <c>Random &lt; CHANCE * gentleMult</c>).
    /// </summary>
    public static double BehavioralChance(double baseChance, bool easyDifficulty) =>
        baseChance * GentleMult(easyDifficulty);

    /// <summary>
    /// Side entries arm only after the first few ordinary spawns — the classic bottom rise
    /// opens the run, then the field starts coming at you sideways too
    /// (WPF ChaosModeService.cs:1145-1148): 0 for the first
    /// <see cref="ChaosTuning.SIDE_DRIFT_GRACE_SPAWNS"/> ordinary spawns, then
    /// <see cref="ChaosTuning.SIDE_DRIFT_CHANCE"/>.
    /// </summary>
    public static double SideDriftChance(int ordinarySpawnsSoFar) =>
        ordinarySpawnsSoFar < ChaosTuning.SIDE_DRIFT_GRACE_SPAWNS ? 0 : ChaosTuning.SIDE_DRIFT_CHANCE;
}
