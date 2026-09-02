using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The skill tree used to be split in two. Stat and analytics nodes were "permanent" and survived
/// the monthly rollover, while the mechanical XP-economy nodes were "seasonal": the server dropped
/// them, the player bought them again, and that re-buy was the entire Prestige loop.
///
/// <para>The Descent ended monthly seasons on 2026-09-01 and the server now suppresses the wipe
/// permanently, so <c>level_reset</c> never fires again. That left the split in a worse state than
/// merely unused, because the half of it that still ran was the half that could take a purchase
/// away: a rolling-backup restore could still prune a tree down to the permanent ids long after
/// any season existed to justify it, and the purchase dialog was still telling users that a skill
/// "resets at the monthly rollover". Every skill is permanent now, and these pin that down.</para>
///
/// <para>Kept pure on purpose. <c>SkillDefinition</c> reaches nothing but its own statics and the
/// language files are read off disk, so the law is exercisable here rather than only through a
/// live sync against a running app.</para>
/// </summary>
public class SkillsAllPermanentTests
{
    /// <summary>
    /// The nodes that used to be dropped at every rollover. If any one of these ever reports
    /// itself as non-permanent again, the mechanical half of the split has come back to life.
    /// </summary>
    private static readonly string[] FormerlySeasonal =
    {
        "sparkle_boost_1", "good_girl_streak", "sparkle_boost_2", "lucky_bimbo",
        "milestone_rewards", "oopsie_insurance", "quest_refresh", "better_quests",
        "sparkle_boost_3", "lucky_bubbles", "pink_rush", "streak_power",
        "reroll_addict", "perfect_bimbo_week", "night_shift", "early_bird_bimbo"
    };

    [Fact]
    public void EverySkillInTheTreeIsPermanent()
    {
        var notPermanent = SkillDefinition.All.Where(s => !s.IsPermanent).Select(s => s.Id).ToList();
        Assert.True(notPermanent.Count == 0,
            "These skills still report themselves as seasonal: " + string.Join(", ", notPermanent));
    }

    [Fact]
    public void PermanentIdsCoversTheWholeTreeAndNothingElse()
    {
        var all = SkillDefinition.All.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var permanent = SkillDefinition.PermanentIds.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(all.Count, SkillDefinition.PermanentIds.Count);
        Assert.True(all.SetEquals(permanent),
            "PermanentIds no longer mirrors the tree. Missing: " +
            string.Join(", ", all.Except(permanent)) + " | unknown: " +
            string.Join(", ", permanent.Except(all)));
    }

    /// <summary>
    /// The sync paths filter with <c>Where(PermanentIds.Contains)</c>, so this is the assertion
    /// that actually protects a purchase: whatever reaches that filter, it can no longer subtract.
    /// </summary>
    [Fact]
    public void TheMechanicalNodesSurviveThePermanentIdsFilter()
    {
        foreach (var id in FormerlySeasonal)
        {
            Assert.True(SkillDefinition.All.Any(s => s.Id == id),
                $"Test is stale: '{id}' is no longer a skill in the tree.");
            Assert.True(SkillDefinition.PermanentIds.Contains(id),
                $"'{id}' would still be dropped by a season-reset filter.");
        }
    }

    /// <summary>
    /// PermanentIds is computed lazily because it sits above <c>All</c> in the file and a field
    /// initializer would read a null list. Touching it first, in a fresh process, is the case that
    /// would have thrown, so it is worth its own test rather than riding on the others' ordering.
    /// </summary>
    [Fact]
    public void PermanentIdsIsSafeToTouchBeforeAnythingElse()
    {
        var ids = SkillDefinition.PermanentIds;
        Assert.NotNull(ids);
        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    // ---------- copy ----------

    [Fact]
    public void TheSeasonalNoteIsRetiredFromEveryLanguage()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            Assert.False(CompanionLocMasters.For(lang).ContainsKey("msg_skill_seasonal_note"),
                $"{lang}.json still carries msg_skill_seasonal_note, which promises a rollover that cannot happen.");
        }
    }

    [Fact]
    public void ThePermanentNoteAndPrestigeTooltipReachedAllNineLanguages()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in new[] { "msg_skill_permanent_note", "tooltip_prestige" })
            {
                Assert.True(file.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text),
                    $"{lang}.json is missing {key}.");
            }
        }
    }

    /// <summary>
    /// House copy law: no em-dashes in anything a user reads. Checked on the two strings this
    /// change rewrote, in all nine files, because a translation is exactly where one sneaks back.
    /// </summary>
    [Fact]
    public void TheRewrittenCopyCarriesNoEmDashes()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in new[] { "msg_skill_permanent_note", "tooltip_prestige" })
            {
                var text = file[key];
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
            }
        }
    }

    /// <summary>
    /// The English masters are the ones this change authored, so they get the substantive check:
    /// neither string may promise, imply or even mention a reset any more.
    /// </summary>
    [Fact]
    public void TheEnglishCopyNoLongerMentionsSeasonsOrResets()
    {
        var forbidden = new[] { "season", "reset", "rollover", "re-buy", "rebuy" };
        foreach (var key in new[] { "msg_skill_permanent_note", "tooltip_prestige" })
        {
            var text = CompanionLocMasters.English[key];
            foreach (var word in forbidden)
            {
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"en.json {key} still says \"{word}\": {text}");
            }
        }
    }
}
