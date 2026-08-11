using System;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The one knob that replaces the cooldown slider, the cooldown-max slider and the (dead)
    /// per-category toggles (doc 02 §8). It maps internally to a line budget per hour, a baseline
    /// worthiness threshold and whether the Rare tier is armed at all.
    ///
    /// <para>Persisted by ordinal, so members must never be reordered.</para>
    /// </summary>
    public enum AwarenessIntensity
    {
        /// <summary>She never comments. The observer still records to the ledger only if awareness is on at all.</summary>
        Off = 0,

        /// <summary>≈2 lines/hour, everything combined.</summary>
        Subtle = 1,

        /// <summary>≈6 lines/hour. The ship default.</summary>
        Chatty = 2,

        /// <summary>≈12 lines/hour. Still bounded — the absolute floors in the arbiter apply at every setting.</summary>
        Unhinged = 3
    }

    /// <summary>
    /// The intensity dial's numbers, in one place. Doc 02 §3.4 sets the line rates; the thresholds are
    /// the scorer's baseline (see <see cref="WorthinessScorer"/>) and the tier gate decides whether a
    /// trend-armed frame is allowed to reach the more expensive model.
    /// </summary>
    public static class AwarenessIntensityProfile
    {
        /// <summary>Target lines per hour across ALL sources — barks included (doc 02 §3.4).</summary>
        public static int LinesPerHour(AwarenessIntensity intensity) => intensity switch
        {
            AwarenessIntensity.Off => 0,
            AwarenessIntensity.Subtle => 2,
            AwarenessIntensity.Unhinged => 12,
            _ => 6
        };

        /// <summary>
        /// Baseline worthiness a frame must beat to be worth an LLM call. The scorer floats a live
        /// threshold above this after every delivered line and decays back to it.
        /// </summary>
        public static double BaselineThreshold(AwarenessIntensity intensity) => intensity switch
        {
            AwarenessIntensity.Off => double.PositiveInfinity,
            AwarenessIntensity.Subtle => 0.65,
            AwarenessIntensity.Unhinged => 0.30,
            _ => 0.45
        };

        /// <summary>
        /// Rare tier (the ledger-armed callback) is the expensive, memorable one. Subtle keeps it —
        /// scarcity is the point of that setting — but Off obviously does not.
        /// </summary>
        public static bool RareEnabled(AwarenessIntensity intensity) => intensity != AwarenessIntensity.Off;

        /// <summary>
        /// Reads the setting without throwing headlessly (App.Settings is null in tests and during
        /// early startup). Missing settings read as the ship default rather than as Off, because a
        /// null settings object is a startup-ordering fact, not a user preference.
        /// </summary>
        public static AwarenessIntensity Current
        {
            get
            {
                try { return App.Settings?.Current?.AwarenessIntensity ?? AwarenessIntensity.Chatty; }
                catch { return AwarenessIntensity.Chatty; }
            }
        }
    }
}
