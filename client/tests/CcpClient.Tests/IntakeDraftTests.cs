using CcpClient.Desktop.Features.Intake;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-054: the drafting sink's deterministic core (QuizSessionGenerator port, scoped per
/// record.md's named limit): difficulty bands, the A5 inverse gate, XP (computed, never
/// granted), naming incl. the #614 archetype suffix, the mantra merge, Lean/Nudge/
/// EnsureRising over the named knobs, tier baselines, and the never-runnable marker.
/// </summary>
public sealed class IntakeDraftTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static IntakeQuizRun Run(double peakDepth, double scorePct = 50, int mantras = 0) =>
        new()
        {
            Niche = "bambi",
            PeakDepth = peakDepth,
            TotalScore = scorePct,
            MaxScore = 100,
            AffirmedMantras = [.. Enumerable.Range(0, mantras).Select(i => $"mantra {i}")],
        };

    private static IntakeQuizAnswer AxisRec(string[] tags, int heat, bool correct) =>
        new() { Band = "deepening", Correct = correct, ChosenIndex = 0, OptionCount = 2, PromptHeat = heat, Tags = [.. tags] };

    // ---------- difficulty bands + the A5 gate ----------

    [Theory]
    [InlineData(0.0, IntakeDraft.IntakeDifficulty.Easy)]
    [InlineData(0.20, IntakeDraft.IntakeDifficulty.Easy)]
    [InlineData(0.21, IntakeDraft.IntakeDifficulty.Medium)]
    [InlineData(0.45, IntakeDraft.IntakeDifficulty.Medium)]
    [InlineData(0.46, IntakeDraft.IntakeDifficulty.Hard)]
    [InlineData(0.72, IntakeDraft.IntakeDifficulty.Hard)]
    [InlineData(0.73, IntakeDraft.IntakeDifficulty.Extreme)]
    [InlineData(1.0, IntakeDraft.IntakeDifficulty.Extreme)]
    [InlineData(-0.5, IntakeDraft.IntakeDifficulty.Easy)]   // clamped 0..1
    [InlineData(2.0, IntakeDraft.IntakeDifficulty.Extreme)] // clamped 0..1
    public void Difficulty_Bands(double depth, IntakeDraft.IntakeDifficulty expected) =>
        Assert.Equal(expected, IntakeDraft.DifficultyFromDepth(depth));

    [Fact]
    public void A5_Well_Sampled_Refusal_Steps_The_Tier_Down_And_Kills_Lock_Cards()
    {
        var run = Run(0.50); // Hard baseline (lock cards ON, freq 2)
        run.Trajectory =
        [
            AxisRec(["confession"], 3, false),
            AxisRec(["honesty"], 4, false),
            AxisRec(["exposure"], 5, false),
        ];
        var draft = IntakeDraft.Generate(run, Now);
        // Tier stepped Hard → Medium; the A5 second half forces lock cards off outright.
        Assert.Equal("Medium", draft.Difficulty);
        Assert.False(draft.Knobs.LockCardEnabled);
        Assert.Null(draft.Knobs.LockCardFrequency);
    }

    [Fact]
    public void A5_Under_Sampled_Never_Fires()
    {
        var run = Run(0.95); // Extreme
        // bambi-class run: zero confession prompts → A5 under-sampled → tier stands.
        var draft = IntakeDraft.Generate(run, Now);
        Assert.Equal("Extreme", draft.Difficulty);
        Assert.True(draft.Profile.Autonomy.UnderSampled);
    }

    // ---------- XP: computed, never granted ----------

    [Theory]
    // 25 + round(clamp(depth)*50) + min(mantras,5)*5, capped 100 (IntakeHostService.cs:389-397).
    [InlineData(0.0, 0, 25)]
    [InlineData(0.55, 2, 63)]  // 25 + round(27.5)=28 + 10
    [InlineData(1.0, 7, 100)]  // 25 + 50 + 25 = 100 (cap)
    [InlineData(1.0, 10, 100)] // mantra clamp: min(10,5)*5 = 25
    [InlineData(0.5, 0, 50)]   // 25 + 25
    public void Completion_Xp_Formula(double depth, int mantras, int expected)
    {
        var run = Run(depth, mantras: mantras);
        Assert.Equal(expected, IntakeDraft.ComputeCompletionXp(run));
        var draft = IntakeDraft.Generate(run, Now);
        Assert.Equal(expected, draft.XpComputed);
    }

    // ---------- naming (#614 archetype suffix) ----------

    [Fact]
    public void Name_Difficulty_Prefix_Niche_And_Archetype_Suffix()
    {
        var run = Run(0.55, scorePct: 66); // 66% → "Intense"
        run.Route.PrimaryArchetypeId = "hers_entirely";
        var draft = IntakeDraft.Generate(run, Now);
        Assert.Equal("Intense Bambi Intake - Hers Entirely", draft.Name);
    }

    [Fact]
    public void Name_No_Archetype_No_Suffix_And_Blank_Niche_Falls_Back_Bambi()
    {
        var run = Run(0.10, scorePct: 10); // 10% → "Gentle"
        run.Niche = "";
        var draft = IntakeDraft.Generate(run, Now);
        Assert.Equal("Gentle Bambi Intake", draft.Name);
        Assert.Equal("bambi", draft.Niche);
        Assert.Contains("Easy", draft.Description);
    }

    [Fact]
    public void Name_Archetype_Already_Contained_Is_Not_Doubled()
    {
        var text = new IntakeDraft.IntakeDraftText { Name = "Deep Bambi Intake - Hers Entirely" };
        var run = Run(0.9, scorePct: 90);
        run.Route.PrimaryArchetypeId = "hers_entirely";
        var draft = IntakeDraft.Generate(run, Now, text);
        Assert.Equal("Deep Bambi Intake - Hers Entirely", draft.Name);
    }

    [Fact]
    public void PrettyId_And_NicheDisplay()
    {
        Assert.Equal("Hers Entirely", IntakeDraft.PrettyId("hers_entirely"));
        Assert.Equal("Locked Beta", IntakeDraft.PrettyId("locked-beta"));
        Assert.Equal("Circe", IntakeDraft.NicheDisplay("circe"));
        Assert.Equal("Intake", IntakeDraft.NicheDisplay(""));
    }

    // ---------- mantra merge ----------

    [Fact]
    public void Mantras_Merge_Verbatim_First_Case_Insensitive_Dedupe()
    {
        var text = new IntakeDraft.IntakeDraftText();
        text.SubliminalPhrases.Add("existing line");
        var run = Run(0.5);
        run.AffirmedMantras = [" i am hers entirely ", "GOOD GIRLS DROP", "good girls drop", "", "  "];
        var draft = IntakeDraft.Generate(run, Now, text);
        Assert.Equal(["i am hers entirely", "GOOD GIRLS DROP", "existing line"], draft.SubliminalPhrases);
        // All three pools get the merge (affirmations read as lock-card lines too).
        Assert.Equal(["i am hers entirely", "GOOD GIRLS DROP"], draft.BouncingTextPhrases);
        Assert.Equal(["i am hers entirely", "GOOD GIRLS DROP"], draft.LockCardPhrases);
    }

    // ---------- Lean / Nudge / EnsureRising ----------

    [Fact]
    public void Lean_And_Nudge_Math()
    {
        Assert.Equal(0.0, IntakeDraft.Lean(IntakeProfiler.Axis.Neutral(2)));
        Assert.Equal(0.3, IntakeDraft.Lean(new IntakeProfiler.Axis { Value = 0.8 }), precision: 6);
        Assert.Equal(11, IntakeDraft.Nudge(10, 0.5, 1, 20));  // half away from zero
        Assert.Equal(9, IntakeDraft.Nudge(10, -0.5, 1, 20));
        Assert.Equal(10, IntakeDraft.Nudge(10, 0.4, 1, 20));
        Assert.Equal(20, IntakeDraft.Nudge(19, 5, 1, 20));    // clamped
        Assert.Equal(1, IntakeDraft.Nudge(2, -5, 1, 20));
    }

    [Fact]
    public void Every_Ramp_Pair_Rises_After_Shaping()
    {
        // Worst case: total refusal on A3 with the Easy baseline (end 30 → 30-30 → clamp 1;
        // start 12 → 12-15 → clamp 1) — EnsureRising must still hold.
        var run = Run(0.10);
        run.Trajectory =
        [
            AxisRec(["arousal"], 4, false), AxisRec(["denial"], 4, false), AxisRec(["chastity"], 4, false),
            AxisRec(["blank"], 4, false), AxisRec(["sinking"], 4, false), AxisRec(["trance"], 4, false),
        ];
        var draft = IntakeDraft.Generate(run, Now);
        Assert.True(draft.Knobs.FlashPerHourEnd >= draft.Knobs.FlashPerHour);
        Assert.True(draft.Knobs.FlashOpacityEnd >= draft.Knobs.FlashOpacity);
        Assert.True(draft.Knobs.SpiralOpacityEnd >= draft.Knobs.SpiralOpacity);
        Assert.True(draft.Knobs.PinkFilterEndOpacity >= draft.Knobs.PinkFilterStartOpacity);
    }

    // ---------- tier baselines + shaping rows ----------

    [Fact]
    public void Tier_Baselines_For_The_Named_Knobs()
    {
        var easy = IntakeDraft.BaselineKnobs(IntakeDraft.IntakeDifficulty.Easy);
        Assert.Equal(12, easy.FlashPerHour);
        Assert.Equal(30, easy.FlashPerHourEnd);
        Assert.Equal(25, easy.FlashOpacity);
        Assert.Equal(45, easy.FlashOpacityEnd);
        Assert.False(easy.FlashHydra);
        Assert.Equal(2, easy.SubliminalPerMin);
        Assert.Equal(40, easy.SubliminalOpacity);
        Assert.False(easy.SpiralEnabled);
        Assert.False(easy.LockCardEnabled);
        Assert.False(easy.MandatoryVideosEnabled);
        Assert.Equal(1, easy.MindWipeBaseMultiplier);
        Assert.Equal(0, easy.PinkFilterStartOpacity);
        Assert.Equal(15, easy.PinkFilterEndOpacity);

        var hard = IntakeDraft.BaselineKnobs(IntakeDraft.IntakeDifficulty.Hard);
        Assert.Equal(70, hard.FlashPerHour);
        Assert.Equal(150, hard.FlashPerHourEnd);
        Assert.True(hard.FlashHydra);
        Assert.True(hard.LockCardEnabled);
        Assert.Equal(2, hard.LockCardFrequency);
        Assert.Equal(10, hard.LockCardStartMinute);
        Assert.True(hard.MandatoryVideosEnabled);
        Assert.Equal(2, hard.VideosPerHour);
        Assert.Equal(3, hard.MindWipeBaseMultiplier);

        var extreme = IntakeDraft.BaselineKnobs(IntakeDraft.IntakeDifficulty.Extreme);
        Assert.Equal(110, extreme.FlashPerHour);
        Assert.Equal(180, extreme.FlashPerHourEnd);
        Assert.Equal(5, extreme.SubliminalPerMin);
        Assert.Equal(3, extreme.LockCardFrequency);
        Assert.Equal(5, extreme.LockCardStartMinute);
        Assert.Equal(3, extreme.VideosPerHour);
    }

    [Fact]
    public void Chase_Branch_Sets_Variable_Ratio_And_Scales_Both_Ramp_Halves()
    {
        var run = Run(0.10); // Easy baseline 12/30
        run.RewardProfile = new IntakeQuizRunReward { ChasedReward = true, ChaseMagnitude = 0.5 };
        var draft = IntakeDraft.Generate(run, Now);
        Assert.True(draft.Knobs.BubblesIntermittent);
        Assert.True(draft.Knobs.FlashHydra);
        // ×(1 + 0.25·0.5) = ×1.125 on BOTH halves, then A3's neutral nudge (no A3 records → lean 0).
        Assert.Equal(13, draft.Knobs.FlashPerHour);      // (int)(12 × 1.125) = 13
        Assert.Equal(33, draft.Knobs.FlashPerHourEnd);   // (int)(30 × 1.125) = 33
    }

    [Fact]
    public void Chase_Below_Threshold_Stays_Steady()
    {
        var run = Run(0.10);
        run.RewardProfile = new IntakeQuizRunReward { ChasedReward = true, ChaseMagnitude = 0.15 };
        var draft = IntakeDraft.Generate(run, Now);
        Assert.False(draft.Knobs.BubblesIntermittent);
        Assert.False(draft.Knobs.FlashHydra);
    }

    [Fact]
    public void A2_Well_Sampled_075_Switches_Lock_Cards_On_At_Easy()
    {
        var run = Run(0.10); // Easy: lock cards OFF at baseline
        // 4 service records, uniform heat, 3 of 4 compliant → 0.75 exactly (the ON/OFF row's floor).
        run.Trajectory =
        [
            AxisRec(["obedience"], 4, true), AxisRec(["service"], 4, true),
            AxisRec(["surrender"], 4, true), AxisRec(["compliance"], 4, false),
        ];
        var draft = IntakeDraft.Generate(run, Now);
        Assert.True(draft.Knobs.LockCardEnabled);
        Assert.Equal(10, draft.Knobs.LockCardStartMinute);
        // freq = Nudge(1, 2×0.25, 1, 4) = 1 + round(0.5) = 2 (AwayFromZero).
        Assert.Equal(2, draft.Knobs.LockCardFrequency);
    }

    [Fact]
    public void A1_Nudges_MindWipe_And_Opacity_But_Not_The_Disabled_Spiral()
    {
        var run = Run(0.10); // Easy: spiral OFF → spiral nudges must not apply.
        run.Trajectory =
        [
            AxisRec(["blank"], 4, true), AxisRec(["sinking"], 4, true), AxisRec(["trance"], 4, true),
        ];
        var draft = IntakeDraft.Generate(run, Now);
        // lean = +0.5: MindWipe 1 + round(1) = 2; SubOpacity 40 + round(10) = 50.
        Assert.Equal(2, draft.Knobs.MindWipeBaseMultiplier);
        Assert.Equal(50, draft.Knobs.SubliminalOpacity);
        Assert.False(draft.Knobs.SpiralEnabled);
        Assert.Equal(0, draft.Knobs.SpiralOpacity);
        Assert.Equal(0, draft.Knobs.SpiralOpacityEnd);
    }

    // ---------- the degraded-delivery contract encoded ----------

    [Fact]
    public void Draft_Is_Marked_Never_Runnable_Typed()
    {
        var draft = IntakeDraft.Generate(Run(0.5), Now);
        Assert.False(draft.Runnable);
        Assert.Equal(IntakeDraft.NeverRunnableReason, draft.RunnableReason);
        Assert.Equal(60, draft.DurationMinutes);
        Assert.Equal(Now, draft.DraftedUtc);
        Assert.False(string.IsNullOrEmpty(draft.Id));
    }
}
