using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Haptics.Core
{
    /// <summary>The five shipped temperament presets, in picker order. The numeric values ARE the
    /// ComboBox indices used by the Haptics tab — do not reorder.</summary>
    public enum HapticTemperamentKind
    {
        Gentle = 0,
        Balanced = 1,
        Tease = 2,
        Intense = 3,
        Cruel = 4
    }

    /// <summary>
    /// PHASE F — TEMPERAMENT DIAL.
    ///
    /// One global "personality" applied to everything the mixer produces. It is deliberately a
    /// small, closed set of MULTIPLIERS rather than another pile of sliders: the routing matrix
    /// already owns per-feature strength, and what was missing was a single dial for how the whole
    /// thing *behaves* — soft and slow, or snappy and mean.
    ///
    /// WHERE IT APPLIES (see <see cref="HapticMixer"/>):
    ///  - <see cref="ContinuousScale"/> multiplies each continuous layer AFTER its routing-row
    ///    intensity and BEFORE the master multiplier / master cap.
    ///  - <see cref="TransientScale"/> multiplies each transient sample as the priority groups are
    ///    summed, likewise before master / cap.
    ///  - <see cref="AttackScale"/> / <see cref="DecayScale"/> stretch or compress every transient
    ///    envelope's attack and decay as it is promoted to an active pulse (hold is untouched, so
    ///    a pattern keeps its rhythm and only changes its EDGE).
    ///  - <see cref="PulsePriorityBias"/> widens or narrows the concurrency window
    ///    (<c>HapticSettingsV2.MaxConcurrentPulses</c>). Priority is only ever consulted when that
    ///    window is full and the weakest active pulse has to be evicted, so biasing the window IS
    ///    biasing how much a low-priority accent can survive alongside a big one.
    ///
    /// THE CAP ALWAYS WINS: every scale lands before <c>min(raw x master, MasterCap)</c>, so a
    /// scale above 1.0 can never push an actuator past the user's safety ceiling.
    ///
    /// <see cref="Balanced"/> is all 1.0 / bias 0 — byte-identical to pre-temperament behaviour —
    /// and is the default.
    /// </summary>
    public sealed class HapticTemperament
    {
        private HapticTemperament(HapticTemperamentKind kind, string key,
                                  double continuousScale, double transientScale,
                                  double attackScale, double decayScale,
                                  int pulsePriorityBias)
        {
            Kind = kind;
            Key = key;
            ContinuousScale = continuousScale;
            TransientScale = transientScale;
            AttackScale = attackScale;
            DecayScale = decayScale;
            PulsePriorityBias = pulsePriorityBias;
        }

        public HapticTemperamentKind Kind { get; }

        /// <summary>Stable lowercase settings key (<c>HapticSettingsV2.Temperament</c>).</summary>
        public string Key { get; }

        /// <summary>Multiplier on every continuous layer (video floor, audio sync, DtRH depth...).</summary>
        public double ContinuousScale { get; }

        /// <summary>Multiplier on every transient envelope (clicks, hits, celebrations).</summary>
        public double TransientScale { get; }

        /// <summary>Attack-time multiplier. &gt;1 = slower swell, &lt;1 = snappier onset.</summary>
        public double AttackScale { get; }

        /// <summary>Decay-time multiplier. &gt;1 = longer tail, &lt;1 = cut off sooner.</summary>
        public double DecayScale { get; }

        /// <summary>Added to the concurrent-pulse allowance; see the class remarks.</summary>
        public int PulsePriorityBias { get; }

        /// <summary>Localization key for the preset's display name.</summary>
        public string NameKey => "haptics_temperament_" + Key;

        /// <summary>Localization key for the one-line description under the picker.</summary>
        public string DescriptionKey => "haptics_temperament_" + Key + "_desc";

        /// <summary>True when this preset changes nothing (Balanced) — lets the mixer skip work.</summary>
        public bool IsNeutral =>
            ContinuousScale == 1.0 && TransientScale == 1.0 &&
            AttackScale == 1.0 && DecayScale == 1.0 && PulsePriorityBias == 0;

        // ---- the table ---------------------------------------------------------------
        //                                                        cont  trans  atk   dec  bias
        public static readonly HapticTemperament Gentle =
            new(HapticTemperamentKind.Gentle, "gentle",           0.70, 0.75, 1.40, 1.30, -1);
        public static readonly HapticTemperament Balanced =
            new(HapticTemperamentKind.Balanced, "balanced",       1.00, 1.00, 1.00, 1.00,  0);
        public static readonly HapticTemperament Tease =
            new(HapticTemperamentKind.Tease, "tease",             0.85, 0.90, 1.60, 1.80,  0);
        public static readonly HapticTemperament Intense =
            new(HapticTemperamentKind.Intense, "intense",         1.15, 1.20, 0.80, 0.90, +1);
        public static readonly HapticTemperament Cruel =
            new(HapticTemperamentKind.Cruel, "cruel",             1.25, 1.40, 0.45, 0.70, +2);

        /// <summary>Every preset, in picker order. Index == <see cref="HapticTemperamentKind"/>.</summary>
        public static readonly IReadOnlyList<HapticTemperament> All =
            new[] { Gentle, Balanced, Tease, Intense, Cruel };

        /// <summary>What a fresh install (and any unrecognised key) gets.</summary>
        public static HapticTemperament Default => Balanced;

        /// <summary>Resolve a stored settings key. Unknown / null / empty falls back to Balanced,
        /// so a hand-edited settings.json can never silence or maul someone's toys.</summary>
        public static HapticTemperament FromKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return Default;
            switch (key.Trim().ToLowerInvariant())
            {
                case "gentle": return Gentle;
                case "balanced": return Balanced;
                case "tease": return Tease;
                case "intense": return Intense;
                case "cruel": return Cruel;
                default: return Default;
            }
        }

        public static HapticTemperament FromIndex(int index)
            => All[Math.Clamp(index, 0, All.Count - 1)];

        public override string ToString() => Key;
    }
}
