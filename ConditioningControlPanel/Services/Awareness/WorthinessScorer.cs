using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>What the arbiter is being told to do with a scored frame.</summary>
    public enum AwarenessVerdict
    {
        /// <summary>Not worth a sound. The frame is still recorded — she can joke about it later.</summary>
        Silence = 0,

        /// <summary>Worth a canned, voiced, free recognition bark.</summary>
        Bark = 1,

        /// <summary>Worth spending tokens on.</summary>
        Llm = 2
    }

    /// <summary>
    /// Everything the scorer needs about a moment. Deliberately a flat value bag rather than the whole
    /// <see cref="ContextFrame"/>: scoring must be testable without building an observer, and the frame
    /// is built FROM the score, not the other way round.
    /// </summary>
    /// <param name="FirstEverSeen">The ledger has never seen this app before. The "she noticed!" case.</param>
    /// <param name="FirstTimeToday">First visit today (but not first ever).</param>
    public sealed record WorthinessInput(
        string AppId,
        bool FirstEverSeen,
        bool FirstTimeToday,
        TransitionKind Transition,
        int DwellSeconds,
        IReadOnlyList<TrendEvent> Trends,
        bool CcpSessionRunning = false,
        bool HasRecentAchievement = false,
        int LoginStreakDays = 0);

    /// <summary>
    /// The output of one scoring pass. Carries the components as well as the total because the
    /// <c>[AWARE]</c> log line has to explain itself: a score with no breakdown is unfalsifiable, and
    /// this is the layer whose misbehaviour looks exactly like "the AI is being weird today".
    /// </summary>
    public sealed record WorthinessResult(
        string AppId,
        double Score,
        double Threshold,
        double Novelty,
        double TrendWeight,
        double DwellWeight,
        double TransitionWeight,
        double InAppBonus,
        double RepetitionPenalty,
        RarityTier Tier,
        AwarenessVerdict Verdict,
        string Reason)
    {
        /// <summary>The one-line <c>[AWARE]</c> decision record (invariant: one per scored event).</summary>
        public string LogLine => string.Create(CultureInfo.InvariantCulture,
            $"[AWARE] app={AwarenessText.SanitizeId(AppId)} score={AwarenessText.Num(Score)} thr={AwarenessText.Num(Threshold)} " +
            $"nov={AwarenessText.Num(Novelty)} trend={AwarenessText.Num(TrendWeight)} dwell={AwarenessText.Num(DwellWeight)} " +
            $"trans={AwarenessText.Num(TransitionWeight)} inapp={AwarenessText.Num(InAppBonus)} rep={AwarenessText.Num(RepetitionPenalty)} " +
            $"tier={Tier} verdict={Verdict} gate={Reason}");
    }

    /// <summary>
    /// The local, deterministic, loggable answer to "is this worth saying?" (doc 02 §4.1).
    ///
    /// <code>
    /// score = w1·novelty + w2·trendWeight + w3·dwellWeight + w4·transitionWeight
    ///         + w5·inAppBonus − r·repetitionPenalty
    /// </code>
    ///
    /// <para>Two pieces of state make the pacing work and both decay: a floating threshold that every
    /// delivered line pushes up and that falls back to the intensity baseline over about twenty minutes
    /// (the silence budget primitive, doc 02 §3.4), and a per-app repetition penalty that makes the
    /// second joke about the same app today much harder to earn than the first.</para>
    ///
    /// <para><b>No clock is read here.</b> Every method takes the instant explicitly, so the whole
    /// pacing model — including the decay curves — is exercised in milliseconds by tests instead of in
    /// twenty real minutes.</para>
    /// </summary>
    public sealed class WorthinessScorer
    {
        // ===================== weights =====================

        /// <summary>Novelty weight. The largest single term: "she noticed something new" is the feature.</summary>
        public const double NoveltyWeight = 0.40;

        /// <summary>Trend weight. Callbacks are the point of the ledger.</summary>
        public const double TrendWeightFactor = 0.30;

        /// <summary>Dwell weight — how long they have actually been there.</summary>
        public const double DwellWeightFactor = 0.08;

        /// <summary>Transition weight — waking up and leaving fullscreen are moments; a tab change is not.</summary>
        public const double TransitionWeightFactor = 0.12;

        /// <summary>In-app state weight (level-ups, running sessions, streaks).</summary>
        public const double InAppWeight = 0.10;

        /// <summary>Repetition penalty coefficient. Subtracted, so the score can be pushed to zero.</summary>
        public const double RepetitionWeight = 0.35;

        /// <summary>Below this, nothing is worth even a free bark.</summary>
        public const double BarkFloor = 0.20;

        /// <summary>How far one delivered line pushes the threshold up.</summary>
        public const double ThresholdBump = 0.15;

        /// <summary>Ceiling on the accumulated bump, so a burst cannot mute her for an hour.</summary>
        public const double MaxThresholdBump = 0.45;

        /// <summary>Half-life of the threshold bump. ~7 min puts it at an eighth after the ~20 min doc 02 §3.4 asks for.</summary>
        public static readonly TimeSpan ThresholdHalfLife = TimeSpan.FromMinutes(7);

        /// <summary>Half-life of the per-app repetition penalty. Longer: the same app twice in an hour is the annoying case.</summary>
        public static readonly TimeSpan RepetitionHalfLife = TimeSpan.FromMinutes(30);

        /// <summary>Steepness of the penalty curve. One recent line ≈ 0.50, two ≈ 0.75, three ≈ 0.88.</summary>
        private const double RepetitionSteepness = 0.7;

        private readonly Func<AwarenessIntensity> _intensity;

        private readonly Dictionary<string, DecayingCounter> _repetition =
            new(StringComparer.OrdinalIgnoreCase);

        private double _thresholdBump;
        private DateTime? _lastBumpAt;

        /// <summary>Production constructor: intensity comes from settings.</summary>
        public WorthinessScorer() : this(null) { }

        /// <param name="intensity">
        /// Injectable intensity. Headless the settings object is null and the dial would always read
        /// Chatty, which would leave the Off and Unhinged ends of the range untested.
        /// </param>
        public WorthinessScorer(Func<AwarenessIntensity>? intensity)
        {
            _intensity = intensity ?? (() => AwarenessIntensityProfile.Current);
        }

        /// <summary>The live threshold at <paramref name="at"/>: baseline plus whatever is left of the bump.</summary>
        public double CurrentThreshold(DateTime at)
        {
            double baseline = AwarenessIntensityProfile.BaselineThreshold(_intensity());
            if (double.IsPositiveInfinity(baseline)) return baseline;
            return baseline + DecayedBump(at);
        }

        /// <summary>
        /// Scores a moment. Pure: calling this twice with the same arguments returns the same result and
        /// changes nothing. Only <see cref="RegisterDelivery"/> moves the pacing state.
        /// </summary>
        public WorthinessResult Score(WorthinessInput input, DateTime at)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            double novelty = Novelty(input);
            double trend = TrendWeightOf(input.Trends);
            double dwell = DwellWeight(input.DwellSeconds);
            double transition = TransitionWeight(input.Transition);
            double inApp = InAppBonus(input);
            double penalty = RepetitionPenalty(input.AppId, at);

            double score =
                NoveltyWeight * novelty +
                TrendWeightFactor * trend +
                DwellWeightFactor * dwell +
                TransitionWeightFactor * transition +
                InAppWeight * inApp -
                RepetitionWeight * penalty;

            score = Math.Clamp(score, 0.0, 1.0);

            var intensity = _intensity();
            double threshold = CurrentThreshold(at);

            RarityTier tier;
            AwarenessVerdict verdict;
            string reason;

            if (intensity == AwarenessIntensity.Off)
            {
                tier = RarityTier.Common;
                verdict = AwarenessVerdict.Silence;
                reason = "intensity-off";
            }
            else if (score >= threshold)
            {
                bool armed = AwarenessIntensityProfile.RareEnabled(intensity) &&
                             input.Trends != null && input.Trends.Any(t => t != null && t.CarriesLedgerHistory);
                tier = armed ? RarityTier.Rare : RarityTier.Uncommon;
                verdict = AwarenessVerdict.Llm;
                reason = armed ? "trend-armed" : "over-threshold";
            }
            else if (score >= BarkFloor)
            {
                tier = RarityTier.Common;
                verdict = AwarenessVerdict.Bark;
                reason = "bark-floor";
            }
            else
            {
                tier = RarityTier.Common;
                verdict = AwarenessVerdict.Silence;
                reason = "below-floor";
            }

            var result = new WorthinessResult(input.AppId, score, threshold, novelty, trend, dwell,
                transition, inApp, penalty, tier, verdict, reason);

            App.Logger?.Information(result.LogLine);
            return result;
        }

        /// <summary>
        /// Records that a line was actually DELIVERED — raising the threshold and this app's repetition
        /// penalty.
        ///
        /// <para>Delivery, never attempt: a refused, moderated, timed-out or <c>[PASS]</c>-ed call that
        /// burned the budget would be silence with no payoff, which is the exact bug the current system
        /// ships (doc 02 §1.4, MASTER-SCOPE Train 0 item 4).</para>
        /// </summary>
        public void RegisterDelivery(string? appId, DateTime at)
        {
            _thresholdBump = Math.Min(MaxThresholdBump, DecayedBump(at) + ThresholdBump);
            _lastBumpAt = at;

            var id = AwarenessText.SanitizeId(appId);
            if (!_repetition.TryGetValue(id, out var counter)) counter = new DecayingCounter();
            counter.Points = counter.Decayed(at) + 1.0;
            counter.At = at;
            _repetition[id] = counter;
        }

        /// <summary>Clears all pacing state — used by the privacy pause/wipe paths and by tests.</summary>
        public void Reset()
        {
            _thresholdBump = 0;
            _lastBumpAt = null;
            _repetition.Clear();
        }

        /// <summary>Forgets one app's repetition history (per-app "forget" in the privacy panel).</summary>
        public void Forget(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            _repetition.Remove(AwarenessText.SanitizeId(appId));
        }

        // ===================== components =====================

        /// <summary>First time ever &gt; first today &gt; seen recently (doc 02 §4.1).</summary>
        public static double Novelty(WorthinessInput input) =>
            input.FirstEverSeen ? 1.0 : input.FirstTimeToday ? 0.6 : 0.1;

        /// <summary>
        /// The strongest trend present, with a small bonus per additional one — two trends at once
        /// ("2am, fourth visit") is a better joke than either alone, but not twice as good.
        /// </summary>
        public static double TrendWeightOf(IReadOnlyList<TrendEvent>? trends)
        {
            if (trends == null || trends.Count == 0) return 0.0;

            double best = 0.0;
            int counted = 0;
            foreach (var trend in trends)
            {
                if (trend == null) continue;
                counted++;
                best = Math.Max(best, WeightOf(trend));
            }

            if (counted == 0) return 0.0;
            return Math.Min(1.0, best + 0.05 * (counted - 1));
        }

        private static double WeightOf(TrendEvent trend) => trend.Kind switch
        {
            TrendKind.NightShift => 0.90,
            TrendKind.MediaLoop => 0.85,
            TrendKind.Backslide => 0.80,
            TrendKind.Streak => 0.70,
            TrendKind.GhostTown => 0.60,
            TrendKind.ReturnVisit => Math.Min(0.90, 0.50 + 0.10 * Math.Max(0, trend.Magnitude - ActivityLedger.ReturnVisitMinimum)),
            TrendKind.LongHaul => trend.Magnitude >= 180 ? 0.95 : trend.Magnitude >= 120 ? 0.80 : trend.Magnitude >= 60 ? 0.65 : 0.50,
            _ => 0.40
        };

        /// <summary>
        /// Zero below a minute (nobody has "been" anywhere yet), one at an hour, logarithmic between —
        /// the difference between 2 and 20 minutes matters far more than between 50 and 60.
        /// </summary>
        public static double DwellWeight(int dwellSeconds)
        {
            if (dwellSeconds < 60) return 0.0;
            if (dwellSeconds >= 3600) return 1.0;
            return Math.Log(dwellSeconds / 60.0) / Math.Log(60.0);
        }

        /// <summary>Waking up and coming out of fullscreen are moments; a tab change almost never is.</summary>
        public static double TransitionWeight(TransitionKind kind) => kind switch
        {
            TransitionKind.WakeFromIdle => 0.90,
            TransitionKind.ExitFullscreen => 0.85,
            TransitionKind.RapidCycling => 0.75,
            TransitionKind.ReturnVisit => 0.70,
            TransitionKind.NewApp => 0.60,
            TransitionKind.Milestone => 0.60,
            TransitionKind.MediaChanged => 0.40,
            TransitionKind.TabChange => 0.20,
            _ => 0.30
        };

        /// <summary>The strongest in-app hook available, not a sum — she gets one reason, not a list.</summary>
        public static double InAppBonus(WorthinessInput input)
        {
            if (input.HasRecentAchievement) return 1.0;
            if (input.CcpSessionRunning) return 0.6;
            if (input.LoginStreakDays >= 3) return 0.3;
            return 0.0;
        }

        /// <summary>How much has already been said about this app lately, 0..1.</summary>
        public double RepetitionPenalty(string? appId, DateTime at)
        {
            var id = AwarenessText.SanitizeId(appId);
            if (!_repetition.TryGetValue(id, out var counter)) return 0.0;
            double points = counter.Decayed(at);
            return points <= 0 ? 0.0 : 1.0 - Math.Exp(-RepetitionSteepness * points);
        }

        private double DecayedBump(DateTime at)
        {
            if (_thresholdBump <= 0 || _lastBumpAt == null) return 0.0;
            var elapsed = at - _lastBumpAt.Value;
            if (elapsed <= TimeSpan.Zero) return _thresholdBump;
            return _thresholdBump * Math.Pow(0.5, elapsed.TotalSeconds / ThresholdHalfLife.TotalSeconds);
        }

        private struct DecayingCounter
        {
            public double Points;
            public DateTime At;

            public double Decayed(DateTime now)
            {
                if (Points <= 0) return 0.0;
                var elapsed = now - At;
                if (elapsed <= TimeSpan.Zero) return Points;
                return Points * Math.Pow(0.5, elapsed.TotalSeconds / RepetitionHalfLife.TotalSeconds);
            }
        }
    }
}
