using System;
using System.Collections.Generic;

// ============================================================================
// GOON GAME — Phase B. Score ticks, attention/interaction multiplier, charge
// economy, and the receiver-side payload rate limiter.
//
// Formula (plan §Scoring): 1 pt/s x (1 + DraftRiskStep x riskTier) x attention mult.
//   Cam   : rolling attention % -> AttentionMultMin .. AttentionMultMax
//   NoCam : 1.0 flat; a failed interaction check drops it to NoCamFailedCheckMult
//           for NoCamFailedCheckPenaltyMs.
//
// Attention % ARRIVES as an input (ReportAttention). This class never touches
// GazeFocusService — Phase E wires the gaze feed and the no-cam check prompts.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>The documented PayloadReceiptMsg.Status values. Keep receipts inside this set.</summary>
    public static class GoonReceiptStatus
    {
        public const string Accepted = "accepted";
        public const string RejectedRate = "rejected_rate";
        public const string RejectedFiltered = "rejected_filtered";
        public const string Completed = "completed";
        public const string Survived = "survived";
    }

    /// <summary>Immutable read-out of the scoring state, for HUD binding / recap / ticks.</summary>
    public sealed class GoonScoreSnapshot
    {
        public int Score { get; init; }
        public double ScoreExact { get; init; }
        public int Charges { get; init; }
        public int AttentionPct { get; init; }
        public double AttentionMultiplier { get; init; }
        public double RiskMultiplier { get; init; }
        public int RiskTier { get; init; }
        public GoonAttentionMode Mode { get; init; }
        public int FailedChecks { get; init; }
        public int ChargesEarned { get; init; }
        public int ChargesSpent { get; init; }
    }

    /// <summary>
    /// One player's own score + charge meter. Ticked once per second by
    /// GoonMatchService's live timer. Pure state — no timers, no services.
    /// </summary>
    public sealed class GoonScoring
    {
        /// <summary>Rolling window over which the cam attention % is averaged.</summary>
        private const int AttentionWindowMs = 30000;

        /// <summary>Cam mode: below this rolling attention the charge trickle pauses (no reset).</summary>
        private const double CleanAttentionFloorPct = 35.0;

        private readonly List<(long AtMs, double Pct)> _attentionSamples = new();

        private double _scoreExact;
        private double _elapsedMs;
        private double _cleanMs;
        private long _failedCheckUntilMs = long.MinValue;

        public GoonScoring(GoonAttentionMode mode, int matchRiskTier)
        {
            Mode = mode;
            RiskTier = Math.Clamp(matchRiskTier, 0, GoonDraft.MaxMatchRiskTier);
        }

        public GoonAttentionMode Mode { get; private set; }
        public int RiskTier { get; private set; }

        public double ScoreExact => _scoreExact;
        public int Score => (int)Math.Floor(_scoreExact);
        public int Charges { get; private set; }
        public int ChargesEarned { get; private set; }
        public int ChargesSpent { get; private set; }
        public int FailedChecks { get; private set; }

        /// <summary>Rolling attention 0..100. NoCam reports "check health" instead (100 clean, 60 penalised).</summary>
        public double AttentionPct { get; private set; } = 100.0;

        public double RiskMultiplier => GoonDraft.RiskMultiplier(RiskTier);

        public event EventHandler<int>? ChargesChanged;

        /// <summary>Late binding for the consent/draft outcome (the draft can change until it locks).</summary>
        public void Configure(GoonAttentionMode mode, int matchRiskTier)
        {
            Mode = mode;
            RiskTier = Math.Clamp(matchRiskTier, 0, GoonDraft.MaxMatchRiskTier);
        }

        /// <summary>
        /// Phase E feeds the cam attention percentage here (0..100). Ignored in NoCam mode —
        /// no-cam players are graded by <see cref="ReportInteractionCheck"/> instead.
        /// </summary>
        public void ReportAttention(double pct)
        {
            if (Mode != GoonAttentionMode.Cam) return;
            pct = Math.Clamp(pct, 0.0, 100.0);
            _attentionSamples.Add(((long)_elapsedMs, pct));
            TrimAttentionWindow();
            AttentionPct = RollingAttention();
        }

        /// <summary>NoCam mode: result of a periodic interaction check. A failure penalises for 60 s.</summary>
        public void ReportInteractionCheck(bool passed)
        {
            if (Mode != GoonAttentionMode.NoCam) return;
            if (passed) return;

            FailedChecks++;
            _failedCheckUntilMs = (long)_elapsedMs + GoonConsts.NoCamFailedCheckPenaltyMs;
            _cleanMs = 0;   // "clean" streak broken -> charge trickle restarts
        }

        /// <summary>Current attention/interaction multiplier applied to the per-second score.</summary>
        public double AttentionMultiplier
        {
            get
            {
                if (Mode == GoonAttentionMode.NoCam)
                {
                    return _elapsedMs < _failedCheckUntilMs ? GoonConsts.NoCamFailedCheckMult : 1.0;
                }

                double t = Math.Clamp(AttentionPct / 100.0, 0.0, 1.0);
                return GoonConsts.AttentionMultMin + (GoonConsts.AttentionMultMax - GoonConsts.AttentionMultMin) * t;
            }
        }

        /// <summary>One score tick. Called once per second from the match engine's DispatcherTimer.</summary>
        public void Tick(double seconds)
        {
            if (seconds <= 0) return;
            _elapsedMs += seconds * 1000.0;

            _scoreExact += seconds * RiskMultiplier * AttentionMultiplier;

            // NoCam health readout mirrors the penalty so the opponent panel shows something honest.
            if (Mode == GoonAttentionMode.NoCam)
                AttentionPct = _elapsedMs < _failedCheckUntilMs ? 60.0 : 100.0;
            else
                TrimAttentionWindow();

            // Charge trickle: +1 per clean ChargeTrickleMs.
            bool clean = Mode == GoonAttentionMode.NoCam
                ? _elapsedMs >= _failedCheckUntilMs
                : AttentionPct >= CleanAttentionFloorPct;

            if (clean)
            {
                _cleanMs += seconds * 1000.0;
                if (_cleanMs >= GoonConsts.ChargeTrickleMs)
                {
                    _cleanMs -= GoonConsts.ChargeTrickleMs;
                    AddCharge("trickle");
                }
            }
        }

        /// <summary>+1 charge for taking an incoming payload all the way to its end.</summary>
        public void AwardPayloadEndured() => AddCharge("payload endured");

        /// <summary>+1 charge for winning a sudden-death round or a mini event.</summary>
        public void AwardEventWon() => AddCharge("event won");

        /// <summary>Spends the cost of a payload kind. False (with cost = required) when short.</summary>
        public bool TrySpend(GoonPayloadKind kind, out int cost)
        {
            cost = GoonConsts.CostOf(kind);
            if (cost <= 0 || cost == int.MaxValue) return false;
            if (Charges < cost) return false;

            Charges -= cost;
            ChargesSpent += cost;
            ChargesChanged?.Invoke(this, Charges);
            return true;
        }

        /// <summary>Refund after a receiver-side rejection (rate/filter). Never exceeds the cap.</summary>
        public void Refund(int cost)
        {
            if (cost <= 0) return;
            ChargesSpent = Math.Max(0, ChargesSpent - cost);
            Charges = Math.Min(GoonConsts.ChargeCap, Charges + cost);
            ChargesChanged?.Invoke(this, Charges);
        }

        public GoonScoreSnapshot Snapshot() => new()
        {
            Score = Score,
            ScoreExact = _scoreExact,
            Charges = Charges,
            AttentionPct = (int)Math.Round(AttentionPct),
            AttentionMultiplier = AttentionMultiplier,
            RiskMultiplier = RiskMultiplier,
            RiskTier = RiskTier,
            Mode = Mode,
            FailedChecks = FailedChecks,
            ChargesEarned = ChargesEarned,
            ChargesSpent = ChargesSpent,
        };

        public void Reset()
        {
            _attentionSamples.Clear();
            _scoreExact = 0;
            _elapsedMs = 0;
            _cleanMs = 0;
            _failedCheckUntilMs = long.MinValue;
            Charges = 0;
            ChargesEarned = 0;
            ChargesSpent = 0;
            FailedChecks = 0;
            AttentionPct = 100.0;
            ChargesChanged?.Invoke(this, Charges);
        }

        private void AddCharge(string reason)
        {
            if (Charges >= GoonConsts.ChargeCap) return;
            Charges++;
            ChargesEarned++;
            App.Logger?.Debug("[GG] charge +1 ({Reason}) -> {Charges}", reason, Charges);
            ChargesChanged?.Invoke(this, Charges);
        }

        private void TrimAttentionWindow()
        {
            long cutoff = (long)_elapsedMs - AttentionWindowMs;
            int drop = 0;
            while (drop < _attentionSamples.Count && _attentionSamples[drop].AtMs < cutoff) drop++;
            if (drop > 0) _attentionSamples.RemoveRange(0, drop);
        }

        private double RollingAttention()
        {
            if (_attentionSamples.Count == 0) return AttentionPct;
            double sum = 0;
            foreach (var s in _attentionSamples) sum += s.Pct;
            return sum / _attentionSamples.Count;
        }
    }

    /// <summary>
    /// Receiver-side payload admission: PayloadMinGapMs steady state with a burst of
    /// PayloadBurst. Token bucket on a monotonic local clock — enforced regardless of
    /// what the wire claims (plan §safety rails). The sender runs one too, so a
    /// well-behaved client never gets rejected.
    /// </summary>
    public sealed class GoonPayloadRateLimiter
    {
        private readonly int _minGapMs;
        private readonly int _burst;
        private double _tokens;
        private long _lastRefillMs;

        public GoonPayloadRateLimiter(int minGapMs = GoonConsts.PayloadMinGapMs, int burst = GoonConsts.PayloadBurst)
        {
            _minGapMs = Math.Max(1000, minGapMs);
            _burst = Math.Max(1, burst);
            _tokens = _burst;
            _lastRefillMs = long.MinValue;
        }

        public double Tokens => _tokens;

        public void Reset()
        {
            _tokens = _burst;
            _lastRefillMs = long.MinValue;
        }

        /// <summary>Consumes one token if available. <paramref name="nowLocalMs"/> must be monotonic.</summary>
        public bool TryAdmit(long nowLocalMs)
        {
            Refill(nowLocalMs);
            if (_tokens < 1.0) return false;
            _tokens -= 1.0;
            return true;
        }

        /// <summary>Milliseconds until the next token, for UI ("next payload in ...").</summary>
        public int MsUntilNextToken(long nowLocalMs)
        {
            Refill(nowLocalMs);
            if (_tokens >= 1.0) return 0;
            return (int)Math.Ceiling((1.0 - _tokens) * _minGapMs);
        }

        private void Refill(long nowLocalMs)
        {
            if (_lastRefillMs == long.MinValue)
            {
                _lastRefillMs = nowLocalMs;
                return;
            }
            long delta = nowLocalMs - _lastRefillMs;
            if (delta <= 0) return;
            _lastRefillMs = nowLocalMs;
            _tokens = Math.Min(_burst, _tokens + (double)delta / _minGapMs);
        }
    }
}
