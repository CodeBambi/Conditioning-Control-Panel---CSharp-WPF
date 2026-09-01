using System;
using System.Collections.Generic;
using System.Linq;

// ============================================================================
// GOON GAME — Phase B. Draft agreement metadata + the deterministic
// "what do we both have to endure" ramp.
//
// REDESIGN (2026-08-03). The draft is no longer three private picks:
//   1. Each player toggles which elements they ALLOW; the effective pool is the
//      INTERSECTION of both allowed sets (SharedPool).
//   2. BuildRamp ROLLS one schedule from that pool + the shared match seed, and
//      BOTH players run it — same elements, same instants, perfectly even.
//   3. Bubbles are an ALWAYS-ON baseline: never toggled, never rolled, emitted
//      from t=0 to the end with intensity climbing 0.15 -> 1.00. The enum code
//      is unchanged and still frozen; it is only out of the toggle/roll set.
//
// Risk tiers below still feed the score formula
// 1 pt/s x (1 + GoonConsts.DraftRiskStep x riskTier) x attention multiplier,
// now computed from the SHARED pool (identical for both players).
//
// This file only PLANS cues. Nothing here touches App.Flash/App.Video/... —
// GoonMatchService raises the cues as events and the executor fans them out.
//
// PARITY: Resources/web/goon/core/draft.js is a line-for-line transcription of
// this file. The rng draw ORDER and COUNT in RollSegments are part of the
// protocol — changing either desyncs a cross-client duel.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>What a planned cue asks the executor to do with an element.</summary>
    public enum GoonCueAction
    {
        Start,      // begin the element (DurationMs > 0 for segments, 0 = sustained until Stop)
        Intensity,  // ramp update, same element, new intensity
        Stop,
    }

    /// <summary>One planned instruction on the shared endurance ramp.</summary>
    public sealed class GoonElementCue
    {
        /// <summary>Milliseconds from the start of the Live phase (local monotonic).</summary>
        public long OffsetMs { get; init; }
        public GoonCueAction Action { get; init; }
        public GoonElement Element { get; init; }
        /// <summary>0..1 — pre-cap. Every receiver-side cap (toy mixer, level gates) still applies.</summary>
        public double Intensity { get; init; }
        /// <summary>Segment length for Start cues; 0 for sustained elements and for Stop/Intensity.</summary>
        public int DurationMs { get; init; }

        public override string ToString() =>
            $"{OffsetMs / 1000}s {Action} {Element} i={Intensity:F2}" + (DurationMs > 0 ? $" d={DurationMs / 1000}s" : "");
    }

    /// <summary>Pacing shape of one element. Values are the v1 tuning pass.</summary>
    public sealed class GoonElementProfile
    {
        public GoonElement Element { get; init; }
        /// <summary>0..3. Higher = harder to endure = better score multiplier.</summary>
        public int RiskTier { get; init; }
        /// <summary>True = the element's natural shape is a continuous presence rather than a burst.</summary>
        public bool Sustained { get; init; }
        /// <summary>Appetite for opening the match: 0.00 leads, 0.35 closes. Orders the first pass.</summary>
        public double EntryFraction { get; init; }
        public int MinDurationMs { get; init; }
        public int MaxDurationMs { get; init; }
        /// <summary>Gap between bursts near the start of the match (long) ...</summary>
        public int EarlyGapMs { get; init; }
        /// <summary>... and near the end (short). Legacy shape data, kept for tooling.</summary>
        public int LateGapMs { get; init; }
        public double IntensityStart { get; init; }
        public double IntensityEnd { get; init; }
    }

    /// <summary>
    /// Agreement tables + deterministic ramp roller.
    ///
    /// v1 risk tiers (unchanged by the redesign):
    ///   0  Flashes, BouncingText      ambient, always-on, low disruption
    ///   1  Subliminals, Bubbles       constant pull on attention, still passive
    ///   2  Videos, LockCards, ToyPatterns, Spiral  demand a response / physically escalate
    ///   3  BrainDrain                 the heavy; also the once-per-match payload
    /// </summary>
    public static class GoonDraft
    {
        /// <summary>LEGACY: the pre-agreement draft size. Nothing enforces it any more.</summary>
        public const int PicksPerPlayer = 3;

        /// <summary>Fewest elements a player may allow, and the smallest workable intersection.</summary>
        public const int MinAllowedElements = 2;

        /// <summary>Highest achievable summed risk tier (BrainDrain + two tier-2s).</summary>
        public const int MaxMatchRiskTier = 7;

        /// <summary>Rotating pool, v1 = the whole enum.</summary>
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

        /// <summary>
        /// The one element that is never toggled and never rolled: it runs the whole match, for
        /// both players. (The Bubbles PAYLOAD — the throwable swarm — is a different thing and is
        /// untouched by this.)
        /// </summary>
        public const GoonElement AlwaysOnElement = GoonElement.Bubbles;

        /// <summary>Always-on band: barely there at the whistle, unmissable at the end.</summary>
        private const double AlwaysOnIntensityStart = 0.15;
        private const double AlwaysOnIntensityEnd = 1.00;

        /// <summary>What the agreement screen may toggle: the pool minus the always-on element.</summary>
        public static readonly IReadOnlyList<GoonElement> TogglePool =
            PoolV1.Where(e => e != AlwaysOnElement).ToArray();

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
            // Modelled on BrainDrain — the same sustained shape — but it opens a tenth of the
            // match earlier and tops out lower, so a spiral+drain pool escalates in two steps.
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

        /// <summary>Segments shorter than this are dropped rather than squeezed against the end.</summary>
        private const int MinUsefulBurstMs = 8000;

        // ------------------------------------------------------ rolled-ramp tuning
        // All integer milliseconds on purpose: passes/stride/segLen must come out bit-identical in
        // C# and JS, and integer division is the only arithmetic that trivially does.

        private const int RollTargetStrideMs = 30000;
        private const int RollSegmentOverlap = 2;
        private const int RollMinStrideMs = 6000;
        private const int RollMinSegmentMs = 12000;
        private const int RollMaxSegmentMs = 120000;
        private const int RollJitterPct = 35;
        private const int RollMaxSegments = 512;

        /// <summary>
        /// Reserved salt code for the ramp roll's sub-stream. Deliberately outside GoonElement so
        /// the roll can never collide with a per-element stream.
        /// </summary>
        private const int RampRollSaltCode = 1000;

        public static GoonElementProfile ProfileOf(GoonElement element) =>
            Profiles.TryGetValue(element, out var p)
                ? p
                : new GoonElementProfile { Element = element, RiskTier = 1, Sustained = true, EntryFraction = 0.1, IntensityStart = 0.3, IntensityEnd = 0.8 };

        public static int RiskTierOf(GoonElement element) => ProfileOf(element).RiskTier;

        /// <summary>Summed risk tier of a pool — the "riskTier" in the score formula.</summary>
        public static int MatchRiskTier(IEnumerable<GoonElement>? draft)
        {
            if (draft == null) return 0;
            var total = draft.Distinct().Sum(RiskTierOf);
            return Math.Clamp(total, 0, MaxMatchRiskTier);
        }

        /// <summary>Score multiplier contributed by the pool: 1 + step x tier.</summary>
        public static double RiskMultiplier(int matchRiskTier) =>
            1.0 + GoonConsts.DraftRiskStep * Math.Clamp(matchRiskTier, 0, MaxMatchRiskTier);

        // ------------------------------------------------------------- agreement

        /// <summary>
        /// Canonical form of an allowed set: distinct, in the v1 pool, always-on element removed,
        /// sorted ASCENDING. Canonical because both engines must derive the same pool from the
        /// same two sets with no ordering to agree on.
        /// </summary>
        public static List<GoonElement> NormalizeAllowed(IEnumerable<GoonElement>? allowed)
        {
            var seen = new HashSet<GoonElement>();
            var outList = new List<GoonElement>();
            if (allowed != null)
            {
                foreach (var e in allowed)
                {
                    if (e == AlwaysOnElement) continue;     // never toggled, never rolled
                    if (!PoolV1.Contains(e)) continue;
                    if (!seen.Add(e)) continue;
                    outList.Add(e);
                }
            }
            outList.Sort((a, b) => ((int)a).CompareTo((int)b));
            return outList;
        }

        /// <summary>Default: everything this pairing can actually run is ON.</summary>
        public static List<GoonElement> DefaultAllowed(IEnumerable<GoonElement>? available)
        {
            var list = available?.ToList();
            return NormalizeAllowed(list != null && list.Count > 0 ? list : PoolV1);
        }

        /// <summary>The effective pool: what BOTH players allow.</summary>
        public static List<GoonElement> SharedPool(IEnumerable<GoonElement>? mine, IEnumerable<GoonElement>? theirs)
        {
            var a = NormalizeAllowed(mine);
            var b = new HashSet<GoonElement>(NormalizeAllowed(theirs));
            return a.Where(b.Contains).ToList();
        }

        /// <summary>A player may not confirm with fewer than <see cref="MinAllowedElements"/> on.</summary>
        public static bool IsValidAllowed(IEnumerable<GoonElement>? allowed, out string error)
        {
            error = "";
            if (NormalizeAllowed(allowed).Count < MinAllowedElements)
            {
                error = $"keep at least {MinAllowedElements} effects switched on";
                return false;
            }
            return true;
        }

        /// <summary>...and the two of you have to leave at least that many in common.</summary>
        public static bool IsValidSharedPool(IEnumerable<GoonElement>? pool, out string error)
        {
            error = "";
            var n = NormalizeAllowed(pool).Count;
            if (n < MinAllowedElements)
            {
                error = $"you two only agree on {n} effect{(n == 1 ? "" : "s")} - open one more up";
                return false;
            }
            return true;
        }

        /// <summary>LEGACY validity check for the old three-pick draft. The engine no longer uses it.</summary>
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

        // ------------------------------------------------------------------ ramp

        /// <summary>
        /// Plans the whole Live-phase ramp up front from the SHARED pool and the combined match
        /// seed. The result is identical on both machines by construction — nothing here reads
        /// host/guest, the local player, or anything but its three arguments.
        /// Cues are returned in <see cref="CompareCues"/> order.
        /// </summary>
        public static List<GoonElementCue> BuildRamp(
            IReadOnlyList<GoonElement>? pool,
            ulong matchSeed,
            int liveDurationSec,
            Func<ulong, IGoonRng> rngFactory)
        {
            var cues = new List<GoonElementCue>();
            if (liveDurationSec <= 0 || rngFactory == null) return cues;

            long liveMs = (long)liveDurationSec * 1000L;

            // 1. The always-on baseline. Not rolled, not toggleable, not optional.
            PushSegment(cues, AlwaysOnElement, 0, liveMs,
                AlwaysOnIntensityStart, AlwaysOnIntensityEnd, liveMs, sustained: true);

            // 2. The rolled schedule over the shared pool.
            var roll = NormalizeAllowed(pool);
            if (roll.Count > 0) RollSegments(cues, roll, matchSeed, liveMs, rngFactory);

            cues.Sort(CompareCues);
            return cues;
        }

        private static void PushSegment(
            List<GoonElementCue> cues, GoonElement element, long startMs, long endMs,
            double intensityStart, double intensityEnd, long liveMs, bool sustained)
        {
            // Intensity is a pure function of GLOBAL match progress, so every element escalates
            // toward the end no matter which slots the roll gave it.
            double At(long t) => Lerp(intensityStart, intensityEnd, liveMs > 0 ? (double)t / liveMs : 1.0);

            cues.Add(new GoonElementCue
            {
                OffsetMs = startMs,
                Action = GoonCueAction.Start,
                Element = element,
                Intensity = At(startMs),
                DurationMs = sustained ? 0 : (int)(endMs - startMs),
            });

            for (long t = startMs + SustainedRampStepMs; t < endMs; t += SustainedRampStepMs)
            {
                cues.Add(new GoonElementCue
                {
                    OffsetMs = t,
                    Action = GoonCueAction.Intensity,
                    Element = element,
                    Intensity = At(t),
                });
            }

            cues.Add(new GoonElementCue
            {
                OffsetMs = endMs,
                Action = GoonCueAction.Stop,
                Element = element,
            });
        }

        /// <summary>
        /// The roll. Consumes the rng in a FIXED order — (K-1) NextInt draws per pass for the
        /// shuffle, then exactly one NextDouble per slot for the jitter, whether or not the slot
        /// survives the clamp. Any change to that order or count is a desync, not a tuning tweak.
        /// </summary>
        private static void RollSegments(
            List<GoonElementCue> cues, List<GoonElement> roll, ulong matchSeed, long liveMs,
            Func<ulong, IGoonRng> rngFactory)
        {
            int k = roll.Count;
            var rng = rngFactory(SaltSeed(matchSeed, RampRollSaltCode));

            long target = (long)k * RollTargetStrideMs;
            long passes = Math.Max(1, (liveMs + target / 2) / target);
            if (passes * k > RollMaxSegments) passes = Math.Max(1, RollMaxSegments / k);

            long stride = Math.Max(RollMinStrideMs, liveMs / (passes * k));
            long jitterMax = stride * RollJitterPct / 100;

            long segLen = Math.Clamp(stride * RollSegmentOverlap, RollMinSegmentMs, RollMaxSegmentMs);
            // Two segments of the SAME element are at least `separation` slots apart (see the
            // pass-boundary swap below); keep them from overlapping each other, because a Stop
            // landing inside the next Start would silently kill the element early and eat the time
            // it was owed.
            long separation = k >= 2 ? 2 : 1;
            long sameElementCap = separation * stride - 2 * jitterMax - 1000;
            if (segLen > sameElementCap) segLen = sameElementCap;
            if (segLen < MinUsefulBurstMs) segLen = MinUsefulBurstMs;

            GoonElement? lastOfPrevPass = null;
            for (long p = 0; p < passes; p++)
            {
                var pass = new List<GoonElement>(roll);
                // Fisher-Yates, identical to the JS binding — inlined because IGoonRng only
                // promises NextInt/NextDouble.
                for (int i = pass.Count - 1; i > 0; i--)
                {
                    int j = rng.NextInt(0, i + 1);
                    (pass[i], pass[j]) = (pass[j], pass[i]);
                }
                // Opening pass only: a closer must not open the match. OrderBy is a STABLE sort,
                // so ties keep the order the roll gave them (the JS Array.sort is stable too).
                if (p == 0) pass = pass.OrderBy(e => ProfileOf(e).EntryFraction).ToList();
                // Each element appears once per pass, but a pass BOUNDARY can hand it two adjacent
                // slots. One deterministic swap (no rng draw) guarantees the two-slot separation
                // the cap assumes.
                if (k >= 2 && lastOfPrevPass.HasValue && pass[0] == lastOfPrevPass.Value)
                {
                    (pass[0], pass[1]) = (pass[1], pass[0]);
                }
                lastOfPrevPass = pass[k - 1];

                for (int i = 0; i < k; i++)
                {
                    long slot = p * k + i;
                    long jitter = (long)((rng.NextDouble() * 2 - 1) * jitterMax);

                    long start = slot * stride + jitter;
                    if (start > liveMs - MinUsefulBurstMs) start = liveMs - MinUsefulBurstMs;
                    if (start < 0) start = 0;

                    long end = start + segLen;
                    if (end > liveMs) end = liveMs;
                    if (end - start < MinUsefulBurstMs) continue;

                    var prof = ProfileOf(pass[i]);
                    PushSegment(cues, pass[i], start, end, prof.IntensityStart, prof.IntensityEnd, liveMs, sustained: false);
                }
            }
        }

        /// <summary>
        /// Total order. Stop before Intensity before Start at one instant; element then duration
        /// break the rest, so an unstable List.Sort cannot produce a different order than the JS
        /// binding's stable one.
        /// </summary>
        private static int CompareCues(GoonElementCue a, GoonElementCue b)
        {
            int c = a.OffsetMs.CompareTo(b.OffsetMs);
            if (c != 0) return c;
            c = ((int)b.Action).CompareTo((int)a.Action);
            if (c != 0) return c;
            c = ((int)a.Element).CompareTo((int)b.Element);
            if (c != 0) return c;
            return a.DurationMs.CompareTo(b.DurationMs);
        }

        private static ulong SaltSeed(ulong matchSeed, GoonElement element) => SaltSeed(matchSeed, (int)element);

        private static ulong SaltSeed(ulong matchSeed, int code)
        {
            unchecked
            {
                ulong salt = (ulong)(code + 1) * 0x9E3779B97F4A7C15UL;
                return matchSeed ^ salt;
            }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0.0, 1.0);
    }
}
