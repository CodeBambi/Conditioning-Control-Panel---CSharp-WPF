namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Live run knobs the engine reads AT USE SITES (never cached) — the cross-platform
/// equivalent of the live lambdas WPF ChaosModeService passes into BeginChaosMode
/// (WPF ChaosModeService.cs:361-381: hitboxScale, liveMagnet, bubbleOpacity, cursorPull,
/// rabbitHoming, spankerOn, spankGrow, electrifiedRabbits, chainReach, …). The chaos
/// service holds the engine's instance (via <c>IBubbleService.ChaosKnobs</c>) and mutates
/// properties whenever owned upgrades or drafted boons change mid-run, so effects take
/// hold immediately instead of only at run start. Defaults are the WPF no-upgrade values.
/// </summary>
public sealed class ChaosRunKnobs
{
    /// <summary>The Spanker toy is active: darters are SMACKED, never caught
    /// (WPF BubbleService.cs:3706-3708). Default off — the toy arms it per-run.</summary>
    public bool SpankerOn { get; set; }

    /// <summary>Resets every knob to its WPF no-upgrade default at run start.</summary>
    public void Reset()
    {
        SpankerOn = false;
    }
}
