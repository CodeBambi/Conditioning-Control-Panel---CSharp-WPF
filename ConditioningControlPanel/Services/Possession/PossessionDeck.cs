using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  PossessionDeck - the PURE decision layer of the haunt. No WPF, no services, no clocks: given the
//  elapsed fraction, the intensity and a plain-data view of the effects + targets, it says WHICH rung
//  we are on, HOW LONG until the next haunt, HOW MANY may run at once and WHAT to pick.
//
//  Why it is split out: the director is a dispatcher-bound state machine that is miserable to test,
//  but the rules it enforces (ladder bands, intensity caps, cooldowns, "never the same victim twice
//  in a row", photosafe skipping) are exactly the parts that must not drift. They live here so
//  PossessionDeckTests can pin them. Keep this file free of WPF types - the metas below exist so the
//  director can hand us a snapshot of the real objects without dragging Visuals into the tests.
//  Modelled on the Arcademy "pressure ladder" deck (Resources/web/arcademy/games/the-deep-end).
// =====================================================================================================

/// <summary>Plain-data view of an <see cref="IPossessionEffect"/> for the deck. Build it with
/// <see cref="PossessionDeck.MetaOf"/> at the call site so the deck never touches a control.</summary>
public readonly record struct PossessionEffectMeta(
    string Id,
    PossessionRung MinRung,
    PossessionIntensity MinIntensity,
    bool UsesFlicker,
    double Weight,
    IReadOnlyList<PossessionRole> Roles);

/// <summary>Plain-data view of a <see cref="PossessionTarget"/> for the deck. OnCooldown is resolved
/// by the caller against its own clock so the deck stays deterministic.</summary>
public readonly record struct PossessionTargetMeta(
    string Key,
    PossessionRole Role,
    bool IsLive,
    bool OnCooldown);

/// <summary>The deck's answer: indexes into the lists that were handed in. TargetIndex is -1 for an
/// effect that needs no victim (title / window effects declare no Roles).</summary>
public readonly record struct PossessionPick(int EffectIndex, int TargetIndex);

public static class PossessionDeck
{
    /// <summary>The room gets to settle before the first haunt, whatever the cadence says. A user who
    /// just hit Start should have time to sit down before the first thing twitches.</summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(45);

    /// <summary>How long a possessed control is left alone after it is released.</summary>
    public static readonly TimeSpan TargetCooldown = TimeSpan.FromSeconds(90);

    // ---------------------------------------------------------------------------------------------
    //  The ladder
    // ---------------------------------------------------------------------------------------------

    /// <summary>Rung for an elapsed fraction (0..1) of the lockdown timer, already clamped by the
    /// intensity cap: Gentle never passes Melt, Eerie caps at Collapse, only Full Doki reaches It Knows.
    /// Bands are lower-inclusive: exactly 10% is already Drift.</summary>
    public static PossessionRung RungFor(double elapsedFraction, PossessionIntensity intensity)
    {
        if (double.IsNaN(elapsedFraction) || elapsedFraction < 0) elapsedFraction = 0;
        if (elapsedFraction > 1) elapsedFraction = 1;

        var raw =
            elapsedFraction < 0.10 ? PossessionRung.Settle :
            elapsedFraction < 0.35 ? PossessionRung.Drift :
            elapsedFraction < 0.60 ? PossessionRung.Melt :
            elapsedFraction < 0.85 ? PossessionRung.Collapse :
                                     PossessionRung.ItKnows;

        var cap = CapFor(intensity);
        return raw > cap ? cap : raw;
    }

    /// <summary>Highest rung an intensity is allowed to reach.</summary>
    public static PossessionRung CapFor(PossessionIntensity intensity) => intensity switch
    {
        PossessionIntensity.Gentle => PossessionRung.Melt,
        PossessionIntensity.FullDoki => PossessionRung.ItKnows,
        _ => PossessionRung.Collapse,
    };

    /// <summary>Max ghosts live at once. One at a time early (so the WHO is never ambiguous), three
    /// once the room is collapsing.</summary>
    public static int MaxLive(PossessionRung rung) => rung switch
    {
        PossessionRung.Settle or PossessionRung.Drift => 1,
        PossessionRung.Melt => 2,
        _ => 3,
    };

    // ---------------------------------------------------------------------------------------------
    //  Cadence
    // ---------------------------------------------------------------------------------------------

    /// <summary>Eerie base range (seconds) per rung. Gentle doubles it, Full Doki takes 80%.</summary>
    private static (double Min, double Max) BaseRangeSeconds(PossessionRung rung) => rung switch
    {
        PossessionRung.Settle => (60, 90),
        PossessionRung.Drift => (30, 45),
        PossessionRung.Melt => (20, 30),
        PossessionRung.Collapse => (12, 20),
        _ => (8, 15),
    };

    private static double CadenceScale(PossessionIntensity intensity) => intensity switch
    {
        PossessionIntensity.Gentle => 2.0,
        PossessionIntensity.FullDoki => 0.8,
        _ => 1.0,
    };

    /// <summary>Delay until the next haunt. Deterministic for a given <paramref name="rng"/>.</summary>
    public static TimeSpan NextDelay(PossessionRung rung, PossessionIntensity intensity, Random rng)
    {
        var (min, max) = BaseRangeSeconds(rung);
        var scale = CadenceScale(intensity);
        var roll = rng?.NextDouble() ?? 0.5;
        if (roll < 0) roll = 0;
        if (roll > 1) roll = 1;
        return TimeSpan.FromSeconds((min + roll * (max - min)) * scale);
    }

    /// <summary>The first haunt of a lockdown: normal cadence, but never sooner than <see cref="FirstWait"/>.</summary>
    public static TimeSpan FirstDelay(PossessionRung rung, PossessionIntensity intensity, Random rng)
    {
        var d = NextDelay(rung, intensity, rng);
        return d < FirstWait ? FirstWait : d;
    }

    // ---------------------------------------------------------------------------------------------
    //  Weighting
    // ---------------------------------------------------------------------------------------------

    /// <summary>Deck weight of an effect at a rung: nothing below its MinRung, and a +50% push per rung
    /// above it so late-ladder effects crowd out the micro-tics. The bonus caps at +100% so a single
    /// early effect cannot end up owning the whole deck at R4.</summary>
    public static double WeightOf(in PossessionEffectMeta e, PossessionRung rung)
    {
        if (rung < e.MinRung) return 0;
        var over = (int)rung - (int)e.MinRung;
        var multiplier = 1.0 + 0.5 * over;
        if (multiplier > 2.0) multiplier = 2.0;
        return e.Weight * multiplier;
    }

    /// <summary>Convenience overload for the director, which holds the real effects.</summary>
    public static double WeightOf(IPossessionEffect e, PossessionRung rung)
        => e == null ? 0 : WeightOf(MetaOf(e), rung);

    /// <summary>Snapshot an effect into the deck's plain-data view.</summary>
    public static PossessionEffectMeta MetaOf(IPossessionEffect e) => new(
        e.Id, e.MinRung, e.MinIntensity, e.UsesFlicker, e.Weight,
        e.Roles ?? Array.Empty<PossessionRole>());

    // ---------------------------------------------------------------------------------------------
    //  Eligibility + the pick
    // ---------------------------------------------------------------------------------------------

    /// <summary>Can this effect run at all right now (rung, intensity gate, photosafe)?</summary>
    public static bool EffectEligible(in PossessionEffectMeta e, PossessionRung rung, PossessionIntensity intensity, bool photosafe)
    {
        if (e.Weight <= 0) return false;
        if (rung < e.MinRung) return false;
        if ((int)e.MinIntensity > (int)intensity) return false;
        if (photosafe && e.UsesFlicker) return false;   // no strobe for anyone who asked for none
        return true;
    }

    /// <summary>Can this effect take THIS victim? Never one that is already possessed, never one still
    /// cooling down, and never the same one twice in a row (that reads as a broken control, not a haunt).
    /// Effects that declare no Roles need no victim and never match here.</summary>
    public static bool TargetEligible(in PossessionTargetMeta t, in PossessionEffectMeta e, string? lastTargetKey)
    {
        if (t.IsLive || t.OnCooldown) return false;
        if (!string.IsNullOrEmpty(lastTargetKey) && string.Equals(t.Key, lastTargetKey, StringComparison.Ordinal)) return false;
        var roles = e.Roles;
        if (roles == null || roles.Count == 0) return false;
        for (int i = 0; i < roles.Count; i++)
            if (roles[i] == t.Role) return true;
        return false;
    }

    /// <summary>Indexes of every target this effect could take right now (empty for a targetless effect).</summary>
    public static List<int> EligibleTargets(in PossessionEffectMeta e, IReadOnlyList<PossessionTargetMeta> targets, string? lastTargetKey)
    {
        var list = new List<int>();
        if (targets == null) return list;
        for (int i = 0; i < targets.Count; i++)
            if (TargetEligible(targets[i], e, lastTargetKey)) list.Add(i);
        return list;
    }

    /// <summary>Weighted pick of one effect plus its victim, or null when nothing may run. The effect is
    /// drawn by weight; the victim is then drawn flat from that effect's eligible pool, so a role with
    /// many controls does not drag the whole deck towards itself. Deterministic for a given rng.</summary>
    public static PossessionPick? Pick(
        IReadOnlyList<PossessionEffectMeta> effects,
        IReadOnlyList<PossessionTargetMeta> targets,
        PossessionRung rung,
        PossessionIntensity intensity,
        bool photosafe,
        string? lastTargetKey,
        Random rng)
    {
        if (effects == null || effects.Count == 0) return null;

        var candidates = new List<(int EffectIndex, List<int> Targets, double Weight)>();
        double total = 0;

        for (int i = 0; i < effects.Count; i++)
        {
            var meta = effects[i];
            if (!EffectEligible(meta, rung, intensity, photosafe)) continue;

            var needsTarget = meta.Roles != null && meta.Roles.Count > 0;
            List<int>? pool = null;
            if (needsTarget)
            {
                pool = EligibleTargets(meta, targets, lastTargetKey);
                if (pool.Count == 0) continue;   // no victim it may take -> not a candidate
            }

            var w = WeightOf(meta, rung);
            if (w <= 0) continue;
            candidates.Add((i, pool ?? new List<int>(), w));
            total += w;
        }

        if (candidates.Count == 0 || total <= 0) return null;

        var roll = (rng?.NextDouble() ?? 0.5) * total;
        foreach (var c in candidates)
        {
            roll -= c.Weight;
            if (roll <= 0) return Draw(c, rng);
        }
        return Draw(candidates[^1], rng);

        static PossessionPick Draw((int EffectIndex, List<int> Targets, double Weight) c, Random? rng)
        {
            if (c.Targets.Count == 0) return new PossessionPick(c.EffectIndex, -1);
            var idx = c.Targets.Count == 1 ? 0 : (rng?.Next(c.Targets.Count) ?? 0);
            if (idx < 0 || idx >= c.Targets.Count) idx = 0;
            return new PossessionPick(c.EffectIndex, c.Targets[idx]);
        }
    }
}
