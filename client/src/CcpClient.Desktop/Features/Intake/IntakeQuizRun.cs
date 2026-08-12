using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the QuizRunResult the intake page emits on `quiz-result` (the whole point of the
/// bridge — web-shim.js:188-190, contracts.js emptyResult :655-674). JSON member names match
/// the payload's camelCase exactly. Ported from the READ-ONLY WPF evidence
/// (Models/QuizRunResult.cs:18-53) as plain deserialization models.
/// </summary>
public sealed class IntakeQuizRun
{
    /// <summary>PRODUCT_NAME at emit time (the fiction string).</summary>
    [JsonPropertyName("product")] public string Product { get; set; } = string.Empty;

    /// <summary>Niche.* — one of bambi / drone / sissy / circe.</summary>
    [JsonPropertyName("niche")] public string Niche { get; set; } = string.Empty;

    [JsonPropertyName("route")] public IntakeQuizRunRoute Route { get; set; } = new();

    /// <summary>0..1 deepest depth the run reached.</summary>
    [JsonPropertyName("peakDepth")] public double PeakDepth { get; set; }

    /// <summary>Band.* — deepest band entered (calibration/establishing/deepening/climax/recovery).</summary>
    [JsonPropertyName("deepestBand")] public string DeepestBand { get; set; } = string.Empty;

    [JsonPropertyName("rewardProfile")] public IntakeQuizRunReward RewardProfile { get; set; } = new();

    [JsonPropertyName("trajectory")] public List<IntakeQuizAnswer> Trajectory { get; set; } = new();

    /// <summary>Verbatim mantra strings the user affirmed — seeded VERBATIM into the drafted session.</summary>
    [JsonPropertyName("affirmedMantras")] public List<string> AffirmedMantras { get; set; } = new();

    /// <summary>tag → count across the run.</summary>
    [JsonPropertyName("tagTallies")] public Dictionary<string, int> TagTallies { get; set; } = new();

    [JsonPropertyName("totalScore")] public double TotalScore { get; set; }

    [JsonPropertyName("maxScore")] public double MaxScore { get; set; }

    /// <summary>performance.now() at emit (relative, ms).</summary>
    [JsonPropertyName("endedAtMs")] public double EndedAtMs { get; set; }

    [JsonPropertyName("endless")] public bool Endless { get; set; }
}

/// <summary>Revealed archetype trajectory (contracts.Route; QuizRunResult.cs:56-65).</summary>
public sealed class IntakeQuizRunRoute
{
    [JsonPropertyName("primary")] public string Primary { get; set; } = string.Empty;

    [JsonPropertyName("primaryArchetypeId")] public string PrimaryArchetypeId { get; set; } = string.Empty;

    [JsonPropertyName("secondaryArchetypeId")] public string? SecondaryArchetypeId { get; set; }

    [JsonPropertyName("primaryShare")] public double PrimaryShare { get; set; }

    [JsonPropertyName("secondaryShare")] public double SecondaryShare { get; set; }
}

/// <summary>Reward-decoupling summary (contracts.RewardProfile; QuizRunResult.cs:68-76).</summary>
public sealed class IntakeQuizRunReward
{
    [JsonPropertyName("chasedReward")] public bool ChasedReward { get; set; }

    [JsonPropertyName("chaseMagnitude")] public double ChaseMagnitude { get; set; }
}

/// <summary>One trajectory entry (contracts.AnswerRecord; QuizRunResult.cs:79-131). The
/// profiler reads the choice fields only; everything else rides along.</summary>
public sealed class IntakeQuizAnswer
{
    [JsonPropertyName("beatId")] public string BeatId { get; set; } = string.Empty;

    [JsonPropertyName("band")] public string Band { get; set; } = string.Empty;

    [JsonPropertyName("depth")] public double Depth { get; set; }

    [JsonPropertyName("mechanic")] public string Mechanic { get; set; } = string.Empty;

    [JsonPropertyName("promptId")] public string PromptId { get; set; } = string.Empty;

    [JsonPropertyName("correct")] public bool Correct { get; set; }

    [JsonPropertyName("score")] public double Score { get; set; }

    [JsonPropertyName("latencyMs")] public double LatencyMs { get; set; }

    [JsonPropertyName("steered")] public bool Steered { get; set; }

    [JsonPropertyName("rewardFired")] public bool RewardFired { get; set; }

    [JsonPropertyName("rewardDecoupled")] public bool RewardDecoupled { get; set; }

    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();

    /// <summary>Index into the beat's option list that was committed. -1 for free-input
    /// mechanics, interludes, and any trajectory written by an older page build — the default
    /// is deliberately -1, NOT 0, so a missing field can never be mistaken for "the user
    /// picked the first option" (QuizRunResult.cs:103-108). LOAD-BEARING for the profiler's
    /// ChosenIndex &lt; 0 exclusion.</summary>
    [JsonPropertyName("chosenIndex")] public int ChosenIndex { get; set; } = -1;

    [JsonPropertyName("chosenLabel")] public string ChosenLabel { get; set; } = string.Empty;

    /// <summary>Number of options the beat offered (0 for free input).</summary>
    [JsonPropertyName("optionCount")] public int OptionCount { get; set; }

    /// <summary>The prompt's authored heat 0..5 — the profiler weights every axis by this.</summary>
    [JsonPropertyName("promptHeat")] public int PromptHeat { get; set; }

    [JsonPropertyName("steerIntensity")] public double SteerIntensity { get; set; }

    [JsonPropertyName("timeoutMs")] public int TimeoutMs { get; set; }

    /// <summary>Bank entry carried "trick": 1 — deliberately unanswerable; every axis skips it.</summary>
    [JsonPropertyName("isTrick")] public bool IsTrick { get; set; }

    /// <summary>Bank entry carried "freeChoice": 1 — every option is "correct"; every axis skips it.</summary>
    [JsonPropertyName("isFreeChoice")] public bool IsFreeChoice { get; set; }
}

/// <summary>
/// SP-058: the graded-run verdict (#870) — the v6.7.x delta's host obligation, ported as a
/// COMPUTED verdict + typed seams. Upstream emits <c>QuizService.RaiseQuizCompleted(
/// (int)Round(TotalScore), passed: true, perfect: MaxScore > 0 && pct >= TopMarksPercent,
/// category: niche)</c> to the GamificationBridge (IntakeHostService.cs:406-422) and loops
/// <c>App.Quests?.TrackMantraCompleted()</c> min(affirmed, 5) times (:435-441). Greenfield has
/// NO achievement bridge and NO quest verifier — both raises are typed seams (the SP-054
/// "XP computed, not granted" class), evidenced by the OnQuizResult log line. held_back is
/// deliberately unwired upstream too (an intake has no fail state; passed is always true).
/// </summary>
public static class IntakeGraded
{
    /// <summary>"Top marks" bar as a percentage of the run's compliance score
    /// (IntakeHostService.cs:45-53): deliberately NOT full marks — a banded descent scores
    /// partly on pacing, so 100% is unreachable and a 100% bar would dead-letter the
    /// achievements exactly as the collapsed quiz launcher did.</summary>
    public const double TopMarksPercent = 90.0;

    /// <summary>The grade the certificate prints (:414): MaxScore-guarded percentage.</summary>
    public static double ScorePercent(IntakeQuizRun run) =>
        run.MaxScore > 0 ? run.TotalScore / run.MaxScore * 100.0 : 0.0;

    /// <summary>perfect = MaxScore > 0 && pct >= 90.0 (:417 — the guard ported verbatim; a
    /// zero-max run is never top marks).</summary>
    public static bool IsTopMarks(IntakeQuizRun run) =>
        run.MaxScore > 0 && ScorePercent(run) >= TopMarksPercent;

    /// <summary>category = the run's niche, normalized (:418-420 — whitespace → the bambi
    /// fallback, trimmed, lower-invariant) so distinct-niche counting (honor_roll) can never
    /// split on case or padding.</summary>
    public static string Category(IntakeQuizRun run) =>
        string.IsNullOrWhiteSpace(run.Niche) ? IntakeNiche.Fallback : run.Niche.Trim().ToLowerInvariant();

    /// <summary>The mantra-program credit count (:437-438): same min(affirmed, 5) cap as the
    /// XP formula so endless laps can't farm program days.</summary>
    public static int MantraCreditCount(IntakeQuizRun run) =>
        Math.Min(run.AffirmedMantras?.Count ?? 0, 5);
}
