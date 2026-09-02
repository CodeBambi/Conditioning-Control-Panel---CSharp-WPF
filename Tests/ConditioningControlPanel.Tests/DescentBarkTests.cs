using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Descent's companion barks. Two halves, and they fail for different reasons.
///
/// <para>The POLICY half pins the edges. Every moment these lines narrate turns over once a day at
/// most, so a bug that fires a milestone twice, or fires it again on every launch, would take a week
/// of real use to notice and a week more to reproduce. It is pure by design precisely so it can be
/// driven a month at a time in milliseconds.</para>
///
/// <para>The MANIFEST half pins the copy. A rule with no lines is a trigger that raises into silence,
/// and the app raises these five from code that has no idea whether a pack answers it, so the packs
/// are checked at source where the lines actually have to exist.</para>
/// </summary>
public class DescentBarkTests
{
    private const string Day1 = "2026-09-01";
    private const string Day2 = "2026-09-02";

    // =====================================================================================
    //  the policy
    // =====================================================================================

    [Fact]
    public void First_sight_of_a_block_seeds_and_says_nothing()
    {
        // An account that has been banking for months before this feature shipped must not be
        // congratulated for today the instant the watcher attaches.
        var memory = new DescentBarkMemory();
        var block = Block(devotionDays: 40, lastDay: Day2, stage: 3, fillPct: 64);

        var decision = DescentBarkPolicy.Decide(block, memory, Day2);

        Assert.Equal(DescentBarkMoment.None, decision.Moment);
        Assert.True(memory.Seeded);
        Assert.Equal(3, memory.LastStage);
        Assert.Equal(Day2, memory.LastBankedDay);
    }

    [Fact]
    public void Crossing_the_line_banks_the_day_once_and_only_once()
    {
        var memory = Seeded(stage: 2, lastBankedDay: Day1);

        // Still short of the line: nothing to announce about banking.
        var below = DescentBarkPolicy.Decide(Block(40, Day1, 2, fillPct: 5), memory, Day2);
        Assert.NotEqual(DescentBarkMoment.DayBanked, below.Moment);

        var crossed = DescentBarkPolicy.Decide(Block(41, Day2, 2, fillPct: 22), memory, Day2);
        Assert.Equal(DescentBarkMoment.DayBanked, crossed.Moment);

        // Every later poll of the same day is silent, and so is a relaunch that re-reads the memory.
        for (int i = 0; i < 5; i++)
        {
            var again = DescentBarkPolicy.Decide(Block(41, Day2, 2, fillPct: 40 + i * 10), memory, Day2);
            Assert.Equal(DescentBarkMoment.None, again.Moment);
        }
    }

    [Fact]
    public void A_new_utc_day_rearms_the_bank_line()
    {
        var memory = Seeded(stage: 2, lastBankedDay: Day1);
        Assert.Equal(DescentBarkMoment.DayBanked,
            DescentBarkPolicy.Decide(Block(41, Day2, 2, fillPct: 25), memory, Day2).Moment);

        const string day3 = "2026-09-03";
        Assert.Equal(DescentBarkMoment.DayBanked,
            DescentBarkPolicy.Decide(Block(42, day3, 2, fillPct: 25), memory, day3).Moment);
    }

    [Fact]
    public void The_near_bank_nudge_lands_inside_its_window_once_a_day()
    {
        var memory = Seeded(stage: 1, lastBankedDay: Day1);

        // Too early to be useful.
        Assert.Equal(DescentBarkMoment.None,
            DescentBarkPolicy.Decide(Block(10, Day1, 1, fillPct: 4), memory, Day2).Moment);

        var nudge = DescentBarkPolicy.Decide(Block(10, Day1, 1, fillPct: 15), memory, Day2);
        Assert.Equal(DescentBarkMoment.NearBank, nudge.Moment);
        Assert.Equal(5.0, nudge.RemainingPct, 3);

        // Not twice, however many times the poll ticks.
        Assert.Equal(DescentBarkMoment.None,
            DescentBarkPolicy.Decide(Block(10, Day1, 1, fillPct: 18), memory, Day2).Moment);
    }

    [Fact]
    public void The_near_bank_nudge_never_fires_on_a_day_that_already_banked()
    {
        var memory = Seeded(stage: 1, lastBankedDay: Day2);
        // A fill above the line is banked by definition, so the coaxing line is not eligible at all.
        var d = DescentBarkPolicy.Decide(Block(11, Day2, 1, fillPct: 55), memory, Day2);
        Assert.Equal(DescentBarkMoment.None, d.Moment);
    }

    [Fact]
    public void A_day_that_banks_and_crosses_a_stage_says_the_bigger_thing_and_spends_both()
    {
        var memory = Seeded(stage: 2, lastBankedDay: Day1);

        var d = DescentBarkPolicy.Decide(Block(21, Day2, 3, fillPct: 30), memory, Day2);
        Assert.Equal(DescentBarkMoment.StageCrossed, d.Moment);
        Assert.Equal(3, d.Stage);

        // The banking it rode in on is spent too, so it cannot resurface an hour later out of context.
        Assert.Equal(Day2, memory.LastBankedDay);
        Assert.Equal(DescentBarkMoment.None,
            DescentBarkPolicy.Decide(Block(21, Day2, 3, fillPct: 70), memory, Day2).Moment);
    }

    [Fact]
    public void A_stage_only_announces_when_it_actually_moves_up()
    {
        var memory = Seeded(stage: 4, lastBankedDay: Day2);
        // Same rung, and a server that somehow reports a lower one, are both silent.
        Assert.Equal(DescentBarkMoment.None,
            DescentBarkPolicy.Decide(Block(101, Day2, 4, fillPct: 50), memory, Day2).Moment);
        Assert.Equal(DescentBarkMoment.None,
            DescentBarkPolicy.Decide(Block(101, Day2, 3, fillPct: 50), memory, Day2).Moment);
    }

    /// <summary>
    /// THESE BLOCKS ARE THE SHAPE THE SERVER ACTUALLY EMITS, and until 2026-09-02 they were not.
    /// The old fixtures paired `surge_active` with a days_away of 10 and 20, which the wire cannot
    /// produce: `applyRelapseSurge` stamps the surge one line before `applyDevotionDay` moves
    /// `devotion_last_day` to today, and `relapseDaysAway` measures the gap off that same field, so
    /// a return reads ZERO days away at the exact moment it becomes a surge. Testing against a
    /// payload nobody ships is how the policy's own days-away floor of 2 survived review while
    /// making the welcome unreachable, so the fixtures moved to the real thing.
    /// </summary>
    [Fact]
    public void A_return_is_welcomed_once_per_surge_not_once_per_surge_day()
    {
        var memory = Seeded(stage: 2, lastBankedDay: "2026-08-20");

        // THE RETURN DAY. Ten days away, the bank that brought them back has already moved the
        // last-banked day to today, and the payout carries the gap the stamp froze.
        var returnDay = new DescentRelapse
        {
            Multiplier = 1.0,
            DaysAway = 0,
            SurgeActive = true,
            SurgeEndsAt = "2026-09-05T00:00:00Z",
            SurgeMultiplier = 1.4,
        };

        var first = DescentBarkPolicy.Decide(Block(30, Day2, 2, fillPct: 25, relapse: returnDay), memory, Day2);
        Assert.Equal(DescentBarkMoment.LapseReturn, first.Moment);
        Assert.Equal(1.4, first.SurgeMultiplier);

        // The same day also banked, and the welcome outranks it rather than firing beside it.
        Assert.Equal(Day2, memory.LastBankedDay);

        // Day two and day three of the same surge are the same welcome, and it has been given. The
        // gap now reads 1 because the return day banked and nothing has banked since.
        const string day3 = "2026-09-03";
        var laterInWindow = new DescentRelapse
        {
            Multiplier = 1.04,
            DaysAway = 1,
            SurgeActive = true,
            SurgeEndsAt = "2026-09-05T00:00:00Z",
            SurgeMultiplier = 1.4,
        };
        Assert.NotEqual(DescentBarkMoment.LapseReturn,
            DescentBarkPolicy.Decide(Block(31, Day2, 2, fillPct: 3, relapse: laterInWindow), memory, day3).Moment);

        // A LATER return is a different surge, and is welcomed again.
        var laterSurge = new DescentRelapse
        {
            Multiplier = 1.0,
            DaysAway = 0,
            SurgeActive = true,
            SurgeEndsAt = "2026-10-01T00:00:00Z",
            SurgeMultiplier = 1.8,
        };
        Assert.Equal(DescentBarkMoment.LapseReturn,
            DescentBarkPolicy.Decide(Block(31, "2026-09-29", 2, fillPct: 22, relapse: laterSurge), memory, "2026-09-29").Moment);
    }

    [Fact]
    public void An_evening_off_is_not_a_return()
    {
        // One day away is an ordinary evening off, and the server never stamps a surge for one:
        // `isMakeupEligible` wants a pre-bank gap of at least two days, so the block that comes
        // back from a single missed evening simply carries no surge at all.
        var memory = Seeded(stage: 2, lastBankedDay: Day1);
        var eveningOff = new DescentRelapse
        {
            Multiplier = 1.04, DaysAway = 1, SurgeActive = false,
            SurgeEndsAt = null, SurgeMultiplier = 1.0,
        };
        Assert.NotEqual(DescentBarkMoment.LapseReturn,
            DescentBarkPolicy.Decide(Block(20, Day1, 2, fillPct: 2, relapse: eveningOff), memory, Day2).Moment);
    }

    [Fact]
    public void A_surge_that_pays_nothing_is_not_welcomed()
    {
        // Every surge stamped before the server froze the multiplier reads back as exactly 1.0x,
        // and the gap those stamps measured is unrecoverable. Welcoming somebody for a faster fill
        // they will never actually feel is worse than staying quiet, so the payout is the gate.
        var memory = Seeded(stage: 2, lastBankedDay: Day1);
        var preFreeze = new DescentRelapse
        {
            Multiplier = 1.0, DaysAway = 0, SurgeActive = true,
            SurgeEndsAt = "2026-09-05T00:00:00Z", SurgeMultiplier = 1.0,
        };
        Assert.NotEqual(DescentBarkMoment.LapseReturn,
            DescentBarkPolicy.Decide(Block(20, Day2, 2, fillPct: 4, relapse: preFreeze), memory, Day2).Moment);
    }

    [Fact]
    public void No_block_is_inert()
    {
        var memory = new DescentBarkMemory();
        Assert.Equal(DescentBarkMoment.None, DescentBarkPolicy.Decide(null, memory, Day2).Moment);
        Assert.False(memory.Seeded);
    }

    [Fact]
    public void A_dark_vat_never_produces_a_jar_line()
    {
        // The server can ship the block with the vat off. There is no honest stand-in for a meter
        // that does not exist, so neither jar line may be reachable.
        var memory = Seeded(stage: 2, lastBankedDay: Day1);
        var block = new DescentBlock { DevotionDays = 30, DevotionLastDay = Day1, Vat = null, Stage = Stage(2, 30) };
        var d = DescentBarkPolicy.Decide(block, memory, Day2);
        Assert.NotEqual(DescentBarkMoment.NearBank, d.Moment);
        Assert.NotEqual(DescentBarkMoment.DayBanked, d.Moment);
    }

    // =====================================================================================
    //  the manifests
    // =====================================================================================

    /// <summary>Base manifest plus every built-in overlay. The base one is what a user with no mod
    /// overlay for these ids actually hears, so it is checked on exactly the same terms.</summary>
    private static readonly string[] Manifests =
    {
        "",                     // Resources/sounds/companion_audio/bark_rules.json
        "builtin-bambisleep",
        "builtin-locked",
        "builtin-sissyhypno",
    };

    /// <summary>Rule id to the trigger key BarkService raises for it.</summary>
    private static readonly Dictionary<string, string> DescentRules = new()
    {
        ["descent_near_bank"] = "DescentNearBank",
        ["descent_day_banked"] = "DescentDayBanked",
        ["descent_stage_crossed"] = "DescentStageCrossed",
        ["descent_lapse_return"] = "DescentLapseReturn",
        ["descent_first_spiral_open"] = "DescentFirstSpiralOpen",
    };

    [Fact]
    public void Every_manifest_carries_every_descent_rule_on_the_right_trigger()
    {
        foreach (var pack in Manifests)
        {
            var rules = LoadRules(pack);
            foreach (var (id, trigger) in DescentRules)
            {
                var rule = rules.FirstOrDefault(r => IdOf(r) == id);
                Assert.True(rule != null, $"{Describe(pack)} has no '{id}' rule");
                Assert.Equal(trigger, (string?)rule!["trigger"]);
            }
        }
    }

    [Fact]
    public void Every_descent_trigger_is_actually_raised_by_the_app()
    {
        // A rule keyed to a trigger nothing raises is a pack that looks complete and never speaks.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services",
                                                   "Companion", "BarkService.cs"));
        foreach (var trigger in DescentRules.Values)
            Assert.True(source.Contains($"Raise(\"{trigger}\""),
                $"BarkService raises no '{trigger}' - the rules for it are unreachable");
    }

    [Fact]
    public void Every_descent_rule_has_at_least_three_lines_that_work_without_audio()
    {
        foreach (var pack in Manifests)
        {
            foreach (var rule in DescentRulesIn(pack))
            {
                var pool = rule["variant_pool"] as JArray;
                Assert.True(pool != null && pool.Count >= 3,
                    $"{Describe(pack)}: '{IdOf(rule)}' needs at least three variants so repetition cannot set in");
                foreach (var variant in pool!)
                {
                    Assert.False(string.IsNullOrWhiteSpace((string?)variant["text"]),
                        $"{Describe(pack)}: '{IdOf(rule)}' has a variant with no text");
                    // Text-only is first class here: nothing is recorded for these yet, so the key is
                    // written as an explicit null rather than omitted, and a later voicing pass
                    // cannot quietly drop a line's text.
                    Assert.True(variant["audio"] != null,
                        $"{Describe(pack)}: '{IdOf(rule)}' variant omits the 'audio' key (write an explicit null)");
                }
            }
        }
    }

    [Fact]
    public void No_descent_line_uses_an_em_dash()
    {
        // House rule: no em-dashes in user-facing text.
        foreach (var pack in Manifests)
            foreach (var text in LinesIn(pack))
                Assert.False(text.Contains('—') || text.Contains('–'),
                    $"{Describe(pack)}: a descent line uses an em/en dash: {text}");
    }

    [Fact]
    public void No_descent_line_calls_the_relapse_bonus_gravity()
    {
        // Owner rule. The mechanic rewards absence; naming it gravity makes it sound like a penalty.
        foreach (var pack in Manifests)
            foreach (var text in LinesIn(pack))
                Assert.DoesNotContain("gravity", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_return_lines_welcome_rather_than_scold()
    {
        // The whole point of the mechanic is that time away pays. A line that reaches for any of
        // these is a line that has quietly turned it into a telling-off.
        string[] banned = { "finally", "at last", "abandon", "neglect", "lazy", "slack", "excuse", "punish" };
        foreach (var pack in Manifests)
        {
            foreach (var text in LinesOf(pack, "descent_lapse_return"))
                foreach (var word in banned)
                    Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_spiral_welcome_is_a_lifetime_one_shot()
    {
        // "First time the Spiral is opened" is enforced by the rule, not by the caller: the view
        // announces on every entry and this latch is what makes it the first one only.
        foreach (var pack in Manifests)
        {
            var rule = DescentRulesIn(pack).Single(r => IdOf(r) == "descent_first_spiral_open");
            Assert.False((bool?)rule["repeatable"] ?? true,
                $"{Describe(pack)}: descent_first_spiral_open must not be repeatable");
            Assert.Equal("lifetime", (string?)rule["scope"]);
        }
    }

    [Fact]
    public void The_recurring_rules_carry_a_cooldown_long_enough_to_never_nag()
    {
        // The watcher's day keys already hold these to once a day, but a rule with no cooldown is a
        // rule that would repeat the moment somebody wired a second caller to the trigger.
        string[] recurring = { "descent_near_bank", "descent_day_banked", "descent_lapse_return" };
        foreach (var pack in Manifests)
        {
            foreach (var id in recurring)
            {
                var rule = DescentRulesIn(pack).Single(r => IdOf(r) == id);
                var cooldown = (long?)rule["cooldown_ms"] ?? 0;
                Assert.True(cooldown >= 6 * 60 * 60 * 1000L,
                    $"{Describe(pack)}: '{id}' has a {cooldown}ms cooldown, which is short enough to nag");
            }
        }
    }

    [Fact]
    public void Each_mod_speaks_the_moment_in_its_own_voice()
    {
        // The overlays exist to sound different. A pack that copied the base pool verbatim would
        // pass every other check here and still be a bug.
        foreach (var id in DescentRules.Keys)
        {
            var baseLines = LinesOf("", id).ToHashSet(StringComparer.Ordinal);
            foreach (var pack in Manifests.Where(p => p.Length > 0))
            {
                var modLines = LinesOf(pack, id).ToList();
                Assert.True(modLines.Count > 0, $"{Describe(pack)}: '{id}' has no lines");
                Assert.False(modLines.All(baseLines.Contains),
                    $"{Describe(pack)}: '{id}' reuses the base pool verbatim instead of its own voice");
            }
        }
    }

    [Fact]
    public void The_teaching_lines_use_the_canonical_phrasing_for_what_moves_the_spiral()
    {
        // Every surface says this the same way (the vat tick tooltip owns the wording). A bark that
        // invented a fifth phrasing would teach the mechanic and blur the sentence at the same time.
        const string canonical = "banked days are the only thing that moves you down the Spiral";
        foreach (var pack in Manifests)
        {
            foreach (var id in new[] { "descent_day_banked", "descent_first_spiral_open" })
            {
                Assert.Contains(LinesOf(pack, id), t => t.Contains(canonical, StringComparison.Ordinal));
            }
        }
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

    private static DescentBlock Block(int devotionDays, string? lastDay, int stage, double fillPct,
                                      DescentRelapse? relapse = null) =>
        new()
        {
            DevotionDays = devotionDays,
            DevotionLastDay = lastDay,
            Stage = Stage(stage, devotionDays),
            Relapse = relapse,
            Vat = new DescentVat { Cap = 4000, TodayXp = (int)(4000 * fillPct / 100), FillPct = fillPct, FillLipPct = 120 },
        };

    private static DescentStage Stage(int n, int bankedDays) =>
        new() { N = n, Key = $"stage_{n}", BankedDays = bankedDays };

    private static DescentBarkMemory Seeded(int stage, string lastBankedDay) =>
        new() { Seeded = true, LastStage = stage, LastBankedDay = lastBankedDay, LastNearBankDay = lastBankedDay };

    private static IEnumerable<JObject> DescentRulesIn(string pack) =>
        LoadRules(pack).Where(r => DescentRules.ContainsKey(IdOf(r)));

    private static IEnumerable<string> LinesIn(string pack) =>
        DescentRulesIn(pack).SelectMany(PoolText);

    private static IEnumerable<string> LinesOf(string pack, string id) =>
        LoadRules(pack).Where(r => IdOf(r) == id).SelectMany(PoolText);

    private static IEnumerable<string> PoolText(JObject rule) =>
        (rule["variant_pool"] as JArray ?? new JArray())
            .Select(v => (string?)v["text"] ?? "")
            .Where(t => t.Length > 0);

    private static List<JObject> LoadRules(string pack)
    {
        var parts = new List<string> { RepoRoot(), "ConditioningControlPanel", "Resources", "sounds", "companion_audio" };
        if (pack.Length > 0) { parts.Add("mods"); parts.Add(pack); }
        parts.Add("bark_rules.json");
        var path = Path.Combine(parts.ToArray());
        Assert.True(File.Exists(path), $"missing bark manifest: {path}");
        // Strict parse on purpose: a malformed manifest must fail here rather than at runtime, where
        // the loader's own leniency would silence the whole companion instead of one rule.
        return JArray.Parse(File.ReadAllText(path)).OfType<JObject>().ToList();
    }

    private static string Describe(string pack) =>
        (pack.Length == 0 ? "base" : pack) + "/bark_rules.json";

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
