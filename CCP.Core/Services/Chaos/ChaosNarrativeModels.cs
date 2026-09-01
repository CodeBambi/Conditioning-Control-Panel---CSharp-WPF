using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

// ============================================================================
// Narrative layer authoring contract (the Madam). This is the schema the line
// catalog is written against — see CHAOS_NARRATIVE_PLAN.md Step 2. Do not
// redesign the fields; add lines, not shapes.
// ============================================================================

/// <summary>Who speaks the line. The vertical slice ships madam only; the field stays for later voices.</summary>
public enum ChaosSpeaker { Madam, Rabbit, Hatter, Enemy, Doll }

/// <summary>
/// Band drives priority + interrupt behavior. STORY may interrupt the current line; REACTIVE and
/// AMBIENT wait for the global narrator min-gap. Higher enum value = higher priority.
/// </summary>
public enum ChaosBand { Ambient = 0, Reactive = 1, Story = 2 }

/// <summary>once = play a single time ever (persisted, accretes across descents). pooled = repeatable, cooldown-gated.</summary>
public enum ChaosLineMode { Once, Pooled }

/// <summary>
/// The register a line is WRITTEN in. The director filters by the register computed from context
/// (hub vs descent depth), which is what makes the voice fall as you go deeper.
///   hub          → at the hub, sass
///   descent_high → depths I–II (and III), composed/teasing
///   descent_low  → depths IV–V (and III), low and seductive
/// </summary>
public enum ChaosRegister { Hub, DescentHigh, DescentLow }

/// <summary>How a depth gate is matched.</summary>
public enum ChaosDepthMatch { Min, Exact, Range }

/// <summary>
/// Optional gate — ALL set conditions must pass for a line to be eligible. Null/zero fields are ignored.
/// </summary>
public sealed class ChaosLineGate
{
    /// <summary>Minimum rank index (ChaosUpgrades.RankIndex) required. 0 = ignore.</summary>
    public int RankMin { get; set; } = 0;

    /// <summary>Depth gate kind; null = no depth gate.</summary>
    public ChaosDepthMatch? DepthMatch { get; set; }
    public int DepthA { get; set; }    // Min: minimum; Exact: the depth; Range: low bound
    public int DepthB { get; set; }    // Range: high bound

    /// <summary>If set, line is eligible only when this is the first-ever sighting (per SeenNarrativeLines on the cue id).</summary>
    public bool? FirstTime { get; set; }

    /// <summary>Require a boon/charm id be owned this run. Null = ignore.</summary>
    public string? ItemOwned { get; set; }

    /// <summary>Require the accepted sin's id to match (for sin_accepted). Null = ignore.</summary>
    public string? SinId { get; set; }

    /// <summary>Run-stat threshold, e.g. ("streak", 10) → streak &gt;= 10. Null = ignore.</summary>
    public string? RunStatKey { get; set; }
    public double RunStatMin { get; set; }
}

/// <summary>One authored narrator line. See <see cref="ChaosLineGate"/> for gating.</summary>
public sealed class ChaosNarrativeCue
{
    public string Id { get; set; } = "";                 // stable, e.g. "madam.first_fall"
    public string Trigger { get; set; } = "";            // moment key (run_start, zone_border, ...)
    public ChaosSpeaker Speaker { get; set; } = ChaosSpeaker.Madam;
    public ChaosBand Band { get; set; } = ChaosBand.Reactive;
    public ChaosLineMode Mode { get; set; } = ChaosLineMode.Pooled;
    public ChaosRegister Register { get; set; } = ChaosRegister.DescentHigh;
    public ChaosLineGate? Gates { get; set; }
    public int CooldownMs { get; set; } = 0;             // per-line min gap (pooled lines)
    public int Weight { get; set; } = 1;                 // pooled selection weight within a band bucket
    public string Text { get; set; } = "";               // on-screen line
    public string? AudioKey { get; set; }                // clip id (Bark 3-tier resolver); placeholder ok
}

// ============================================================================
// Conversations — multi-line STORY beats rendered as a Hades-style character card
// (portrait + dialogue box + press-to-advance, field paused). These are the
// punctuation; reactive/ambient single lines (above) stay as overlay text over
// live play. A STORY trigger prefers an eligible conversation over a single line.
// Authoring contract — add conversations, not shapes.
// ============================================================================

/// <summary>once = open a single time ever (persisted on the conversation id). repeatable = re-opens each time its trigger + gates hold.</summary>
public enum ChaosConversationMode { Once, Repeatable }

/// <summary>One spoken beat within a conversation. Advanced by press-forward or after its audio + a short hold.</summary>
public sealed class ChaosConversationLine
{
    public string Text { get; set; } = "";
    public string? AudioKey { get; set; }      // assets/Chaos/narrator/{key}.(mp3|wav); placeholder ok (text-only still ducks)
    public bool Emphasis { get; set; }         // render the line italic (the reference's stressed beats)
}

/// <summary>A character-card conversation. See <see cref="ChaosConversationLine"/> for the lines.</summary>
public sealed class ChaosConversation
{
    public string Id { get; set; } = "";                  // stable; persisted in SeenNarrativeLines when Mode == Once
    public string Trigger { get; set; } = "";             // story moment (hub_return, depthV_enter, ...)
    public ChaosSpeaker Speaker { get; set; } = ChaosSpeaker.Madam;
    public string? Title { get; set; }                    // optional line under the speaker name ("of the Rabbit Hole")
    public ChaosRegister Register { get; set; } = ChaosRegister.Hub;   // Hub = hub-only; descent_* = in-run
    public ChaosConversationMode Mode { get; set; } = ChaosConversationMode.Once;
    public ChaosLineGate? Gates { get; set; }
    public string PortraitId { get; set; } = "madam";     // ChaosArt.Resolve("portraits", PortraitId)
    public bool PortraitOnLeft { get; set; }              // which side the portrait enters from / anchors to
    public List<ChaosConversationLine> Lines { get; set; } = new();
}

/// <summary>Context handed to the director when a moment fires — the live run snapshot the gates read.</summary>
public sealed class ChaosNarrativeContext
{
    public string Trigger { get; set; } = "";
    public int Depth { get; set; } = 1;                  // ActIndex (1..5)
    public int RankIndex { get; set; } = 0;
    public IReadOnlyCollection<string>? OwnedItemIds { get; set; }
    public string? SinId { get; set; }                   // for sin_accepted
    public IReadOnlyDictionary<string, double>? RunStats { get; set; }  // "streak", "sinsAccepted", ...
}
