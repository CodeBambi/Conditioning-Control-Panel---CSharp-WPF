namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Immutable description of a chaos-mode effect bubble. The engine assigns <see cref="Id"/>.
/// </summary>
public sealed class ChaosBubbleSpec
{
    /// <summary>Unique bubble identifier assigned by the engine.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Variant identifier used by art/sfx lookup.</summary>
    public string VariantId { get; init; } = "";

    /// <summary>High-level payload kind: Flash, Subliminal, Overlay, BambiFreeze, Video, GifCascade.</summary>
    public string PayloadKind { get; init; } = "";

    /// <summary>Optional overlay style, e.g. "pink_filter", "spiral", "braindrain".</summary>
    public string? OverlayKind { get; init; }

    /// <summary>Visual diameter in device-independent pixels.</summary>
    public double SizePx { get; init; } = 100.0;

    /// <summary>Tint red channel.</summary>
    public byte TintR { get; init; }

    /// <summary>Tint green channel.</summary>
    public byte TintG { get; init; }

    /// <summary>Tint blue channel.</summary>
    public byte TintB { get; init; }

    /// <summary>Label displayed on top of the bubble.</summary>
    public string Label { get; init; } = "";

    /// <summary>True for live bombs with a defuse fuse.</summary>
    public bool IsLive { get; init; }

    /// <summary>Fuse length in milliseconds for live bubbles.</summary>
    public int FuseMs { get; init; }

    /// <summary>Movement pattern for the bubble.</summary>
    public ChaosMotion Motion { get; init; } = ChaosMotion.FloatUp;

    /// <summary>Freezing touch flag (Stage 2a stub).</summary>
    public bool IsFreeze { get; init; }

    /// <summary>Golden treat flag.</summary>
    public bool IsGolden { get; init; }

    /// <summary>Fast darter flag.</summary>
    public bool IsDarter { get; init; }

    /// <summary>Heart-shaped/treat flag.</summary>
    public bool IsHeart { get; init; }

    /// <summary>Droplet/rain flag.</summary>
    public bool IsDroplet { get; init; }

    /// <summary>Prism splitter flag (Stage 2a stub).</summary>
    public bool IsPrism { get; init; }

    /// <summary>Brittle one-hit flag (Stage 2a stub).</summary>
    public bool IsBrittle { get; init; }

    /// <summary>Sweeper arc flag (Stage 2a stub).</summary>
    public bool IsSweeper { get; init; }

    /// <summary>Echo duplicate flag (Stage 2a stub).</summary>
    public bool IsEcho { get; init; }

    /// <summary>Chaperone live escort flag (Stage 2a stub).</summary>
    public bool IsChaperoneLive { get; init; }

    /// <summary>Escort bubble flag (Stage 2a stub).</summary>
    public bool IsEscort { get; init; }

    /// <summary>Tease bubble flag (Stage 2a stub).</summary>
    public bool IsTease { get; init; }

    /// <summary>Bound-to-half-screen flag (Stage 2a stub).</summary>
    public bool IsBoundHalf { get; init; }

    /// <summary>Explicit spawn X, or null for automatic placement.</summary>
    public double? SpawnAtPxX { get; init; }

    /// <summary>Explicit spawn Y, or null for automatic placement.</summary>
    public double? SpawnAtPxY { get; init; }

    /// <summary>Score/currency multiplier granted on pop.</summary>
    public double PayMult { get; init; } = 1.0;

    /// <summary>Speed multiplier applied to the base motion velocity.</summary>
    public double SpeedMult { get; init; } = 1.0;

    /// <summary>Override lifetime for treat bubbles in milliseconds.</summary>
    public int TreatLifeMs { get; init; }

    /// <summary>Total lifetime in milliseconds (defaults to 8s).</summary>
    public int LifetimeMs { get; init; } = 8000;

    /// <summary>Pre-spawn telegraph delay in milliseconds.</summary>
    public int TelegraphMs { get; init; }

    /// <summary>Quick-action bonus window in milliseconds.</summary>
    public int QuickWindowMs { get; init; }

    /// <summary>Darter speed override in DIPs per second.</summary>
    public double DarterSpeed { get; init; }

    /// <summary>Maximum wall bounces before a darter vanishes.</summary>
    public int DarterMaxBounces { get; init; } = 3;

    /// <summary>Pair identifier for linked bubbles (Stage 2a stub).</summary>
    public int PairId { get; init; }

    /// <summary>Milliseconds the second half of a bound pair has to be defused before enraging.</summary>
    public int BoundWindowMs { get; init; } = 3500;

    /// <summary>Mimic target variant identifier (Stage 2a stub).</summary>
    public string MimicVariantId { get; init; } = "";

    /// <summary>True if the bubble should receive a spotlight highlight.</summary>
    public bool Spotlight { get; init; }

    /// <summary>Visual/effect intensity multiplier (0..1+). WPF parity; safe default 1.0.</summary>
    public double EffectIntensity { get; init; } = 1.0;

    /// <summary>Chance this bubble drifts sideways during motion. WPF parity.</summary>
    public double SideDriftChance { get; init; }

    /// <summary>Payload strength 0..100, computed at build time from the CLASSIC (pre-scale)
    /// size and already scaled by effect intensity — WPF parity: <c>payload.Strength =
    /// (int)clamp(round(clamp((classicSize-150)/170, 0, 1)*100) * effectIntensity, 0, 100)</c>
    /// (WPF ChaosBubbleVariants.cs Build/EffectBubbleSpec.Strength). Consumers should read
    /// this instead of re-deriving strength from <see cref="SizePx"/> (the visual size is
    /// shrunk by the global field scale, so a SizePx-derived value would be wrong).</summary>
    public int Strength { get; init; }
}
