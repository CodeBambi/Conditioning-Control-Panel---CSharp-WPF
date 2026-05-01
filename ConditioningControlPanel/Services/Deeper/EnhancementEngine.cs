using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Runtime brain for a loaded Enhancement. Compiles haptic_tracks +
    /// time_reached rules into one sorted timeline; reacts to webcam events
    /// for gaze/blink/mouth/attention rules; tracks region transitions for
    /// region_entered / region_exited; enforces per-rule cooldowns.
    ///
    /// Lifecycle: eager construct (cheap, no subscriptions), call Start to
    /// bind the time source + webcam subscriptions, Stop to release. Safe to
    /// Start/Stop repeatedly.
    /// </summary>
    public sealed class EnhancementEngine : IDisposable
    {
        private readonly Enhancement _enhancement;
        private readonly IPlaybackTimeSource _source;
        private readonly IActionDispatcher _dispatcher;
        private readonly Services.WebcamTrackingService? _webcam;

        private List<TimelineEntry> _timeline = new();
        private int _lastFiredIndex = -1;
        private double _lastTickTime = -1;
        private string? _currentRegionId;

        // Per-rule last-fired timestamps (DateTime.UtcNow ticks) for cooldown enforcement.
        private readonly Dictionary<EnhancementRule, DateTime> _ruleLastFired = new();

        // Gaze dwell state — keyed by rule so target/avoid each get their own clock.
        private readonly Dictionary<EnhancementRule, DateTime> _gazeDwellSince = new();

        // Attention-lost dwell — single clock since attention_lost has no per-rule state.
        private DateTime? _faceLostSince;

        private bool _running;
        private bool _disposed;

        public bool IsRunning => _running;
        public Enhancement Enhancement => _enhancement;

        public EnhancementEngine(
            Enhancement enhancement,
            IPlaybackTimeSource source,
            IActionDispatcher dispatcher,
            Services.WebcamTrackingService? webcam = null)
        {
            _enhancement = enhancement ?? throw new ArgumentNullException(nameof(enhancement));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _webcam = webcam;
        }

        // -- Compile -----------------------------------------------------------

        private void Compile()
        {
            var entries = new List<TimelineEntry>();

            // Haptic events → synthetic TriggerHapticAction at event start time.
            foreach (var track in _enhancement.HapticTracks)
            {
                if (track?.Events == null) continue;
                foreach (var ev in track.Events)
                {
                    if (ev == null) continue;
                    var synthetic = new TriggerHapticAction
                    {
                        PatternName = ev.PatternName,
                        CustomPattern = ev.CustomPattern,
                        Intensity = ev.Intensity,
                        DurationMs = (int)Math.Max(50, ev.Duration * 1000)
                    };
                    entries.Add(new TimelineEntry(ev.Start, synthetic, ownerRule: null));
                }
            }

            // time_reached rules.
            foreach (var rule in _enhancement.Rules)
            {
                if (rule?.Trigger is TimeReachedTrigger tr && rule.Enabled)
                    entries.Add(new TimelineEntry(tr.Time, rule.Action, ownerRule: rule));
            }

            entries.Sort((a, b) => a.Time.CompareTo(b.Time));
            _timeline = entries;
            _lastFiredIndex = -1;
        }

        // -- Lifecycle ---------------------------------------------------------

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EnhancementEngine));
            if (_running) return;

            Compile();
            _ruleLastFired.Clear();
            _gazeDwellSince.Clear();
            _faceLostSince = null;

            // Prime the cursor to the current playback position so events that
            // already passed before Start don't fire in a burst on the first tick.
            var t0 = _source.GetCurrentTimeSeconds();
            RewindCursor(t0);
            _lastTickTime = t0;
            _currentRegionId = FindRegionAt(t0);

            _source.PlaybackTimeChanged += OnPlaybackTime;

            // Only subscribe to webcam events that an enabled rule actually needs.
            if (_webcam != null)
            {
                if (HasActiveTrigger<BlinkDetectedTrigger>())
                    _webcam.OnBlink += OnBlink;
                if (HasActiveTrigger<MouthOpenTrigger>())
                    _webcam.OnMouthOpen += OnMouthOpen;
                if (HasActiveTrigger<GazeTargetTrigger>() || HasActiveTrigger<GazeAvoidTrigger>())
                    _webcam.OnGazeMove += OnGazeMove;
                if (HasActiveTrigger<AttentionLostTrigger>())
                {
                    _webcam.OnFaceLost += OnFaceLost;
                    _webcam.OnFaceFound += OnFaceFound;
                }
            }

            _running = true;
            App.Logger?.Information("EnhancementEngine started: {Name} ({Tl} timeline entries, {Rules} rules)",
                _enhancement.Metadata?.Name, _timeline.Count, _enhancement.Rules.Count);
        }

        public void Stop()
        {
            if (!_running) return;
            _source.PlaybackTimeChanged -= OnPlaybackTime;

            if (_webcam != null)
            {
                _webcam.OnBlink -= OnBlink;
                _webcam.OnMouthOpen -= OnMouthOpen;
                _webcam.OnGazeMove -= OnGazeMove;
                _webcam.OnFaceLost -= OnFaceLost;
                _webcam.OnFaceFound -= OnFaceFound;
            }

            _running = false;
            App.Logger?.Information("EnhancementEngine stopped: {Name}", _enhancement.Metadata?.Name);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }

        // -- Tick (driven by IPlaybackTimeSource.PlaybackTimeChanged) ----------

        private void OnPlaybackTime(double t)
        {
            if (!_running) return;
            try
            {
                // Detect seek-back: rewind the cursor to the highest entry still
                // strictly before t, so forward-progress fires re-arm correctly.
                if (_lastTickTime >= 0 && t + 0.05 < _lastTickTime)
                    RewindCursor(t);

                // Region transition (computed before firing so region_entered
                // rules see the new region as "current").
                var newRegionId = FindRegionAt(t);
                if (newRegionId != _currentRegionId)
                {
                    var prev = _currentRegionId;
                    _currentRegionId = newRegionId;
                    if (prev != null)
                        FireRegionRules<RegionExitedTrigger>(prev, t, tr => tr.RegionId);
                    if (newRegionId != null)
                        FireRegionRules<RegionEnteredTrigger>(newRegionId, t, tr => tr.RegionId);
                }

                // Fire any sorted-timeline entries whose time has been reached.
                while (_lastFiredIndex + 1 < _timeline.Count
                       && _timeline[_lastFiredIndex + 1].Time <= t)
                {
                    _lastFiredIndex++;
                    var entry = _timeline[_lastFiredIndex];
                    if (entry.OwnerRule != null && !PassesRuleGate(entry.OwnerRule, t)) continue;
                    DispatchSafely(entry.Action, t, entry.OwnerRule);
                }

                _lastTickTime = t;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementEngine tick error: {Error}", ex.Message);
            }
        }

        private void RewindCursor(double t)
        {
            int idx = -1;
            for (int i = 0; i < _timeline.Count; i++)
            {
                if (_timeline[i].Time < t) idx = i;
                else break;
            }
            _lastFiredIndex = idx;
        }

        private string? FindRegionAt(double t)
        {
            // Inclusive-start, exclusive-end so adjacent regions don't double-fire.
            // Validator guarantees no overlap so first-match wins.
            foreach (var r in _enhancement.Regions)
            {
                if (t >= r.Start && t < r.End) return r.Id;
            }
            return null;
        }

        // -- Editor preview injection ------------------------------------------
        // Public entry points that mirror the webcam handlers so editor preview
        // can drive the engine from keyboard / mouse without a real camera.
        // Safe to call from the UI thread on a stopped engine (no-op).

        public void InjectBlink() => OnBlink();
        public void InjectMouthOpen() => OnMouthOpen();
        public void InjectGaze(System.Windows.Point screenPoint) => OnGazeMove(screenPoint);

        /// <summary>
        /// Simulate an attention-lost gap of the given duration. Mirrors the
        /// face-lost → face-found pattern (rules fire on "found" if the gap
        /// satisfied their MinDurationMs).
        /// </summary>
        public void InjectAttentionLost(int gapMs)
        {
            if (!_running) return;
            _faceLostSince = DateTime.UtcNow.AddMilliseconds(-Math.Max(0, gapMs));
            OnFaceFound();
        }

        // -- Webcam handlers ---------------------------------------------------

        private void OnBlink()
        {
            if (!_running) return;
            FireWebcamRules<BlinkDetectedTrigger>(_ => true);
        }

        private void OnMouthOpen()
        {
            if (!_running) return;
            FireWebcamRules<MouthOpenTrigger>(_ => true);
        }

        private void OnGazeMove(System.Windows.Point screenPoint)
        {
            if (!_running) return;
            var videoRect = _source.GetVideoRect();
            if (videoRect.IsEmpty) return;

            var now = DateTime.UtcNow;
            foreach (var rule in _enhancement.Rules)
            {
                if (!rule.Enabled) continue;
                if (rule.Trigger is GazeTargetTrigger gt)
                {
                    var hit = HitsRect(screenPoint, videoRect, gt.Rect);
                    EvaluateGazeDwell(rule, hit, gt.MinDwellMs, now);
                }
                else if (rule.Trigger is GazeAvoidTrigger ga)
                {
                    var inRect = HitsRect(screenPoint, videoRect, ga.Rect);
                    // "Avoid" fires when the user keeps gaze OUTSIDE the rect for the dwell.
                    EvaluateGazeDwell(rule, !inRect, ga.MinDwellMs, now);
                }
            }
        }

        private void OnFaceLost()
        {
            if (!_running) return;
            _faceLostSince = DateTime.UtcNow;
        }

        private void OnFaceFound()
        {
            if (!_running) return;
            // If the face was lost long enough to satisfy any rule, fire on return.
            // Done on found so the action fires once per attention gap, not per frame.
            if (_faceLostSince.HasValue)
            {
                var gap = (DateTime.UtcNow - _faceLostSince.Value).TotalMilliseconds;
                FireWebcamRules<AttentionLostTrigger>(t => gap >= t.MinDurationMs);
                _faceLostSince = null;
            }
        }

        private void EvaluateGazeDwell(EnhancementRule rule, bool conditionMet, int minDwellMs, DateTime now)
        {
            if (conditionMet)
            {
                if (!_gazeDwellSince.TryGetValue(rule, out var since))
                {
                    _gazeDwellSince[rule] = now;
                    return;
                }
                if ((now - since).TotalMilliseconds >= minDwellMs)
                {
                    var t = _source.GetCurrentTimeSeconds();
                    if (PassesRuleGate(rule, t))
                    {
                        DispatchSafely(rule.Action, t, rule);
                        // Reset dwell window so we don't immediately re-fire next frame.
                        _gazeDwellSince[rule] = now;
                    }
                }
            }
            else
            {
                _gazeDwellSince.Remove(rule);
            }
        }

        private static bool HitsRect(System.Windows.Point screenPoint, Rect videoRect, double[] normalized)
        {
            if (normalized == null || normalized.Length < 4) return false;
            var rx = videoRect.X + normalized[0] * videoRect.Width;
            var ry = videoRect.Y + normalized[1] * videoRect.Height;
            var rw = normalized[2] * videoRect.Width;
            var rh = normalized[3] * videoRect.Height;
            return screenPoint.X >= rx && screenPoint.X <= rx + rw
                && screenPoint.Y >= ry && screenPoint.Y <= ry + rh;
        }

        // -- Rule firing helpers ----------------------------------------------

        private void FireWebcamRules<TTrigger>(Func<TTrigger, bool> predicate) where TTrigger : EnhancementTrigger
        {
            var t = _source.GetCurrentTimeSeconds();
            foreach (var rule in _enhancement.Rules)
            {
                if (!rule.Enabled) continue;
                if (rule.Trigger is not TTrigger trig) continue;
                if (!predicate(trig)) continue;
                if (!PassesRuleGate(rule, t)) continue;
                DispatchSafely(rule.Action, t, rule);
            }
        }

        private void FireRegionRules<TTrigger>(string regionId, double t, Func<TTrigger, string> idSelector)
            where TTrigger : EnhancementTrigger
        {
            foreach (var rule in _enhancement.Rules)
            {
                if (!rule.Enabled) continue;
                if (rule.Trigger is not TTrigger trig) continue;
                if (idSelector(trig) != regionId) continue;
                if (!PassesRuleGate(rule, t)) continue;
                DispatchSafely(rule.Action, t, rule);
            }
        }

        private bool PassesRuleGate(EnhancementRule rule, double t)
        {
            // Region constraint: rule only fires while playhead is in that region.
            if (!string.IsNullOrEmpty(rule.RegionConstraint)
                && rule.RegionConstraint != _currentRegionId)
                return false;

            // Cooldown.
            var now = DateTime.UtcNow;
            if (_ruleLastFired.TryGetValue(rule, out var last))
            {
                if ((now - last).TotalMilliseconds < rule.CooldownMs) return false;
            }
            _ruleLastFired[rule] = now;
            return true;
        }

        private async void DispatchSafely(EnhancementAction action, double t, EnhancementRule? owner)
        {
            try
            {
                var ctx = new EnhancementDispatchContext(_enhancement, _source, t, _currentRegionId);
                await _dispatcher.DispatchAsync(action, ctx);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementEngine dispatch error: {Error}", ex.Message);
            }
        }

        private bool HasActiveTrigger<TTrigger>() where TTrigger : EnhancementTrigger
            => _enhancement.Rules.Any(r => r.Enabled && r.Trigger is TTrigger);

        // -- Compiled timeline entry ------------------------------------------

        private sealed class TimelineEntry
        {
            public double Time { get; }
            public EnhancementAction Action { get; }
            public EnhancementRule? OwnerRule { get; }

            public TimelineEntry(double time, EnhancementAction action, EnhancementRule? ownerRule)
            {
                Time = time;
                Action = action;
                OwnerRule = ownerRule;
            }
        }
    }
}
