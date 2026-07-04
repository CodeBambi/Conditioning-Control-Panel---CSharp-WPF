using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Mutable state for a single ambient bubble. Coordinates are in device-independent pixels (DIPs).
/// </summary>
public sealed class BubbleState
{
    /// <summary>Unique bubble identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Index of the screen this bubble was spawned on.</summary>
    public int ScreenIndex { get; init; }

    /// <summary>Screen bounds in DIPs that this bubble is constrained to.</summary>
    public PixelRect ScreenBounds { get; init; }

    /// <summary>Scaling factor for the screen this bubble lives on.</summary>
    public double Scaling { get; init; } = 1.0;

    /// <summary>Horizontal DIP position of the top-left corner.</summary>
    public double X { get; set; }

    /// <summary>Vertical DIP position of the top-left corner.</summary>
    public double Y { get; set; }

    /// <summary>Horizontal DIP velocity per second.</summary>
    public double Vx { get; set; }

    /// <summary>Vertical DIP velocity per second.</summary>
    public double Vy { get; set; }

    /// <summary>Bubble visual size in DIPs.</summary>
    public double Size { get; init; }

    /// <summary>Seconds of life remaining.</summary>
    public double LifeRemainingSec { get; set; }

    /// <summary>Initial life in seconds.</summary>
    public double MaxLifeSec { get; init; }

    /// <summary>True while the pop animation is running.</summary>
    public bool IsPopping { get; set; }

    /// <summary>Visual scale multiplier.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Visual opacity multiplier.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>True if the bubble can be clicked.</summary>
    public bool Clickable { get; init; } = true;

    // ---- Chaos-mode fields (Stage 2a) ----

    /// <summary>Chaos spec attached to this bubble, or null for ambient bubbles.</summary>
    public ChaosBubbleSpec? Spec { get; init; }

    /// <summary>
    /// Trigger Bubbles: an effect payload fired when this ambient bubble is popped. Non-null only
    /// when the user has enabled Trigger Bubbles and the per-bubble roll selected an effect variant.
    /// </summary>
    public EffectPayload? EffectPayload { get; set; }

    /// <summary>Remaining fuse time in milliseconds for live chaos bubbles.</summary>
    public double FuseRemainingMs { get; set; }

    /// <summary>True after a live bubble has detonated.</summary>
    public bool IsDetonated { get; set; }

    /// <summary>True after a live bubble has been successfully defused.</summary>
    public bool IsDefused { get; set; }

    /// <summary>True while the player is holding a defuse channel on this live bubble.</summary>
    public bool IsChanneling { get; set; }

    /// <summary>Parent bubble id for linked/mimic/echo bubbles.</summary>
    public Guid? ParentId { get; init; }

    // ---- Stage 2b advanced variant state ----

    /// <summary>True while a chaperone live bubble is protected by its escort.</summary>
    public bool IsShielded { get; set; }

    /// <summary>For chaperone live bubbles, the id of the escort that shields them.</summary>
    public Guid? ChaperoneEscortId { get; set; }

    /// <summary>For chaperone escort bubbles, the id of the live bubble they protect.</summary>
    public Guid? ChaperoneLiveId { get; set; }

    /// <summary>Pair identifier for bound-half bubbles.</summary>
    public int BoundPairId { get; set; }

    /// <summary>True when the mate of a bound pair has been resolved and the enrage timer is running.</summary>
    public bool BoundHalfResolved { get; set; }

    /// <summary>Remaining milliseconds before the unresolved bound half enrages.</summary>
    public double BoundResolveTimeRemainingMs { get; set; }

    /// <summary>True after a bound half has enraged.</summary>
    public bool BoundEnraged { get; set; }

    /// <summary>Remaining telegraph pause for darter bubbles.</summary>
    public double TelegraphRemainingMs { get; set; }

    /// <summary>True once a darter telegraph has finished and movement begins.</summary>
    public bool TelegraphComplete { get; set; }

    /// <summary>Number of wall bounces a darter has performed.</summary>
    public int BounceCount { get; set; }

    /// <summary>Age of the bubble in milliseconds, used for quick-action checks.</summary>
    public double AgeMs { get; set; }

    /// <summary>True after a darter's first smack — the Spanker first-contact lesson tick
    /// fires only on the first smack (WPF BubbleService.cs:3789 <c>if (!_isSpanked)</c>), so this
    /// latch stops the callback re-firing on later smacks.</summary>
    public bool IsSpanked { get; set; }

    // ---- Stage 2c field hazards ----

    /// <summary>Recent trail points in physical pixels for Tail-Plug rabbit trails.</summary>
    public List<(Point Px, DateTime T)> TrailPoints { get; } = new();

    /// <summary>Last physical pixel position where a trail point was emitted.</summary>
    public Point LastTrailEmitPx { get; set; }
}
