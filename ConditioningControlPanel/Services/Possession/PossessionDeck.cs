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
    /// just hit Start should have time to sit down before the first thing twitches.
    ///
    /// <para>Wave 2 (owner play-test 2026-08-23, "not dense"): 45 s of nothing at the top of a 10 min
    /// lockdown is a quarter of the Settle band spent proving the feature is off. 20 s is still long
    /// enough to sit down.</para></summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(20);

    /// <summary>How long a possessed control is left alone after it is released. Halved in wave 2:
    /// at the new cadence a 90 s cooldown starved the pool of the handful of controls the user is
    /// actually looking at, and the haunt drifted to the window's dead corners.</summary>
    public static readonly TimeSpan TargetCooldown = TimeSpan.FromSeconds(45);

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

    /// <summary>Max ghosts live at once. Two early (one at a time read as an empty room, and the
    /// attribution grammar - charge, outline, cursor ring - already answers WHO even when two things
    /// are moving), four once the room is collapsing.</summary>
    public static int MaxLive(PossessionRung rung) => rung switch
    {
        PossessionRung.Settle or PossessionRung.Drift => 2,
        PossessionRung.Melt => 3,
        _ => 4,
    };

    /// <summary>Does a haunt worth <paramref name="slots"/> still fit under the rung's cap? A single
    /// effect is one slot; a SCENE is its beat count (POSSESSION.md: "a scene counts as its beat
    /// count"), which is why this takes a weight instead of assuming one. The director used to elect a
    /// scene only after checking a flat "+2", so a three-beat choreography could be waved into a Melt
    /// room (cap 3) that already had a ghost live and push it to four.</summary>
    public static bool FitsConcurrency(int liveSlots, int slots, PossessionRung rung)
    {
        if (slots <= 0) slots = 1;
        if (liveSlots < 0) liveSlots = 0;
        return liveSlots + slots <= MaxLive(rung);
    }

    // ---------------------------------------------------------------------------------------------
    //  Cadence
    // ---------------------------------------------------------------------------------------------

    /// <summary>Eerie base range (seconds) per rung. Gentle doubles it, Full Doki takes 80%.
    ///
    /// <para>Wave 2 sped the whole ladder up ~2.5x. The first live 10-minute Eerie run produced two
    /// named effects in nine minutes, which is not a haunted room, it is a rare glitch. At the new
    /// numbers a 10 min run lands roughly 40 haunts and the escalation is legible minute to minute.</para></summary>
    private static (double Min, double Max) BaseRangeSeconds(PossessionRung rung) => rung switch
    {
        PossessionRung.Settle => (20, 30),
        PossessionRung.Drift => (12, 18),
        PossessionRung.Melt => (8, 12),
        PossessionRung.Collapse => (5, 8),
        _ => (4, 6),
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
    /// many controls does not drag the whole deck towards itself. Deterministic for a given rng.
    ///
    /// <para><paramref name="nearTargets"/> is the proximity option (wave 2, A5): indexes into
    /// <paramref name="targets"/> that sit near the cursor. When it is supplied the pick is tried
    /// against that shortlist FIRST and falls back to the full pool if nothing there may run - a haunt
    /// the user cannot see is worth less than one on the wrong control. Passing null (or fewer than two
    /// indexes) is the plain full-pool behaviour.</para></summary>
    public static PossessionPick? Pick(
        IReadOnlyList<PossessionEffectMeta> effects,
        IReadOnlyList<PossessionTargetMeta> targets,
        PossessionRung rung,
        PossessionIntensity intensity,
        bool photosafe,
        string? lastTargetKey,
        Random rng,
        IReadOnlyCollection<int>? nearTargets = null)
    {
        if (nearTargets != null && nearTargets.Count >= 2)
        {
            var near = PickCore(effects, targets, rung, intensity, photosafe, lastTargetKey, rng, nearTargets);
            if (near != null) return near;
        }
        return PickCore(effects, targets, rung, intensity, photosafe, lastTargetKey, rng, null);
    }

    private static PossessionPick? PickCore(
        IReadOnlyList<PossessionEffectMeta> effects,
        IReadOnlyList<PossessionTargetMeta> targets,
        PossessionRung rung,
        PossessionIntensity intensity,
        bool photosafe,
        string? lastTargetKey,
        Random rng,
        IReadOnlyCollection<int>? restrictTargets)
    {
        if (effects == null || effects.Count == 0) return null;

        var restrict = restrictTargets == null ? null : new HashSet<int>(restrictTargets);

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
                if (restrict != null)
                {
                    // A targetless effect ignores the shortlist on purpose: a title typo or an edge
                    // pulse is already "wherever you are looking", it has no coordinates to be near.
                    pool = pool.FindAll(restrict.Contains);
                }
                if (pool.Count == 0) continue;   // no victim it may take -> not a candidate
            }
            else if (restrict != null)
            {
                continue;   // proximity round: only effects that can land ON something near the cursor
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

    // ---------------------------------------------------------------------------------------------
    //  Proximity (wave 2, A5)
    // ---------------------------------------------------------------------------------------------

    /// <summary>How near the cursor a control has to be to count as "where the user is", in window
    /// pixels. Roughly one card's reach: wide enough that the pointer resting anywhere on a card puts
    /// that whole card plus its neighbours in range, narrow enough that the other half of the window
    /// is not.</summary>
    public const double ProximityRadius = 160.0;

    /// <summary>Share of picks that try the cursor's neighbourhood first. Half: all-proximity would
    /// leave the rest of the room provably safe (and the user would learn to look away), none is the
    /// wave-1 behaviour the owner called empty.</summary>
    public const double ProximityChance = 0.5;

    /// <summary>Roll for a proximity round. Split out so the director stays a plumbing file and the
    /// odds stay pinnable.</summary>
    public static bool ShouldUseProximity(Random rng) => (rng?.NextDouble() ?? 1.0) < ProximityChance;

    /// <summary>Indexes of every centre within <paramref name="radius"/> of the origin. Plain doubles
    /// rather than a Point so the deck keeps its no-WPF rule; the director does the transform.</summary>
    public static List<int> WithinRadius(IReadOnlyList<(double X, double Y)> centres, double originX, double originY, double radius)
    {
        var hits = new List<int>();
        if (centres == null || radius <= 0) return hits;
        if (double.IsNaN(originX) || double.IsNaN(originY)) return hits;
        var r2 = radius * radius;
        for (int i = 0; i < centres.Count; i++)
        {
            var dx = centres[i].X - originX;
            var dy = centres[i].Y - originY;
            if (double.IsNaN(dx) || double.IsNaN(dy)) continue;
            if (dx * dx + dy * dy <= r2) hits.Add(i);
        }
        return hits;
    }
}
