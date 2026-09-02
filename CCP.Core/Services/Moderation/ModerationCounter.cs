using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Sliding-window moderation hit counter with escalation, persisted across launches.
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
    ///
    /// Persistence (P2-H8): state is hydrated from / saved to
    /// <c>%APPDATA%/ConditioningControlPanel/moderation-counter.json</c>. Without
    /// persistence the cooldown could be bypassed in 3 seconds by restarting the app
    /// (the H8 finding in C:\tmp\HOSTILE_REVIEW.md). Saves are fire-and-forget
    /// best-effort; failures don't break the counter pipeline.
    /// </summary>
    public interface IModerationCounter
    {
        void RecordHit(ProhibitedCategory category, string source);
        ModerationCounterState GetState();

        /// <summary>
        /// Hydrates in-memory state from the persisted JSON file. Safe to call once
        /// at startup. Prunes timestamps older than <see cref="ModerationCounter.WindowMinutes"/>
        /// and discards any cooldown that has already expired.
        /// </summary>
        void LoadFromDisk();

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

    /// <summary>
    /// Persisted on-disk shape. Kept stable across releases — extending fields is
    /// allowed but renaming requires a migration. ISO-8601 UTC strings (round-trip
    /// "o" format) so the file is human-readable.
    /// </summary>
    internal sealed class ModerationCounterPersistedState
    {
        [JsonProperty("hits")]
        public List<string> Hits { get; set; } = new();

        [JsonProperty("cooldownEndsAt")]
        public string? CooldownEndsAt { get; set; }
    }

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
        private readonly string _persistencePath;

        public event Action<ModerationCounterState>? WarningTriggered;
        public event Action<DateTime>? CooldownStarted;
        public event Action? CooldownEnded;

        public ModerationCounter()
        {
            // Same path computation pattern as ModerationLog — we cannot reference
            // App.UserDataPath here without a circular dependency at startup.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "ConditioningControlPanel");
            _persistencePath = Path.Combine(dir, "moderation-counter.json");
        }

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

            // Persist after every hit + every cooldown state transition. Fire-and-forget
            // — a failed write must not break the moderation pipeline.
            SaveToDiskAsync();

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
            bool cooldownEndedThisCall = false;

            ModerationCounterState state;
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
                        cooldownEndedThisCall = true;
                    }
                    else
                    {
                        cooldownActive = true;
                    }
                }

                state = new ModerationCounterState(
                    HitsInLastTenMinutes: _hits.Count,
                    WarningTriggered: _warningShown,
                    CooldownActive: cooldownActive,
                    CooldownEndsAt: endsAt);
            }

            if (cooldownEndedThisCall)
            {
                // Persist the cleared state + fire the ended event outside the lock.
                SaveToDiskAsync();
                try { CooldownEnded?.Invoke(); } catch { /* ignore */ }
            }

            return state;
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

        /// <summary>
        /// Hydrates from <c>moderation-counter.json</c>. Called once from
        /// <c>App.OnStartup</c> after the counter is constructed. Idempotent;
        /// safe if the file is missing or corrupt.
        /// </summary>
        public void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_persistencePath)) return;

                var json = File.ReadAllText(_persistencePath);
                if (string.IsNullOrWhiteSpace(json)) return;

                var persisted = JsonConvert.DeserializeObject<ModerationCounterPersistedState>(json);
                if (persisted == null) return;

                lock (_lock)
                {
                    _hits.Clear();
                    var cutoff = DateTime.UtcNow.AddMinutes(-WindowMinutes);
                    foreach (var s in persisted.Hits)
                    {
                        if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                        {
                            var utc = ts.ToUniversalTime();
                            if (utc >= cutoff) _hits.Add(utc);
                        }
                    }

                    _cooldownEndsAt = null;
                    if (!string.IsNullOrEmpty(persisted.CooldownEndsAt) &&
                        DateTime.TryParse(persisted.CooldownEndsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var endsAt))
                    {
                        var utc = endsAt.ToUniversalTime();
                        if (utc > DateTime.UtcNow) _cooldownEndsAt = utc;
                    }

                    // If hits exist at-or-above warning threshold, treat the warning
                    // latch as already shown so the user is not re-prompted on every
                    // restart. (The dialog is one-shot per threshold-cross anyway.)
                    _warningShown = _hits.Count >= WarningThreshold || _cooldownEndsAt != null;
                }
            }
            catch
            {
                // Best-effort hydration. Same pattern as ModerationLog.
            }
        }

        /// <summary>
        /// Snapshots current state under the lock then writes it on a thread-pool
        /// task. Best-effort; swallows IO errors.
        /// </summary>
        private void SaveToDiskAsync()
        {
            List<string> hits;
            string? cooldownIso;
            lock (_lock)
            {
                hits = new List<string>(_hits.Count);
                foreach (var t in _hits) hits.Add(t.ToUniversalTime().ToString("o"));
                cooldownIso = _cooldownEndsAt?.ToUniversalTime().ToString("o");
            }

            var payload = new ModerationCounterPersistedState
            {
                Hits = hits,
                CooldownEndsAt = cooldownIso,
            };
            var path = _persistencePath;

            _ = Task.Run(() =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                    File.WriteAllText(path, json);
                }
                catch
                {
                    // Best-effort, same as ModerationLog.
                }
            });
        }
    }
}
