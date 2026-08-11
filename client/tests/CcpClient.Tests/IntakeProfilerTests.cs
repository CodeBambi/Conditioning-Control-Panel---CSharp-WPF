using CcpClient.Desktop.Features.Intake;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-054: the profiler matrix vs the WPF-pinned cases (IntakeProfiler.cs full-source port).
/// Binary endorsement, heat weighting, A5 inversion at heat ≥ 3, the exclusion rules, and
/// the under-sampled → 0.5 neutral contract.
/// </summary>
public sealed class IntakeProfilerTests
{
    private static IntakeQuizAnswer Rec(
        string[] tags, int heat, bool correct, string band = "deepening",
        int chosenIndex = 0, int optionCount = 2, bool isTrick = false, bool isFreeChoice = false) =>
        new()
        {
            Band = band,
            Correct = correct,
            ChosenIndex = chosenIndex,
            OptionCount = optionCount,
            PromptHeat = heat,
            IsTrick = isTrick,
            IsFreeChoice = isFreeChoice,
            Tags = [.. tags],
        };

    private static IntakeQuizRun Run(params IntakeQuizAnswer[] records) =>
        new() { Trajectory = [.. records] };

    [Fact]
    public void Null_And_Empty_Trajectory_All_Neutral_Under_Sampled()
    {
        var nullProfile = IntakeProfiler.ProfileRun(null);
        Assert.Equal(0.5, nullProfile.Blankness.Value);
        Assert.True(nullProfile.Blankness.UnderSampled);
        Assert.Equal(0, nullProfile.ScoreableRecords);

        var emptyProfile = IntakeProfiler.ProfileRun(Run());
        Assert.True(emptyProfile.Service.UnderSampled);
        Assert.Equal(0, emptyProfile.ScoreableRecords);
    }

    [Fact]
    public void All_Compliant_Axis_Scores_1()
    {
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec(["obedience"], 2, true),
            Rec(["service"], 3, true),
            Rec(["surrender"], 5, true)));
        Assert.Equal(1.0, profile.Service.Value);
        Assert.Equal(3, profile.Service.ItemCount);
        Assert.False(profile.Service.UnderSampled);
    }

    [Fact]
    public void All_Refused_Axis_Scores_0()
    {
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec(["arousal"], 2, false),
            Rec(["denial"], 3, false),
            Rec(["chastity"], 4, false)));
        Assert.Equal(0.0, profile.Arousal.Value);
        Assert.False(profile.Arousal.UnderSampled);
    }

    [Fact]
    public void Heat_Weighting_Not_Item_Count()
    {
        // Σ(heat·endorse)/Σ(heat): heat-5 compliant + heat-2 refused + heat-3 refused
        // = 5/10 = 0.5 — the heat-2 refusal costs less than the heat-3 (the point of the weighting).
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec(["blank"], 5, true),
            Rec(["sinking"], 2, false),
            Rec(["trance"], 3, false)));
        Assert.False(profile.Blankness.UnderSampled);
        Assert.Equal(0.5, profile.Blankness.Value, precision: 6);
    }

    [Fact]
    public void Under_Sampled_Axis_Is_Neutral_With_Flag()
    {
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec(["femme"], 4, true),
            Rec(["pink"], 5, true)));
        Assert.Equal(0.5, profile.Presentation.Value);
        Assert.Equal(2, profile.Presentation.ItemCount);
        Assert.True(profile.Presentation.UnderSampled);
    }

    [Fact]
    public void A5_Inverts_And_Uses_The_Hotter_Floor()
    {
        // Three heat-3 confession prompts, all REFUSED → refusal rate 1.0.
        var refused = IntakeProfiler.ProfileRun(Run(
            Rec(["confession"], 3, false),
            Rec(["honesty"], 4, false),
            Rec(["exposure"], 5, false)));
        Assert.Equal(1.0, refused.Autonomy.Value);
        Assert.False(refused.Autonomy.UnderSampled);

        // All confessed → refusal rate 0.0.
        var confessed = IntakeProfiler.ProfileRun(Run(
            Rec(["confession"], 3, true),
            Rec(["honesty"], 4, true),
            Rec(["exposure"], 5, true)));
        Assert.Equal(0.0, confessed.Autonomy.Value);

        // Heat-2 confession records sit below A5's floor (MinHeat=2 < AutonomyHotHeat=3).
        var tooCool = IntakeProfiler.ProfileRun(Run(
            Rec(["confession"], 2, false),
            Rec(["honesty"], 2, false),
            Rec(["exposure"], 2, false)));
        Assert.True(tooCool.Autonomy.UnderSampled);
        Assert.Equal(0, tooCool.Autonomy.ItemCount);
    }

    [Theory]
    // trick: deliberately unanswerable — no preference signal.
    [InlineData("trick", 5, true, "deepening", 0, 2, true, false)]
    // freeChoice: every option "correct" — correctness means nothing.
    [InlineData("freechoice", 5, true, "deepening", 0, 4, false, true)]
    // recovery band: never graded.
    [InlineData("recovery", 5, true, "recovery", 0, 2, false, false)]
    // no committed option (mantra / check-in / interlude).
    [InlineData("nochoice", 5, true, "deepening", -1, 2, false, false)]
    // forced-compliance single option.
    [InlineData("mono", 5, true, "deepening", 0, 1, false, false)]
    public void Excluded_Records_Never_Score(string _, int heat, bool correct, string band,
        int chosenIndex, int optionCount, bool isTrick, bool isFreeChoice)
    {
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec(["surrender", "obedience"], heat, correct, band, chosenIndex, optionCount, isTrick, isFreeChoice)));
        Assert.Equal(0, profile.ScoreableRecords);
        Assert.True(profile.Service.UnderSampled);
    }

    [Fact]
    public void Empty_Tags_And_Disqualifying_Tags_Excluded()
    {
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec([], 5, true),
            Rec(["trivia", "surrender"], 5, true),
            Rec(["colorpick", "arousal"], 5, true),
            Rec(["trick", "obedience"], 5, true)));
        Assert.Equal(0, profile.ScoreableRecords);
    }

    [Fact]
    public void Structural_Tags_Never_Satisfy_An_Axis()
    {
        // "mantra" alone: scoreable record (no disqualifying tag) but satisfies NO axis.
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec(["mantra"], 4, true),
            Rec(["curious"], 4, true),
            Rec(["mono"], 4, true)));
        Assert.Equal(3, profile.ScoreableRecords);
        Assert.True(profile.Blankness.UnderSampled);
        Assert.True(profile.Service.UnderSampled);
        Assert.True(profile.Arousal.UnderSampled);
        Assert.True(profile.Presentation.UnderSampled);
        Assert.True(profile.Autonomy.UnderSampled);
    }

    [Fact]
    public void Below_MinHeat_Never_Counts()
    {
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec(["obedience"], 1, true),
            Rec(["service"], 1, true),
            Rec(["surrender"], 1, true)));
        Assert.True(profile.Service.UnderSampled);
        Assert.Equal(0, profile.Service.ItemCount);
    }

    [Fact]
    public void A_Record_With_Two_Axis_Tags_Scores_Into_Both()
    {
        var profile = IntakeProfiler.ProfileRun(Run(
            Rec(["arousal", "locked"], 4, true),   // A3 (arousal + locked are both A3 tags)
            Rec(["locked", "chastity"], 4, false),
            Rec(["denial", "permission"], 4, true)));
        // All three records are A3; 2 of 3 compliant, uniform heat → 8/12.
        Assert.False(profile.Arousal.UnderSampled);
        Assert.Equal(8.0 / 12.0, profile.Arousal.Value, precision: 6);
    }
}
