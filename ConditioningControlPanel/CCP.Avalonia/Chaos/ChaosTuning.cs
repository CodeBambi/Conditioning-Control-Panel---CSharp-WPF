namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Focus economy and hold-to-defuse tuning for the Avalonia Chaos Mode port.
/// Mirrors the WPF <c>ConditioningControlPanel.Services.Chaos.ChaosTuning</c> constants
/// where applicable and adds the Avalonia-specific regen knob requested by the port.
/// </summary>
public static class ChaosTuning
{
    // ============================ Focus resource ============================
    public const double FocusMax = 100;
    public const double FocusStart = 50;

    public const double FocusPerPop = 10;      // flash, subliminal, any standard treat
    public const double FocusPerGolden = 12;   // lucky golden bubble
    public const double FocusPerRabbit = 15;   // white rabbit catch
    public const double FocusPerHeavy = 15;    // heavy giant (3x pay, slightly more focus)

    /// <summary>
    /// Passive focus regeneration per second. WPF parity (WPF ChaosTuning.cs:11-13): focus is
    /// EARNED by popping treats and catching rabbits only — there is NO passive regen and no decay.
    /// Kept as a zero constant (not deleted) to document the deliberate WPF-parity decision.
    /// </summary>
    public const double FocusRegenPerSec = 0.0;

    /// <summary>Focus bar turns red and pulses below this value.</summary>
    public const double FocusLowThreshold = 30;

    // ============================ Defuse channel ============================

    /// <summary>Focus spent per COMPLETED channel (deducted on completion, never on start).</summary>
    public const double DefuseCost = 30;

    /// <summary>Per-half defuse cost for a Bound pair — the pair totals one normal defuse.</summary>
    public const double DefuseCostBound = 15;

    /// <summary>Hold this long over a live bubble to defuse it. The fuse pauses meanwhile.</summary>
    public const int DefuseHoldMs = 1000;

    /// <summary>
    /// Press+release faster than this reads as a CLICK (the bubble still detonates;
    /// this threshold only picks the click-detonate feedback over the early-release one).
    /// </summary>
    public const int ClickThresholdMs = 180;

    /// <summary>Legacy alias used by some port code; same semantic as <see cref="ClickThresholdMs"/>.</summary>
    public const double ChannelBreakThresholdSec = 0.15;

    // ============================ Echo / hydra split ============================

    /// <summary>Scale factor applied to an Echo bubble's visual size for each child.</summary>
    public const double EchoChildScale = 0.60;

    /// <summary>Minimum fuse length for an Echo split child (ms).</summary>
    public const int EchoChildFuseMinMs = 2500;

    /// <summary>Maximum fuse length for an Echo split child (ms).</summary>
    public const int EchoChildFuseMaxMs = 4500;

    /// <summary>Speed multiplier for Echo split children so they scatter faster.</summary>
    public const double EchoChildSpeedMult = 1.40;

    /// <summary>Visual shrink floor while channeling (scale 1.0 → this by completion).</summary>
    public const double ChannelMinScale = 0.55;

    /// <summary>Seconds of "focus below cost while lives hang on screen" before the once-per-run warning bark fires.</summary>
    public const double FocusLowBarkSec = 8;

    // ============================ Field pace ============================

    /// <summary>Global chaos bubble speed bump — verticals travel farther before they rot.</summary>
    public const double ChaosSpeedMult = 1.4375;

    /// <summary>Global simulation pace, &lt;1 = slower. Every per-tick motion step and countdown
    /// routes through Bubble.TimeScale, so this one knob uniformly slows travel AND lengthens
    /// fuses/fades for both ambient and chaos bubbles. Pre-6.1.6 the UI thread was heavily loaded,
    /// starving the fixed-step timer; 6.1.6 restored the timer's true ~31 fps, making the field feel
    /// too fast. This constant dials the perceived pace back without reverting the perf wins.</summary>
    public const double FieldPace = 0.8;

    /// <summary>Fuse remaining (ms) at which the ring starts flashing yellow↔red.</summary>
    public const double RingFlashFromMs = 2400;

    /// <summary>Fuse remaining (ms) at which the ring goes solid red.</summary>
    public const double RingBrinkMs = 800;

    // ============================ Active toys ============================

    public const double FREEZE_DURATION_SEC = 3.5;
    public const int FREEZE_VIBRATE_MS = 200;

    public const double VIBE_POP_SWEEP_DIP = 120;

    // ============================ The Ripple ============================
    public const double DEBUT_FUSE_MULT = 1.5;

    public const double RIPPLE_RECHARGE_SEC = 15;
    public const double RIPPLE_RADIUS_PX = 260;
    public const double RIPPLE_LIFE_MS = 520;
    public const double RIPPLE_RADIUS_PER_LVL_PX = 45;
    public const double RIPPLE_LIFE_PER_LVL_MS = 110;
    public const double RIPPLE_TRIGGER_GRACE_PX = 80;
    public const int RIPPLE_WAVE_GAP_MS = 1000;
}
