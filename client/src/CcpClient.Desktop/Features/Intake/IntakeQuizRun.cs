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
/// COMPUTED verdict. Upstream emits <c>QuizService.RaiseQuizCompleted(
/// (int)Round(TotalScore), passed: true, perfect: MaxScore > 0 && pct >= TopMarksPercent,
/// category: niche)</c> to the GamificationBridge (IntakeHostService.cs:431-435) and loops
/// <c>App.Quests?.TrackMantraCompleted()</c> min(affirmed, 5) times (:451-453).
///
/// <para>SP-128: the achievement half is NO LONGER A SEAM — <see cref="Record"/> feeds the
/// verdict to <see cref="Progression.GradedRunAwards"/>, which awards <c>top_of_the_class</c>
/// and <c>honor_roll</c> at upstream's thresholds (GamificationBridge.cs:598-609). The QUEST
/// half is still a typed seam (no quest verifier in this port), and held_back is deliberately
/// unwired upstream too (an intake has no fail state; passed is always true — :433).</para>
///
/// <para><b>CITATIONS REPAIRED AT SP-128.</b> Every bare <c>:NNN</c> below resolves against
/// <c>Services/Quiz/IntakeHostService.cs</c>, and all seven were wrong: four were wrong the day
/// they were written (D223 recorded two of them) and three drifted when <c>f7b4c317c</c>
/// (<i>"feat(media): remote media app-wide"</i>) added 106 lines to that file — the ONLY commit
/// to touch it since SP-058's stated baseline <c>0c9947a6</c>. The worst was the category
/// citation, which pointed at the <c>held_back</c> comment, i.e. the semantic opposite of what
/// it claimed. Re-derived against the bytes at <c>71ab1bac2</c>; see D232.</para>
/// </summary>
public static class IntakeGraded
{
    /// <summary>"Top marks" bar as a percentage of the run's compliance score
    /// (IntakeHostService.cs:48-55 — the rationale at :49-53, the constant at :55):
    /// deliberately NOT full marks — a banded descent scores partly on pacing, so 100% is
    /// unreachable and a 100% bar would dead-letter the achievements exactly as the collapsed
    /// quiz launcher did.</summary>
    public const double TopMarksPercent = 90.0;

    /// <summary>The grade the certificate prints (:426): MaxScore-guarded percentage.</summary>
    public static double ScorePercent(IntakeQuizRun run) =>
        run.MaxScore > 0 ? run.TotalScore / run.MaxScore * 100.0 : 0.0;

    /// <summary>perfect = MaxScore > 0 && pct >= 90.0 (:434 — the guard ported verbatim; a
    /// zero-max run is never top marks).</summary>
    public static bool IsTopMarks(IntakeQuizRun run) =>
        run.MaxScore > 0 && ScorePercent(run) >= TopMarksPercent;

    /// <summary>category = the run's niche, normalized (:427-429 — whitespace → the bambi
    /// fallback, trimmed, lower-invariant) so distinct-niche counting (honor_roll) can never
    /// split on case or padding. The award consumer normalises AGAIN, on purpose: this call is
    /// the producer's and upstream shows why a producer is the wrong place to rely on it
    /// (GradedRunAwards remarks; D226).</summary>
    public static string Category(IntakeQuizRun run) =>
        string.IsNullOrWhiteSpace(run.Niche) ? IntakeNiche.Fallback : run.Niche.Trim().ToLowerInvariant();

    /// <summary>The mantra-program credit count (:451, looped at :452-453): same min(affirmed, 5)
    /// cap as the XP formula so endless laps can't farm program days. Still a typed seam — this
    /// port has no quest verifier to credit.</summary>
    public static int MantraCreditCount(IntakeQuizRun run) =>
        Math.Min(run.AffirmedMantras?.Count ?? 0, 5);

    /// <summary>
    /// SP-128: hand this run's verdict to the award consumer — the port's stand-in for
    /// <c>QuizService.RaiseQuizCompleted</c> (:431-435) reaching
    /// <c>GamificationBridge.OnQuizCompleted</c> (GamificationBridge.cs:578).
    ///
    /// <para>The static event and its event args (<c>QuizService.cs:29</c>, <c>:32-35</c>) are
    /// NOT ported: a process-wide static event is the mechanism, not the outcome, and this port
    /// has exactly one producer. The property upstream's comment actually asserts — that the
    /// handler <i>"reads a grade and a category and does not care which surface produced
    /// them"</i> (GamificationBridge.cs:566-567) — is preserved in the CONSUMER'S SIGNATURE,
    /// which takes those two values and nothing else.</para>
    /// </summary>
    public static Progression.GradedRunAwardOutcome Record(Progression.GradedRunAwards awards, IntakeQuizRun run) =>
        awards.RecordGradedRun(IsTopMarks(run), Category(run));
}
