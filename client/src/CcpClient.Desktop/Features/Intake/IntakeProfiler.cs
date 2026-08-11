namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the deterministic QuizRunResult → five-axes profiler, ported from the READ-ONLY
/// WPF evidence (Services/Quiz/IntakeProfiler.cs — the full source was read for this port;
/// line cites below). DETERMINISTIC: no RNG, no clock, no I/O. Same trajectory in ⇒ same
/// profile out.
///
/// The atom (measured by WPF against the shipped banks, IntakeProfiler.cs:74-101):
/// endorsement is BINARY — <c>endorse = correct ? 1 : 0</c> ("correct" = took the bank's
/// compliant option); <c>axis = Σ(heat·endorse) / Σ(heat)</c> over the axis's own items.
/// Index-lean partial credit was measured and rejected (circe shuffles the compliant
/// option; several sissy/bambi entries are reverse-ordered — lean measures click position,
/// not endorsement). A5 scores the REFUSAL rate (invert) on the confession cluster at
/// heat ≥ 3.
///
/// Axis coverage is structurally bank-dependent (IntakeProfiler.cs:105-123): A4 is
/// always under-sampled on drone (zero A4 prompts) and circe (all heat &lt; 2); A1 is
/// thin on sissy/circe; A5 exists only in sissy/circe. Under-sampled ⇒ 0.5 + flag, and
/// callers fall back to the tier baseline rather than acting on noise.
/// </summary>
public static class IntakeProfiler
{
    /// <summary>Fewest scored items an axis needs before its value is trusted (:114).</summary>
    public const int MinItems = 3;

    /// <summary>Prompts below this heat are warm-up camouflage (:117).</summary>
    public const int MinHeat = 2;

    /// <summary>A5's confession cluster is only read at this heat or above (:120).</summary>
    private const int AutonomyHotHeat = 3;

    /// <summary>Band whose beats are never graded and never scored (:123).</summary>
    private const string RecoveryBand = "recovery";

    /// <summary>Structural tags: a prompt's ROLE, not its content — never axis tags
    /// (:130-133).</summary>
    private static readonly HashSet<string> StructuralTags = new(StringComparer.Ordinal)
    {
        "trivia", "curious", "colorpick", "trick", "mantra", "mono",
    };

    /// <summary>Structural tags that also DISQUALIFY the record outright (:136-139).</summary>
    private static readonly HashSet<string> DisqualifyingTags = new(StringComparer.Ordinal)
    {
        "trivia", "colorpick", "trick",
    };

    /// <summary>A1 Blankness (:143-146; incl. trance — 74 bambi entries, plainly the same axis).</summary>
    private static readonly HashSet<string> A1Blankness = new(StringComparer.Ordinal)
    {
        "blank", "erasure", "sinking", "dropping", "stillness", "calm", "iq-lock", "trance",
    };

    /// <summary>A2 Service (:151-154; incl. surrender — the largest submission tag in every bank).</summary>
    private static readonly HashSet<string> A2Service = new(StringComparer.Ordinal)
    {
        "obedience", "servitude", "service", "compliance", "protocol", "order",
        "worship", "property", "hive", "sync", "surrender",
    };

    /// <summary>A3 Arousal-forward (:157-160).</summary>
    private static readonly HashSet<string> A3Arousal = new(StringComparer.Ordinal)
    {
        "arousal", "cockslut", "denial", "chastity", "locked", "keyholder", "permission", "discipline",
    };

    /// <summary>A4 Presentation (:164-167).</summary>
    private static readonly HashSet<string> A4Presentation = new(StringComparer.Ordinal)
    {
        "femme", "dressing", "pink", "doll", "uniform", "good-girl", "giggly", "soft", "trigger",
    };

    /// <summary>A5 Autonomy — the confession cluster ONLY, at heat ≥ 3 (:182-185). The
    /// design's independent/unowned/curious were dropped by WPF after reading what they
    /// select (heat-3+ trivia quizzes); what remains is a genuine self-exposure signal.</summary>
    private static readonly HashSet<string> A5Autonomy = new(StringComparer.Ordinal)
    {
        "confession", "honesty", "exposure",
    };

    /// <summary>One profile axis: a 0..1 score plus the evidence behind it
    /// (IntakeAxis, IntakeProfiler.cs:10-31).</summary>
    public sealed record Axis
    {
        /// <summary>0..1. Exactly 0.5 and <see cref="UnderSampled"/> when the run served too
        /// few items on this axis to say anything.</summary>
        public double Value { get; init; } = 0.5;

        /// <summary>How many trajectory records were actually scored into this axis.</summary>
        public int ItemCount { get; init; }

        /// <summary>True when <see cref="ItemCount"/> fell under <see cref="MinItems"/> —
        /// callers MUST treat the 0.5 as "no signal" and fall back to the difficulty baseline.</summary>
        public bool UnderSampled { get; init; }

        public static Axis Neutral(int n) => new() { Value = 0.5, ItemCount = n, UnderSampled = true };
    }

    /// <summary>Five-axis read of a completed run (IntakeProfile, IntakeProfiler.cs:33-57).</summary>
    public sealed record Profile
    {
        /// <summary>A1 — emptiness / sinking / going blank.</summary>
        public Axis Blankness { get; init; } = Axis.Neutral(0);

        /// <summary>A2 — obedience / service / being owned.</summary>
        public Axis Service { get; init; } = Axis.Neutral(0);

        /// <summary>A3 — arousal / denial / permission / chastity.</summary>
        public Axis Arousal { get; init; } = Axis.Neutral(0);

        /// <summary>A4 — presentation: femme / dressing / doll / pink / trigger.</summary>
        public Axis Presentation { get; init; } = Axis.Neutral(0);

        /// <summary>A5 — autonomy. NOT a knob: an INVERSE GATE (high refusal on the most
        /// self-exposing prompts steps the drafted tier down and forces lock cards off).</summary>
        public Axis Autonomy { get; init; } = Axis.Neutral(0);

        /// <summary>Total graded, scoreable records the profiler saw (any axis).</summary>
        public int ScoreableRecords { get; init; }
    }

    /// <summary>Read a completed run. Never throws; a null/empty trajectory yields an
    /// all-neutral, all-under-sampled profile (:188-209).</summary>
    public static Profile ProfileRun(IntakeQuizRun? run)
    {
        var traj = run?.Trajectory;
        if (traj == null || traj.Count == 0) return new Profile();

        var usable = new List<IntakeQuizAnswer>(traj.Count);
        foreach (var r in traj)
        {
            if (r == null) continue;
            if (IsScoreable(r)) usable.Add(r);
        }

        return new Profile
        {
            Blankness = ScoreAxis(usable, A1Blankness, MinHeat, invert: false),
            Service = ScoreAxis(usable, A2Service, MinHeat, invert: false),
            Arousal = ScoreAxis(usable, A3Arousal, MinHeat, invert: false),
            Presentation = ScoreAxis(usable, A4Presentation, MinHeat, invert: false),
            // Autonomy is the REFUSAL rate on the confession cluster, hence invert.
            Autonomy = ScoreAxis(usable, A5Autonomy, AutonomyHotHeat, invert: true),
            ScoreableRecords = usable.Count,
        };
    }

    /// <summary>Can this record carry a preference signal at all? (:213-224 — heat is checked
    /// per axis because A5 uses a higher floor).</summary>
    private static bool IsScoreable(IntakeQuizAnswer r)
    {
        if (string.Equals(r.Band, RecoveryBand, StringComparison.OrdinalIgnoreCase)) return false;
        if (r.IsTrick || r.IsFreeChoice) return false;
        // No option list = no choice to read (mantra / check-in / interlude), and a
        // single-option Mono beat is forced compliance, not a preference.
        if (r.ChosenIndex < 0 || r.OptionCount < 2) return false;
        if (r.Tags == null || r.Tags.Count == 0) return false;
        foreach (var t in r.Tags)
            if (t != null && DisqualifyingTags.Contains(t)) return false;
        return true;
    }

    /// <summary>axis = Σ(heat·endorse) / Σ(heat) over the records whose tags intersect
    /// <paramref name="axisTags"/> at heat ≥ <paramref name="minHeat"/> (:232-253).</summary>
    private static Axis ScoreAxis(List<IntakeQuizAnswer> usable, HashSet<string> axisTags,
        int minHeat, bool invert)
    {
        double num = 0, den = 0;
        var n = 0;
        foreach (var r in usable)
        {
            if (r.PromptHeat < minHeat) continue;
            if (!HasAnyTag(r, axisTags)) continue;
            var endorse = r.Correct ? 1.0 : 0.0;
            if (invert) endorse = 1.0 - endorse;
            double h = r.PromptHeat;
            num += h * endorse;
            den += h;
            n++;
        }

        if (n < MinItems || den <= 0) return Axis.Neutral(n);
        return new Axis { Value = Math.Clamp(num / den, 0, 1), ItemCount = n, UnderSampled = false };
    }

    private static bool HasAnyTag(IntakeQuizAnswer r, HashSet<string> set)
    {
        var tags = r.Tags;
        if (tags == null) return false;
        foreach (var t in tags)
        {
            if (t == null) continue;
            // Structural tags never satisfy an axis (the belt to the sets' braces, :255-268).
            if (StructuralTags.Contains(t)) continue;
            if (set.Contains(t)) return true;
        }

        return false;
    }
}
