using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private readonly Action<string>? _diag;

        private List<TimelineEntry> _timeline = new();
        private List<TimelineItem> _reactiveRules = new();
        private int _lastFiredIndex = -1;
        private double _lastTickTime = -1;
        private string? _currentRegionId;

        // Per-item last-fired timestamps for cooldown enforcement.
        private readonly Dictionary<TimelineItem, DateTime> _itemLastFired = new();

        // Per-entry latch: items that have already fired since the playhead
        // last entered their band. Without this, a webcam trigger with
        // cooldown_ms <= 0 re-fires on every blink/face/gaze event while the
        // playhead sits inside the band. Cleared when the playhead exits the
        // band, on seek-back, and on Start/Stop.
        private readonly HashSet<TimelineItem> _firedInCurrentEntry = new();

        // Gaze dwell state — keyed by item so target/avoid each get their own clock.
        private readonly Dictionary<TimelineItem, DateTime> _gazeDwellSince = new();

        // Attention-lost dwell — single clock since attention_lost has no per-item state.
        private DateTime? _faceLostSince;

        private bool _running;
        private bool _disposed;

        // Token source canceled by Stop so in-flight DispatchSafely calls
        // (haptic patterns, audio playback chains) can short-circuit instead
        // of running on after the user pressed stop.
        private CancellationTokenSource? _runCts;

        public bool IsRunning => _running;
        public Enhancement Enhancement => _enhancement;

        public EnhancementEngine(
            Enhancement enhancement,
            IPlaybackTimeSource source,
            IActionDispatcher dispatcher,
            Services.WebcamTrackingService? webcam = null,
            Action<string>? diag = null)
        {
            _enhancement = enhancement ?? throw new ArgumentNullException(nameof(enhancement));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _webcam = webcam;
            _diag = diag;
        }

        private void Diag(string line)
        {
            try { _diag?.Invoke(line); } catch { /* never let a diag subscriber kill the engine */ }
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
            _firedInCurrentEntry.Clear();
            _gazeDwellSince.Clear();
            _faceLostSince = null;
            _runCts = new CancellationTokenSource();

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

            int blinkRules = _reactiveRules.Count(i => i.Trigger is BlinkDetectedTrigger);
            int gazeRules = _reactiveRules.Count(i => i.Trigger is GazeTargetTrigger or GazeAvoidTrigger);
            int mouthRules = _reactiveRules.Count(i => i.Trigger is MouthOpenTrigger);
            int attentionRules = _reactiveRules.Count(i => i.Trigger is AttentionLostTrigger);
            int webcamRules = blinkRules + gazeRules + mouthRules + attentionRules;
            if (webcamRules > 0 && _webcam == null)
            {
                Diag($"⚠ {webcamRules} webcam rule(s) but App.Webcam is null — they will never fire.");
            }
            else if (webcamRules > 0)
            {
                Diag($"engine ready: {_timeline.Count} timeline entries, {_reactiveRules.Count} reactive rule(s) ({blinkRules} blink, {gazeRules} gaze, {mouthRules} mouth, {attentionRules} attention)");
            }
            else
            {
                Diag($"engine ready: {_timeline.Count} timeline entries, {_reactiveRules.Count} reactive rule(s)");
            }
        }

        public void Stop()
        {
            if (!_running) return;
            // Flip _running first so any in-flight tick / dispatch short-circuits
            // before we start tearing down subscriptions.
            _running = false;

            try { _source.PlaybackTimeChanged -= OnPlaybackTime; } catch { }

            if (_webcam != null)
            {
                try { _webcam.OnBlink -= OnBlink; } catch { }
                try { _webcam.OnMouthOpen -= OnMouthOpen; } catch { }
                try { _webcam.OnGazeMove -= OnGazeMove; } catch { }
                try { _webcam.OnFaceLost -= OnFaceLost; } catch { }
                try { _webcam.OnFaceFound -= OnFaceFound; } catch { }
            }

            try { _runCts?.Cancel(); } catch { }
            try { _runCts?.Dispose(); } catch { }
            _runCts = null;

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

            // Browser sources can briefly forward NaN currentTime during
            // reloads. NaN comparisons are false everywhere so the rest of the
            // tick would silently no-op AND store NaN as _lastTickTime,
            // breaking seek-back detection until the next clean tick.
            if (double.IsNaN(t) || double.IsInfinity(t)) return;

            // Suppress the metadata-load phantom tick from a WebView2 source.
            // Browser pages emit a tick at t=0 the moment <video> metadata
            // loads (currentTime is 0, but duration just became known) which
            // would otherwise fire any entry at Time=0 before the user pressed
            // play. After the first real tick has been processed (_lastTickTime
            // set), normal flow resumes including legitimate t=0 entries on
            // the first playing tick (where _source.IsPlaying becomes true).
            if (_lastTickTime < 0 && t <= 0 && !_source.IsPlaying) return;

            try
            {
                // Detect seek-back: rewind the cursor to the highest entry still
                // strictly before t, so forward-progress fires re-arm correctly.
                if (_lastTickTime >= 0 && t + 0.05 < _lastTickTime)
                    RewindCursor(t);

                // Clear the per-entry latch for items whose band the playhead
                // has now left, so re-entering them re-arms a single fire.
                if (_firedInCurrentEntry.Count > 0)
                {
                    _firedInCurrentEntry.RemoveWhere(item =>
                        !(item.Duration > 0 && item.Duration < double.MaxValue
                          && t >= item.Start && t < item.Start + item.Duration));
                }

                // Region transition (computed before firing so region_entered
                // rules see the new region as "current").
                var newRegionId = FindRegionAt(t);
                if (newRegionId != _currentRegionId)
                {
                    var prev = _currentRegionId;
                    _currentRegionId = newRegionId;
                    if (prev != null)
                        FireRegionRules<RegionExitedTrigger>(prev, t, tr => tr.RegionId);
                    if (!_running) return;
                    if (newRegionId != null)
                        FireRegionRules<RegionEnteredTrigger>(newRegionId, t, tr => tr.RegionId);
                    if (!_running) return;
                }

                // Fire any sorted-timeline entries whose time has been reached.
                // Re-check _running between dispatches: a Stop() called by a
                // dispatched action (or another thread) must short-circuit
                // remaining fires for this tick.
                while (_lastFiredIndex + 1 < _timeline.Count
                       && _timeline[_lastFiredIndex + 1].Time <= t)
                {
                    if (!_running) break;
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
            // Seek-back invalidates per-entry latches: a band the user just
            // jumped back into should be able to fire its single shot again.
            _firedInCurrentEntry.Clear();
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
        // Always invoked from the UI thread, so they bypass the marshal step.

        public void InjectBlink() => OnBlinkCore();
        public void InjectMouthOpen() => OnMouthOpenCore();
        public void InjectGaze(System.Windows.Point screenPoint) => OnGazeMoveCore(screenPoint);

        /// <summary>
        /// Simulate an attention-lost gap of the given duration. Mirrors the
        /// face-lost → face-found pattern (rules fire on "found" if the gap
        /// satisfied their MinDurationMs).
        /// </summary>
        public void InjectAttentionLost(int gapMs)
        {
            if (!_running) return;
            _faceLostSince = DateTime.UtcNow.AddMilliseconds(-Math.Max(0, gapMs));
            OnFaceFoundCore();
        }

        // -- Webcam handlers ---------------------------------------------------
        // WebcamTrackingService raises these on its capture thread. Engine
        // state (Dictionary/HashSet, _faceLostSince, _itemLastFired, etc.) is
        // not thread-safe and OnPlaybackTime mutates the same fields on the UI
        // thread, so each handler marshals onto the dispatcher before touching
        // anything. The Core methods assume UI-thread context.

        private void OnBlink() => MarshalToUi(_onBlinkCore);
        private void OnMouthOpen() => MarshalToUi(_onMouthOpenCore);
        private void OnGazeMove(System.Windows.Point screenPoint)
        {
            // Capture by value to avoid sharing the struct across threads.
            var p = screenPoint;
            MarshalToUi(() => OnGazeMoveCore(p));
        }
        private void OnFaceLost() => MarshalToUi(_onFaceLostCore);
        private void OnFaceFound() => MarshalToUi(_onFaceFoundCore);

        // Cached delegates so the no-arg handlers don't allocate on every event.
        private Action _onBlinkCore => OnBlinkCore;
        private Action _onMouthOpenCore => OnMouthOpenCore;
        private Action _onFaceLostCore => OnFaceLostCore;
        private Action _onFaceFoundCore => OnFaceFoundCore;

        private static void MarshalToUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            if (d.CheckAccess()) { action(); return; }
            try { d.BeginInvoke(action); } catch { /* dispatcher shutting down */ }
        }

        private void OnBlinkCore()
        {
            if (!_running) return;
            int eligible = _reactiveRules.Count(i => i.Trigger is BlinkDetectedTrigger);
            int fired = FireWebcamRules<BlinkDetectedTrigger>(_ => true);
            Diag($"• blink ({eligible} rule(s), {fired} fired)");
        }

        private void OnMouthOpenCore()
        {
            if (!_running) return;
            int eligible = _reactiveRules.Count(i => i.Trigger is MouthOpenTrigger);
            int fired = FireWebcamRules<MouthOpenTrigger>(_ => true);
            Diag($"• mouth_open ({eligible} rule(s), {fired} fired)");
        }

        private void OnGazeMoveCore(System.Windows.Point screenPoint)
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

        private void OnFaceLostCore()
        {
            if (!_running) return;
            _faceLostSince = DateTime.UtcNow;
            Diag("• face_lost");
        }

        private void OnFaceFoundCore()
        {
            if (!_running) return;
            // If the face was lost long enough to satisfy any rule, fire on return.
            // Done on found so the action fires once per attention gap, not per frame.
            if (_faceLostSince.HasValue)
            {
                var gap = (DateTime.UtcNow - _faceLostSince.Value).TotalMilliseconds;
                int eligible = _reactiveRules.Count(i => i.Trigger is AttentionLostTrigger);
                int fired = FireWebcamRules<AttentionLostTrigger>(t => gap >= t.MinDurationMs);
                Diag($"• face_found (gap {gap:F0}ms, {eligible} rule(s), {fired} fired)");
                _faceLostSince = null;
            }
            else
            {
                Diag("• face_found");
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

        private int FireWebcamRules<TTrigger>(Func<TTrigger, bool> predicate) where TTrigger : EnhancementTrigger
        {
            var t = _source.GetCurrentTimeSeconds();
            int fired = 0;
            foreach (var item in _reactiveRules)
            {
                if (item.Trigger is not TTrigger trig) continue;
                if (!predicate(trig)) continue;
                if (!PassesRuleGate(item, t)) continue;
                DispatchSafely(item.Action!, t);
                fired++;
            }
            return fired;
        }

        private void FireRegionRules<TTrigger>(string regionId, double t, Func<TTrigger, string> idSelector)
            where TTrigger : EnhancementTrigger
        {
            foreach (var item in _reactiveRules)
            {
                if (item.Trigger is not TTrigger trig) continue;
                if (idSelector(trig) != regionId) continue;
                if (!PassesRuleGate(item, t)) continue;
                // Pin the band id explicitly so loop_region / seek with no
                // RegionId still resolves on RegionExited (where the playhead
                // has just left the band, so _currentRegionId is null).
                DispatchSafely(item.Action!, t, regionId);
            }
        }

        private bool PassesRuleGate(TimelineItem item, double t)
        {
            // Band window: rule only fires while the playhead is inside its
            // [Start, Start+Duration) span. Duration <= 0 is point-style (only
            // exact-time matches via the sorted timeline). Duration == double.MaxValue
            // is "anywhere" (used by the loader projection for v1 rules with no
            // RegionConstraint and a non-time-reached trigger).
            bool finiteBand = item.Duration > 0 && item.Duration < double.MaxValue;
            if (finiteBand)
            {
                if (t < item.Start || t >= item.Start + item.Duration) return false;
            }

            // Cooldown.
            var now = DateTime.UtcNow;
            if (_itemLastFired.TryGetValue(item, out var last))
            {
                if ((now - last).TotalMilliseconds < item.CooldownMs) return false;
            }

            // Per-entry latch: with cooldown_ms <= 0 on a finite band, fire at
            // most once per band entry. Without this, high-frequency triggers
            // (blink, gaze) would re-fire on every event while the playhead
            // sits inside the band. The latch clears on band exit (see
            // OnPlaybackTime) and on seek-back (see RewindCursor).
            if (finiteBand && item.CooldownMs <= 0)
            {
                if (!_firedInCurrentEntry.Add(item)) return false;
            }

            _itemLastFired[item] = now;
            return true;
        }

        private async void DispatchSafely(EnhancementAction action, double t, string? explicitRegionId = null)
        {
            // Snapshot the CTS once: Stop() may dispose _runCts on another
            // thread between our null-check and .Token access. Reading the
            // local reference and tolerating ObjectDisposedException keeps
            // dispatch from crashing on a teardown race.
            var cts = _runCts;
            CancellationToken ct;
            try { ct = cts?.Token ?? CancellationToken.None; }
            catch (ObjectDisposedException) { ct = CancellationToken.None; }

            try
            {
                var ctx = new EnhancementDispatchContext(_enhancement, _source, t, explicitRegionId ?? _currentRegionId);
                await _dispatcher.DispatchAsync(action, ctx, ct);
            }
            catch (OperationCanceledException) { /* engine stopped mid-dispatch */ }
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
