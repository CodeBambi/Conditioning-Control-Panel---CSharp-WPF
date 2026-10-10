using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// The five picture flavours plus "Mine", mirrored from the web pickers
/// (Resources/web/goon/ui/flavours.js, itself a copy of breakout/flavours.js). The web tree
/// cannot be read from C#, so the table is COPIED here and FlavourPresetsTests reads the js
/// file and fails the moment the two drift. Change a niche there, change it here.
///
/// A flavour is a PRESET over the desktop's own model, not a third selection: applying one
/// sets <c>FypOnlineNiches</c> to the catalog niches that cover its subs and puts the rest
/// in the pool as custom subs (<see cref="Resolve"/>). Extras stay suggestions, off by
/// default, the way the web card treats them.
/// </summary>
internal static class FlavourPresets
{
    public sealed record Flavour(string Id, string Name, string Line, string Tint,
        IReadOnlyList<string> Subs, IReadOnlyList<string> Extras);

    public const string MineId = "mine";

    /// <summary>COPIED from flavours.js FLAVOURS (order, names, lines, tints, subs, extras).</summary>
    public static readonly IReadOnlyList<Flavour> All = new[]
    {
        new Flavour("trance", "Trance", "Spirals and soft voices.", "#b99cff",
            new[] { "EroticHypnosis", "HypnoHentai" }, new[] { "GoonCaves" }),
        new Flavour("pink", "Pink", "Bimbo, top to bottom.", "#ff87c7",
            new[] { "bimbofication", "Bimbos", "BimboOrNot" }, new[] { "bimbo", "BimboHypno" }),
        new Flavour("frills", "Frills", "Lace, bows, best behaviour.", "#ffb3d9",
            new[] { "sissyhypno", "SissyInspiration", "Sissyperfection" }, new[] { "sissydressing", "sissycaptions" }),
        new Flavour("shiny", "Shiny", "Latex, rubber, drones.", "#5fffd0",
            new[] { "ShinyPorn", "Dronification", "latexcosplay" }, new[] { "LatexUnderClothes", "rubber" }),
        new Flavour("censored", "Censored", "Look, never see.", "#9fb4c8",
            new[] { "censoredporn", "BetaCensored", "Censored_Porn" }, new[] { "censored" }),
    };

    /// <summary>COPIED from flavours.js MINE: the player's own selection.</summary>
    public static readonly Flavour Mine = new(MineId, "Mine", "Your own niches.", "#c9c9d2",
        Array.Empty<string>(), Array.Empty<string>());

    public static Flavour? ById(string? id) =>
        string.Equals(id, MineId, StringComparison.OrdinalIgnoreCase)
            ? Mine
            : All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>What a flavour sets: the catalog niches and the leftover custom subs.</summary>
    public sealed record Selection(IReadOnlyList<string> NicheIds, IReadOnlyList<string> CustomSubs);

    /// <summary>
    /// Greedy cover of a flavour's core subs by the catalog: repeatedly take the niche that
    /// covers the most still-uncovered subs (ties go to the niche carrying the fewest subs from
    /// outside the flavour, then catalog order), until no niche covers anything new. Whatever
    /// is left becomes custom subs. Deterministic, so the tile can tell whether it is lit.
    /// </summary>
    public static Selection Resolve(Flavour flavour, IReadOnlyList<FypOnlineCoordinator.Niche> catalog)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var own = new HashSet<string>(flavour.Subs.Concat(flavour.Extras), cmp);
        var left = new HashSet<string>(flavour.Subs, cmp);
        var picked = new List<string>();

        while (left.Count > 0)
        {
            FypOnlineCoordinator.Niche? best = null;
            int bestCover = 0, bestForeign = int.MaxValue;
            foreach (var n in catalog)
            {
                if (n?.Id == null || picked.Contains(n.Id)) continue;
                var subs = n.Subs ?? Array.Empty<string>();
                int cover = subs.Count(s => left.Contains(s));
                if (cover == 0) continue;
                int foreign = subs.Count(s => !own.Contains(s));
                if (cover > bestCover || (cover == bestCover && foreign < bestForeign))
                {
                    best = n; bestCover = cover; bestForeign = foreign;
                }
            }
            if (best == null) break;
            picked.Add(best.Id);
            foreach (var s in best.Subs) left.Remove(s);
        }

        var custom = flavour.Subs.Where(s => left.Contains(s)).ToList();
        return new Selection(picked, custom);
    }

    /// <summary>The flavour whose preset equals this selection exactly, or null (= Mine).</summary>
    public static Flavour? Match(IEnumerable<string>? nicheIds, IEnumerable<string>? customSubs,
        IReadOnlyList<FypOnlineCoordinator.Niche> catalog)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var niches = new HashSet<string>(nicheIds ?? Array.Empty<string>(), cmp);
        var subs = new HashSet<string>(customSubs ?? Array.Empty<string>(), cmp);
        foreach (var f in All)
        {
            var sel = Resolve(f, catalog);
            if (niches.SetEquals(sel.NicheIds) && subs.SetEquals(sel.CustomSubs)) return f;
        }
        return null;
    }

    /// <summary>The flavour that "owns" a catalog niche (its preset selects it), for the
    /// niche pill's tint. Null when no flavour selects it.</summary>
    public static Flavour? OwnerOf(string nicheId, IReadOnlyList<FypOnlineCoordinator.Niche> catalog) =>
        All.FirstOrDefault(f => Resolve(f, catalog).NicheIds
            .Contains(nicheId, StringComparer.OrdinalIgnoreCase));
}
