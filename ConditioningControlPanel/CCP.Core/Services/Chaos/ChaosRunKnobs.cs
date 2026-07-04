namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Live run knobs the engine reads AT USE SITES (never cached) — the cross-platform
/// equivalent of the live lambdas WPF ChaosModeService passes into BeginChaosMode
/// (WPF ChaosModeService.cs:361-381: hitboxScale, liveMagnet, bubbleOpacity, cursorPull,
/// rabbitHoming, spankerOn, spankGrow, electrifiedRabbits, chainReach, rabbitTrailSec).
/// The chaos service holds the engine's instance (via <c>IBubbleService.ChaosKnobs</c>) and
/// mutates properties whenever owned upgrades or drafted boons change mid-run, so effects
/// take hold immediately instead of only at run start. Defaults are the WPF no-upgrade values.
/// wandShimmer is deliberately absent: magic_wand was retired 2026-06-10
/// (WPF ChaosModeService.cs:370 hard-codes it to false).
/// </summary>
public sealed class ChaosRunKnobs
{
    /// <summary>The Spanker toy is active: darters are SMACKED, never caught
    /// (WPF BubbleService.cs:3706-3708). WPF lambda: <c>() =&gt; _state?.SpankerActive == true</c>
    /// (ChaosModeService.cs:373). Default off — the toy arms it per-run.</summary>
    public bool SpankerOn { get; set; }

    /// <summary>Chain Reaction reach in engine DIPs from the popped bubble's centre (0 = off).
    /// WPF lambda: <c>() =&gt; _state?.ChainReactionReach ?? 0</c> (ChaosModeService.cs:367), a
    /// box-MULTIPLE consumed by grown-box intersect in WPF ChainPopNeighbors
    /// (BubbleService.cs:1610-1613, &lt;=1 = off); this engine's pre-existing ChainPop is a
    /// centre-distance DIP radius, so the service maps the multiple onto DIPs when it syncs.
    /// Default 0 (WPF no-boon = no chaining); BeginChaosMode seeds it from its
    /// <c>chainReachDip</c> parameter for existing callers/fakes.</summary>
    public double ChainReachDip { get; set; }

    /// <summary>Hitbox multiple sampled AT SPAWN per bubble (silk_touch 1.25 / Mesmer Reach).
    /// WPF lambda: <c>() =&gt; _state?.Config?.HitboxScale ?? 1.0</c> (ChaosModeService.cs:368);
    /// consumed once in the Bubble ctor: <c>hitMult = Clamp(hitboxScale, 1.0, 2.0)</c> and
    /// <c>_hitSize = plainEffectBubble ? Max(size, Round(size*hitMult)) : size</c>
    /// (WPF BubbleService.cs:2539-2541). Default 1.0.</summary>
    public double HitboxScale { get; set; } = 1.0;

    /// <summary>Blindfold: effect-bubble opacity multiplier sampled AT SPAWN per bubble.
    /// WPF lambda: <c>() =&gt; _state?.BlindfoldActive == true ? _state.BlindfoldOpacity : 1.0</c>
    /// (ChaosModeService.cs:369); consumed once in the Bubble ctor:
    /// <c>_baseOpacity = plainEffectBubble ? Clamp(opacityMult, 0.2, 1.0) : 1.0</c>
    /// (WPF BubbleService.cs:2542) — pickups/rewards stay fully visible. Default 1.0.</summary>
    public double BubbleOpacity { get; set; } = 1.0;

    /// <summary>The Pull / Cam Girl: signed per-WPF-frame (32ms) DIP drift toward (+) or away
    /// from (−) the cursor for ordinary chaos bubbles. WPF lambda:
    /// <c>() =&gt; (_state?.CursorPullStrength ?? 0) - (_state?.CamGirlFlee ?? 0)</c>
    /// (ChaosModeService.cs:371); consumed per-frame in Bubble.AnimateFrame
    /// (WPF BubbleService.cs:3213-3247: 30-DIP dead zone when pulling, 260-DIP
    /// distance-faded flee with screen clamp when negative). Default 0 = off.</summary>
    public double CursorPull { get; set; }

    /// <summary>The Pull: darters steer toward the cursor with a capped turn rate
    /// (0.065 rad/frame, WPF BubbleService.cs:3023-3039). WPF lambda:
    /// <c>() =&gt; (_state?.CursorPullStrength ?? 0) &gt; 0</c> (ChaosModeService.cs:372).
    /// Default off.</summary>
    public bool RabbitHoming { get; set; }

    /// <summary>The Spanker: one-time swell factor stamped on a darter's FIRST smack —
    /// <c>_spankGrowth = Max(1.0, spankGrow)</c>, re-smacks never re-grow
    /// (WPF BubbleService.cs:3794-3796). WPF lambda:
    /// <c>() =&gt; _state?.SpankGrowFactor ?? 1.0</c> (ChaosModeService.cs:374), clamped
    /// <c>Max(1.0, …)</c> at the per-tick sample (WPF BubbleService.cs:489). Default 1.0.</summary>
    public double SpankGrow { get; set; } = 1.0;

    /// <summary>Silk Touch: a near-miss on a LIVE still touches — the hit ellipse of live
    /// bubbles is widened ×1.4 (2.0 ceiling) AT SPAWN (WPF BubbleService.cs:2540). WPF lambda:
    /// <c>() =&gt; _state?.MagnetEnabled == true</c> (ChaosModeService.cs:375). Default off.</summary>
    public bool LiveMagnet { get; set; }

    /// <summary>Tail-Plug: seconds of treat-popping trail rabbits drag (0 = boon not taken).
    /// WPF lambda: <c>() =&gt; _state?.RabbitTrailSec ?? 0</c> (ChaosModeService.cs:378), clamped
    /// <c>Max(0, …)</c> at the per-tick sample (WPF BubbleService.cs:490); consumed by the trail
    /// recorder (WPF :3078-3103, sweepers always drag <c>Max(0.5, now)</c>) and the trail-pop
    /// sweep (WPF :1427-1441). Default 0.</summary>
    public double RabbitTrailSec { get; set; }

    /// <summary>Electrified Rabbits (Spanker + E-Stim duo): every bubble a spanked rabbit's
    /// body mows also discharges free E-Stim arcs into its neighbours
    /// (WPF BubbleService.cs:1576 <c>if (ChaosElectrifiedNow) EStimBurstAt(victimPx, 3)</c>).
    /// WPF lambda: <c>() =&gt; _state?.ElectrifiedRabbits == true</c> (ChaosModeService.cs:379).
    /// Default off.</summary>
    public bool ElectrifiedRabbits { get; set; }

    /// <summary>Resets every knob to its WPF no-upgrade default at run start
    /// (WPF BeginChaosMode resets the sampled statics the same way,
    /// BubbleService.cs:1092-1093 and :1106; ClearChaos again at :1674-1687).</summary>
    public void Reset()
    {
        SpankerOn = false;
        ChainReachDip = 0.0;
        HitboxScale = 1.0;
        BubbleOpacity = 1.0;
        CursorPull = 0.0;
        RabbitHoming = false;
        SpankGrow = 1.0;
        LiveMagnet = false;
        RabbitTrailSec = 0.0;
        ElectrifiedRabbits = false;
    }
}
