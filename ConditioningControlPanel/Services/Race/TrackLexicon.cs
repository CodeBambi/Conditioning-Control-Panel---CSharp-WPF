using System;
using System.Collections.Generic;
using System.Text;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// The phrases the word spotter listens for: CHART.md STRUCTURE_WORDS + the active mod's trigger
/// phrases + the user's custom triggers + keyword-trigger phrases; lowercased, letters and spaces
/// only, distinct. It is also the recognizer's grammar, so it is capped: a user with a thousand
/// keyword triggers would otherwise turn the word pass into a free-dictation pass in all but name.
/// </summary>
public static class TrackLexicon
{
    /// <summary>The most phrases a grammar may carry. Structure words go in first, so they survive.</summary>
    public const int MaxPhrases = 200;

    /// <summary>
    /// CHART.md STRUCTURE_WORDS (English v1). Spotted occurrences of these become "word" events;
    /// everything else in the lexicon came from a mod or the settings and becomes a "trigger".
    /// </summary>
    public static readonly IReadOnlyList<string> StructureWords = new[]
    {
        "drop", "dropping", "sleep", "sleepy", "asleep", "deeper", "deep", "down", "sink", "sinking",
        "relax", "relaxing", "breathe", "breath", "blank", "empty", "obey", "listen", "focus",
        "surrender", "melt", "float", "floating", "heavy", "wake", "awake", "waking", "up", "open",
        "count", "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "now", "good", "girl", "bimbo", "doll", "mind", "mindless", "pink", "spiral", "trance",
        "trigger"
    };

    /// <summary>CHART.md DROP_WORDS. The "now" rule (only after a count) lives in the fold.</summary>
    public static readonly IReadOnlyList<string> DropWords = new[]
    {
        "drop", "dropping", "sleep", "asleep", "deeper", "sink", "sinking", "now"
    };

    private static readonly HashSet<string> StructureSet = new(StructureWords, StringComparer.Ordinal);
    private static readonly HashSet<string> DropSet = new(DropWords, StringComparer.Ordinal);

    /// <summary>True when the phrase is one of CHART.md's structure words rather than a trigger.</summary>
    public static bool IsStructureWord(string phrase) => StructureSet.Contains(phrase);

    /// <summary>True when a spotted word is a drop word.</summary>
    public static bool IsDropWord(string phrase) => DropSet.Contains(phrase);

    /// <summary>
    /// Build the grammar for a word pass. Never throws: with no settings and no mod loaded the
    /// structure words alone come back, which is enough to chart a file on its own.
    /// </summary>
    public static IReadOnlyList<string> Build()
    {
        var raw = new List<string>(StructureWords);

        // Everything below is best effort. This runs on a worker while the user watches a progress
        // plate, and a half-initialised service is never worth failing a chart over.
        try
        {
            // The active mod's trigger corpus. ModService folds a manifest's list into
            // AppSettings.CustomTriggers per mod, so both are read: the settings copy is what the
            // user actually hears, the manifest copy covers a mod loaded but not yet applied.
            var mods = App.Mods;
            if (mods != null)
            {
                raw.AddRange(mods.GetDefaultCustomTriggers());
                var t = mods.ActiveMod?.Manifest?.Triggers;
                if (t?.Freeze != null) raw.Add(t.Freeze);
                if (t?.Reset != null) raw.Add(t.Reset);
                if (t?.CumAndCollapse != null) raw.Add(t.CumAndCollapse);
            }

            var settings = App.Settings?.Current;
            if (settings != null)
            {
                if (settings.CustomTriggers != null) raw.AddRange(settings.CustomTriggers);
                if (settings.UserAddedCustomTriggers != null) raw.AddRange(settings.UserAddedCustomTriggers);
                if (settings.KeywordTriggers != null)
                {
                    foreach (var kt in settings.KeywordTriggers)
                    {
                        // A regex trigger has no phrase a recognizer could listen for.
                        if (kt == null || kt.MatchType == Models.KeywordMatchType.Regex) continue;
                        raw.Add(kt.Keyword);
                    }
                }
            }
        }
        catch (Exception ex) { App.Logger?.Information(ex, "TrackLexicon: trigger sources unavailable, using structure words only"); }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var phrases = new List<string>(Math.Min(raw.Count, MaxPhrases));
        foreach (var entry in raw)
        {
            var norm = Normalize(entry);
            if (norm.Length == 0) continue;
            if (!seen.Add(norm)) continue;
            phrases.Add(norm);
            if (phrases.Count >= MaxPhrases) break;
        }
        return phrases;
    }

    /// <summary>
    /// Lowercase, letters and spaces only, trimmed, single-spaced. Digits and punctuation go: the
    /// numbers a hypno file speaks are words ("three"), never glyphs, and a grammar wants words.
    /// </summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
        {
            sb.Append(ch >= 'a' && ch <= 'z' ? ch : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
