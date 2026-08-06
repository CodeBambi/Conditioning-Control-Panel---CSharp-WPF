using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// Train 2 core's arbiter: the cooldown ledger is real and enforced; the speaking half is not built
    /// yet.
    ///
    /// <para><b>Shell status.</b> <see cref="SubmitAsync"/> applies the gates and reports what SHOULD
    /// happen, but never calls an LLM and never speaks — the arbiter package fills that in (bark-first
    /// with an LLM chaser, the &gt;8s fallback-to-bark path, <c>[PASS]</c> handling, the delivery-time
    /// staleness re-tag). Until then this returns Bark/Silence and nothing double-fires, which is the
    /// invariant that had to hold from day one.</para>
    ///
    /// <para>The gates it already owns are the ones nothing else may duplicate: a second cooldown
    /// dialect somewhere else in the codebase is exactly how the two-mouths bug happened the first
    /// time.</para>
    /// </summary>
    public sealed class ReactionArbiter : IReactionArbiter
    {
        private readonly ReactionCooldownLedger _cooldowns;
        private readonly WorthinessScorer? _scorer;
        private readonly Func<DateTime> _clock;

        public ReactionArbiter(
            ReactionCooldownLedger? cooldowns = null,
            WorthinessScorer? scorer = null,
            Func<DateTime>? localClock = null)
        {
            _cooldowns = cooldowns ?? new ReactionCooldownLedger();
            _scorer = scorer;
            _clock = localClock ?? (() => DateTime.Now);
        }

        /// <summary>The shared cooldown state. Exposed so the packages extend it rather than inventing a second one.</summary>
        public ReactionCooldownLedger Cooldowns => _cooldowns;

        /// <inheritdoc />
        public Task<ArbiterDecision> SubmitAsync(ContextFrame frame, CancellationToken cancellationToken = default)
        {
            if (frame == null) return Task.FromResult(ArbiterDecision.Silent("null-frame"));

            var now = _clock();
            var source = frame.Tier == RarityTier.Common ? ReactionSource.Bark : ReactionSource.AwarenessLlm;

            if (!_cooldowns.CanSpeak(source, frame.AppId, now, out var reason))
            {
                return Task.FromResult(ArbiterDecision.Silent(reason));
            }

            // TODO(arbiter package): bark-first with an LLM chaser for Uncommon+, an >8s timeout that
            // falls back to the bark exactly once, [PASS] honoured with a budget refund, and the
            // delivery-time staleness re-tag from doc 02 §4.3. Until then the only thing that may be
            // promised is the free, instant, always-available tier.
            return Task.FromResult(new ArbiterDecision(AwarenessVerdict.Bark, RarityTier.Common, "shell-bark-only"));
        }

        /// <inheritdoc />
        public void RecordExternalLine(ReactionSource source, string? appId = null)
        {
            var now = _clock();
            _cooldowns.RecordDelivery(source, appId, now);
            if (source == ReactionSource.AwarenessLlm) _scorer?.RegisterDelivery(appId, now);
        }

        /// <inheritdoc />
        public bool CanSpeak(ReactionSource source, string? appId = null) =>
            _cooldowns.CanSpeak(source, appId, _clock(), out _);
    }
}
