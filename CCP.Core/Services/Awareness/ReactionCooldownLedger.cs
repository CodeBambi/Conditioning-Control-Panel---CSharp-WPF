using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The shared cooldown state behind <see cref="IReactionArbiter"/> — pure logic, no timers, no
    /// services, no clock of its own.
    ///
    /// <para>It is a separate class from the arbiter for one reason: this is the part that must be
    /// provably right. "Zero double-reactions" is a ship criterion (MASTER-SCOPE §10), and a ship
    /// criterion that lives inside an async service wired to four other services is a ship criterion
    /// nobody can test.</para>
    ///
    /// <para>Three floors, all from doc 02 §3.4/§5.1:</para>
    /// <list type="bullet">
    /// <item><b>Global gap</b> — no two ambient lines from ANY source inside 60s (BarkService's existing
    /// <c>GlobalMinGapMs</c>, kept as the outer floor for everything non-safety).</item>
    /// <item><b>LLM gap</b> — never two LLM lines inside 90s, on top of the global gap.</item>
    /// <item><b>Per-app gap</b> — never two lines about the same app inside 10 minutes, whatever the
    /// tier, whatever the source. This is the one that kills "I see you're on Twitter~" for the
    /// fortieth time.</item>
    /// </list>
    /// <para>Plus the hourly line budget from the intensity dial, counted across every source.</para>
    /// </summary>
    public sealed class ReactionCooldownLedger
    {
        /// <summary>Outer floor between any two ambient lines, from any source.</summary>
        public static readonly TimeSpan GlobalGap = TimeSpan.FromSeconds(60);

        /// <summary>Extra floor between two LLM lines.</summary>
        public static readonly TimeSpan LlmGap = TimeSpan.FromSeconds(90);

        /// <summary>Floor between two lines about the same app, regardless of tier or source.</summary>
        public static readonly TimeSpan PerAppGap = TimeSpan.FromMinutes(10);

        /// <summary>Window the hourly line budget is counted over.</summary>
        public static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(1);

        /// <summary>
        /// Keyword triggers are user-configured, so they are exempt from the LLM gap and the budget —
        /// but not from the global gap, because two voices inside a second is the bug being fixed.
        /// </summary>
        private static bool IsUserRequested(ReactionSource source) => source == ReactionSource.Keyword;

        private readonly Func<AwarenessIntensity> _intensity;
        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _perApp = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DateTime> _recent = new();

        private DateTime? _lastAny;
        private DateTime? _lastLlm;

        public ReactionCooldownLedger(Func<AwarenessIntensity>? intensity = null)
        {
            _intensity = intensity ?? (() => AwarenessIntensityProfile.Current);
        }

        /// <summary>Lines delivered inside the trailing budget window, all sources.</summary>
        public int LinesLastHour(DateTime at)
        {
            lock (_lock)
            {
                TrimLocked(at);
                return _recent.Count;
            }
        }

        /// <summary>
        /// Whether <paramref name="source"/> may speak about <paramref name="appId"/> at
        /// <paramref name="at"/>. <paramref name="reason"/> names the gate that said no — it goes
        /// straight into the <c>[AWARE]</c> line, which is how a "she went quiet" report gets diagnosed.
        /// </summary>
        public bool CanSpeak(ReactionSource source, string? appId, DateTime at, out string reason)
        {
            lock (_lock)
            {
                TrimLocked(at);

                var intensity = _intensity();
                if (intensity == AwarenessIntensity.Off && !IsUserRequested(source))
                {
                    reason = "intensity-off";
                    return false;
                }

                if (_lastAny is { } lastAny && at - lastAny < GlobalGap)
                {
                    reason = "global-gap";
                    return false;
                }

                if (!IsUserRequested(source))
                {
                    if (source == ReactionSource.AwarenessLlm && _lastLlm is { } lastLlm && at - lastLlm < LlmGap)
                    {
                        reason = "llm-gap";
                        return false;
                    }

                    int budget = AwarenessIntensityProfile.LinesPerHour(intensity);
                    if (_recent.Count >= budget)
                    {
                        reason = "hourly-budget";
                        return false;
                    }
                }

                if (!string.IsNullOrWhiteSpace(appId))
                {
                    var id = AwarenessText.SanitizeId(appId);
                    if (_perApp.TryGetValue(id, out var lastApp) && at - lastApp < PerAppGap)
                    {
                        reason = "same-app-gap";
                        return false;
                    }
                }

                reason = "ok";
                return true;
            }
        }

        /// <summary>
        /// Records a line that was actually DELIVERED. Never called for a refusal, a timeout, a
        /// <c>[PASS]</c> or a moderated line — those cost the user silence already; charging them the
        /// cooldown too is the bug this whole rework starts with.
        /// </summary>
        public void RecordDelivery(ReactionSource source, string? appId, DateTime at)
        {
            lock (_lock)
            {
                TrimLocked(at);

                _lastAny = at;
                if (source == ReactionSource.AwarenessLlm) _lastLlm = at;
                if (!string.IsNullOrWhiteSpace(appId)) _perApp[AwarenessText.SanitizeId(appId)] = at;
                _recent.Add(at);
            }
        }

        /// <summary>
        /// Clears every cooldown. Reached from the privacy panel's wipe through
        /// <see cref="AwarenessLive.ResetPacingState"/>: the per-app map is keyed by the app ids the
        /// wipe just erased, so leaving it behind leaves an artifact of what was forgotten.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _lastAny = null;
                _lastLlm = null;
                _perApp.Clear();
                _recent.Clear();
            }
        }

        /// <summary>
        /// Drops one app's per-app cooldown. The per-app "forget this app" control's half of the
        /// erasure — the global gap and the hourly budget are about pacing, not about that app, and
        /// stay where they are.
        /// </summary>
        public void Forget(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            lock (_lock) _perApp.Remove(AwarenessText.SanitizeId(appId));
        }

        private void TrimLocked(DateTime at)
        {
            var cutoff = at - BudgetWindow;
            _recent.RemoveAll(t => t <= cutoff);

            if (_perApp.Count == 0) return;
            List<string>? stale = null;
            foreach (var pair in _perApp)
            {
                if (at - pair.Value < PerAppGap) continue;
                (stale ??= new List<string>()).Add(pair.Key);
            }
            if (stale == null) return;
            foreach (var key in stale) _perApp.Remove(key);
        }
    }
}
