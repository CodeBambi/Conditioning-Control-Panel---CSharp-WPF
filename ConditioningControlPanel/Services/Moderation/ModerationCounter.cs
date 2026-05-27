using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Per-launch moderation hit counter with sliding-window escalation.
    ///
    /// Flow:
    ///   * 1-2 hits within <see cref="WindowMinutes"/> -&gt; silent (log only).
    ///   * <see cref="WarningThreshold"/> hits -&gt; raise one-shot
    ///     <see cref="WarningTriggered"/> -&gt; UI shows ContentPolicyWarningDialog.
    ///   * <see cref="CooldownThreshold"/> hits -&gt; engage
    ///     <see cref="ModerationCounterState.CooldownActive"/> for
    ///     <see cref="CooldownMinutes"/>. Chat input is disabled + countdown shown.
    ///   * When the cooldown expires, the window is reset (user starts fresh, not
    ///     stuck at the threshold).
    ///
    /// Window is sliding: we keep timestamps in a list and prune entries older
    /// than <see cref="WindowMinutes"/> on every read. Lock-protected, thread-safe.
    /// </summary>
    public interface IModerationCounter
    {
        void RecordHit(ProhibitedCategory category, string source);
        ModerationCounterState GetState();

        /// <summary>
        /// Raised on the UI dispatcher when the counter crosses
        /// <see cref="ModerationCounter.WarningThreshold"/> (once per threshold-cross,
        /// not on every additional hit). Consumer should show
        /// <c>ContentPolicyWarningDialog</c>.
        /// </summary>
        event Action<ModerationCounterState>? WarningTriggered;

        /// <summary>
        /// Raised when cooldown begins. Carries the cooldown end time.
        /// </summary>
        event Action<DateTime>? CooldownStarted;

        /// <summary>
        /// Raised when cooldown ends naturally (timer or natural reset).
        /// </summary>
        event Action? CooldownEnded;
    }

    public record ModerationCounterState(
        int HitsInLastTenMinutes,
        bool WarningTriggered,
        bool CooldownActive,
        DateTime? CooldownEndsAt);

    public sealed class ModerationCounter : IModerationCounter
    {
        public const int WarningThreshold = 3;
        public const int CooldownThreshold = 5;
        public const int WindowMinutes = 10;
        public const int CooldownMinutes = 5;

        private readonly object _lock = new();
        private readonly List<DateTime> _hits = new();
        private bool _warningShown;
        private DateTime? _cooldownEndsAt;

        public event Action<ModerationCounterState>? WarningTriggered;
        public event Action<DateTime>? CooldownStarted;
        public event Action? CooldownEnded;

        public void RecordHit(ProhibitedCategory category, string source)
        {
            bool fireWarning = false;
            bool fireCooldown = false;
            DateTime cooldownEnd = default;

            lock (_lock)
            {
                // If cooldown is currently active, ignore further hits — they should
                // not stack into a longer cooldown. Just log via the existing
                // ModerationLog pipeline (which the caller already does separately).
                PruneExpired_NoLock();

                _hits.Add(DateTime.UtcNow);
                var count = _hits.Count;

                // Cooldown threshold (5 hits): engage cooldown if not already active.
                if (count >= CooldownThreshold && _cooldownEndsAt == null)
                {
                    cooldownEnd = DateTime.UtcNow.AddMinutes(CooldownMinutes);
                    _cooldownEndsAt = cooldownEnd;
                    fireCooldown = true;
                }
                // Warning threshold (3 hits): fire once per threshold-cross.
                else if (count >= WarningThreshold && !_warningShown)
                {
                    _warningShown = true;
                    fireWarning = true;
                }
            }

            try
            {
                if (fireCooldown)
                {
                    CooldownStarted?.Invoke(cooldownEnd);
                }
                if (fireWarning)
                {
                    WarningTriggered?.Invoke(GetState());
                }
            }
            catch
            {
                // Listeners must not break the counter pipeline.
            }
        }

        public ModerationCounterState GetState()
        {
            lock (_lock)
            {
                PruneExpired_NoLock();
                bool cooldownActive = false;
                DateTime? endsAt = _cooldownEndsAt;
                if (_cooldownEndsAt.HasValue)
                {
                    if (DateTime.UtcNow >= _cooldownEndsAt.Value)
                    {
                        // Cooldown just ended — reset window so user starts fresh.
                        _cooldownEndsAt = null;
                        _hits.Clear();
                        _warningShown = false;
                        endsAt = null;
                        // Fire the ended event outside the lock.
                        try { CooldownEnded?.Invoke(); } catch { /* ignore */ }
                    }
                    else
                    {
                        cooldownActive = true;
                    }
                }

                return new ModerationCounterState(
                    HitsInLastTenMinutes: _hits.Count,
                    WarningTriggered: _warningShown,
                    CooldownActive: cooldownActive,
                    CooldownEndsAt: endsAt);
            }
        }

        private void PruneExpired_NoLock()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-WindowMinutes);
            // Walk from front; list stays sorted by insertion time.
            int firstKeep = 0;
            for (int i = 0; i < _hits.Count; i++)
            {
                if (_hits[i] >= cutoff) { firstKeep = i; break; }
                firstKeep = i + 1;
            }
            if (firstKeep > 0) _hits.RemoveRange(0, firstKeep);

            // When the window empties out AND no cooldown is active, reset the
            // warning-shown latch so the user can be warned again on a future
            // threshold-cross.
            if (_hits.Count == 0 && _cooldownEndsAt == null)
            {
                _warningShown = false;
            }
        }
    }
}
