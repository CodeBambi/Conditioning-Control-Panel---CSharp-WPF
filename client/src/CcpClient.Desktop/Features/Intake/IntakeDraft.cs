using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the session-drafting sink (the degraded-delivery contract encoded): a completed
/// intake run drafts a session document that is **marked never-runnable** — no session
/// engine exists in greenfield (the row's verbatim contract: "the drafted session is never
/// runnable (no session engine — punches pend forever, silently)").
///
/// Port scope (pre-approach consult + advisor Correction 1, both adopted): the deterministic
/// core of WPF QuizSessionGenerator.GenerateSession(QuizRunResult) —
/// <list type="bullet">
/// <item>DifficultyFromDepth bands (≤0.20 Easy / ≤0.45 Medium / ≤0.72 Hard / else Extreme,
/// :191-199) + the A5 inverse gate (!UnderSampled &amp;&amp; ≥0.70 → step the tier down one,
/// :93-94, :178-187);</item>
/// <item>scorePercent + diffPrefix + NicheDisplay + PrettyId + the archetype-suffix naming
/// rule (the bug-#614 fix — user-observable, :107-146);</item>
/// <item>SeedAffirmedMantras / MergeVerbatim (verbatim, case-insensitive de-dupe, mantras
/// first, :221-253);</item>
/// <item>ApplyRunShaping's Lean/Nudge/EnsureRising math (:269-397) applied to
/// <see cref="IntakeDraftKnobs"/> — exactly the knobs the axis→knob table names, with the
/// per-tier baselines for those knobs (BuildSettings :425-700);</item>
/// <item>the host completion-loop XP formula (IntakeHostService.cs:389-397) — COMPUTED,
/// never granted (no greenfield XP store — typed seam, consult ruling 8).</item>
/// </list>
/// TYPED NAMED LIMIT (record.md): WPF's BuildPhases, GetCategoryIcon, GetFallbackContent
/// per-niche phrase banks, BonusXP, and the BrainDrain ramp pair (the withheld Delta-1
/// feature) are NOT drafted — they belong to the unfiled session-engine + content rows, and
/// drafting them into a never-runnable document would invent a schema no consumer validates.
/// </summary>
public static class IntakeDraft
{
    /// <summary>The drafted-session difficulty tiers (WPF SessionDifficulty; bands below).</summary>
    public enum IntakeDifficulty
    {
        Easy,
        Medium,
        Hard,
        Extreme,
    }

    /// <summary>A5 autonomy at or above this refuses the drafted tier one step down
    /// (:178 — calibrated above the coin-flip band 0.62-0.66).</summary>
    public const double AutonomyClampThreshold = 0.70;

    /// <summary>Real ceiling of the flash-frequency knob (:259-262 — AppSettings.FlashFrequency
    /// clamps 1..180; anything above is silently truncated at runtime in WPF).</summary>
    public const int FlashPerHourMax = 180;

    /// <summary>Drafted-session length (:158-172).</summary>
    public const int DurationMinutes = 60;

    /// <summary>The knobs the axis→knob table names (:289-349), with per-tier baselines from
    /// BuildSettings (:425-700). Baselines are set by <see cref="BaselineKnobs"/>; shaping
    /// nudges them. Only the knobs the table names (plus the two enabled-gates the nudges
    /// read) exist here — never a WPF SessionSettings clone.</summary>
    public sealed record IntakeDraftKnobs
    {
        // Flash ramp pair (A3 ±30/±60, 1..180; chase scales both halves ×(1+0.25·chase)).
        public int FlashPerHour { get; set; }
        public int FlashPerHourEnd { get; set; }

        // Flash opacity ramp pair (A3 ±15/±25, 10..100).
        public int FlashOpacity { get; set; }
        public int FlashOpacityEnd { get; set; }

        /// <summary>Chase knob: true at Hard/Extreme baseline, or when the run chased the
        /// decoupled reward (&gt;0.15).</summary>
        public bool FlashHydra { get; set; }

        /// <summary>Chase knobs (chased &amp;&amp; magnitude &gt; 0.15 → bursty variable-ratio).</summary>
        public bool BubblesEnabled { get; set; }
        public bool BubblesIntermittent { get; set; }

        // A1 ±2 (1..4).
        public int MindWipeBaseMultiplier { get; set; }

        // A1 ±20 (20..100).
        public int SubliminalOpacity { get; set; }

        // A4 ±2 (1..6).
        public int SubliminalPerMin { get; set; }

        /// <summary>Gate for the spiral nudges (Easy baseline: off — no nudge applies).</summary>
        public bool SpiralEnabled { get; set; }

        // A1 ±8/±16 (0..50), spiral-on only.
        public int SpiralOpacity { get; set; }
        public int SpiralOpacityEnd { get; set; }

        /// <summary>A2 may switch this ON at a well-sampled ≥0.75 (the one ON/OFF row);
        /// A5 ≥0.70 forces it OFF with Frequency null (:322-370).</summary>
        public bool LockCardEnabled { get; set; }
        public int? LockCardFrequency { get; set; }
        public int? LockCardStartMinute { get; set; }

        /// <summary>Gate for the VideosPerHour nudge (Easy/Medium baseline: off).</summary>
        public bool MandatoryVideosEnabled { get; set; }

        // A2 ±2 (1..4), videos-on only.
        public int? VideosPerHour { get; set; }

        // Pink filter ramp pair; the END is A4-nudged ±20 (0..100); EnsureRising reads the start.
        public int PinkFilterStartOpacity { get; set; }
        public int PinkFilterEndOpacity { get; set; }
    }

    /// <summary>The merge target for affirmed mantras (:221-253). Empty by default — WPF's
    /// per-niche fallback banks (GetFallbackContent :712-878) are the named limit above;
    /// an AI-synthesized pool is a future content row. The mantra merge is real either way.</summary>
    public sealed class IntakeDraftText
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> SubliminalPhrases { get; set; } = new();
        public List<string> BouncingTextPhrases { get; set; } = new();
        public List<string> LockCardPhrases { get; set; } = new();
    }

    /// <summary>The drafted-session document (the sink's file shape). <see cref="Runnable"/>
    /// is ALWAYS false with the typed reason — the degraded-delivery contract encoded in the
    /// artifact itself, so no future consumer can mistake the draft for a runnable session.</summary>
    public sealed record IntakeDraftDocument
    {
        [JsonPropertyName("runnable")] public bool Runnable { get; init; }

        [JsonPropertyName("runnableReason")] public string RunnableReason { get; init; } = string.Empty;

        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;

        [JsonPropertyName("niche")] public string Niche { get; init; } = string.Empty;

        [JsonPropertyName("difficulty")] public string Difficulty { get; init; } = string.Empty;

        [JsonPropertyName("durationMinutes")] public int DurationMinutes { get; init; } = IntakeDraft.DurationMinutes;

        [JsonPropertyName("draftedUtc")] public DateTimeOffset DraftedUtc { get; init; }

        /// <summary>COMPUTED completion XP (IntakeHostService.cs:389-397), never granted —
        /// no greenfield XP store exists (typed seam).</summary>
        [JsonPropertyName("xpComputed")] public int XpComputed { get; init; }

        [JsonPropertyName("profile")] public IntakeProfiler.Profile Profile { get; init; } = new();

        [JsonPropertyName("knobs")] public IntakeDraftKnobs Knobs { get; init; } = new();

        [JsonPropertyName("subliminalPhrases")] public List<string> SubliminalPhrases { get; init; } = new();

        [JsonPropertyName("bouncingTextPhrases")] public List<string> BouncingTextPhrases { get; init; } = new();

        [JsonPropertyName("lockCardPhrases")] public List<string> LockCardPhrases { get; init; } = new();
    }

    /// <summary>The never-runnable marker (verbatim from the row's degraded-delivery contract).</summary>
    public const string NeverRunnableReason = "no-session-engine (degraded-delivery contract)";

    /// <summary>Draft a session from a completed run (GenerateSession(QuizRunResult) :74-172,
    /// scoped per the class comment). Deterministic apart from the new Guid id and the
    /// caller-supplied clock — same run in ⇒ same shaping out.</summary>
    public static IntakeDraftDocument Generate(IntakeQuizRun run, DateTimeOffset draftedUtc,
        IntakeDraftText? text = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        var niche = string.IsNullOrWhiteSpace(run.Niche) ? "bambi" : run.Niche.Trim().ToLowerInvariant();

        var profile = IntakeProfiler.ProfileRun(run);

        var difficulty = DifficultyFromDepth(run.PeakDepth);
        // A5 AUTONOMY — an inverse GATE, not a knob (:89-94): sustained refusal on the
        // heat-3+ confession prompts steps the tier down; under-sampled ⇒ never fires
        // (bambi/drone have no confession prompts — never fires on noise).
        if (!profile.Autonomy.UnderSampled && profile.Autonomy.Value >= AutonomyClampThreshold)
        {
            difficulty = StepDownTier(difficulty);
        }

        var scorePercent = run.MaxScore > 0
            ? run.TotalScore / run.MaxScore * 100.0
            : Math.Clamp(run.PeakDepth, 0, 1) * 100.0;

        var content = text ?? new IntakeDraftText();
        SeedAffirmedMantras(content, run.AffirmedMantras);

        var nicheDisplay = NicheDisplay(niche);
        var diffPrefix = scorePercent switch
        {
            <= 25 => "Gentle",
            <= 50 => "Guided",
            <= 75 => "Intense",
            _ => "Deep",
        };
        var archetype = run.Route?.PrimaryArchetypeId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content.Name))
        {
            content.Name = $"{diffPrefix} {nicheDisplay} Intake";
        }

        // BUG #614 fix (:122-146): the revealed primary archetype is the one thing that
        // genuinely varies between two runs at the same tier — it goes in the name.
        if (!string.IsNullOrWhiteSpace(archetype) && !string.IsNullOrWhiteSpace(content.Name))
        {
            var routeName = PrettyId(archetype);
            if (!string.IsNullOrWhiteSpace(routeName)
                && content.Name.IndexOf(routeName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                content.Name = $"{content.Name} - {routeName}";
            }
        }

        if (string.IsNullOrWhiteSpace(content.Description))
        {
            content.Description = $"A {difficulty} session drafted from your {nicheDisplay} Intake"
                + (string.IsNullOrWhiteSpace(archetype) ? "." : $" — {PrettyId(archetype)} route.");
        }

        var knobs = BaselineKnobs(difficulty);
        ApplyRunShaping(knobs, run, profile);

        return new IntakeDraftDocument
        {
            Runnable = false,
            RunnableReason = NeverRunnableReason,
            Id = Guid.NewGuid().ToString(),
            Name = content.Name,
            Description = content.Description,
            Niche = niche,
            Difficulty = difficulty.ToString(),
            DurationMinutes = DurationMinutes,
            DraftedUtc = draftedUtc,
            XpComputed = ComputeCompletionXp(run),
            Profile = profile,
            Knobs = knobs,
            SubliminalPhrases = content.SubliminalPhrases,
            BouncingTextPhrases = content.BouncingTextPhrases,
            LockCardPhrases = content.LockCardPhrases,
        };
    }

    /// <summary>The host completion-loop XP (IntakeHostService.cs:389-397): COMPUTED, never
    /// granted (no greenfield XP store — typed seam). 25 base + depth share (banker's
    /// rounding, WPF's Math.Round default) + ≤5 affirmed mantras ×5, capped at 100.</summary>
    public static int ComputeCompletionXp(IntakeQuizRun run)
    {
        var xp = 25 + (int)Math.Round(Math.Clamp(run.PeakDepth, 0, 1) * 50)
            + Math.Min(run.AffirmedMantras?.Count ?? 0, 5) * 5;
        return Math.Min(xp, 100);
    }

    /// <summary>peakDepth (0..1) → difficulty, aligned to the web core's band floors
    /// (Establishing 0.18, Deepening 0.42, Climax 0.72) (:191-199).</summary>
    public static IntakeDifficulty DifficultyFromDepth(double peakDepth)
    {
        var d = Math.Clamp(peakDepth, 0, 1);
        return d switch
        {
            <= 0.20 => IntakeDifficulty.Easy,
            <= 0.45 => IntakeDifficulty.Medium,
            <= 0.72 => IntakeDifficulty.Hard,
            _ => IntakeDifficulty.Extreme,
        };
    }

    /// <summary>One notch gentler; Easy is the floor (:181-187).</summary>
    public static IntakeDifficulty StepDownTier(IntakeDifficulty d) => d switch
    {
        IntakeDifficulty.Extreme => IntakeDifficulty.Hard,
        IntakeDifficulty.Hard => IntakeDifficulty.Medium,
        IntakeDifficulty.Medium => IntakeDifficulty.Easy,
        _ => IntakeDifficulty.Easy,
    };

    /// <summary>The per-tier baselines for exactly the knobs the axis→knob table names
    /// (BuildSettings :425-700, scoped per the class comment).</summary>
    public static IntakeDraftKnobs BaselineKnobs(IntakeDifficulty difficulty)
    {
        var s = new IntakeDraftKnobs();
        switch (difficulty)
        {
            case IntakeDifficulty.Easy:
                s.FlashPerHour = 12; s.FlashPerHourEnd = 30;
                s.FlashOpacity = 25; s.FlashOpacityEnd = 45;
                s.FlashHydra = false;
                s.SubliminalPerMin = 2; s.SubliminalOpacity = 40;
                s.PinkFilterStartOpacity = 0; s.PinkFilterEndOpacity = 15;
                s.SpiralEnabled = false;
                s.LockCardEnabled = false;
                s.MandatoryVideosEnabled = false;
                s.MindWipeBaseMultiplier = 1;
                s.BubblesEnabled = true;
                break;
            case IntakeDifficulty.Medium:
                s.FlashPerHour = 38; s.FlashPerHourEnd = 80;
                s.FlashOpacity = 48; s.FlashOpacityEnd = 70;
                s.FlashHydra = false;
                s.SubliminalPerMin = 3; s.SubliminalOpacity = 55;
                s.PinkFilterStartOpacity = 5; s.PinkFilterEndOpacity = 30;
                s.SpiralEnabled = true; s.SpiralOpacity = 5; s.SpiralOpacityEnd = 15;
                s.LockCardEnabled = false;
                s.MandatoryVideosEnabled = false;
                s.MindWipeBaseMultiplier = 2;
                s.BubblesEnabled = true;
                break;
            case IntakeDifficulty.Hard:
                s.FlashPerHour = 70; s.FlashPerHourEnd = 150;
                s.FlashOpacity = 70; s.FlashOpacityEnd = 90;
                s.FlashHydra = true;
                s.SubliminalPerMin = 4; s.SubliminalOpacity = 70;
                s.PinkFilterStartOpacity = 10; s.PinkFilterEndOpacity = 45;
                s.SpiralEnabled = true; s.SpiralOpacity = 10; s.SpiralOpacityEnd = 25;
                s.LockCardEnabled = true; s.LockCardStartMinute = 10; s.LockCardFrequency = 2;
                s.MandatoryVideosEnabled = true; s.VideosPerHour = 2;
                s.MindWipeBaseMultiplier = 3;
                s.BubblesEnabled = true;
                break;
            case IntakeDifficulty.Extreme:
                s.FlashPerHour = 110; s.FlashPerHourEnd = 180;
                s.FlashOpacity = 68; s.FlashOpacityEnd = 100;
                s.FlashHydra = true;
                s.SubliminalPerMin = 5; s.SubliminalOpacity = 85;
                s.PinkFilterStartOpacity = 15; s.PinkFilterEndOpacity = 55;
                s.SpiralEnabled = true; s.SpiralOpacity = 10; s.SpiralOpacityEnd = 35;
                s.LockCardEnabled = true; s.LockCardStartMinute = 5; s.LockCardFrequency = 3;
                s.MandatoryVideosEnabled = true; s.VideosPerHour = 3;
                s.MindWipeBaseMultiplier = 3;
                s.BubblesEnabled = true;
                break;
        }

        return s;
    }

    /// <summary>Shape the tier baseline by the reward profile and the five-axis profile
    /// (:269-372). DETERMINISTIC: no dice anywhere in the draft path.</summary>
    public static void ApplyRunShaping(IntakeDraftKnobs s, IntakeQuizRun run, IntakeProfiler.Profile profile)
    {
        var chase = Math.Clamp(run.RewardProfile?.ChaseMagnitude ?? 0, 0, 1);
        var chased = run.RewardProfile?.ChasedReward == true;

        if (chased && chase > 0.15)
        {
            // Variable-ratio feel (:274-284); BOTH ramp halves scale together.
            s.BubblesEnabled = true;
            s.BubblesIntermittent = true;
            s.FlashHydra = true;
            var chaseMul = 1.0 + 0.25 * chase;
            s.FlashPerHour = Math.Clamp((int)(s.FlashPerHour * chaseMul), 1, FlashPerHourMax);
            s.FlashPerHourEnd = Math.Clamp((int)(s.FlashPerHourEnd * chaseMul), 1, FlashPerHourMax);
        }
        else
        {
            s.BubblesIntermittent = false;
        }

        var a1 = Lean(profile.Blankness);
        var a2 = Lean(profile.Service);
        var a3 = Lean(profile.Arousal);
        var a4 = Lean(profile.Presentation);

        // --- A1 blankness -------------------------------------------------
        s.MindWipeBaseMultiplier = Nudge(s.MindWipeBaseMultiplier, 2 * a1, 1, 4);
        s.SubliminalOpacity = Nudge(s.SubliminalOpacity, 20 * a1, 20, 100);
        if (s.SpiralEnabled)
        {
            s.SpiralOpacity = Nudge(s.SpiralOpacity, 8 * a1, 0, 50);
            s.SpiralOpacityEnd = Nudge(s.SpiralOpacityEnd, 16 * a1, 0, 50);
        }

        // --- A2 service ---------------------------------------------------
        const double lockCardEnableAt = 0.75;
        if (!s.LockCardEnabled && !profile.Service.UnderSampled && profile.Service.Value >= lockCardEnableAt)
        {
            s.LockCardEnabled = true;
            s.LockCardStartMinute = 10;
            s.LockCardFrequency = 1;
        }

        if (s.LockCardEnabled)
        {
            s.LockCardFrequency = Nudge(s.LockCardFrequency ?? 1, 2 * a2, 1, 4);
        }

        if (s.MandatoryVideosEnabled)
        {
            s.VideosPerHour = Nudge(s.VideosPerHour ?? 1, 2 * a2, 1, 4);
        }

        // --- A3 arousal-forward -------------------------------------------
        s.FlashPerHour = Nudge(s.FlashPerHour, 30 * a3, 1, FlashPerHourMax);
        s.FlashPerHourEnd = Nudge(s.FlashPerHourEnd, 60 * a3, 1, FlashPerHourMax);
        s.FlashOpacity = Nudge(s.FlashOpacity, 15 * a3, 10, 100);
        s.FlashOpacityEnd = Nudge(s.FlashOpacityEnd, 25 * a3, 10, 100);

        // --- A4 presentation ----------------------------------------------
        s.SubliminalPerMin = Nudge(s.SubliminalPerMin, 2 * a4, 1, 6);
        s.PinkFilterEndOpacity = Nudge(s.PinkFilterEndOpacity, 20 * a4, 0, 100);

        // --- A5 autonomy: the second half of the inverse gate (:322-370) ---
        if (!profile.Autonomy.UnderSampled && profile.Autonomy.Value >= AutonomyClampThreshold)
        {
            s.LockCardEnabled = false;
            s.LockCardFrequency = null;
        }

        EnsureRising(s);
    }

    /// <summary>Axis → signed lean in [-0.5, +0.5]; under-sampled leans 0 (:381).</summary>
    public static double Lean(IntakeProfiler.Axis axis) => axis.UnderSampled ? 0.0 : axis.Value - 0.5;

    /// <summary>base + round(delta), clamped; half away from zero (:385-386).</summary>
    public static int Nudge(int baseValue, double delta, int lo, int hi) =>
        Math.Clamp(baseValue + (int)Math.Round(delta, MidpointRounding.AwayFromZero), lo, hi);

    /// <summary>Every ramp pair still RISES after shaping (:390-397; the BrainDrain pair is
    /// the named limit — the withheld Delta-1 feature).</summary>
    public static void EnsureRising(IntakeDraftKnobs s)
    {
        if (s.FlashPerHourEnd < s.FlashPerHour) s.FlashPerHourEnd = s.FlashPerHour;
        if (s.FlashOpacityEnd < s.FlashOpacity) s.FlashOpacityEnd = s.FlashOpacity;
        if (s.SpiralOpacityEnd < s.SpiralOpacity) s.SpiralOpacityEnd = s.SpiralOpacity;
        if (s.PinkFilterEndOpacity < s.PinkFilterStartOpacity) s.PinkFilterEndOpacity = s.PinkFilterStartOpacity;
    }

    /// <summary>Merge the affirmed mantras VERBATIM (mantras first, case-insensitive
    /// de-dupe) into all three pools (:221-253). Never uppercased or reworded.</summary>
    public static void SeedAffirmedMantras(IntakeDraftText content, List<string>? mantras)
    {
        if (mantras == null || mantras.Count == 0) return;

        var clean = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mantras)
        {
            var t = m?.Trim();
            if (string.IsNullOrEmpty(t) || !seen.Add(t)) continue;
            clean.Add(t);
        }

        if (clean.Count == 0) return;

        content.SubliminalPhrases = MergeVerbatim(clean, content.SubliminalPhrases);
        content.BouncingTextPhrases = MergeVerbatim(clean, content.BouncingTextPhrases);
        content.LockCardPhrases = MergeVerbatim(clean, content.LockCardPhrases);
    }

    private static List<string> MergeVerbatim(List<string> lead, List<string> rest)
    {
        var outList = new List<string>(lead);
        var seen = new HashSet<string>(lead, StringComparer.OrdinalIgnoreCase);
        foreach (var s in rest ?? new List<string>())
        {
            var t = s?.Trim();
            if (string.IsNullOrEmpty(t) || !seen.Add(t)) continue;
            outList.Add(t);
        }

        return outList;
    }

    public static string NicheDisplay(string niche) => niche switch
    {
        "bambi" => "Bambi",
        "drone" => "Drone",
        "sissy" => "Sissy",
        "circe" => "Circe",
        _ => string.IsNullOrEmpty(niche) ? "Intake" : char.ToUpperInvariant(niche[0]) + niche[1..],
    };

    public static string PrettyId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return id;
        var words = id.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        }

        return string.Join(' ', words);
    }
}
