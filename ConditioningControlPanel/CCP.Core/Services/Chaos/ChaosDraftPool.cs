using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// The gating surface one draftable card exposes to the pure draft dealer. The head's
/// boon model implements this so the deal logic lives in Core, testable and shared
/// (WPF ChaosBoon fields on ChaosModels.cs:296-311).
/// </summary>
public interface IChaosDraftCard
{
    /// <summary>Stable card id (WPF ChaosBoon.Id).</summary>
    string Id { get; }

    /// <summary>True for sins — they deal through the dedicated sin slot (WPF ChaosBoon.IsCurse).</summary>
    bool IsCurse { get; }

    /// <summary>One-shot cards: once taken this run, never re-offered (WPF ChaosBoon.Unique).</summary>
    bool Unique { get; }

    /// <summary>Duo gating: draftable only when at least one of these lifetime-boon/upgrade ids
    /// is active. Null = always draftable (WPF ChaosBoon.RequiresAny).</summary>
    string[]? RequiresAny { get; }

    /// <summary>Trio gating: ALL of these must be active; combines with RequiresAny
    /// (WPF ChaosBoon.RequiresAll).</summary>
    string[]? RequiresAll { get; }
}

/// <summary>
/// Pure draft dealer extracted verbatim from WPF <c>ChaosBoonPool.Draft</c>
/// (WPF ChaosModels.cs:404-431): duo/trio ReqMet gating, Unique-taken exclusion,
/// the dedicated sin slot (includeCurse roll + Surrender guarantee), the 2-4 candidate
/// clamp and the boon top-up when the pool runs short. Randomness and the ReqMet
/// predicate are injected (the WPF original used a private static Random,
/// ChaosModels.cs:400, and static ChaosMeta reads, :407) so callers/tests stay
/// deterministic — the same pattern as <see cref="ChaosSpawnCatalog"/>.
/// </summary>
public static class ChaosDraftPool
{
    /// <summary>
    /// Deal <paramref name="choices"/> options (clamped 2-4; WPF ChaosModels.cs:406):
    /// mostly boons plus at most one sin through the dedicated sin slot.
    /// </summary>
    /// <param name="pool">The full card pool (WPF ChaosBoonPool.All).</param>
    /// <param name="reqMet">True when a requirement id names an active lifetime boon or
    /// upgrade (WPF ChaosModels.cs:407-409 ReqMet: IsBoonActive || IsUpgradeActive).</param>
    /// <param name="rng">Injected randomness (shuffle keys + the sin-slot roll).</param>
    /// <param name="allowCurses">False = sins never deal (WPF ChaosRunConfig.AllowCurses).</param>
    /// <param name="choices">Requested deal size; the draft4 upgrade raises it to 4
    /// (WPF ChaosUpgrades.cs:86).</param>
    /// <param name="guaranteeCurse">Surrender capstone: every draft carries a sin
    /// (WPF ChaosModeService.cs:1482).</param>
    /// <param name="takenIds">Run-boon ids already drafted — Unique cards sit the rest out
    /// (WPF ChaosModeService.cs:1511 TakenBoonIds).</param>
    /// <param name="sinChance">The dedicated sin-slot roll odds (WPF ChaosRunConfig.SinChance,
    /// ramped by ChaosRunRules.DefaultSinChance).</param>
    public static List<T> Draft<T>(IReadOnlyList<T> pool, Func<string, bool> reqMet, Random rng,
                                   bool allowCurses = true, int choices = 3, bool guaranteeCurse = false,
                                   IReadOnlyCollection<string>? takenIds = null, double sinChance = 0.5)
        where T : class, IChaosDraftCard
    {
        choices = Math.Clamp(choices, 2, 4);   // WPF ChaosModels.cs:406
        // A requirement id may name a lifetime boon (skill/accessory/charm) OR a trained habit
        // (WPF ChaosModels.cs:407-410 Draftable — verbatim gating).
        bool Draftable(T b) =>
            (b.RequiresAny == null || b.RequiresAny.Any(reqMet))
            && (b.RequiresAll == null || b.RequiresAll.All(reqMet))
            && !(b.Unique && takenIds != null && takenIds.Contains(b.Id));
        var boons = pool.Where(b => !b.IsCurse && Draftable(b)).OrderBy(_ => rng.Next()).ToList();   // WPF :411
        var curses = pool.Where(b => b.IsCurse && Draftable(b)).OrderBy(_ => rng.Next()).ToList();   // WPF :412

        var draft = new List<T>();
        // The sin slot: one roll (or the Surrender guarantee) reserves one seat for curses[0]
        // (WPF ChaosModels.cs:414-416 — note the short-circuit: no roll when guaranteed/disallowed).
        bool includeCurse = allowCurses && (guaranteeCurse || rng.NextDouble() < sinChance) && curses.Count > 0;
        int boonCount = includeCurse ? choices - 1 : choices;

        draft.AddRange(boons.Take(Math.Min(boonCount, boons.Count)));   // WPF :419
        if (includeCurse) draft.Add(curses[0]);                          // WPF :420

        // Top up from boons if the pool ran short (WPF ChaosModels.cs:422-427).
        foreach (var b in boons.Skip(boonCount))
        {
            if (draft.Count >= choices) break;
            draft.Add(b);
        }
        return draft.Take(choices).ToList();   // WPF :428
    }
}
