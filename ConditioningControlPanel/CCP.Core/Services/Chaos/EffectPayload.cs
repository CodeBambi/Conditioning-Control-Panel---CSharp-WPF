using System;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// The kinds of one-shot effect a Chaos bubble can carry. Each maps to a
/// concrete <see cref="EffectPayload"/> that wraps an existing effect trigger.
/// </summary>
public enum EffectBubblePayloadKind
{
    Flash,
    Subliminal,
    Overlay,
    Video,
    HtLink,
    Audio,
    BambiFreeze,
    BouncingText,
    GifCascade
}

/// <summary>
/// A Chaos bubble's payload — the effect that fires when the bubble is popped
/// (benign "treat") or when a live bubble's fuse expires undefused
/// ("detonation"). Concrete payloads are thin wrappers over the app's existing
/// one-shot triggers. <see cref="Strength"/> (0..100, derived from bubble size
/// at spawn) scales the effect: bigger bubble ⇒ stronger payload.
/// </summary>
public abstract class EffectPayload
{
    /// <summary>Short human label for the bubble tag, payload feed and bark context.</summary>
    public abstract string DisplayName { get; }

    public abstract EffectBubblePayloadKind Kind { get; }

    /// <summary>0..100, scaled from bubble size at spawn. Drives effect intensity.</summary>
    public int Strength { get; set; }

    /// <summary>True when this payload was built for a dashboard "Trigger Bubble" rather than a
    /// chaos-run bubble (stamped by the head from <see cref="ChaosBubbleSpec.Ambient"/>, itself set
    /// by the <c>ChaosSpawnCatalog.Build</c> ambient branch). Some payloads behave differently on
    /// the calm dashboard — e.g. video must not arm a random start segment there (#456/#458). A
    /// PER-INSTANCE flag, NOT a run-active check at fire time: ambient bubbles can pop while a chaos
    /// run happens to be active, and a run can end between spawn and pop
    /// (WPF EffectPayload.cs:48-54).</summary>
    public bool Ambient { get; set; }

    /// <summary>
    /// Global multiplier on time-based payload durations (flashes/overlays linger). 1.0 = normal.
    /// </summary>
    public static double GlobalDurationMult { get; set; } = 1.0;

    /// <summary>Run the wrapped one-shot effect, scaled by <see cref="Strength"/>.</summary>
    public abstract void Fire();

    /// <summary>Lerp helper: map Strength 0..100 onto [min,max].</summary>
    protected int Scale(int min, int max) =>
        min + (int)Math.Round((max - min) * Math.Clamp(Strength, 0, 100) / 100.0);

    protected double ScaleD(double min, double max) =>
        min + (max - min) * Math.Clamp(Strength, 0, 100) / 100.0;
}
