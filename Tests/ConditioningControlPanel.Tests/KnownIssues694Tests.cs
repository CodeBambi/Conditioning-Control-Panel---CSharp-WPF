using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The 6.9.4 known-issue sweep: the pieces of it that are pure enough to pin down without WPF.
/// </summary>
public class KnownIssues694Tests
{
    [Fact]
    public void Preset_round_trips_the_five_lock_card_dials()
    {
        var s = new AppSettings
        {
            LockCardEnabled = true,
            LockCardRandomRepeats = true,
            LockCardRepeatsMin = 2,
            LockCardRepeats = 7,
            LockCardTargetLengthEnabled = true,
            LockCardTargetLength = 240,
            LockCardTargetLengthVariance = 55,
        };

        var preset = Preset.FromSettings(s, "dials");
        var applied = new AppSettings();
        preset.ApplyTo(applied);

        Assert.True(applied.LockCardRandomRepeats);
        Assert.Equal(2, applied.LockCardRepeatsMin);
        Assert.Equal(7, applied.LockCardRepeats);
        Assert.True(applied.LockCardTargetLengthEnabled);
        Assert.Equal(240, applied.LockCardTargetLength);
        Assert.Equal(55, applied.LockCardTargetLengthVariance);
    }

    [Fact]
    public void Preset_written_before_the_dials_applies_them_as_off()
    {
        // A preset object with the new members at their defaults is what an older JSON deserialises to.
        var preset = new Preset();
        var applied = new AppSettings
        {
            LockCardRandomRepeats = true,
            LockCardTargetLengthEnabled = true,
        };
        preset.ApplyTo(applied);

        Assert.False(applied.LockCardRandomRepeats);
        Assert.False(applied.LockCardTargetLengthEnabled);
        Assert.Equal(1, applied.LockCardRepeatsMin);
        Assert.Equal(120, applied.LockCardTargetLength);
        Assert.Equal(20, applied.LockCardTargetLengthVariance);
    }

    [Fact]
    public void Cloud_restore_keeps_the_machine_local_settings()
    {
        var current = new AppSettings
        {
            CustomAssetsPath = @"D:\stuff\ccp-assets",
            DiscordWebhookUrl = "https://discord.com/api/webhooks/1/abc",
            LastSeenUtc = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
        };
        var restored = new AppSettings();   // a backup never carries these, so they arrive at defaults

        ProfileSyncService.PreserveLocalOnlyFields(current, restored);

        Assert.Equal(@"D:\stuff\ccp-assets", restored.CustomAssetsPath);
        Assert.Equal("https://discord.com/api/webhooks/1/abc", restored.DiscordWebhookUrl);
        Assert.Equal(current.LastSeenUtc, restored.LastSeenUtc);
    }

    [Fact]
    public void Cloud_restore_helper_tolerates_nulls()
    {
        ProfileSyncService.PreserveLocalOnlyFields(null!, new AppSettings());
        ProfileSyncService.PreserveLocalOnlyFields(new AppSettings(), null!);
    }

    [Theory]
    [InlineData("flash", QuestCategory.Flash)]
    [InlineData("BLINK", QuestCategory.BlinkTrainer)]
    [InlineData("no-such-category", QuestCategory.Combined)]
    [InlineData(null, QuestCategory.Combined)]
    public void Unknown_quest_categories_still_land_in_combined(string? raw, QuestCategory expected)
    {
        Assert.Equal(expected, QuestDefinition.ParseCategory(raw!));
    }

    [Theory]
    [InlineData(null, 0, 0)]
    [InlineData("", 0, 0)]
    [InlineData("good girl", 0, 1)]
    [InlineData("line one\nline two\r\nline three", 0, 3)]
    [InlineData("a\uFFFDb\u0001c\td", 2, 1)]
    public void Ai_reply_shape_counts_garbage_and_lines_not_text(string? text, int garbled, int lines)
    {
        Assert.Equal(garbled, AiService.CountGarbledChars(text));
        Assert.Equal(lines, AiService.CountLines(text));
    }

    [Fact]
    public void Mod_messages_block_is_kept_when_only_a_new_field_is_set()
    {
        Assert.False(ModMessages.HasAnyValue(null));
        Assert.False(ModMessages.HasAnyValue(new ModMessages()));
        Assert.False(ModMessages.HasAnyValue(new ModMessages { AttentionCheckFail = "" }));
        Assert.True(ModMessages.HasAnyValue(new ModMessages { QuizTrickAnswer = "bambi" }));
        Assert.True(ModMessages.HasAnyValue(new ModMessages { MarqueeBanner = "GOOD GIRLS CONDITION DAILY" }));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(50, false)]
    public void Dashboard_toggle_hint_retires_after_three_uses(int uses, bool shown)
    {
        Assert.Equal(shown, DashboardToggleHintRule.ShouldShow(uses));
    }
}
