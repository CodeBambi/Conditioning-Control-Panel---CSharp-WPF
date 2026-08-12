using CcpClient.Desktop.Features.Intake;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-058: the graded-run verdict (#870) — TopMarksPercent pinned WITH its comparison
/// (PROMPT framing c: a test that only asserts the constant is not evidence the verdict
/// matches). Upstream: IntakeHostService.cs:45-53 (the 90.0 bar + why-not-100 comment),
/// :414-422 (pct + perfect + category), :435-441 (mantra credit cap).
/// </summary>
public sealed class IntakeGradedTests
{
    private static IntakeQuizRun Run(double total, double max, string niche = "bambi", int mantras = 0) => new()
    {
        Niche = niche,
        TotalScore = total,
        MaxScore = max,
        AffirmedMantras = Enumerable.Range(0, mantras).Select(i => $"m{i}").ToList(),
    };

    [Fact]
    public void Bar_Is_Ninety_Not_Full_Marks()
    {
        // :45-53 — deliberately NOT 100 (a banded descent scores partly on pacing).
        Assert.Equal(90.0, IntakeGraded.TopMarksPercent);
    }

    [Fact]
    public void Exactly_Ninety_Is_Top_Marks()
    {
        // The boundary: pct >= 90.0 (>=, not >) — 9/10 lands exactly on the bar.
        var run = Run(9, 10);
        Assert.Equal(90.0, IntakeGraded.ScorePercent(run));
        Assert.True(IntakeGraded.IsTopMarks(run));
    }

    [Fact]
    public void Just_Below_The_Bar_Is_Not_Top_Marks()
    {
        // 8.999/10 = 89.99000000000001 in doubles — just under, must NOT round up.
        var run = Run(8.999, 10);
        Assert.False(IntakeGraded.IsTopMarks(run));
    }

    [Fact]
    public void Full_Marks_On_A_Zero_Max_Run_Is_Never_Top_Marks()
    {
        // The MaxScore > 0 guard (:417) — ported verbatim even though pct would be 0.0
        // anyway: a zero-max run has no grade at all.
        var run = Run(0, 0);
        Assert.Equal(0.0, IntakeGraded.ScorePercent(run));
        Assert.False(IntakeGraded.IsTopMarks(run));
    }

    [Fact]
    public void Percent_Is_Zero_When_Max_Is_Zero_Even_With_A_Score()
    {
        Assert.Equal(0.0, IntakeGraded.ScorePercent(Run(5, 0)));
    }

    [Theory]
    [InlineData("bambi", "bambi")]
    [InlineData(" Sissy ", "sissy")]   // trimmed + lower-invariant (:418-420)
    [InlineData("DRONE", "drone")]
    [InlineData("", "bambi")]          // whitespace/empty → the fallback niche
    [InlineData("   ", "bambi")]
    public void Category_Is_The_Normalized_Niche(string niche, string expected)
    {
        Assert.Equal(expected, IntakeGraded.Category(Run(1, 1, niche)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(9, 5)]   // the min(affirmed, 5) cap (:437-438) — endless laps can't farm
    public void Mantra_Credit_Caps_At_Five(int affirmed, int expected)
    {
        Assert.Equal(expected, IntakeGraded.MantraCreditCount(Run(1, 1, mantras: affirmed)));
    }
}
