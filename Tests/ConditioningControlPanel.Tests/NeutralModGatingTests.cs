using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Wave 1 (the "a fresh install with no mod is neutral" pass) replaced four Bambi-flavoured
/// literals with neutral ones and forgot the mod lookup at each, so a user who had chosen
/// BambiSleep lost the line they had been reading since 6.9.3. The lines now live in the mod
/// manifest (<see cref="ModMessages"/>), which means two invariants are worth pinning:
///
/// <list type="number">
///   <item>BambiSleep still carries the exact 6.9.3 wording.</item>
///   <item>CCP Default - the base mod, and therefore what an unmodded install resolves to -
///         carries the neutral wording, so nothing has to fall back to a hard-coded literal.</item>
/// </list>
///
/// No App statics are touched: these read the built-in manifests directly and exercise the pure
/// half of the quiz resolver.
/// </summary>
public class NeutralModGatingTests
{
    // The 6.9.3 troll line, verbatim (attention check PASSED, watch it again anyway).
    private const string Bambi693Troll = "GOOD GIRL!\nWATCH AGAIN \U0001F61C";

    [Fact]
    public void BambiSleep_keeps_the_6_9_3_troll_replay_line()
        => Assert.Equal(Bambi693Troll, BuiltInMods.BambiSleep.Messages!.AttentionCheckTroll);

    [Fact]
    public void BambiSleep_keeps_the_6_9_3_gaze_praise_card()
        => Assert.Equal("GOOD GIRL", BuiltInMods.BambiSleep.Messages!.GazeCorrect);

    [Fact]
    public void BambiSleep_keeps_the_6_9_3_trick_question_and_its_answer()
    {
        var messages = BuiltInMods.BambiSleep.Messages!;
        Assert.Equal("Are you a good girl?", messages.QuizTrickQuestion);
        Assert.Equal("Obviously", messages.QuizTrickAnswer);
    }

    [Fact]
    public void CcpDefault_carries_the_neutral_wording_for_both_walked_messages()
    {
        var messages = BuiltInMods.CCPDefault.Messages!;
        Assert.Equal("NICE TRY!\nWATCH AGAIN \U0001F61C", messages.AttentionCheckTroll);
        Assert.Equal("GOOD", messages.GazeCorrect);
    }

    /// <summary>
    /// The quiz accessor deliberately does not walk to the base mod, so CCP Default naming a trick
    /// question would make the neutral pool a one-liner for every mod that ships none.
    /// </summary>
    [Fact]
    public void CcpDefault_names_no_trick_question()
    {
        var messages = BuiltInMods.CCPDefault.Messages!;
        Assert.Null(messages.QuizTrickQuestion);
        Assert.Null(messages.QuizTrickAnswer);
    }

    /// <summary>
    /// Every built-in that speaks in a voice of its own answers both walked messages itself,
    /// rather than inheriting CCP Default's neutral wording by accident.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemedBuiltIns))]
    public void Every_themed_builtin_answers_both_walked_messages(ModManifest mod)
    {
        Assert.False(string.IsNullOrWhiteSpace(mod.Messages?.AttentionCheckTroll));
        Assert.False(string.IsNullOrWhiteSpace(mod.Messages?.GazeCorrect));
    }

    public static TheoryData<ModManifest> ThemedBuiltIns() => new()
    {
        BuiltInMods.BambiSleep,
        BuiltInMods.SissyHypno,
        BuiltInMods.Dronification,
        BuiltInMods.Locked,
    };

    // ---- The quiz resolver -------------------------------------------------

    [Fact]
    public void No_mod_question_leaves_the_neutral_pool_untouched()
    {
        var pool = QuizWindow.ResolveTrickQuestions(null, null);

        Assert.Equal("Are you ready to let go?", pool[QuizWindow.ModTrickSlot].Question);
        Assert.Equal("Obviously", pool[QuizWindow.ModTrickSlot].Answer);
    }

    [Fact]
    public void A_mod_pair_takes_over_exactly_one_slot()
    {
        var neutral = QuizWindow.ResolveTrickQuestions(null, null);
        var modded = QuizWindow.ResolveTrickQuestions("Are you a good girl?", "Obviously");

        Assert.Equal(neutral.Length, modded.Length);
        Assert.Equal("Are you a good girl?", modded[QuizWindow.ModTrickSlot].Question);
        for (int i = 0; i < neutral.Length; i++)
        {
            if (i == QuizWindow.ModTrickSlot) continue;
            Assert.Equal(neutral[i], modded[i]);
        }
    }

    /// <summary>
    /// Half a pair is worse than none: a mod's question under someone else's answer reads as a bug
    /// on the card, so the resolver ignores it.
    /// </summary>
    [Theory]
    [InlineData("Are you a good girl?", null)]
    [InlineData(null, "Obviously")]
    [InlineData("Are you a good girl?", "   ")]
    [InlineData("  ", "Obviously")]
    public void Half_a_pair_is_ignored(string? question, string? answer)
    {
        var pool = QuizWindow.ResolveTrickQuestions(question, answer);

        Assert.Equal("Are you ready to let go?", pool[QuizWindow.ModTrickSlot].Question);
    }

    /// <summary>
    /// The resolver clones, so a themed mod cannot leak its wording into the static neutral pool
    /// for the rest of the process (the quiz is opened many times per run).
    /// </summary>
    [Fact]
    public void Resolving_for_a_mod_does_not_mutate_the_shared_neutral_pool()
    {
        QuizWindow.ResolveTrickQuestions("Are you a good girl?", "Obviously");

        var after = QuizWindow.ResolveTrickQuestions(null, null);
        Assert.Equal("Are you ready to let go?", after[QuizWindow.ModTrickSlot].Question);
    }

    // ---- Sanitisation ------------------------------------------------------

    /// <summary>
    /// The new fields are author-supplied in a downloaded .ccpmod, so they are capped like every
    /// other manifest string. The quiz pair is capped shorter: both land inside a fixed card.
    /// </summary>
    [Fact]
    public void Over_long_new_message_fields_are_truncated_not_rejected()
    {
        var manifest = new ModManifest
        {
            Id = "test-caps",
            Name = "Caps",
            Messages = new ModMessages
            {
                AttentionCheckTroll = new string('a', 900),
                GazeCorrect = new string('b', 900),
                QuizTrickQuestion = new string('c', 900),
                QuizTrickAnswer = new string('d', 900),
            }
        };

        var error = ModService.SanitizeManifest(manifest);

        Assert.Null(error);
        Assert.Equal(500, manifest.Messages.AttentionCheckTroll!.Length);
        Assert.Equal(500, manifest.Messages.GazeCorrect!.Length);
        Assert.Equal(200, manifest.Messages.QuizTrickQuestion!.Length);
        Assert.Equal(60, manifest.Messages.QuizTrickAnswer!.Length);
    }
}
