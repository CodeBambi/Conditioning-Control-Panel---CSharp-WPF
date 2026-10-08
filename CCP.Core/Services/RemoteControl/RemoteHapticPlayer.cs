using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>
    /// One remote haptic, as the controller sent it: a row of levels played at a fixed step
    /// (<c>haptic_pattern</c>) or one level held for a while (<c>haptic_level</c>). Levels are
    /// 0..100 as on the wire. The server validates these shapes too; the desktop clamps again
    /// because the queue is untrusted input by the time it reaches us.
    /// </summary>
    public sealed class RemoteHapticPlan
    {
        public const int MaxLevels = 240, MinStepMs = 50, MaxStepMs = 500, MaxPassMs = 60_000, MaxNameLength = 24;
        public const int MinHoldMs = 100, MaxHoldMs = 3000;

        public IReadOnlyList<int> Levels { get; }
        public int StepMs { get; }
        public bool Loop { get; }
        public string? Name { get; }
        /// <summary>True for <c>haptic_level</c> (hold-to-buzz), false for a pattern.</summary>
        public bool IsHold { get; }
        public int PassMs => Levels.Count * StepMs;

        public RemoteHapticPlan(IReadOnlyList<int> levels, int stepMs, bool loop, string? name, bool isHold)
        {
            Levels = levels;
            StepMs = stepMs;
            Loop = loop;
            Name = name;
            IsHold = isHold;
        }

        /// <summary>Reads <c>{ levels, step_ms, loop, name? }</c>. Null (with a short reason) when
        /// the levels are missing or not numbers; out-of-range values are clamped, an overlong
        /// pattern is cut at 240 steps and at 60 s.</summary>
        public static RemoteHapticPlan? FromPattern(JObject? p, out string? reason)
        {
            reason = null;
            if (p?["levels"] is not JArray arr || arr.Count == 0) { reason = "no pattern"; return null; }

            var step = Math.Clamp(ReadInt(p["step_ms"]) ?? 100, MinStepMs, MaxStepMs);
            var max = Math.Min(MaxLevels, MaxPassMs / step);
            var levels = new List<int>(Math.Min(arr.Count, max));
            foreach (var t in arr)
            {
                if (levels.Count >= max) break;
                var v = ReadInt(t);
                if (v == null) { reason = "bad pattern"; return null; }
                levels.Add(Math.Clamp(v.Value, 0, 100));
            }

            string? name = p["name"]?.Type == JTokenType.String ? p["name"]!.ToString().Trim() : null;
            if (string.IsNullOrEmpty(name)) name = null;
            else if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);

            var loop = p["loop"]?.Type == JTokenType.Boolean && p["loop"]!.Value<bool>();
            return new RemoteHapticPlan(levels, step, loop, name, isHold: false);
        }

        /// <summary>Reads <c>{ level, ms }</c>: one level held for <c>ms</c> (clamped 100..3000).</summary>
        public static RemoteHapticPlan? FromLevel(JObject? p, out string? reason)
        {
            reason = null;
            var level = ReadInt(p?["level"]);
            if (level == null) { reason = "no level"; return null; }
            var ms = Math.Clamp(ReadInt(p?["ms"]) ?? 1500, MinHoldMs, MaxHoldMs);
            return new RemoteHapticPlan(new[] { Math.Clamp(level.Value, 0, 100) }, ms, loop: false, name: null, isHold: true);
        }

        private static int? ReadInt(JToken? t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Integer) return (int)Math.Clamp(t.Value<long>(), int.MinValue, int.MaxValue);
            if (t.Type == JTokenType.Float)
            {
                var d = t.Value<double>();
                if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                return (int)Math.Round(Math.Clamp(d, int.MinValue, int.MaxValue));
            }
            if (t.Type == JTokenType.String
                && int.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
            return null;
        }
    }

    /// <summary>One flat stretch of buzz, at an absolute time on <see cref="Environment.TickCount64"/>.
    /// Intensity is 0..1, already multiplied by the Easy factor.</summary>
    public readonly record struct RemoteHapticRun(long StartMs, int DurationMs, double Intensity, int Priority);

    /// <summary>What one <see cref="RemoteHapticPlayer.Tick"/> asks the driver to do.</summary>
    public readonly record struct RemoteHapticTick(IReadOnlyList<RemoteHapticRun> Queue, bool Ended, string? EndReason)
    {
        public static readonly RemoteHapticTick Idle = new(Array.Empty<RemoteHapticRun>(), false, null);
    }

    /// <summary>
    /// The timing core of remote haptics, with no clock, no WPF and no toy in it. The driver
    /// (<see cref="RemoteHapticDriver"/>) feeds it the time and hands what comes back to the
    /// haptic mixer as transient pulses.
    ///
    /// A pass is rendered as runs (consecutive equal levels merged, silences skipped) and the
    /// next pass of a loop is queued <see cref="LookaheadMs"/> before the current one ends, so a
    /// loop has no seam from timer jitter. Neighbouring runs alternate between two mixer
    /// priorities: equal priorities SUM in the mixer and different ones take the max, so an
    /// edge between two runs (or a refresh landing over a live run) never doubles the level.
    ///
    /// Safety: a looping pattern ends by itself <see cref="LoopIdleCapMs"/> after the last
    /// controller command (<see cref="NoteCommand"/>).
    /// </summary>
    public sealed class RemoteHapticPlayer
    {
        public const long LoopIdleCapMs = 10 * 60 * 1000;
        public const int LookaheadMs = 250;
        public const int PriorityA = 2, PriorityB = 3;

        private RemoteHapticPlan? _plan;
        private long _passStart;
        private int _nextPass;
        private long _lastCommandMs;
        private int _lastPriority = PriorityB;
        private readonly List<RemoteHapticRun> _queued = new();

        public RemoteHapticPlan? Plan => _plan;
        public bool IsPlaying => _plan != null;

        /// <summary>Replaces whatever plays with <paramref name="plan"/>, starting now. Returns the
        /// runs of the first pass. The caller cancels what it had queued for the old plan.</summary>
        public IReadOnlyList<RemoteHapticRun> Start(RemoteHapticPlan plan, long nowMs, double scale)
        {
            // The first new run must not share a priority with the run playing right now: the
            // driver queues the new pulses before it cancels the old ones.
            var live = PriorityAt(nowMs);
            if (live != null) _lastPriority = live.Value;
            _queued.Clear();
            _plan = plan;
            _passStart = nowMs;
            _nextPass = 0;
            _lastCommandMs = nowMs;
            return QueuePass(scale);
        }

        /// <summary>Any controller command resets the loop's idle cap.</summary>
        public void NoteCommand(long nowMs) => _lastCommandMs = nowMs;

        public void Stop()
        {
            _plan = null;
            _queued.Clear();
        }

        public RemoteHapticTick Tick(long nowMs, double scale)
        {
            var plan = _plan;
            if (plan == null) return RemoteHapticTick.Idle;
            _queued.RemoveAll(r => r.StartMs + r.DurationMs <= nowMs);

            if (!plan.Loop)
            {
                if (nowMs >= _passStart + plan.PassMs) { Stop(); return new(Array.Empty<RemoteHapticRun>(), true, "done"); }
                return RemoteHapticTick.Idle;
            }

            if (nowMs - _lastCommandMs >= LoopIdleCapMs) { Stop(); return new(Array.Empty<RemoteHapticRun>(), true, "idle"); }

            var nextStart = _passStart + (long)_nextPass * plan.PassMs;
            if (nowMs < nextStart - LookaheadMs) return RemoteHapticTick.Idle;
            // Woke up late (a starved timer, a sleeping PC): start the next pass now rather than
            // firing a whole pass's runs at once.
            if (nowMs > nextStart) _passStart = nowMs - (long)_nextPass * plan.PassMs;
            return new(QueuePass(scale), false, null);
        }

        /// <summary>The commanded level (0..100, before the Easy factor) at <paramref name="nowMs"/>.</summary>
        public int LevelAt(long nowMs)
        {
            var plan = _plan;
            if (plan == null || plan.PassMs <= 0 || nowMs < _passStart) return 0;
            var elapsed = nowMs - _passStart;
            if (plan.Loop) elapsed %= plan.PassMs;
            else if (elapsed >= plan.PassMs) return 0;
            var i = (int)(elapsed / plan.StepMs);
            return plan.Levels[Math.Clamp(i, 0, plan.Levels.Count - 1)];
        }

        private int? PriorityAt(long nowMs)
        {
            foreach (var r in _queued)
                if (r.StartMs <= nowMs && nowMs < r.StartMs + r.DurationMs) return r.Priority;
            return null;
        }

        private IReadOnlyList<RemoteHapticRun> QueuePass(double scale)
        {
            var plan = _plan!;
            var k = Math.Clamp(scale, 0, 1);
            var start = _passStart + (long)_nextPass * plan.PassMs;
            _nextPass++;

            var runs = new List<RemoteHapticRun>();
            var levels = plan.Levels;
            var i = 0;
            while (i < levels.Count)
            {
                var j = i;
                while (j + 1 < levels.Count && levels[j + 1] == levels[i]) j++;
                if (levels[i] > 0)
                {
                    _lastPriority = _lastPriority == PriorityA ? PriorityB : PriorityA;
                    runs.Add(new RemoteHapticRun(start + (long)i * plan.StepMs, (j - i + 1) * plan.StepMs,
                        levels[i] / 100.0 * k, _lastPriority));
                }
                i = j + 1;
            }
            _queued.AddRange(runs);
            return runs;
        }
    }
}
