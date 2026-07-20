using System.Collections.Generic;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// C# mirror of the "Graded Intake" web core's final emit — the JSON shape defined
    /// in <c>Resources/web/intake/core/contracts.js</c> (the <c>QuizRunResult</c> typedef).
    /// The web page posts <c>{ type:'quiz-result', result: QuizRunResult }</c> over the
    /// WebView2 bridge (web-shim.js emitResult); <see cref="Services.Quiz.IntakeHostService"/>
    /// deserialises the <c>result</c> object into this DTO and hands it to
    /// <see cref="Services.QuizSessionGenerator.GenerateSession(QuizRunResult, Services.SessionTextContent?)"/>.
    ///
    /// Property names are pinned with <see cref="JsonPropertyAttribute"/> so the mapping
    /// stays byte-for-byte with contracts.js regardless of Newtonsoft's default casing.
    /// Numbers are doubles: the web side emits raw JS numbers (scores may be fractional).
    /// </summary>
    public sealed class QuizRunResult
    {
        /// <summary>PRODUCT_NAME at emit time (contracts.PRODUCT_NAME — the fiction string).</summary>
        [JsonProperty("product")] public string Product { get; set; } = string.Empty;

        /// <summary>Niche.* — one of bambi / drone / sissy.</summary>
        [JsonProperty("niche")] public string Niche { get; set; } = string.Empty;

        [JsonProperty("route")] public QuizRunRoute Route { get; set; } = new();

        /// <summary>0..1 deepest depth the run reached.</summary>
        [JsonProperty("peakDepth")] public double PeakDepth { get; set; }

        /// <summary>Band.* — deepest band entered (calibration/establishing/deepening/climax/recovery).</summary>
        [JsonProperty("deepestBand")] public string DeepestBand { get; set; } = string.Empty;

        [JsonProperty("rewardProfile")] public QuizRunRewardProfile RewardProfile { get; set; } = new();

        [JsonProperty("trajectory")] public List<QuizRunAnswerRecord> Trajectory { get; set; } = new();

        /// <summary>Verbatim mantra strings the user affirmed — seeded VERBATIM into the drafted session.</summary>
        [JsonProperty("affirmedMantras")] public List<string> AffirmedMantras { get; set; } = new();

        /// <summary>tag -&gt; count across the run (drives subsystem emphasis).</summary>
        [JsonProperty("tagTallies")] public Dictionary<string, int> TagTallies { get; set; } = new();

        [JsonProperty("totalScore")] public double TotalScore { get; set; }

        [JsonProperty("maxScore")] public double MaxScore { get; set; }

        /// <summary>performance.now() at emit (relative, ms).</summary>
        [JsonProperty("endedAtMs")] public double EndedAtMs { get; set; }

        /// <summary>Was this an endless run.</summary>
        [JsonProperty("endless")] public bool Endless { get; set; }
    }

    /// <summary>Revealed archetype trajectory (contracts.Route).</summary>
    public sealed class QuizRunRoute
    {
        [JsonProperty("primary")] public string Primary { get; set; } = string.Empty;
        [JsonProperty("primaryArchetypeId")] public string PrimaryArchetypeId { get; set; } = string.Empty;
        [JsonProperty("secondaryArchetypeId")] public string? SecondaryArchetypeId { get; set; }
        [JsonProperty("primaryShare")] public double PrimaryShare { get; set; }
        [JsonProperty("secondaryShare")] public double SecondaryShare { get; set; }
    }

    /// <summary>Reward-decoupling summary (contracts.RewardProfile).</summary>
    public sealed class QuizRunRewardProfile
    {
        /// <summary>Did the user drift toward the decoupled reward.</summary>
        [JsonProperty("chasedReward")] public bool ChasedReward { get; set; }

        /// <summary>0..1 how strongly (correct-rate drop as reward decoupled).</summary>
        [JsonProperty("chaseMagnitude")] public double ChaseMagnitude { get; set; }
    }

    /// <summary>One entry of the run trajectory (contracts.AnswerRecord).</summary>
    public sealed class QuizRunAnswerRecord
    {
        [JsonProperty("beatId")] public string BeatId { get; set; } = string.Empty;
        [JsonProperty("band")] public string Band { get; set; } = string.Empty;
        [JsonProperty("depth")] public double Depth { get; set; }
        [JsonProperty("mechanic")] public string Mechanic { get; set; } = string.Empty;
        [JsonProperty("promptId")] public string PromptId { get; set; } = string.Empty;
        [JsonProperty("correct")] public bool Correct { get; set; }
        [JsonProperty("score")] public double Score { get; set; }
        [JsonProperty("latencyMs")] public double LatencyMs { get; set; }
        [JsonProperty("steered")] public bool Steered { get; set; }
        [JsonProperty("rewardFired")] public bool RewardFired { get; set; }
        [JsonProperty("rewardDecoupled")] public bool RewardDecoupled { get; set; }
        [JsonProperty("tags")] public List<string> Tags { get; set; } = new();
    }
}
