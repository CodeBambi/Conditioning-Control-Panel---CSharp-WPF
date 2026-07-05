using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// First-contact verb hints: a small text pill teaches every chaos bubble whose interaction the
/// player hasn't performed correctly yet ("hold to snap", "click to pop", "do not touch"...). The
/// FIRST correct play of that verb marks it learned in chaos_meta and the pill never shows again.
/// Pure onboarding: no scoring, no gameplay state, only a display key + text and a persisted
/// learned-set. Ported verbatim from WPF ConditioningControlPanel/Services/Chaos/ChaosBubbleHints.cs
/// (KeyFor/TextFor/IsLearned); the WPF class typed its input as the WPF <c>EffectBubbleSpec</c>, this
/// port reads the portable <see cref="ChaosBubbleSpec"/> which carries the same interaction flags.
///
/// The learned-set persistence (MarkLearned + the on-screen HideChaosHints strip) lives head-side —
/// <c>ChaosMeta</c> is an Avalonia-head static facade, and the hint pill has no portable render seam —
/// so this Core class holds only the pure key/text tables and a learned-set predicate. Contract:
/// docs/chaos-run-engine-contracts/spawn-system.md §6.
/// </summary>
public static class ChaosBubbleHints
{
    /// <summary>The learned-set key for a spec's interaction archetype. Per-VARIANT for the regular
    /// pool (every bubble kind gets its own first text); special flags first. Null = no hint (ambient
    /// bubbles, sweepers — nothing for the player to learn). Priority ladder verbatim from WPF
    /// ChaosBubbleHints.cs KeyFor.</summary>
    public static string? KeyFor(ChaosBubbleSpec? spec)
    {
        if (spec == null) return null;
        if (spec.IsSweeper) return null;                          // uncatchable — nothing to teach
        if (spec.IsDarter) return "rabbit";
        if (spec.IsFreeze) return "freeze";
        if (spec.IsTease) return "tease";
        if (spec.IsBrittle) return "brittle";
        if (spec.IsEscort || spec.IsChaperoneLive) return "chaperone";
        if (spec.IsEcho) return "echo";
        if (spec.IsBoundHalf) return "bound";
        if (spec.IsGolden) return "golden";
        if (spec.IsHeart) return "heart";
        if (spec.IsDroplet) return "droplet";
        if (spec.IsPrism) return "prism";
        if (spec.PayMult >= 2.0) return "heavy";                  // Heavy Drop (pays x3)
        return (spec.IsLive ? "live:" : "treat:") + spec.VariantId;
    }

    /// <summary>Hint text for a spec (lowercase, lexicon voice, SHORT). The chaperone pair shares one
    /// learned-key but each half teaches its own side of the lesson. Verbatim from WPF
    /// ChaosBubbleHints.cs TextFor.</summary>
    public static string TextFor(ChaosBubbleSpec? spec)
    {
        if (spec == null) return "";
        if (spec.IsChaperoneLive) return "pop my escort first";
        if (spec.IsEscort) return "pop me first";
        string? key = KeyFor(spec);
        if (key == null) return "";
        if (key.StartsWith("live:", StringComparison.Ordinal)) return "hold to snap";
        if (key.StartsWith("treat:", StringComparison.Ordinal)) return "click to pop";
        return key switch
        {
            "rabbit" => "click to catch",
            "freeze" => "click to freeze",
            "tease" => "don't touch. let it leave",
            "brittle" => "glass. dodge it",
            "echo" => "hold fully or it splits",
            "bound" => "hold both. fast",
            "golden" => "pop for gold",
            "heart" => "click. +1 resistance",
            "droplet" => "catch the gold",
            "prism" => "pop. pays 10x",
            "heavy" => "click. pays x3",
            _ => ""
        };
    }

    /// <summary>True when this verb is already learned (the pill must NOT show). Pure predicate over
    /// the persisted set (head passes <c>ChaosMeta.State.BubbleHintsLearned</c>). Fails toward
    /// "learned" — a null/empty key or a missing set means NO hint clutter, matching WPF
    /// ChaosBubbleHints.IsLearned's catch (returns true on any meta hiccup).</summary>
    public static bool IsLearned(IReadOnlyCollection<string>? learned, string? key)
    {
        if (string.IsNullOrEmpty(key)) return true;
        if (learned == null) return true;
        return learned.Contains(key);
    }
}
