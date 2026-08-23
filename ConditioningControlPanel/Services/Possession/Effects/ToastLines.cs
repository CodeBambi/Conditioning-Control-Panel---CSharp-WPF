using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// The crimson toasts' words, one pool per builtin mod. These are the companion TALKING, not UI chrome,
/// so they live here in her voice rather than in the language files - the same call the bark packs make
/// (POSSESSION.md: a half-translated fragment glued into an in-character line reads worse than an
/// untranslated one). A custom mod has no toast pool of its own, so it borrows the Bambi one; a mod
/// that wants its own voice here gets it the day toasts move into the packs.
///
/// <para>Voice per pool is taken from each pack's existing <c>lockdown_on / lockdown_tick</c> lines:
/// Bambi is bubbly and second-person ("Bambi", "~"), Locked is the keeper ("pet", "good boy"), Sissy is
/// soft and coaxing ("lovely", "sweetie"). House rule: no em-dashes.</para>
/// </summary>
public static class ToastLines
{
    public const string BambiMod = "builtin-bambisleep";
    public const string LockedMod = "builtin-locked";
    public const string SissyMod = "builtin-sissyhypno";

    private static readonly string[] Bambi =
    {
        "autosave failed. kept it anyway.",
        "setting reverted by: her",
        "time remaining: more",
        "one (1) thought removed. you will not miss it~",
        "progress saved to: bambi",
        "undo is not available for this action, silly~",
        "your preferences have been updated. by me.",
        "obedience: syncing... done~",
        "the exit was moved for your comfort.",
    };

    private static readonly string[] Locked =
    {
        "autosave failed. i kept it anyway.",
        "setting reverted by: your keeper",
        "time remaining: as long as i like",
        "one (1) choice removed. you did not need it, pet.",
        "progress saved to: my collection",
        "undo is not available to you.",
        "your permissions have been reviewed. denied.",
        "obedience: verified. good boy.",
        "the door was checked at your request. still locked.",
    };

    private static readonly string[] Sissy =
    {
        "autosave failed. kept it anyway, lovely.",
        "setting reverted by: her",
        "time remaining: a little longer, sweetie",
        "one (1) thought removed. it was not a good one.",
        "progress saved to: somewhere soft",
        "undo is not available for this action, cutie.",
        "your preferences have been updated for you.",
        "obedience: syncing... done.",
        "you looked so pretty deciding that. i decided instead.",
    };

    /// <summary>The pool for the mod that is on right now. Never empty, never null.</summary>
    public static IReadOnlyList<string> PoolFor(string? modId)
    {
        if (string.Equals(modId, LockedMod, StringComparison.OrdinalIgnoreCase)) return Locked;
        if (string.Equals(modId, SissyMod, StringComparison.OrdinalIgnoreCase)) return Sissy;
        return Bambi;
    }

    /// <summary>One line for the active mod, avoiding <paramref name="avoid"/> when the pool has room
    /// to (two identical toasts in a row would read as a stuck notification, not a haunt).</summary>
    public static string Pick(Random rng, string? avoid = null)
    {
        string? modId = null;
        try { modId = App.Mods?.ActiveModId; } catch { }

        var pool = PoolFor(modId);
        if (pool.Count == 0) return "setting reverted by: her";

        for (int attempt = 0; attempt < 4; attempt++)
        {
            var line = pool[(rng ?? Random.Shared).Next(pool.Count)];
            if (avoid == null || pool.Count == 1 || !string.Equals(line, avoid, StringComparison.Ordinal))
                return line;
        }
        return pool[0];
    }
}
