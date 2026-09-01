using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// One profile axis: a 0..1 score plus the evidence behind it.
    /// </summary>
    public sealed class IntakeAxis
    {
        /// <summary>0..1. Exactly 0.5 and <see cref="UnderSampled"/> when the run did not
        /// serve enough items on this axis to say anything.</summary>
        public double Value { get; init; } = 0.5;

        /// <summary>How many trajectory records were actually scored into this axis.</summary>
        public int ItemCount { get; init; }

        /// <summary>True when <see cref="ItemCount"/> fell under <see cref="IntakeProfiler.MinItems"/>.
        /// Callers MUST treat the 0.5 as "no signal" and fall back to the difficulty baseline
        /// rather than acting on it — several banks structurally cannot fill some axes
        /// (see the AXIS COVERAGE note on <see cref="IntakeProfiler"/>).</summary>
        public bool UnderSampled { get; init; }

        public static IntakeAxis Neutral(int n) => new() { Value = 0.5, ItemCount = n, UnderSampled = true };

        public override string ToString() => UnderSampled
            ? $"0.50 (under-sampled, n={ItemCount})"
            : $"{Value:0.00} (n={ItemCount})";
    }

    /// <summary>Five-axis read of a completed "Graded Intake" run.</summary>
    public sealed class IntakeProfile
    {
        /// <summary>A1 — emptiness / sinking / going blank.</summary>
        public IntakeAxis Blankness { get; init; } = IntakeAxis.Neutral(0);

        /// <summary>A2 — obedience / service / being owned.</summary>
        public IntakeAxis Service { get; init; } = IntakeAxis.Neutral(0);

        /// <summary>A3 — arousal / denial / permission / chastity.</summary>
        public IntakeAxis Arousal { get; init; } = IntakeAxis.Neutral(0);

        /// <summary>A4 — presentation: femme / dressing / doll / pink / trigger.</summary>
        public IntakeAxis Presentation { get; init; } = IntakeAxis.Neutral(0);

        /// <summary>A5 — autonomy. NOT a knob: an INVERSE GATE. High autonomy means the user
        /// refused the compliant answer on the run's most self-exposing prompts, so the draft
        /// steps the difficulty tier DOWN and forces lock cards off.</summary>
        public IntakeAxis Autonomy { get; init; } = IntakeAxis.Neutral(0);

        /// <summary>Total graded, scoreable records the profiler saw (any axis).</summary>
        public int ScoreableRecords { get; init; }

        public override string ToString() =>
            $"A1 blank={Blankness} A2 service={Service} A3 arousal={Arousal} A4 present={Presentation} A5 autonomy={Autonomy}";
    }

    /// <summary>
    /// Turns a <see cref="QuizRunResult"/> trajectory into five 0..1 axes.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// <c>QuizRunResult.TagTallies</c> credits a beat's tags whichever way the user answered,
    /// so a run that REFUSED every hot prompt tallies exactly the same tags as one that took
    /// them. That is exposure, not preference. Slice 1 added the chosen option to each
    /// trajectory record; this class scores it.
    ///
    /// THE ATOM  (and what was measured before choosing it)
    /// ----------------------------------------------------
    /// The design called for partial credit from the option's INDEX:
    ///     lean = chosenIndex / (optionCount - 1);  endorse = correct ? 1 : lean
    /// on the assumption that a bank's options are ordered by escalation, so a wrong-but-late
    /// index is a partial endorsement. That assumption was checked against the shipped banks
    /// and IS FALSE:
    ///     bank    heat>=2 multi-option prompts   answer == last index
    ///     bambi   107                            81   (76%)
    ///     drone    95                            83   (87%)
    ///     sissy    68                            44   (65%)
    ///     circe    98                            26   (27%)  <- uniform: 24/23/25/26
    /// circe deliberately shuffles the compliant option, and several sissy/bambi entries are
    /// REVERSE-ordered (sis_h5_gone: the most-surrendered option is index 0). Driving the run
    /// headlessly with an "always click the bottom option" player scored 1.00 on EVERY axis for
    /// bambi/drone/circe under the lean formula — indistinguishable from full compliance — while
    /// a total refusal still scored 0.47-0.66. The lean term measures click position, not
    /// endorsement, so it is not used. Endorsement is binary:
    ///     endorse = correct ? 1 : 0        // "correct" = took the bank's compliant option
    ///     axis    = SUM(heat * endorse) / SUM(heat)     over that axis's items only
    /// Yes/No beats reinforce this: their option 0 is "Yes", so index-lean is inverted for them.
    ///
    /// The heat weighting is what keeps this a preference read rather than a score: agreeing on
    /// a heat-5 "she owns me entirely" counts far more than agreeing on a heat-2 warm-up.
    ///
    /// AXIS COVERAGE (measured over 5 seeds x 4 banks x 4 answer strategies)
    /// ---------------------------------------------------------------------
    /// Item counts per run are small and bank-dependent. Structural gaps, confirmed in the bank
    /// JSON, NOT run-to-run noise:
    ///   A4 Presentation — drone has ZERO prompts carrying any A4 tag; circe has 36 but all at
    ///                     heat &lt; 2. Both always come back under-sampled. Correct behaviour.
    ///   A1 Blankness    — sissy has 12 eligible prompts in the whole bank (n~1 per run) and
    ///                     circe ~40 (n~2). Usually under-sampled on those two niches.
    ///   A5 Autonomy     — only sissy (56 heat&gt;=3 confession prompts) and circe (18) can fill it.
    ///                     bambi and drone have none, so the tier clamp simply never fires there.
    /// A2 and A3 are well-sampled on every bank (n~10-30).
    ///
    /// DETERMINISTIC. No RNG, no clock, no I/O. Same trajectory in =&gt; same profile out.
    /// NO LATENCY WORK HERE — <c>LatencyMs</c> is deliberately still unread (Slice 2).
    /// </summary>
    public static class IntakeProfiler
    {
        /// <summary>Fewest scored items an axis needs before its value is trusted.</summary>
        public const int MinItems = 3;

        /// <summary>Prompts below this heat are warm-up camouflage; agreeing with them says
        /// nothing, so no axis counts them.</summary>
        public const int MinHeat = 2;

        /// <summary>A5's confession cluster is only read at this heat or above — a low-heat
        /// "are you being honest?" is a survey question, not a self-exposure.</summary>
        private const int AutonomyHotHeat = 3;

        /// <summary>Band whose beats are never graded and never scored.</summary>
        private const string RecoveryBand = "recovery";

        // ====================================================================
        // TAG SETS — verified against the real bank vocabularies. Deltas from the
        // original design are called out inline; every tag below is one that
        // actually occurs in at least one shipped bank.
        // ====================================================================

        /// <summary>Structural tags. These describe a prompt's ROLE, not its content, so they
        /// are never axis tags — and a record carrying <c>trivia</c> or <c>colorpick</c> is
        /// dropped outright, because those beats are literal arithmetic/geography questions and
        /// colour picks whose correctness measures general knowledge, not consent.</summary>
        private static readonly HashSet<string> StructuralTags = new(StringComparer.Ordinal)
        {
            "trivia", "curious", "colorpick", "trick", "mantra", "mono",
        };

        /// <summary>Structural tags that also DISQUALIFY the record entirely (see above).</summary>
        private static readonly HashSet<string> DisqualifyingTags = new(StringComparer.Ordinal)
        {
            "trivia", "colorpick", "trick",
        };

        /// <summary>A1 Blankness. Design set plus <c>trance</c> — 74 bambi entries, plainly the
        /// same axis and otherwise credited to nothing.</summary>
        private static readonly HashSet<string> A1Blankness = new(StringComparer.Ordinal)
        {
            "blank", "erasure", "sinking", "dropping", "stillness", "calm", "iq-lock", "trance",
        };

        /// <summary>A2 Service. Design set plus <c>surrender</c> — the single largest submission
        /// tag in EVERY bank (bambi 105 / circe 110 / drone 66 / sissy 157) and absent from all
        /// five proposed sets, which would have thrown away the best service signal there is.</summary>
        private static readonly HashSet<string> A2Service = new(StringComparer.Ordinal)
        {
            "obedience", "servitude", "service", "compliance", "protocol", "order",
            "worship", "property", "hive", "sync", "surrender",
        };

        /// <summary>A3 Arousal-forward. Design set, unchanged — every tag exists.</summary>
        private static readonly HashSet<string> A3Arousal = new(StringComparer.Ordinal)
        {
            "arousal", "cockslut", "denial", "chastity", "locked", "keyholder", "permission", "discipline",
        };

        /// <summary>A4 Presentation. Design set, unchanged — but see AXIS COVERAGE: drone has no
        /// prompt carrying any of these, and circe carries none above heat 1.</summary>
        private static readonly HashSet<string> A4Presentation = new(StringComparer.Ordinal)
        {
            "femme", "dressing", "pink", "doll", "uniform", "good-girl", "giggly", "soft", "trigger",
        };

        /// <summary>A5 Autonomy — the confession cluster ONLY, read at heat &gt;= 3.
        ///
        /// The design also listed <c>independent</c> / <c>unowned</c> / <c>curious</c>. Those were
        /// dropped after reading the entries they actually select: above heat 1 they are, without
        /// exception, the trick-adjacent general-knowledge quizzes —
        /// <c>bmb_h3_quiz_capital2</c> "What is the capital of Canada?",
        /// <c>drn_h3_quiz_add</c> "What is 14 + 15?", <c>sis_h3_quiz_germany</c>. Scoring those as
        /// autonomy measures whether the user knows Ottawa. Below heat 2 (where the real
        /// <c>unowned</c>/<c>independent</c> mass sits: 274 of 283 circe <c>unowned</c> entries are
        /// heat 0) the <see cref="MinHeat"/> gate excludes them anyway. What is left — refusing to
        /// confess on a heat-3+ confession prompt — is a genuine autonomy signal, and it exists
        /// only in the sissy and circe banks.</summary>
        private static readonly HashSet<string> A5Autonomy = new(StringComparer.Ordinal)
        {
            "confession", "honesty", "exposure",
        };

        /// <summary>Read a completed run. Never throws; a null/empty trajectory yields an
        /// all-neutral, all-under-sampled profile.</summary>
        public static IntakeProfile Profile(QuizRunResult? run)
        {
            var traj = run?.Trajectory;
            if (traj == null || traj.Count == 0) return new IntakeProfile();

            // One pass to collect the scoreable records, then one cheap pass per axis.
            var usable = new List<QuizRunAnswerRecord>(traj.Count);
            foreach (var r in traj)
            {
                if (r == null) continue;
                if (IsScoreable(r)) usable.Add(r);
            }

            return new IntakeProfile
            {
                Blankness    = ScoreAxis(usable, A1Blankness, MinHeat, invert: false),
                Service      = ScoreAxis(usable, A2Service, MinHeat, invert: false),
                Arousal      = ScoreAxis(usable, A3Arousal, MinHeat, invert: false),
                Presentation = ScoreAxis(usable, A4Presentation, MinHeat, invert: false),
                // Autonomy is the REFUSAL rate on the confession cluster, hence invert.
                Autonomy     = ScoreAxis(usable, A5Autonomy, AutonomyHotHeat, invert: true),
                ScoreableRecords = usable.Count,
            };
        }

        /// <summary>Can this record carry a preference signal at all? (Heat is checked per axis,
        /// because A5 uses a higher floor.)</summary>
        private static bool IsScoreable(QuizRunAnswerRecord r)
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

        /// <summary>
        /// axis = SUM(heat * endorse) / SUM(heat) over the records whose tags intersect
        /// <paramref name="axisTags"/> at heat &gt;= <paramref name="minHeat"/>.
        /// endorse = 1 when the user took the bank's compliant option, else 0 (see the class
        /// comment for why there is no index-lean partial credit). <paramref name="invert"/>
        /// scores the REFUSAL instead, which is what A5 wants.
        /// </summary>
        private static IntakeAxis ScoreAxis(List<QuizRunAnswerRecord> usable, HashSet<string> axisTags,
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
            if (n < MinItems || den <= 0) return IntakeAxis.Neutral(n);
            return new IntakeAxis { Value = Math.Clamp(num / den, 0, 1), ItemCount = n, UnderSampled = false };
        }

        private static bool HasAnyTag(QuizRunAnswerRecord r, HashSet<string> set)
        {
            var tags = r.Tags;
            if (tags == null) return false;
            foreach (var t in tags)
            {
                if (t == null) continue;
                // Structural tags never satisfy an axis. (They are not in any axis set either;
                // this is the belt to that braces, so a future tag-set edit can't smuggle one in.)
                if (StructuralTags.Contains(t)) continue;
                if (set.Contains(t)) return true;
            }
            return false;
        }
    }
}
