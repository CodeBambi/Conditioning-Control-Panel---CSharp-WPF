using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The last two ungated Wave 1 sites: the Pop Quiz question bank and the generated-session phrase
/// pools. Both were neutralised with no mod lookup at all, so a user with BambiSleep active was
/// asked "What do good subjects do?" and praised with "Good." - wording they had never seen, in a
/// voice that is not their mod's.
///
/// Both resolvers are pure and static, so nothing here needs an App: the Pop Quiz one is called
/// with the strings a mod would supply, and the generator's pool swap is proven through the
/// built-in manifests plus the no-mod path.
/// </summary>
public class NeutralModGatingQuizTests
{
    // ---- Pop Quiz ----------------------------------------------------------

    /// <summary>
    /// The slot constants are indices into a 25-entry array, so they are exactly the kind of thing
    /// that rots when a question is inserted. Pin the neutral text at each one.
    /// </summary>
    [Fact]
    public void The_three_themed_slots_still_point_at_the_right_questions()
    {
        Assert.Equal("What do good subjects do?", PopQuizService.QuestionPool[PopQuizSlots.Obedience].QuestionText);
        Assert.Equal("When I hear praise, I feel...", PopQuizService.QuestionPool[PopQuizSlots.PraiseHeard].QuestionText);
        Assert.Equal("Your favorite word is...", PopQuizService.QuestionPool[PopQuizSlots.FavouriteWord].QuestionText);
    }

    /// <summary>An unmodded install gets the neutral bank back, by reference.</summary>
    [Fact]
    public void No_mod_gets_the_neutral_bank_untouched()
        => Assert.Same(PopQuizService.QuestionPool, PopQuizService.ResolveQuestionPool(null, null, null));

    /// <summary>The 6.9.3 wording, restored for a mod that carries it.</summary>
    [Fact]
    public void BambiSleep_gets_its_6_9_3_questions_and_praise_back()
    {
        var messages = BuiltInMods.BambiSleep.Messages!;

        var pool = PopQuizService.ResolveQuestionPool(
            messages.QuizPraise, messages.QuizObedienceQuestion, messages.QuizPraiseHeardQuestion);

        Assert.Equal("What do good girls do?", pool[PopQuizSlots.Obedience].QuestionText);
        Assert.Equal("Good girl.", pool[PopQuizSlots.Obedience].Affirmations[0]);
        Assert.Equal("When I hear 'good girl,' I feel...", pool[PopQuizSlots.PraiseHeard].QuestionText);
        Assert.Equal("Good girl.", pool[PopQuizSlots.PraiseHeard].Affirmations[0]);
        Assert.Equal("Good girl", pool[PopQuizSlots.FavouriteWord].Answers[PopQuizSlots.FavouriteWordAnswer]);
        Assert.Equal("Good girl.", pool[PopQuizSlots.FavouriteWord].Affirmations[PopQuizSlots.FavouriteWordAnswer]);
    }

    /// <summary>Substitution, not an append: the odds of drawing any one question do not move.</summary>
    [Fact]
    public void A_themed_pool_is_the_same_size_and_only_the_three_slots_differ()
    {
        var messages = BuiltInMods.BambiSleep.Messages!;

        var pool = PopQuizService.ResolveQuestionPool(
            messages.QuizPraise, messages.QuizObedienceQuestion, messages.QuizPraiseHeardQuestion);

        Assert.Equal(PopQuizService.QuestionPool.Length, pool.Length);
        for (int i = 0; i < pool.Length; i++)
        {
            if (i is PopQuizSlots.Obedience or PopQuizSlots.PraiseHeard or PopQuizSlots.FavouriteWord) continue;
            Assert.Same(PopQuizService.QuestionPool[i], pool[i]);
        }
    }

    /// <summary>
    /// A mod's question under the neutral praise reads as a bug on the card, so the praise line
    /// gates all three slots - the same rule the trick pair follows.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    public void A_mod_with_no_praise_line_is_ignored_entirely(string? praise)
    {
        var pool = PopQuizService.ResolveQuestionPool(praise, "What do good girls do?", "When I hear 'good girl,' I feel...");

        Assert.Equal("What do good subjects do?", pool[PopQuizSlots.Obedience].QuestionText);
    }

    /// <summary>
    /// Half of a slot is allowed to be missing the other way round: a mod that names a praise line
    /// but no question still gets its praise on the answer chip, which needs no question of its own.
    /// </summary>
    [Fact]
    public void A_praise_line_alone_still_reaches_the_answer_chip()
    {
        var pool = PopQuizService.ResolveQuestionPool("Good pet.", null, null);

        Assert.Equal("What do good subjects do?", pool[PopQuizSlots.Obedience].QuestionText);
        Assert.Equal("Good pet", pool[PopQuizSlots.FavouriteWord].Answers[PopQuizSlots.FavouriteWordAnswer]);
        Assert.Equal("Good pet.", pool[PopQuizSlots.FavouriteWord].Affirmations[PopQuizSlots.FavouriteWordAnswer]);
    }

    /// <summary>
    /// The resolver clones. The Pop Quiz draws many times a session, and one leak would make a
    /// themed mod's wording permanent for the rest of the process.
    /// </summary>
    [Fact]
    public void Resolving_for_a_mod_does_not_mutate_the_shared_neutral_bank()
    {
        PopQuizService.ResolveQuestionPool("Good girl.", "What do good girls do?", "When I hear 'good girl,' I feel...");

        Assert.Equal("What do good subjects do?", PopQuizService.QuestionPool[PopQuizSlots.Obedience].QuestionText);
        Assert.Equal("Good.", PopQuizService.QuestionPool[PopQuizSlots.Obedience].Affirmations[0]);
        Assert.Equal("Deeper", PopQuizService.QuestionPool[PopQuizSlots.FavouriteWord].Answers[PopQuizSlots.FavouriteWordAnswer]);
    }

    /// <summary>Every themed built-in answers both halves, or neither - never a lone question.</summary>
    [Theory]
    [InlineData(nameof(BuiltInMods.BambiSleep))]
    [InlineData(nameof(BuiltInMods.SissyHypno))]
    [InlineData(nameof(BuiltInMods.Dronification))]
    [InlineData(nameof(BuiltInMods.Locked))]
    public void Every_themed_builtin_names_a_praise_line_for_the_questions_it_names(string mod)
    {
        var messages = Manifest(mod).Messages!;

        Assert.False(string.IsNullOrWhiteSpace(messages.QuizPraise));
        Assert.False(string.IsNullOrWhiteSpace(messages.QuizObedienceQuestion));
        Assert.False(string.IsNullOrWhiteSpace(messages.QuizPraiseHeardQuestion));
    }

    /// <summary>
    /// CCP Default names none. The neutral bank is 25 questions in PopQuizService, and the accessors
    /// do not walk to the base mod, so a value here would be dead weight that silently drifts.
    /// </summary>
    [Fact]
    public void CcpDefault_names_no_quiz_wording()
    {
        var messages = BuiltInMods.CCPDefault.Messages!;

        Assert.Null(messages.QuizPraise);
        Assert.Null(messages.QuizObedienceQuestion);
        Assert.Null(messages.QuizPraiseHeardQuestion);
    }

    [Fact]
    public void Over_long_quiz_wording_is_truncated_not_rejected()
    {
        var manifest = new ModManifest
        {
            Id = "test-quiz-caps",
            Name = "Quiz caps",
            Messages = new ModMessages
            {
                QuizPraise = new string('a', 900),
                QuizObedienceQuestion = new string('b', 900),
                QuizPraiseHeardQuestion = new string('c', 900)
            }
        };

        var error = ModService.SanitizeManifest(manifest);

        Assert.Null(error);
        Assert.Equal(60, manifest.Messages.QuizPraise!.Length);
        Assert.Equal(200, manifest.Messages.QuizObedienceQuestion!.Length);
        Assert.Equal(200, manifest.Messages.QuizPraiseHeardQuestion!.Length);
    }

    // ---- Generated session phrase pools ------------------------------------

    /// <summary>
    /// The seven pools Wave 1 rewrote. A mod that carries them has to carry all seven, or a session
    /// reads half in its voice and half in the house one.
    /// </summary>
    private static readonly string[] QuizPoolCategories =
    {
        "QuizObedienceSubliminal", "QuizObedienceLockCard",
        "QuizSubmissionSubliminal", "QuizSubmissionLockCard",
        "QuizCustomSubliminal", "QuizCustomBouncingText", "QuizCustomLockCard"
    };

    [Theory]
    [InlineData(nameof(BuiltInMods.BambiSleep))]
    [InlineData(nameof(BuiltInMods.SissyHypno))]
    public void The_two_mods_that_owned_the_6_9_3_wording_carry_all_seven_pools(string mod)
    {
        var phrases = Manifest(mod).Phrases!;

        foreach (var category in QuizPoolCategories)
        {
            Assert.True(phrases.ContainsKey(category), $"{mod} is missing {category}");
            Assert.NotEmpty(phrases[category]);
        }
    }

    /// <summary>The exact phrases the neutral pass took away, spot-checked per pool.</summary>
    [Fact]
    public void BambiSleep_keeps_the_6_9_3_session_phrases()
    {
        var phrases = BuiltInMods.BambiSleep.Phrases!;

        Assert.Contains("Good girls listen", phrases["QuizObedienceSubliminal"]);
        Assert.Contains("Good girls follow instructions", phrases["QuizObedienceLockCard"]);
        Assert.Contains("Good girls submit", phrases["QuizSubmissionSubliminal"]);
        Assert.Contains("Good girls surrender", phrases["QuizSubmissionLockCard"]);
        Assert.Contains("Good girl", phrases["QuizCustomSubliminal"]);
        Assert.Contains("So pretty", phrases["QuizCustomSubliminal"]);
        Assert.Contains("GOOD GIRL", phrases["QuizCustomBouncingText"]);
        Assert.Contains("I am a good girl", phrases["QuizCustomLockCard"]);
    }

    /// <summary>
    /// A themed pool has to be a drop-in replacement for the neutral one, or a session drawn from
    /// it is shorter or longer than the generator's own shaping expects.
    /// </summary>
    [Fact]
    public void A_themed_pool_is_the_same_size_as_the_neutral_one_it_replaces()
    {
        var phrases = BuiltInMods.BambiSleep.Phrases!;
        var obedience = QuizSessionGenerator.GetFallbackContent("obedience", 50);
        var submission = QuizSessionGenerator.GetFallbackContent("submission", 50);
        var custom = QuizSessionGenerator.GetFallbackContent("whatever-else", 50);

        Assert.Equal(obedience.SubliminalPhrases.Count, phrases["QuizObedienceSubliminal"].Length);
        Assert.Equal(obedience.LockCardPhrases.Count, phrases["QuizObedienceLockCard"].Length);
        Assert.Equal(submission.SubliminalPhrases.Count, phrases["QuizSubmissionSubliminal"].Length);
        Assert.Equal(submission.LockCardPhrases.Count, phrases["QuizSubmissionLockCard"].Length);
        Assert.Equal(custom.SubliminalPhrases.Count, phrases["QuizCustomSubliminal"].Length);
        Assert.Equal(custom.BouncingTextPhrases.Count, phrases["QuizCustomBouncingText"].Length);
        Assert.Equal(custom.LockCardPhrases.Count, phrases["QuizCustomLockCard"].Length);
    }

    /// <summary>
    /// With no mod layer the generator still answers, in the neutral wording. This is the unmodded
    /// path and also the one every other test in this suite runs on.
    /// </summary>
    [Fact]
    public void With_no_mod_the_generator_serves_the_neutral_pools()
    {
        var obedience = QuizSessionGenerator.GetFallbackContent("obedience", 50);
        var custom = QuizSessionGenerator.GetFallbackContent("whatever-else", 50);

        Assert.Contains("Listening is easy", obedience.SubliminalPhrases);
        Assert.DoesNotContain("Good girls listen", obedience.SubliminalPhrases);
        Assert.Contains("SO GOOD", custom.BouncingTextPhrases);
        Assert.DoesNotContain("GOOD GIRL", custom.BouncingTextPhrases);
    }

    /// <summary>
    /// The per-niche cases are chosen by the intake niche, not by a quiz score, so they were always
    /// the mod's own words and Wave 1 left them alone. Guard against a later pass "neutralising"
    /// them, which would empty the themed niches of their voice.
    /// </summary>
    [Fact]
    public void The_per_niche_cases_are_still_themed()
    {
        Assert.Contains("Bambi Sleep", QuizSessionGenerator.GetFallbackContent("bambi", 50).SubliminalPhrases);
        Assert.Contains("Good girl", QuizSessionGenerator.GetFallbackContent("sissy", 50).SubliminalPhrases);
        Assert.Contains("She holds the key", QuizSessionGenerator.GetFallbackContent("circe", 50).SubliminalPhrases);
    }

    /// <summary>The seven new categories fit inside the manifest's 50-category ceiling.</summary>
    [Theory]
    [InlineData(nameof(BuiltInMods.BambiSleep))]
    [InlineData(nameof(BuiltInMods.SissyHypno))]
    [InlineData(nameof(BuiltInMods.Dronification))]
    [InlineData(nameof(BuiltInMods.Locked))]
    [InlineData(nameof(BuiltInMods.CCPDefault))]
    public void No_builtin_mod_exceeds_the_phrase_category_ceiling(string mod)
    {
        // Read-only on purpose: SanitizeManifest mutates, and these manifests are process-wide
        // singletons that every other test reads.
        var manifest = Manifest(mod);

        Assert.True(manifest.Phrases!.Count <= 50, $"{mod} has {manifest.Phrases.Count} phrase categories");
    }

    /// <summary>
    /// Dronification and Circe's Lock deliberately carry none of the seven. The 6.9.3 wording was
    /// never theirs ("good girls" for a drone), so the neutral pool is the better answer and this
    /// records that as a choice rather than an omission.
    /// </summary>
    [Theory]
    [InlineData(nameof(BuiltInMods.Dronification))]
    [InlineData(nameof(BuiltInMods.Locked))]
    [InlineData(nameof(BuiltInMods.CCPDefault))]
    public void The_mods_the_6_9_3_wording_never_fitted_stay_on_the_neutral_pools(string mod)
    {
        var phrases = Manifest(mod).Phrases!;

        Assert.Empty(QuizPoolCategories.Where(phrases.ContainsKey));
    }

    private static ModManifest Manifest(string name) => name switch
    {
        nameof(BuiltInMods.BambiSleep) => BuiltInMods.BambiSleep,
        nameof(BuiltInMods.SissyHypno) => BuiltInMods.SissyHypno,
        nameof(BuiltInMods.Dronification) => BuiltInMods.Dronification,
        nameof(BuiltInMods.Locked) => BuiltInMods.Locked,
        _ => BuiltInMods.CCPDefault
    };
}
