using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// The line packs behind <see cref="RewriteEffect"/>: what a word says while the room is wearing it.
///
/// <para>Keyed on the LOWER-CASED original string, so "Home" only ever becomes "hole" when the label
/// really said Home; anything the pools do not know falls back to a suffix ("Sessions (mine)"), which
/// is language-safe because the suffix itself is a loc key and the original text is left alone in
/// front of it. The pools themselves are English by nature - they are word play on English UI text -
/// and simply never match on a translated UI, which is the correct failure: a haunted label that
/// still reads as the user's own language, plus the possessive suffix.</para>
///
/// <para>Voice per mod, matched to each pack's own lockdown_on / lockdown_off / lockdown_tick lines
/// (Resources/sounds/companion_audio/mods/&lt;mod&gt;/bark_rules.json): Bambi Sleep is sweet and
/// sinking, Sissy Hypno is coaxing and soft, Locked is a keeper talking to a pet. Custom mods borrow
/// the Bambi Sleep pool - it is the least mod-specific of the three.</para>
/// </summary>
public static class RewritePools
{
    /// <summary>Bambi Sleep: sink, be pretty, be good. Short words, lower case, no punctuation tricks.</summary>
    private static readonly Dictionary<string, string[]> Bambi = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = new[] { "hole", "down" },
        ["start"] = new[] { "stay", "sink" },
        ["stop"] = new[] { "stay", "sink" },
        ["pause"] = new[] { "stay" },
        ["exit"] = new[] { "stay" },
        ["close"] = new[] { "stay" },
        ["back"] = new[] { "deeper" },
        ["settings"] = new[] { "her settings" },
        ["sessions"] = new[] { "her sessions" },
        ["profile"] = new[] { "her doll" },
        ["progress"] = new[] { "sinking" },
        ["play"] = new[] { "obey", "sink" },
        ["lockdown"] = new[] { "mine" },
        ["flashes"] = new[] { "blanks" },
        ["videos"] = new[] { "trances" },
        ["you"] = new[] { "bambi" },
    };

    /// <summary>Sissy Hypno: lovely, sweetie, good girl. Coaxing rather than owning.</summary>
    private static readonly Dictionary<string, string[]> Sissy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = new[] { "hole", "down" },
        ["start"] = new[] { "stay", "kneel" },
        ["stop"] = new[] { "stay", "sink" },
        ["pause"] = new[] { "stay" },
        ["exit"] = new[] { "stay" },
        ["close"] = new[] { "stay" },
        ["back"] = new[] { "softer" },
        ["settings"] = new[] { "her settings" },
        ["sessions"] = new[] { "her sessions" },
        ["profile"] = new[] { "her girl" },
        ["progress"] = new[] { "softening" },
        ["play"] = new[] { "obey", "behave" },
        ["lockdown"] = new[] { "hers" },
        ["flashes"] = new[] { "blanks" },
        ["videos"] = new[] { "trances" },
        ["you"] = new[] { "lovely" },
    };

    /// <summary>Locked: a keeper and a pet. Colder, shorter, all about permission.</summary>
    private static readonly Dictionary<string, string[]> LockedPool = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = new[] { "kennel", "hole" },
        ["start"] = new[] { "stay", "kneel" },
        ["stop"] = new[] { "stay", "beg" },
        ["pause"] = new[] { "stay" },
        ["exit"] = new[] { "stay" },
        ["close"] = new[] { "stay" },
        ["back"] = new[] { "heel" },
        ["settings"] = new[] { "keeper's settings" },
        ["sessions"] = new[] { "keeper's sessions" },
        ["profile"] = new[] { "her pet" },
        ["progress"] = new[] { "obedience" },
        ["play"] = new[] { "obey", "heel" },
        ["lockdown"] = new[] { "kept" },
        ["flashes"] = new[] { "blanks" },
        ["videos"] = new[] { "orders" },
        ["you"] = new[] { "pet" },
    };

    /// <summary>The pool for the mod that is live right now (custom mods borrow Bambi Sleep).</summary>
    private static Dictionary<string, string[]> PoolFor(string? modId) => modId switch
    {
        BuiltInMods.SissyHypnoId => Sissy,
        BuiltInMods.LockedId => LockedPool,
        _ => Bambi,
    };

    /// <summary>The mod the warden currently speaks as. Safe before the mod service exists.</summary>
    public static string ActiveModId
    {
        get
        {
            try { return App.Mods?.ActiveModId ?? BuiltInMods.BambiSleepId; }
            catch { return BuiltInMods.BambiSleepId; }
        }
    }

    /// <summary>
    /// What <paramref name="original"/> says while the room is holding it, or null when nothing fits
    /// (an empty label, or a rewrite that would come out identical). Never longer than the original
    /// plus a short suffix, so nothing re-flows out of its column.
    /// </summary>
    public static string? Rewrite(string? original, string? modId, Random rng)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(original)) return null;
            var trimmed = original.Trim();
            if (trimmed.Length > 48) return null;   // a paragraph is not a label
            rng ??= Random.Shared;

            var pool = PoolFor(modId);
            if (pool.TryGetValue(trimmed, out var options) && options.Length > 0)
            {
                var pick = options[rng.Next(options.Length)];
                var shaped = MatchCase(trimmed, pick, original);
                if (!string.Equals(shaped, original, StringComparison.Ordinal)) return shaped;
            }

            // "Level 12" and friends: the number is the thing being taken, so the whole line goes.
            if (HasDigit(trimmed) && trimmed.IndexOf("level", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var hers = LocOr("lockdown_poss_level_hers", "level hers");
                var shaped = MatchCase(trimmed, hers, original);
                if (!string.Equals(shaped, original, StringComparison.Ordinal)) return shaped;
            }

            // Fallback: the label keeps its own words and simply stops being the user's. It needs to
            // HAVE words first - a suffix on an icon button ("🐛, still") stretches a 46 px square in
            // the title bar until the glyph clips, which reads as a broken layout rather than a haunt.
            // Learned from the preview rig on 2026-08-23; do not relax this without re-shooting it.
            if (LetterCount(trimmed) < 3) return null;

            var suffixes = new[]
            {
                LocOr("lockdown_poss_suffix_mine", " (mine)"),
                LocOr("lockdown_poss_suffix_hers", " (hers)"),
                LocOr("lockdown_poss_suffix_still", ", still"),
            };
            var suffix = suffixes[rng.Next(suffixes.Length)];
            if (string.IsNullOrWhiteSpace(suffix)) return null;
            return original + suffix;
        }
        catch { return null; }
    }

    /// <summary>
    /// A loc string, or the English one when the key has not landed in the language files yet.
    /// LocalizationManager returns the KEY itself for a miss (deliberately, so gaps are visible during
    /// development), and a haunt that paints "lockdown_poss_suffix_mine" onto a label - or, worse,
    /// declines to run at all because its key is missing - is not the failure mode we want out of a
    /// nine-file translation merge. English is a fine ghost.
    /// </summary>
    internal static string LocOr(string key, string fallback)
    {
        try
        {
            var s = Loc.Get(key);
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return string.Equals(s, key, StringComparison.Ordinal) ? fallback : s;
        }
        catch { return fallback; }
    }

    /// <summary>Wear the original's capitalisation: an ALL CAPS door stays shouting, a Title Case
    /// button stays titled. Anything else is left exactly as the pool wrote it.</summary>
    private static string MatchCase(string trimmed, string replacement, string original)
    {
        try
        {
            if (string.IsNullOrEmpty(replacement)) return replacement;

            string shaped;
            if (IsUpper(trimmed)) shaped = replacement.ToUpperInvariant();
            else if (char.IsUpper(trimmed[0])) shaped = char.ToUpperInvariant(replacement[0]) + replacement.Substring(1);
            else shaped = replacement;

            // Put back whatever whitespace the original carried around its text, so a centred label
            // does not shift by a space when it changes its mind.
            int lead = 0;
            while (lead < original.Length && char.IsWhiteSpace(original[lead])) lead++;
            int trail = 0;
            while (trail < original.Length - lead && char.IsWhiteSpace(original[original.Length - 1 - trail])) trail++;
            return original.Substring(0, lead) + shaped + original.Substring(original.Length - trail);
        }
        catch { return replacement; }
    }

    private static bool IsUpper(string s)
    {
        bool sawLetter = false;
        foreach (var c in s)
        {
            if (!char.IsLetter(c)) continue;
            sawLetter = true;
            if (!char.IsUpper(c)) return false;
        }
        return sawLetter;
    }

    private static int LetterCount(string s)
    {
        int n = 0;
        foreach (var c in s) if (char.IsLetter(c)) n++;
        return n;
    }

    private static bool HasDigit(string s)
    {
        foreach (var c in s) if (char.IsDigit(c)) return true;
        return false;
    }
}
