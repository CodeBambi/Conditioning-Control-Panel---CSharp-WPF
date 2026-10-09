namespace ConditioningControlPanel.Views.Controls.Companion
{
    // Moved verbatim from the WPF head's CompanionVmPrimitives.cs so both heads draw the same
    // attention ladder and constellation states. The one edit: FractionToStarConverter.ToFraction
    // (a WPF converter) became Clamp01, which is what it does for the doubles these are given.
    /// <summary>A constellation node's visual state.</summary>
    public enum ConstellationNodeState
    {
        /// <summary>Reached and passed — pink filled star.</summary>
        Filled,
        /// <summary>Where you are — gold star, the one node that pulses.</summary>
        Current,
        /// <summary>Not reached — faint outline.</summary>
        Future
    }

    /// <summary>
    /// The attention meter's copy ladder (Z6). Pure function of the remaining fraction so it is
    /// unit-testable and so the thresholds live in exactly one place. Never the word "tokens";
    /// the floor state must still say her voice keeps working.
    /// </summary>
    public enum AttentionMood
    {
        /// <summary>&gt;= 40% — "Plenty of her attention left today."</summary>
        Plenty,
        /// <summary>&lt; 40% — "she's saving her best lines".</summary>
        Saving,
        /// <summary>&lt; 15% — "she's whispering to conserve energy".</summary>
        Whispering,
        /// <summary>0% — "she'll be all yours again tomorrow~".</summary>
        Spent
    }

    /// <summary>
    /// Attention-meter decision logic (doc 01 §4.3 / design §3 Z6). Thresholds are inclusive at
    /// the top: exactly 40% is still <see cref="AttentionMood.Plenty"/>, exactly 15% is still
    /// <see cref="AttentionMood.Saving"/>, and only a true zero is <see cref="AttentionMood.Spent"/>.
    /// </summary>
    public static class AttentionCopy
    {
        /// <summary>Below this fraction the quiet, in-voice Patreon line appears.</summary>
        public const double UpsellThreshold = 0.40;
        /// <summary>Below this fraction she "whispers to conserve energy".</summary>
        public const double WhisperThreshold = 0.15;

        /// <summary>Maps 0..1 remaining attention to the copy ladder. Clamps, never throws.</summary>
        public static AttentionMood MoodFor(double fraction)
        {
            double f = CompanionMeterMath.Clamp01(fraction);
            if (f <= 0.0) return AttentionMood.Spent;
            if (f < WhisperThreshold) return AttentionMood.Whispering;
            if (f < UpsellThreshold) return AttentionMood.Saving;
            return AttentionMood.Plenty;
        }

        /// <summary>The loc key for the headline copy at this fraction.</summary>
        public static string CopyKeyFor(double fraction) => MoodFor(fraction) switch
        {
            AttentionMood.Spent => "companion_attention_spent",
            AttentionMood.Whispering => "companion_attention_whispering",
            AttentionMood.Saving => "companion_attention_saving",
            _ => "companion_attention_plenty"
        };

        /// <summary>The quiet upsell shows below 40% — and only there. Never at full, never twice.</summary>
        public static bool ShowUpsell(double fraction)
            => CompanionMeterMath.Clamp01(fraction) < UpsellThreshold;

        /// <summary>
        /// The bar never renders as literally nothing: a spent meter keeps a 4% sliver (the mockup's
        /// <c>.att-empty</c>) so the card reads as "empty", not "broken".
        /// </summary>
        public static double BarFractionFor(double fraction)
        {
            double f = CompanionMeterMath.Clamp01(fraction);
            return f <= 0.0 ? SpentBarFraction : f;
        }

        /// <summary>The sliver a spent meter keeps. Not a threshold — a drawing minimum.</summary>
        public const double SpentBarFraction = 0.04;

        /// <summary>
        /// Whether the meter is actually spent, as opposed to merely drawing the spent sliver.
        ///
        /// <para>The card used to key its desaturated "empty" styling off
        /// <c>BarFraction == 0.04</c>, which cannot tell the two apart: a user with 4 of 100 chats
        /// left produces the same IEEE754 double as the sliver and got the glowless, greyed bar
        /// while the copy beside it still said she had attention left. Ask for the intent, never
        /// for the magic number.</para>
        /// </summary>
        public static bool IsSpent(double fraction) => MoodFor(fraction) == AttentionMood.Spent;

        /// <summary>
        /// Whether the barks-only floor promise is shown. It is not detail-on-demand: doc 01 §5.4
        /// and design §3 Z6 both require the card to SAY, out loud, that her voice keeps working —
        /// it is the whole answer to the budget-backlash reading. It appears once the meter is low
        /// enough for the question to occur to anyone (whispering or spent); above that it would be
        /// reassurance nobody asked for.
        /// </summary>
        public static bool ShowFloorNote(double fraction)
        {
            var mood = MoodFor(fraction);
            return mood == AttentionMood.Whispering || mood == AttentionMood.Spent;
        }
    }

    /// <summary>
    /// Relationship-constellation maths (Z1 bottom). Five stages, ratchet design — the mechanics
    /// stay vague in the UI but the node states are deterministic.
    /// </summary>
    public static class ConstellationMath
    {
        /// <summary>New ▸ Warming ▸ Bestie ▸ Possessive ▸ Inevitable.</summary>
        public const int StageCount = 5;

        /// <summary>Clamps any stage index into 0..4.</summary>
        public static int ClampStage(int stage)
            => stage < 0 ? 0 : (stage >= StageCount ? StageCount - 1 : stage);

        /// <summary>
        /// The node state for <paramref name="index"/> given the current stage. Pre-T4 (dormant)
        /// every node is <see cref="ConstellationNodeState.Future"/> — outlines with names, no
        /// lock icon, because this is a promise and not a paywall.
        /// </summary>
        public static ConstellationNodeState StateFor(int index, int currentStage, bool isLive)
        {
            if (!isLive) return ConstellationNodeState.Future;
            int cur = ClampStage(currentStage);
            if (index < cur) return ConstellationNodeState.Filled;
            if (index == cur) return ConstellationNodeState.Current;
            return ConstellationNodeState.Future;
        }

        /// <summary>The loc key for a stage name, with an optional per-mod reflavor override.</summary>
        public static string StageKey(int index, string? modId = null)
        {
            int i = ClampStage(index);
            return string.IsNullOrWhiteSpace(modId)
                ? $"companion_stage_{i}"
                : $"companion_stage_{i}_{modId}";
        }

        /// <summary>
        /// How far the pink connector runs, 0..1. The line stops just past the current node so the
        /// unearned tail stays hairline — the gradient in the theme does the fade.
        /// </summary>
        public static double ConnectorFraction(int currentStage, bool isLive)
        {
            if (!isLive) return 0.0;
            int cur = ClampStage(currentStage);
            return (cur + 0.5) / StageCount;
        }
    }

    internal static class CompanionMeterMath
    {
        /// <summary>FractionToStarConverter.ToFraction for a double: NaN/infinity -> 0, clamped to 0..1.</summary>
        internal static double Clamp01(double f)
            => double.IsNaN(f) || double.IsInfinity(f) || f < 0.0 ? 0.0 : (f > 1.0 ? 1.0 : f);
    }
}
