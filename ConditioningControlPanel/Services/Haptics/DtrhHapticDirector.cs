using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Haptics for the DtRH browser game. NOT a 1:1 event→buzz mapper — the game emits
    /// dozens of events per minute, and Buttplug commands carry ~1.3s of latency, so a
    /// naive mapping is either a constant buzz or arrives seconds late. Instead this
    /// director maintains a two-layer envelope:
    ///
    ///   AMBIENT — a slow "depth gauge" floor driven by the page's throttled
    ///   haptic-state feed (the game's own intensity() 0..1 signal + Surfacing melt).
    ///   Issued as long 30s commands refreshed rarely, exactly like
    ///   HapticService.StartVideoBackgroundVibeAsync — near-zero command traffic.
    ///
    ///   ACCENTS — short pattern spikes on meaningful moments, tapped from the bark
    ///   event stream DtrhHostService already receives. Three tiers with cooldowns:
    ///   tier 3 moments always fire and preempt, tier 2 respects a shared gap, tier 1
    ///   micro-events (pops/defuses) are COALESCED into one swell scaled by count.
    ///
    /// Lifecycle-safe: everything stops on run end, world freeze, covering video,
    /// window close, or the settings toggles flipping off mid-run.
    /// </summary>
    internal static class DtrhHapticDirector
    {
        private sealed record Accent(int Tier, double Rel, VibrationMode Mode, int Ms);

        // The curated verb table. Rel is a fraction of the user's DtrhIntensity slider.
        private static readonly Dictionary<string, Accent> Map = new()
        {
            // ---- tier 3: rare moments (preempt whatever is playing) ----
            ["act-changed"]          = new(3, 0.95, VibrationMode.Escalate,  900),
            ["ending-soon"]          = new(3, 0.85, VibrationMode.Heartbeat, 1400),

            // ---- tier 2: notable beats (shared cooldown) ----
            ["detonated"]            = new(2, 1.00, VibrationMode.Constant,  300),
            ["detonated-absorbed"]   = new(2, 0.70, VibrationMode.Pulse,     250),
            ["curse-picked"]         = new(2, 0.85, VibrationMode.Earthquake, 800),
            ["boon-picked"]          = new(2, 0.70, VibrationMode.Heartbeat, 600),
            ["crafted"]              = new(2, 0.70, VibrationMode.Pulse,     450),
            ["combo-milestone"]      = new(2, 0.70, VibrationMode.Escalate,  500),
            ["combo-big"]            = new(2, 0.90, VibrationMode.Escalate,  700),
            ["tease-denied"]         = new(2, 0.80, VibrationMode.Pulse,     350),
            ["tease-denied-streak"]  = new(2, 0.90, VibrationMode.Pulse,     500),
            ["wave-cleared"]         = new(2, 0.60, VibrationMode.Wave,      500),
            ["wave-escalated"]       = new(2, 0.75, VibrationMode.Escalate,  600),
            ["freeze-caught"]        = new(2, 0.60, VibrationMode.Constant,  300),
            ["gold-first"]           = new(2, 0.70, VibrationMode.Heartbeat, 500),
            ["reveal-flash"]         = new(2, 0.65, VibrationMode.Wave,      500),

            // ---- tier 1: micro-events (coalesced into one swell) ----
            ["benign-popped"]        = new(1, 0.40, VibrationMode.Constant,  150),
            ["defused"]              = new(1, 0.50, VibrationMode.Wave,      300),
            ["darter-caught"]        = new(1, 0.50, VibrationMode.Pulse,     200),
            ["rabbit-caught"]        = new(1, 0.55, VibrationMode.Pulse,     200),
            ["tease-clicked"]        = new(1, 0.45, VibrationMode.Pulse,     200),
        };

        private static readonly object Gate = new();
        private static bool _active;          // host window alive (Launch..DisposeAll)
        private static bool _testMode;
        private static bool _runActive;       // between run-started and run-ended
        private static bool _pageRunning;     // page state === 'running' (haptic-state feed)
        private static bool _worldFrozen;     // in-world Freeze bubble holds the real world
        private static bool _videoCovering;   // a mandatory native video owns haptics (VideoEnabled path)
        private static double _depth;         // game's intensity() 0..1
        private static double _melt;          // Surfacing melt 0..1

        private static Timer? _ambientTimer;
        private static double _lastAmbientSent = -1;
        private static DateTime _lastAmbientSendUtc = DateTime.MinValue;

        private static CancellationTokenSource? _accentCts;
        private static int _accentTierPlaying;              // 0 = idle
        private static DateTime _lastAccentEndUtc = DateTime.MinValue;

        // tier-1 coalescer: micro-events within a short window become ONE swell
        private static Timer? _coalesceTimer;
        private static int _coalesceCount;
        private static Accent? _coalesceBest;
        private const int CoalesceWindowMs = 700;

        private static HapticSettings? Settings => App.Settings?.Current?.Haptics;

        private static bool Ready
        {
            get
            {
                var s = Settings;
                return _active && !_testMode && s is { Enabled: true, DtrhEnabled: true }
                       && App.Haptics is { IsConnected: true };
            }
        }

        // ============================ lifecycle (DtrhHostService taps) ============================

        public static void OnLaunch(bool testMode)
        {
            lock (Gate)
            {
                _active = true;
                _testMode = testMode;
                _runActive = false;
                _pageRunning = false;
                _worldFrozen = false;
                _videoCovering = false;
                _depth = 0; _melt = 0;
                _lastAmbientSent = -1;
            }
            var s = Settings;
            if (s != null) s.PropertyChanged += OnSettingsChanged;
        }

        public static void OnClosed()
        {
            var s = Settings;
            if (s != null) s.PropertyChanged -= OnSettingsChanged;
            bool wasActive;
            lock (Gate)
            {
                wasActive = _active;
                _active = false;
                _runActive = false;
                _pageRunning = false;
            }
            StopAmbientTimer();
            CancelCoalesce();
            _accentCts?.Cancel();
            if (wasActive) _ = SafeStopDevice();
        }

        public static void OnRunStarted()
        {
            lock (Gate)
            {
                _runActive = true;
                _depth = 0; _melt = 0;
                _lastAmbientSent = -1;
            }
            // Timer runs regardless — each tick re-checks Ready, so connecting the
            // device mid-run still picks up the ambient floor.
            StartAmbientTimer();
            if (!Ready) return;
            // A gentle wave marks the drop-in — the descent has begun.
            _ = PlayAccent(new Accent(3, 0.50, VibrationMode.Wave, 800), "run-started");
        }

        public static void OnRunEnded()
        {
            bool notify;
            lock (Gate)
            {
                notify = _runActive;
                _runActive = false;
                _pageRunning = false;
                _depth = 0; _melt = 0;
            }
            StopAmbientTimer();
            CancelCoalesce();
            _accentCts?.Cancel();
            if (!notify || !Ready) { _ = SafeStopDevice(); return; }
            // One soft farewell wave, then silence — the recap should feel like surfacing.
            _ = Task.Run(async () =>
            {
                try
                {
                    await PlayPattern(0.45, 600, VibrationMode.Wave, CancellationToken.None);
                    await SafeStopDevice();
                }
                catch { }
            });
        }

        public static void OnWorldFreeze(bool on)
        {
            lock (Gate) { _worldFrozen = on; }
            if (on)
            {
                _accentCts?.Cancel();
                CancelCoalesce();
                _ = SafeStopDevice();
                lock (Gate) { _lastAmbientSent = -1; }   // reassert floor when the freeze lifts
            }
        }

        /// <summary>A mandatory native video covers the game: its own haptic feature
        /// (Video background + TargetHit) owns the device until it closes.</summary>
        public static void OnVideoCovering(bool on)
        {
            lock (Gate) { _videoCovering = on; _lastAmbientSent = -1; }
            if (on) { _accentCts?.Cancel(); CancelCoalesce(); }
        }

        // ============================ page feeds ============================

        /// <summary>haptic-state {running, depth, melt} — the page's ~2s ambient feed.</summary>
        public static void OnHapticState(JObject o)
        {
            lock (Gate)
            {
                _pageRunning = (bool?)o["running"] ?? false;
                _depth = Math.Clamp((double?)o["depth"] ?? 0, 0, 1);
                _melt = Math.Clamp((double?)o["melt"] ?? 0, 0, 1);
            }
        }

        /// <summary>A bark-stream game event (tapped in DtrhHostService BEFORE the
        /// vn-speaking gate — the moment happened whether or not she talks over it).</summary>
        public static void OnGameEvent(JObject o)
        {
            if (!Ready) return;
            var evt = (string?)o["event"];
            if (evt == null || !Map.TryGetValue(evt, out var accent)) return;
            lock (Gate)
            {
                if (_worldFrozen || _videoCovering) return;
            }

            var density = Settings?.DtrhDensity ?? 1;
            if (accent.Tier == 1)
            {
                if (density == 0) return;   // Sparse: big moments only
                Coalesce(accent);
                return;
            }
            _ = PlayAccent(accent, evt);
        }

        // ============================ accents ============================

        private static double GapMult => (Settings?.DtrhDensity ?? 1) switch
        {
            0 => 2.0,   // Sparse
            2 => 0.6,   // Rich
            _ => 1.0,
        };

        private static async Task PlayAccent(Accent accent, string label)
        {
            var haptics = App.Haptics;
            if (haptics == null) return;

            CancellationTokenSource cts;
            lock (Gate)
            {
                var sinceLast = (DateTime.UtcNow - _lastAccentEndUtc).TotalSeconds;
                if (_accentTierPlaying > 0)
                {
                    // Busy: only a tier-3 moment preempts; everything else is dropped.
                    if (accent.Tier < 3) return;
                    _accentCts?.Cancel();
                }
                else
                {
                    var minGap = accent.Tier switch { 3 => 1.0, 2 => 4.0, _ => 1.5 } * GapMult;
                    if (sinceLast < minGap) return;
                }
                cts = new CancellationTokenSource();
                _accentCts = cts;
                _accentTierPlaying = accent.Tier;
            }

            try
            {
                var s = Settings;
                var intensity = Math.Clamp((s?.DtrhIntensity ?? 0.6) * accent.Rel, 0.06, 1.0);
                // Buttplug's ~1.3s command latency turns short patterns into mush — stretch them.
                var ms = haptics.IsButtplugProvider ? accent.Ms * 2 : accent.Ms;
                App.Logger?.Debug("DtrhHaptics: accent {Label} T{Tier} {Pct}% {Ms}ms {Mode}",
                    label, accent.Tier, (int)(intensity * 100), ms, accent.Mode);
                await PlayPattern(intensity, ms, accent.Mode, cts.Token);
            }
            catch { }
            finally
            {
                lock (Gate)
                {
                    if (_accentCts == cts) { _accentTierPlaying = 0; _accentCts = null; }
                    _lastAccentEndUtc = DateTime.UtcNow;
                    _lastAmbientSent = -1;   // the accent overrode the floor — reassert it soon
                }
            }
        }

        private static Task PlayPattern(double intensity, int ms, VibrationMode mode, CancellationToken token)
            => App.Haptics?.ApplyVibrationModeAsync(intensity, ms, mode, token) ?? Task.CompletedTask;

        // tier-1 micro-events: first one opens a short window; everything landing inside
        // becomes a single swell whose strength grows with the count (a chain-pop feels
        // like one surge, not twelve stutters).
        private static void Coalesce(Accent accent)
        {
            lock (Gate)
            {
                _coalesceCount++;
                if (_coalesceBest == null || accent.Rel > _coalesceBest.Rel) _coalesceBest = accent;
                if (_coalesceTimer != null) return;
                _coalesceTimer = new Timer(_ => FlushCoalesced(), null, CoalesceWindowMs, Timeout.Infinite);
            }
        }

        private static void FlushCoalesced()
        {
            Accent? best; int count;
            lock (Gate)
            {
                best = _coalesceBest; count = _coalesceCount;
                _coalesceBest = null; _coalesceCount = 0;
                _coalesceTimer?.Dispose(); _coalesceTimer = null;
            }
            if (best == null || count == 0 || !Ready) return;
            // Swell: base strength + a nudge per extra event, longer for bigger bursts.
            var rel = Math.Min(0.75, best.Rel + 0.05 * (count - 1));
            var ms = Math.Min(600, best.Ms + 60 * (count - 1));
            _ = PlayAccent(new Accent(1, rel, count >= 3 ? VibrationMode.Wave : best.Mode, ms), $"swell x{count}");
        }

        private static void CancelCoalesce()
        {
            lock (Gate)
            {
                _coalesceTimer?.Dispose(); _coalesceTimer = null;
                _coalesceBest = null; _coalesceCount = 0;
            }
        }

        // ============================ ambient floor ============================

        private static void StartAmbientTimer()
        {
            StopAmbientTimer();
            _ambientTimer = new Timer(_ => AmbientTick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        }

        private static void StopAmbientTimer()
        {
            _ambientTimer?.Dispose();
            _ambientTimer = null;
        }

        private static void AmbientTick()
        {
            try
            {
                if (!Ready) return;
                double target;
                lock (Gate)
                {
                    if (!_runActive || !_pageRunning || _worldFrozen || _videoCovering || _accentTierPlaying > 0)
                        return;
                    var s = Settings;
                    var cap = s?.DtrhAmbientIntensity ?? 0;
                    // The toy as a depth gauge: a whisper at the surface, the slider's full
                    // value at the bottom, and the Surfacing melt pushes past it a touch.
                    target = cap <= 0 ? 0 : cap * (0.30 + 0.70 * _depth) * (1 + 0.5 * _melt);
                    if (target > 0) target = Math.Clamp(target, 0.06, 1.0);   // clear Lovense's <=0.05 = off cutoff

                    var refreshDue = (DateTime.UtcNow - _lastAmbientSendUtc).TotalSeconds > 20;
                    if (Math.Abs(target - _lastAmbientSent) < 0.02 && !refreshDue) return;
                    _lastAmbientSent = target;
                    _lastAmbientSendUtc = DateTime.UtcNow;
                }
                if (target <= 0)
                {
                    _ = SafeStopDevice();
                    return;
                }
                // Long-lived command, refreshed rarely (the video-background trick):
                // the device holds the level with no further traffic.
                _ = PlayPattern(target, 30000, VibrationMode.Constant, CancellationToken.None);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DtrhHaptics ambient tick: {E}", ex.Message);
            }
        }

        // ============================ safety ============================

        private static void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(HapticSettings.Enabled) or nameof(HapticSettings.DtrhEnabled)))
                return;
            if (Ready) return;   // still on — nothing to kill
            _accentCts?.Cancel();
            CancelCoalesce();
            lock (Gate) { _lastAmbientSent = -1; }
            _ = SafeStopDevice();
        }

        private static async Task SafeStopDevice()
        {
            try { if (App.Haptics != null) await App.Haptics.StopAsync(); }
            catch { }
        }
    }
}
