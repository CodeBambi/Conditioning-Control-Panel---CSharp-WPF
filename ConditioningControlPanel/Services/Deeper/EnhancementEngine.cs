using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Runtime brain for a loaded Enhancement. Walks <see cref="Enhancement.TimelineItems"/>
    /// to compile the firing schedule: Effect items synthesize their dispatcher action
    /// at <c>Start</c>; Rule items with a <see cref="TimeReachedTrigger"/> are added
    /// to the sorted point timeline; other Rule items are evaluated reactively against
    /// webcam events and region transitions, gated by the band's
    /// <c>[Start, Start+Duration)</c> firing window (which replaces v1's
    /// <c>region_constraint</c>).
    ///
    /// Lifecycle: eager construct (cheap, no subscriptions), call Start to bind the
    /// time source + webcam subscriptions, Stop to release. Safe to Start/Stop
    /// repeatedly.
    /// </summary>
    public sealed class EnhancementEngine : IDisposable
    {
        private readonly Enhancement _enhancement;
        private readonly IPlaybackTimeSource _source;
        private readonly IActionDispatcher _dispatcher;
        private readonly Services.WebcamTrackingService? _webcam;

        private List<TimelineEntry> _timeline = new();
        private List<TimelineItem> _reactiveRules = new();
        private int _lastFiredIndex = -1;
        private double _lastTickTime = -1;
        private string? _currentRegionId;

        // Per-item last-fired timestamps for cooldown enforcement.
        private readonly Dictionary<TimelineItem, DateTime> _itemLastFired = new();

        // Gaze dwell state — keyed by item so target/avoid each get their own clock.
        private readonly Dictionary<TimelineItem, DateTime> _gazeDwellSince = new();

        // Attention-lost dwell — single clock since attention_lost has no per-item state.
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
            var reactive = new List<TimelineItem>();
            // Trigger references already represented by a TimelineItem so we
            // can skip the legacy fallback for items that round-tripped
            // through the loader's projection.
            var seenTriggers = new HashSet<EnhancementTrigger>();

            foreach (var item in _enhancement.TimelineItems)
            {
                if (item == null) continue;

                if (item.Kind == TimelineItemKind.Effect)
                {
                    var action = SynthesizeEffectAction(item);
                    if (action != null)
                        entries.Add(new TimelineEntry(item.Start, action, ownerItem: item));
                }
                else if (item.Kind == TimelineItemKind.Rule
                         && item.Enabled
                         && item.Trigger != null
                         && item.Action != null)
                {
                    if (item.Trigger is TimeReachedTrigger tr)
                        entries.Add(new TimelineEntry(tr.Time, item.Action, ownerItem: item));
                    else
                        reactive.Add(item);
                    seenTriggers.Add(item.Trigger);
                }
            }

            // Editor preview path: rules added via right-click → AddRuleAt land
            // in _enhancement.Rules (legacy) without a matching TimelineItem
            // (back-projection only happens at save). Synthesize transient
            // TimelineItems for any unseen rules so preview compiles them too.
            foreach (var rule in _enhancement.Rules)
            {
                if (rule == null || !rule.Enabled) continue;
                if (rule.Trigger == null || rule.Action == null) continue;
                if (seenTriggers.Contains(rule.Trigger)) continue;

                var synth = SynthesizeLegacyRuleItem(rule);
                if (synth == null) continue;

                if (synth.Trigger is TimeReachedTrigger tr)
                    entries.Add(new TimelineEntry(tr.Time, synth.Action!, ownerItem: synth));
                else
                    reactive.Add(synth);
            }

            entries.Sort((a, b) => a.Time.CompareTo(b.Time));
            _timeline = entries;
            _reactiveRules = reactive;
            _lastFiredIndex = -1;
        }

        /// <summary>Build a transient Rule TimelineItem from a legacy
        /// EnhancementRule. RegionConstraint is resolved against
        /// <see cref="Enhancement.Regions"/> so band-style rules fire inside
        /// their region span like a saved-and-reloaded file would.</summary>
        private TimelineItem? SynthesizeLegacyRuleItem(EnhancementRule rule)
        {
            double start = 0;
            double duration = double.MaxValue;
            string id = TimelineItem.NewId();

            if (rule.Trigger is TimeReachedTrigger tr)
            {
                start = Math.Max(0, tr.Time);
                duration = 0;
            }
            else if (!string.IsNullOrEmpty(rule.RegionConstraint))
            {
                var region = _enhancement.Regions.FirstOrDefault(r => r.Id == rule.RegionConstraint);
                if (region != null)
                {
                    start = region.Start;
                    duration = Math.Max(0, region.End - region.Start);
                    id = region.Id;
                }
            }

            return new TimelineItem
            {
                Id = id,
                Kind = TimelineItemKind.Rule,
                Start = start,
                Duration = duration,
                Trigger = rule.Trigger,
                Action = rule.Action,
                CooldownMs = rule.CooldownMs,
                Enabled = rule.Enabled
            };
        }

        private static EnhancementAction? SynthesizeEffectAction(TimelineItem item)
        {
            int durationMs = item.Duration > 0
                ? (int)Math.Max(50, item.Duration * 1000)
                : Math.Max(50, item.EffectDurationMs);

            return item.EffectType switch
            {
                EffectTypes.Haptic => new TriggerHapticAction
                {
                    PatternName = item.EffectPatternName,
                    CustomPattern = item.EffectCustomPattern,
                    Intensity = item.EffectIntensity,
                    DurationMs = durationMs
                },
                EffectTypes.Flash => new TriggerEffectAction
                {
                    EffectType = EffectTypes.Flash,
                    // Defense in depth: validator already rejects UNC paths in
                    // shared files, but if a hand-edited file slips through,
                    // strip the path to avoid the NTLM-leak / arbitrary-host
                    // probe at flash dispatch time.
                    ImagePath = IsSafeImagePath(item.EffectImagePath) ? item.EffectImagePath : null,
                    PlaySound = item.EffectPlaySound,
                    DurationMs = durationMs,
                    Intensity = item.EffectIntensity
                },
                EffectTypes.Bubble => new TriggerEffectAction
                {
                    EffectType = EffectTypes.Bubble,
                    MaxBubbles = item.EffectMaxBubbles,
                    DurationMs = durationMs,
                    Intensity = item.EffectIntensity
                },
                EffectTypes.Subliminal => new TriggerEffectAction
                {
                    EffectType = EffectTypes.Subliminal,
                    Text = item.EffectText,
                    DurationMs = durationMs,
                    Intensity = item.EffectIntensity
                },
                EffectTypes.Overlay => new TriggerEffectAction
                {
                    EffectType = EffectTypes.Overlay,
                    OverlayKind = item.EffectOverlayKind,
                    Opacity = item.EffectOpacity,
                    DurationMs = durationMs,
                    Intensity = item.EffectIntensity
                },
                _ => null
            };
        }

        private static bool IsSafeImagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true; // null is fine — flash falls back to default
            if (path.StartsWith("stock:", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("\\\\", System.StringComparison.Ordinal)) return false;
            if (path.StartsWith("//", System.StringComparison.Ordinal)) return false;
            return true;
        }

        // -- Lifecycle ---------------------------------------------------------

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EnhancementEngine));
            if (_running) return;

            Compile();
            _itemLastFired.Clear();
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
            App.Logger?.Information("EnhancementEngine started: {Name} ({Tl} timeline entries, {Rules} reactive rules)",
                _enhancement.Metadata?.Name, _timeline.Count, _reactiveRules.Count);
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
                    if (entry.OwnerItem != null && !PassesRuleGate(entry.OwnerItem, t)) continue;
                    DispatchSafely(entry.Action, t);
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
            // Inclusive-start, exclusive-end. First match wins (overlapping bands
            // are allowed in the new model, but only the first hit drives
            // region-entered/exited semantics — by file order).
            foreach (var item in _enhancement.TimelineItems)
            {
                if (item.Kind != TimelineItemKind.Rule) continue;
                if (item.Duration <= 0 || double.IsInfinity(item.Duration)) continue;
                if (item.Duration >= double.MaxValue) continue;
                if (t >= item.Start && t < item.Start + item.Duration) return item.Id;
            }
            // Editor preview fallback: legacy regions live in _enhancement.Regions
            // and only get projected into TimelineItems on save. Walk them so
            // region_entered / region_exited triggers fire correctly during
            // unsaved editor preview sessions.
            foreach (var region in _enhancement.Regions)
            {
                if (region == null || string.IsNullOrEmpty(region.Id)) continue;
                if (t >= region.Start && t < region.End) return region.Id;
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
            foreach (var item in _reactiveRules)
            {
                if (item.Trigger is GazeTargetTrigger gt)
                {
                    var hit = HitsRect(screenPoint, videoRect, gt.Rect);
                    EvaluateGazeDwell(item, hit, gt.MinDwellMs, now);
                }
                else if (item.Trigger is GazeAvoidTrigger ga)
                {
                    var inRect = HitsRect(screenPoint, videoRect, ga.Rect);
                    // "Avoid" fires when the user keeps gaze OUTSIDE the rect for the dwell.
                    EvaluateGazeDwell(item, !inRect, ga.MinDwellMs, now);
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

        private void EvaluateGazeDwell(TimelineItem item, bool conditionMet, int minDwellMs, DateTime now)
        {
            if (conditionMet)
            {
                if (!_gazeDwellSince.TryGetValue(item, out var since))
                {
                    _gazeDwellSince[item] = now;
                    return;
                }
                if ((now - since).TotalMilliseconds >= minDwellMs)
                {
                    var t = _source.GetCurrentTimeSeconds();
                    if (PassesRuleGate(item, t))
                    {
                        DispatchSafely(item.Action!, t);
                        // Reset dwell window so we don't immediately re-fire next frame.
                        _gazeDwellSince[item] = now;
                    }
                }
            }
            else
            {
                _gazeDwellSince.Remove(item);
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
            foreach (var item in _reactiveRules)
            {
                if (item.Trigger is not TTrigger trig) continue;
                if (!predicate(trig)) continue;
                if (!PassesRuleGate(item, t)) continue;
                DispatchSafely(item.Action!, t);
            }
        }

        private void FireRegionRules<TTrigger>(string regionId, double t, Func<TTrigger, string> idSelector)
            where TTrigger : EnhancementTrigger
        {
            foreach (var item in _reactiveRules)
            {
                if (item.Trigger is not TTrigger trig) continue;
                if (idSelector(trig) != regionId) continue;
                if (!PassesRuleGate(item, t)) continue;
                DispatchSafely(item.Action!, t);
            }
        }

        private bool PassesRuleGate(TimelineItem item, double t)
        {
            // Band window: rule only fires while the playhead is inside its
            // [Start, Start+Duration) span. Duration <= 0 is point-style (only
            // exact-time matches via the sorted timeline). Duration == double.MaxValue
            // is "anywhere" (used by the loader projection for v1 rules with no
            // RegionConstraint and a non-time-reached trigger).
            if (item.Duration > 0 && item.Duration < double.MaxValue)
            {
                if (t < item.Start || t >= item.Start + item.Duration) return false;
            }

            // Cooldown.
            var now = DateTime.UtcNow;
            if (_itemLastFired.TryGetValue(item, out var last))
            {
                if ((now - last).TotalMilliseconds < item.CooldownMs) return false;
            }
            _itemLastFired[item] = now;
            return true;
        }

        private async void DispatchSafely(EnhancementAction action, double t)
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
            => _reactiveRules.Any(item => item.Trigger is TTrigger);

        // -- Compiled timeline entry ------------------------------------------

        private sealed class TimelineEntry
        {
            public double Time { get; }
            public EnhancementAction Action { get; }
            public TimelineItem? OwnerItem { get; }

            public TimelineEntry(double time, EnhancementAction action, TimelineItem? ownerItem)
            {
                Time = time;
                Action = action;
                OwnerItem = ownerItem;
            }
        }
    }
}
