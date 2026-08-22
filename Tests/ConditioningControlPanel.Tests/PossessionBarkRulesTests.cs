using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Possession;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Possession's warden only speaks through the bark packs (Services/Possession/POSSESSION.md - Barks),
/// so a missing rule is a SILENT feature loss: the haunt still moves the room, nothing names it, and
/// "was that Lockdown?" stops being answerable. These are source-level checks against the three
/// shipped packs, because that is where the rules actually have to exist.
///
/// The variants are deliberately text-only ("audio": null): no clips are recorded yet, ResolveBarkAudio
/// returns null for a null file, and the bubble still shows. The test pins that shape too, so a later
/// voicing pass cannot quietly drop a line's text.
/// </summary>
public class PossessionBarkRulesTests
{
    private static readonly string[] Packs =
    {
        "builtin-bambisleep",
        "builtin-locked",
        "builtin-sissyhypno",
    };

    /// <summary>Every trigger the Possession layer raises. Kept as the constants rather than literals so
    /// renaming one in PossessionBarkTriggers breaks the test instead of silently orphaning the rules.</summary>
    private static readonly string[] Triggers =
    {
        PossessionBarkTriggers.RungChanged,
        PossessionBarkTriggers.Effect,
        PossessionBarkTriggers.Tripwire,
        PossessionBarkTriggers.Warden,
        PossessionBarkTriggers.Rules,
    };

    private static readonly string[] EscapeKindValues =
    {
        EscapeKinds.Close,
        EscapeKinds.Minimize,
        EscapeKinds.SystemKey,
        EscapeKinds.Stop,
        EscapeKinds.WrongPhrase,
        EscapeKinds.Settings,
    };

    [Fact]
    public void Every_pack_parses_strictly()
    {
        foreach (var pack in Packs)
        {
            var rules = LoadRules(pack);
            Assert.True(rules.Count > 0, $"{pack}/bark_rules.json parsed to an empty ruleset");
        }
    }

    [Fact]
    public void Every_pack_covers_every_possession_trigger()
    {
        foreach (var pack in Packs)
        {
            var rules = LoadRules(pack);
            foreach (var trigger in Triggers)
            {
                var matching = rules.Where(r => TriggerOf(r) == trigger).ToList();
                Assert.True(matching.Count > 0,
                    $"{pack}/bark_rules.json has no rule for the '{trigger}' trigger");
                foreach (var rule in matching)
                {
                    var pool = rule["variant_pool"] as JArray;
                    Assert.True(pool != null && pool.Count > 0,
                        $"{pack}: rule '{IdOf(rule)}' ({trigger}) has an empty variant_pool");
                }
            }
        }
    }

    [Fact]
    public void Every_pack_covers_every_escape_kind()
    {
        foreach (var pack in Packs)
        {
            var kinds = LoadRules(pack)
                .Where(r => TriggerOf(r) == PossessionBarkTriggers.Tripwire)
                .Select(r => (string?)r["conditions"]?["kind_eq"])
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k!.ToLowerInvariant())
                .ToHashSet();

            foreach (var kind in EscapeKindValues)
            {
                Assert.True(kinds.Contains(kind.ToLowerInvariant()),
                    $"{pack}/bark_rules.json has no PossessionTripwire rule for kind_eq '{kind}'. "
                    + "A tripwire nobody reacts to is a dead escape attempt.");
            }
        }
    }

    [Fact]
    public void Every_rung_of_the_ladder_has_a_line()
    {
        foreach (var pack in Packs)
        {
            var rungs = LoadRules(pack)
                .Where(r => TriggerOf(r) == PossessionBarkTriggers.RungChanged)
                .Select(r => (int?)r["conditions"]?["rung_eq"])
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToHashSet();

            foreach (PossessionRung rung in Enum.GetValues<PossessionRung>())
            {
                Assert.True(rungs.Contains((int)rung),
                    $"{pack}/bark_rules.json has no PossessionRungChanged rule for rung_eq {(int)rung} ({rung})");
            }
        }
    }

    [Fact]
    public void Every_warden_verb_has_a_line()
    {
        // StareAsync leans on the bark system for its one line and only falls back to a hardcoded
        // string when there is no BarkService at all, so a missing 'stare' rule leaves the warden
        // gliding to the middle of the window in complete silence.
        string[] verbs = { "knock", "stare", "leave", "return" };
        foreach (var pack in Packs)
        {
            var have = LoadRules(pack)
                .Where(r => TriggerOf(r) == PossessionBarkTriggers.Warden)
                .Select(r => (string?)r["conditions"]?["verb_eq"])
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.ToLowerInvariant())
                .ToHashSet();

            foreach (var verb in verbs)
                Assert.True(have.Contains(verb),
                    $"{pack}/bark_rules.json has no PossessionWarden rule for verb_eq '{verb}'");
        }
    }

    [Fact]
    public void No_possession_variant_uses_an_em_dash()
    {
        // House rule: no em-dashes in user-facing text (they read as machine punctuation and the
        // speech bubble renders them as a stray bar).
        foreach (var pack in Packs)
        {
            foreach (var rule in PossessionRules(pack))
            {
                foreach (var variant in rule["variant_pool"] as JArray ?? new JArray())
                {
                    var text = (string?)variant["text"] ?? "";
                    Assert.False(text.Contains('—') || text.Contains('–'),
                        $"{pack}: rule '{IdOf(rule)}' variant uses an em/en dash: {text}");
                }
            }
        }
    }

    [Fact]
    public void Possession_variants_carry_text_and_declare_their_audio()
    {
        foreach (var pack in Packs)
        {
            foreach (var rule in PossessionRules(pack))
            {
                var pool = rule["variant_pool"] as JArray;
                Assert.True(pool != null && pool.Count >= 2,
                    $"{pack}: rule '{IdOf(rule)}' needs at least two variants so the line can vary");
                foreach (var variant in pool!)
                {
                    Assert.False(string.IsNullOrWhiteSpace((string?)variant["text"]),
                        $"{pack}: rule '{IdOf(rule)}' has a variant with no text");
                    Assert.True(variant["audio"] != null,
                        $"{pack}: rule '{IdOf(rule)}' variant omits the 'audio' key (write an explicit null)");
                }
            }
        }
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

    private static IEnumerable<JObject> PossessionRules(string pack) =>
        LoadRules(pack).Where(r => Triggers.Contains(TriggerOf(r)));

    private static List<JObject> LoadRules(string pack)
    {
        var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "sounds",
                                "companion_audio", "mods", pack, "bark_rules.json");
        Assert.True(File.Exists(path), $"missing bark pack: {path}");
        // Strict parse on purpose: Newtonsoft is lenient about a lot, but a malformed pack must fail
        // here rather than at runtime, where LoadLanguageFile-style leniency would hide it.
        var array = JArray.Parse(File.ReadAllText(path));
        return array.OfType<JObject>().ToList();
    }

    private static string TriggerOf(JObject rule) => (string?)rule["trigger"] ?? "";
    private static string IdOf(JObject rule) => (string?)rule["id"] ?? "(no id)";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
