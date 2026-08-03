using System;
using System.Collections.Generic;
using System.Linq;

// ============================================================================
// GOON GAME — Phase B. Draft pool metadata + the deterministic "what do I have
// to endure" ramp built from the drafted elements and the combined match seed.
//
// The draft defines what YOU endure. Risk tiers below are the v1 balance
// proposal for the plan's open question "Draft pool risk-tier values per
// element" — see the table in the class doc. They feed the score formula
// 1 pt/s x (1 + GoonConsts.DraftRiskStep x riskTier) x attention multiplier.
//
// This file only PLANS cues. Nothing here touches App.Flash/App.Video/... —
// GoonMatchService raises the cues as events and Phase B's
// GoonPayloadExecutor / Phase E wiring performs the fan-out.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>What a planned cue asks the executor to do with an element.</summary>
    public enum GoonCueAction
    {
        Start,      // begin the element (DurationMs > 0 for bursts, 0 = sustained until Stop)
        Intensity,  // sustained element only: ramp update, same element, new intensity
        Stop,
    }

    /// <summary>One planned instruction on the local player's endurance ramp.</summary>
    public sealed class GoonElementCue
    {
        /// <summary>Milliseconds from the start of the Live phase (local monotonic).</summary>
        public long OffsetMs { get; init; }
        public GoonCueAction Action { get; init; }
        public GoonElement Element { get; init; }
        /// <summary>0..1 — pre-cap. Every receiver-side cap (toy mixer, level gates) still applies.</summary>
        public double Intensity { get; init; }
        /// <summary>Burst length for Start cues; 0 for sustained elements and for Stop/Intensity.</summary>
        public int DurationMs { get; init; }

        public override string ToString() =>
            $"{OffsetMs / 1000}s {Action} {Element} i={Intensity:F2}" + (DurationMs > 0 ? $" d={DurationMs / 1000}s" : "");
    }

    /// <summary>Pacing shape of one drafted element. Values are the v1 tuning pass.</summary>
    public sealed class GoonElementProfile
    {
        public GoonElement Element { get; init; }
        /// <summary>0..3. Higher = harder to endure = better score multiplier.</summary>
        public int RiskTier { get; init; }
        /// <summary>True = one Start at entry, periodic Intensity ramp cues, one Stop at the end.</summary>
        public bool Sustained { get; init; }
        /// <summary>Fraction of the live phase that passes before this element first fires.</summary>
        public double EntryFraction { get; init; }
        public int MinDurationMs { get; init; }
        public int MaxDurationMs { get; init; }
        /// <summary>Gap between bursts near the start of the match (long) ...</summary>
        public int EarlyGapMs { get; init; }
        /// <summary>... and near the end (short). The ramp lerps between them.</summary>
        public int LateGapMs { get; init; }
        public double IntensityStart { get; init; }
        public double IntensityEnd { get; init; }
    }

    /// <summary>
    /// Draft pool tables + deterministic ramp planner.
    ///
    /// v1 risk tiers (open balance question in the plan — these are the starting point):
    ///   0  Flashes, BouncingText      ambient, always-on, low disruption
    ///   1  Subliminals, Bubbles       constant pull on attention, still passive
    ///   2  Videos, LockCards, ToyPatterns, Spiral  demand a response / physically escalate
    ///   3  BrainDrain                 the heavy; also the once-per-match payload
    /// A draft of 3 therefore sums to 1..7 (min 0+0+1, max 3+2+2), i.e. a score
    /// multiplier of 1.15x .. 2.05x at GoonConsts.DraftRiskStep = 0.15.
    /// Spiral (2026-08-03) joins the tier-2 band: sustained like BrainDrain but
    /// earlier and gentler, and unlike the heavy it may be fired repeatedly.
    /// </summary>
    public static class GoonDraft
    {
        public const int PicksPerPlayer = 3;

        /// <summary>Highest achievable summed risk tier for a legal 3-element draft (BrainDrain + two tier-2s).</summary>
        public const int MaxMatchRiskTier = 7;

        /// <summary>Rotating pool, v1 = the whole enum (plan §Match flow step 3).</summary>
        public static readonly IReadOnlyList<GoonElement> PoolV1 = new[]
        {
            GoonElement.Flashes,
            GoonElement.Videos,
            GoonElement.Subliminals,
            GoonElement.Bubbles,
            GoonElement.LockCards,
            GoonElement.ToyPatterns,
            GoonElement.BrainDrain,
            GoonElement.BouncingText,
            GoonElement.Spiral,
        };

        private static readonly Dictionary<GoonElement, GoonElementProfile> Profiles = new()
        {
            [GoonElement.Flashes] = new GoonElementProfile
            {
                Element = GoonElement.Flashes,
                RiskTier = 0,
                Sustained = true,
                EntryFraction = 0.00,
                IntensityStart = 0.35,
                IntensityEnd = 0.90,
            },
            [GoonElement.BouncingText] = new GoonElementProfile
            {
                Element = GoonElement.BouncingText,
                RiskTier = 0,
                Sustained = true,
                EntryFraction = 0.10,
                IntensityStart = 0.30,
                IntensityEnd = 0.80,
            },
            [GoonElement.Subliminals] = new GoonElementProfile
            {
                Element = GoonElement.Subliminals,
                RiskTier = 1,
                Sustained = true,
                EntryFraction = 0.05,
                IntensityStart = 0.40,
                IntensityEnd = 1.00,
            },
            [GoonElement.Bubbles] = new GoonElementProfile
            {
                Element = GoonElement.Bubbles,
                RiskTier = 1,
                Sustained = false,
                EntryFraction = 0.12,
                MinDurationMs = 45000,
                MaxDurationMs = 90000,
                EarlyGapMs = 210000,
                LateGapMs = 90000,
                IntensityStart = 0.35,
                IntensityEnd = 0.85,
            },
            [GoonElement.Videos] = new GoonElementProfile
            {
                Element = GoonElement.Videos,
                RiskTier = 2,
                Sustained = false,
                EntryFraction = 0.15,
                MinDurationMs = 60000,
                MaxDurationMs = 120000,
                EarlyGapMs = 240000,
                LateGapMs = 120000,
                IntensityStart = 0.50,
                IntensityEnd = 1.00,
            },
            [GoonElement.LockCards] = new GoonElementProfile
            {
                Element = GoonElement.LockCards,
                RiskTier = 2,
                Sustained = false,
                EntryFraction = 0.20,
                MinDurationMs = 30000,
                MaxDurationMs = 60000,
                EarlyGapMs = 300000,
                LateGapMs = 150000,
                IntensityStart = 0.40,
                IntensityEnd = 0.90,
            },
            [GoonElement.ToyPatterns] = new GoonElementProfile
            {
                Element = GoonElement.ToyPatterns,
                RiskTier = 2,
                Sustained = false,
                EntryFraction = 0.05,
                MinDurationMs = 30000,
                MaxDurationMs = 75000,
                EarlyGapMs = 180000,
                LateGapMs = 75000,
                IntensityStart = 0.30,
                IntensityEnd = 0.85,
            },
            [GoonElement.BrainDrain] = new GoonElementProfile
            {
                Element = GoonElement.BrainDrain,
                RiskTier = 3,
                Sustained = true,
                EntryFraction = 0.35,
                IntensityStart = 0.25,
                IntensityEnd = 0.75,
            },
            // Modelled on BrainDrain — the same sustained shape (one Start, SustainedRampStepMs
            // Intensity cues, one Stop) — but it opens a tenth of the match earlier and tops out
            // lower, so a spiral+drain draft escalates in two visible steps instead of one wall.
            [GoonElement.Spiral] = new GoonElementProfile
            {
                Element = GoonElement.Spiral,
                RiskTier = 2,
                Sustained = true,
                EntryFraction = 0.25,
                IntensityStart = 0.20,
                IntensityEnd = 0.65,
            },
        };

        /// <summary>Sustained elements get an Intensity refresh cue on this cadence.</summary>
        private const int SustainedRampStepMs = 30000;

        /// <summary>Bursts shorter than this are dropped rather than squeezed against the end of the match.</summary>
        private const int MinUsefulBurstMs = 8000;

        public static GoonElementProfile ProfileOf(GoonElement element) =>
            Profiles.TryGetValue(element, out var p)
                ? p
                : new GoonElementProfile { Element = element, RiskTier = 1, Sustained = true, EntryFraction = 0.1, IntensityStart = 0.3, IntensityEnd = 0.8 };

        public static int RiskTierOf(GoonElement element) => ProfileOf(element).RiskTier;

        /// <summary>Summed risk tier of a draft — the "riskTier" in the score formula.</summary>
        public static int MatchRiskTier(IEnumerable<GoonElement>? draft)
        {
            if (draft == null) return 0;
            var total = draft.Distinct().Sum(RiskTierOf);
            return Math.Clamp(total, 0, MaxMatchRiskTier);
        }

        /// <summary>Score multiplier contributed by the draft: 1 + step x tier.</summary>
        public static double RiskMultiplier(int matchRiskTier) =>
            1.0 + GoonConsts.DraftRiskStep * Math.Clamp(matchRiskTier, 0, MaxMatchRiskTier);

        /// <summary>Exactly <see cref="PicksPerPlayer"/> distinct elements drawn from the pool.</summary>
        public static bool IsValidDraft(IReadOnlyList<GoonElement>? picks, out string error)
        {
            error = "";
            if (picks == null || picks.Count != PicksPerPlayer)
            {
                error = $"draft must contain exactly {PicksPerPlayer} elements";
                return false;
            }
            if (picks.Distinct().Count() != picks.Count)
            {
                error = "draft contains duplicates";
                return false;
            }
            foreach (var p in picks)
            {
                if (!PoolV1.Contains(p))
                {
                    error = $"element {p} is not in the v1 pool";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Plans the whole Live-phase ramp up front, deterministically, from the combined
        /// match seed. Each element is salted by its own id (NOT by host/guest), so two
        /// players who draft the same element endure the same shape — fair by construction.
        /// Cues are returned sorted by <see cref="GoonElementCue.OffsetMs"/>.
        /// </summary>
        public static List<GoonElementCue> BuildRamp(
            IReadOnlyList<GoonElement>? draft,
            ulong matchSeed,
            int liveDurationSec,
            Func<ulong, IGoonRng> rngFactory)
        {
            var cues = new List<GoonElementCue>();
            if (draft == null || draft.Count == 0 || liveDurationSec <= 0 || rngFactory == null) return cues;

            long liveMs = (long)liveDurationSec * 1000L;

            foreach (var element in draft.Distinct())
            {
                var profile = ProfileOf(element);
                var rng = rngFactory(SaltSeed(matchSeed, element));
                long entry = (long)(liveMs * Math.Clamp(profile.EntryFraction, 0.0, 0.9));

                if (profile.Sustained)
                {
                    cues.Add(new GoonElementCue
                    {
                        OffsetMs = entry,
                        Action = GoonCueAction.Start,
                        Element = element,
                        Intensity = profile.IntensityStart,
                        DurationMs = 0,
                    });

                    for (long t = entry + SustainedRampStepMs; t < liveMs; t += SustainedRampStepMs)
                    {
                        double p = liveMs > entry ? (double)(t - entry) / (liveMs - entry) : 1.0;
                        cues.Add(new GoonElementCue
                        {
                            OffsetMs = t,
                            Action = GoonCueAction.Intensity,
                            Element = element,
                            Intensity = Lerp(profile.IntensityStart, profile.IntensityEnd, p),
                        });
                    }

                    cues.Add(new GoonElementCue
                    {
                        OffsetMs = liveMs,
                        Action = GoonCueAction.Stop,
                        Element = element,
                    });
                    continue;
                }

                // Burst element: shrinking gaps + rising intensity as the match progresses.
                long cursor = entry;
                int guard = 0;
                while (cursor < liveMs && guard++ < 512)
                {
                    double p = Math.Clamp((double)cursor / liveMs, 0.0, 1.0);
                    int duration = profile.MaxDurationMs > profile.MinDurationMs
                        ? rng.NextInt(profile.MinDurationMs, profile.MaxDurationMs + 1)
                        : profile.MinDurationMs;

                    if (cursor + duration > liveMs) duration = (int)(liveMs - cursor);
                    if (duration < MinUsefulBurstMs) break;

                    double intensity = Lerp(profile.IntensityStart, profile.IntensityEnd, p);

                    cues.Add(new GoonElementCue
                    {
                        OffsetMs = cursor,
                        Action = GoonCueAction.Start,
                        Element = element,
                        Intensity = intensity,
                        DurationMs = duration,
                    });
                    cues.Add(new GoonElementCue
                    {
                        OffsetMs = cursor + duration,
                        Action = GoonCueAction.Stop,
                        Element = element,
                    });

                    double baseGap = Lerp(profile.EarlyGapMs, profile.LateGapMs, p);
                    double jitter = 0.75 + rng.NextDouble() * 0.5;         // +-25 %
                    cursor += duration + (long)Math.Max(5000, baseGap * jitter);
                }
            }

            cues.Sort((a, b) =>
            {
                int c = a.OffsetMs.CompareTo(b.OffsetMs);
                if (c != 0) return c;
                // Stop before Start at the same instant, so a re-trigger reads cleanly downstream.
                return ((int)b.Action).CompareTo((int)a.Action);
            });
            return cues;
        }

        private static ulong SaltSeed(ulong matchSeed, GoonElement element)
        {
            unchecked
            {
                ulong salt = (ulong)((int)element + 1) * 0x9E3779B97F4A7C15UL;
                return matchSeed ^ salt;
            }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0.0, 1.0);
    }
}
