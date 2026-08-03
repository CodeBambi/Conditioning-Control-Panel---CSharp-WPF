using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Services
{
    public enum HapticTestResult
    {
        Success,
        NotConnected,
        Unreachable
    }

    /// <summary>
    /// Facade over the v2 haptics engine.
    ///
    /// Every public method that existed before v6.7 still exists with the same signature, so all
    /// ~25 consumer call sites compile untouched — but none of them talk to a device any more.
    /// They post SEMANTIC events (<see cref="PostEvent"/>) or set CONTINUOUS layers
    /// (<see cref="SetLayer"/>) on <see cref="HapticMixer"/>, which owns mixing, safety and the
    /// single 10 Hz output loop.
    ///
    /// Bugs this rewrite closes:
    ///  - ConnectAsync used to force <c>Settings.Enabled = true</c> behind the user's back.
    ///  - Two CancellationTokenSources (flash decay, video vibe) were cancelled and disposed from
    ///    fire-and-forget continuations, throwing ObjectDisposedException into
    ///    UnobservedTaskException. The mixer schedules everything on its own loop, so both are gone.
    ///  - <c>_currentEventType</c> was a single unsynchronized string shared by every feature.
    ///  - <c>Dispose</c> did <c>DisconnectAsync().Wait(1000)</c> on the UI thread during shutdown.
    ///  - <c>RampUpAsync</c> had no callers and drove the device directly (kept as a thin shim).
    ///  - The premium/enabled gate was re-checked (inconsistently) in every method; it is now
    ///    evaluated once, inside the mixer.
    /// </summary>
    public class HapticService : IDisposable
    {
        private readonly HapticDeviceManager _deviceManager;
        private readonly HapticMixer _mixer;
        private bool _disposed;

        private System.Threading.Timer? _pingTimer;
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
        // A single failed ping used to drop the device immediately, so one transient blip
        // (Wi-Fi hiccup, device momentarily busy) killed the connection until the user manually
        // reconnected (#302). Require several consecutive failures (~90s) before giving up.
        private int _consecutivePingFailures;
        private const int MaxConsecutivePingFailures = 3;

        /// <summary>Latest sequence per event kind, so flipping a feature toggle off mid-pattern
        /// still stops that pattern (and only that one).</summary>
        private readonly Dictionary<HapticEventKind, HapticSequence> _liveByKind = new();
        private readonly object _liveGate = new();

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<string>? DeviceDiscovered;
        public event EventHandler<string>? Error;
        public event EventHandler<string>? HapticTriggered;

        public HapticSettings Settings { get; }

        /// <summary>The mixer. New code should prefer PostEvent/SetLayer on this service.</summary>
        public HapticMixer Mixer => _mixer;
        /// <summary>Device registry (roles, trims, battery, capabilities) for the Phase E UI.</summary>
        public HapticDeviceManager DeviceManager => _deviceManager;

        public bool IsConnected => _deviceManager.IsConnected;
        public string ProviderName => _deviceManager.ProviderNames;
        public bool IsButtplugProvider => Settings.Provider == HapticProviderType.Buttplug;

        /// <summary>
        /// Buttplug.io has ~1.3s latency, so we need to trigger haptics earlier
        /// </summary>
        public int SubliminalAnticipationMs => IsButtplugProvider ? 1300 : 250;

        public List<string> ConnectedDevices
        {
            get
            {
                var list = new List<string>();
                foreach (var d in _deviceManager.Devices)
                    list.Add(string.IsNullOrWhiteSpace(d.Nickname) ? d.Name : d.Nickname);
                return list;
            }
        }

        public HapticService(HapticSettings settings)
        {
            Settings = settings;
            Settings.EnsureV2Migrated();

            _deviceManager = new HapticDeviceManager(settings);
            _mixer = new HapticMixer(_deviceManager, settings);

            _deviceManager.ConnectionChanged += (s, connected) => { try { ConnectionChanged?.Invoke(this, connected); } catch { } };
            _deviceManager.DeviceDiscovered += (s, name) => { try { DeviceDiscovered?.Invoke(this, name); } catch { } };
            _deviceManager.Error += (s, error) => { try { Error?.Invoke(this, error); } catch { } };
            _mixer.Activity += (s, msg) => { try { HapticTriggered?.Invoke(this, msg); } catch { } };

            Settings.PropertyChanged += OnSettingsChanged;
            _mixer.Start();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Master toggle off => everything stops right now.
            if (e.PropertyName == nameof(HapticSettings.Enabled) && !Settings.Enabled)
            {
                _mixer.PanicStop();
                return;
            }

            // A feature turned off mid-pattern stops THAT pattern (the old code stopped the
            // device outright, which also killed unrelated features).
            switch (e.PropertyName)
            {
                case nameof(HapticSettings.BubblePopEnabled) when !Settings.BubblePopEnabled: CancelKind(HapticEventKind.BubblePop); break;
                case nameof(HapticSettings.FlashDisplayEnabled) when !Settings.FlashDisplayEnabled: CancelKind(HapticEventKind.FlashDecay); break;
                case nameof(HapticSettings.FlashClickEnabled) when !Settings.FlashClickEnabled: CancelKind(HapticEventKind.FlashClick); break;
                case nameof(HapticSettings.TargetHitEnabled) when !Settings.TargetHitEnabled: CancelKind(HapticEventKind.VideoTargetHit); break;
                case nameof(HapticSettings.SubliminalEnabled) when !Settings.SubliminalEnabled: CancelKind(HapticEventKind.SubliminalTrigger); break;
                case nameof(HapticSettings.LevelUpEnabled) when !Settings.LevelUpEnabled: CancelKind(HapticEventKind.LevelUp); break;
                case nameof(HapticSettings.AchievementEnabled) when !Settings.AchievementEnabled:
                    CancelKind(HapticEventKind.Achievement);
                    CancelKind(HapticEventKind.QuestComplete);
                    CancelKind(HapticEventKind.AvatarEasterEgg);
                    break;
                case nameof(HapticSettings.BouncingTextEnabled) when !Settings.BouncingTextEnabled: CancelKind(HapticEventKind.BouncingTextBounce); break;
                case nameof(HapticSettings.BlinkEnabled) when !Settings.BlinkEnabled: CancelKind(HapticEventKind.BlinkPulse); break;
                case nameof(HapticSettings.VideoEnabled) when !Settings.VideoEnabled: _mixer.SetLayer(HapticLayer.Video, 0); break;
            }
        }

        // ================================================================== connection

        public async Task<bool> ConnectAsync()
        {
            await DisconnectAsync();

            // NOTE: deliberately does NOT set Settings.Enabled = true. Connecting a toy is not
            // consent to start buzzing; the master toggle is the user's.
            Log.Information("Connecting haptic providers");
            var result = await _deviceManager.ConnectAsync(CancellationToken.None);
            if (result)
            {
                _mixer.Start();
                StartPingTimer();
            }
            else
            {
                Error?.Invoke(this, "No haptic provider connected");
            }
            return result;
        }

        public async Task DisconnectAsync()
        {
            StopPingTimer();
            _mixer.ClearAll();
            await _deviceManager.DisconnectAsync();
        }

        private void StartPingTimer()
        {
            _consecutivePingFailures = 0;
            _pingTimer?.Dispose();
            _pingTimer = new System.Threading.Timer(_ => _ = PingTickAsync(), null, PingInterval, PingInterval);
        }

        private void StopPingTimer()
        {
            _pingTimer?.Dispose();
            _pingTimer = null;
        }

        private async Task PingTickAsync()
        {
            try
            {
                if (!_deviceManager.IsConnected) return;

                var ok = await _deviceManager.PingAsync();
                if (ok)
                {
                    _consecutivePingFailures = 0;
                    return;
                }

                _consecutivePingFailures++;
                if (_consecutivePingFailures < MaxConsecutivePingFailures)
                {
                    App.Logger?.Warning("Haptic ping failed ({Count}/{Max}) — device briefly unreachable, will retry before disconnecting",
                        _consecutivePingFailures, MaxConsecutivePingFailures);
                    return;
                }

                App.Logger?.Warning("Haptic ping failed {Max}x consecutively — device unreachable, marking disconnected", MaxConsecutivePingFailures);
                _consecutivePingFailures = 0;
                await _deviceManager.DisconnectAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "Haptic ping tick error (non-fatal)");
            }
        }

        // ================================================================== new first-class API

        /// <summary>
        /// Fire a semantic event. The routing matrix decides whether it plays, how hard, with
        /// which pattern and at which toy role. This is the API new consumers should call.
        /// </summary>
        public HapticSequence PostEvent(HapticEventKind kind, double? intensityOverride = null)
            => PostEvent(kind, intensityOverride, null, null, 0);

        /// <summary>Set a continuous 0..1 source. Layers combine by MAX; transients ride over them.
        /// <paramref name="autoZeroMs"/> makes the layer self-clear (live sliders).</summary>
        public void SetLayer(HapticLayer layer, double value, int autoZeroMs = 0)
            => _mixer.SetLayer(layer, value, autoZeroMs);

        public double GetLayer(HapticLayer layer) => _mixer.GetLayer(layer);

        /// <summary>Everything off NOW, bypassing throttles and unchanged-send suppression.</summary>
        public void PanicStop() => _mixer.PanicStop();

        /// <summary>Play a rendered pattern with an explicit priority (the DtRH director's tiers).</summary>
        public async Task PlayPatternAsync(double intensity, int durationMs, VibrationMode mode,
                                           int priority, ToyRole target = ToyRole.All,
                                           CancellationToken token = default)
        {
            var steps = HapticPatterns.Render(mode, intensity, durationMs, priority, target);
            var seq = _mixer.Play(steps);
            if (!token.CanBeCanceled) { await seq.Completion; return; }
            using var reg = token.Register(seq.Cancel);
            try { await seq.Completion; } catch (OperationCanceledException) { }
        }

        private HapticSequence PostEvent(HapticEventKind kind, double? intensityOverride,
                                         int? durationOverride, VibrationMode? modeOverride, int priority)
        {
            var rule = Settings.V2.Rule(kind);
            if (!rule.Enabled) return HapticSequence.Completed();

            var intensity = Math.Clamp(intensityOverride ?? rule.Intensity, MinPerceptibleIntensity, 1.0);
            var mode = modeOverride ?? rule.Mode;
            var duration = durationOverride ?? DefaultDurationMs(kind);

            var steps = HapticPatterns.Render(mode, intensity, duration, priority, rule.Target);
            var seq = _mixer.Play(steps);
            TrackKind(kind, seq);
            Announce(kind.ToString(), intensity);
            return seq;
        }

        private static int DefaultDurationMs(HapticEventKind kind) => kind switch
        {
            HapticEventKind.BubblePop => 100,
            HapticEventKind.BouncingTextBounce => 60,
            HapticEventKind.BlinkPulse => 150,
            HapticEventKind.VideoTargetHit => 100,
            HapticEventKind.SubliminalTrigger => 150,
            HapticEventKind.KeywordTrigger => 150,
            HapticEventKind.GazeReward => 150,
            HapticEventKind.AiCommand => 800,
            HapticEventKind.ToyButtonReward => 250,
            _ => 300
        };

        private void TrackKind(HapticEventKind kind, HapticSequence seq)
        {
            lock (_liveGate) _liveByKind[kind] = seq;
        }

        private void CancelKind(HapticEventKind kind)
        {
            HapticSequence? seq;
            lock (_liveGate)
            {
                _liveByKind.TryGetValue(kind, out seq);
                _liveByKind.Remove(kind);
            }
            seq?.Cancel();
        }

        private void Announce(string label, double intensity)
        {
            try { HapticTriggered?.Invoke(this, $"{label}: {(int)(intensity * 100)}%"); } catch { }
        }

        // ================================================================== legacy shims

        /// <summary>
        /// Lowest intensity that still maps to a real vibration level (#516). Mirrors
        /// <see cref="HapticMixer.MinPerceptibleIntensity"/>.
        /// </summary>
        private const double MinPerceptibleIntensity = HapticMixer.MinPerceptibleIntensity;

        private static double GetSliderIntensity(double sliderValue)
            => Math.Clamp(sliderValue, MinPerceptibleIntensity, 1.0);

        /// <summary>
        /// Legacy entry point: renders <paramref name="mode"/> as a mixer envelope sequence and
        /// awaits it. The six modes now actually feel different (see <see cref="HapticPatterns"/>).
        /// </summary>
        public Task ApplyVibrationModeAsync(double intensity, int durationMs, VibrationMode mode,
                                            CancellationToken? token = null)
            => PlayPatternAsync(intensity, durationMs, mode, priority: 0, target: ToyRole.All,
                                token: token ?? CancellationToken.None);

        public Task TriggerAsync(string eventType, double sliderIntensity, int durationMs)
        {
            var kind = MapEventType(eventType);
            Log.Debug("TriggerAsync: {Event} -> {Kind} at {Intensity}% for {Duration}ms",
                eventType, kind, (int)(sliderIntensity * 100), durationMs);
            var seq = PostEvent(kind, GetSliderIntensity(sliderIntensity), durationMs, null, 0);
            return seq.Completion;
        }

        private static HapticEventKind MapEventType(string eventType) => eventType switch
        {
            "BubblePop" => HapticEventKind.BubblePop,
            "FlashDisplay" => HapticEventKind.FlashDecay,
            "FlashClick" => HapticEventKind.FlashClick,
            "TargetHit" => HapticEventKind.VideoTargetHit,
            "Subliminal" => HapticEventKind.SubliminalTrigger,
            "Keyword" => HapticEventKind.KeywordTrigger,
            "LevelUp" => HapticEventKind.LevelUp,
            "Achievement" => HapticEventKind.Achievement,
            "Quest" => HapticEventKind.QuestComplete,
            "BouncingText" => HapticEventKind.BouncingTextBounce,
            "Blink" => HapticEventKind.BlinkPulse,
            "Gaze" => HapticEventKind.GazeReward,
            "Dtrh" => HapticEventKind.DtrhAccent,
            _ => HapticEventKind.AiCommand      // remote_control, AI commands, anything ad-hoc
        };

        public async Task<HapticTestResult> TestAsync()
        {
            if (!_deviceManager.IsConnected)
            {
                App.Logger?.Warning("TestAsync: Not connected");
                Error?.Invoke(this, "Not connected to any device");
                return HapticTestResult.NotConnected;
            }

            // IsConnected can stay true after a VPN flip breaks routing, so confirm on the wire.
            var reachable = await _deviceManager.PingAsync();
            if (!reachable)
            {
                App.Logger?.Warning("TestAsync: Device unreachable — likely VPN/network change");
                await _deviceManager.DisconnectAsync();
                Error?.Invoke(this, "Device unreachable");
                return HapticTestResult.Unreachable;
            }

            App.Logger?.Information("TestAsync: Starting test pattern");
            // The test must work even if the user has not flipped the master toggle on yet.
            _mixer.AllowTestWindow(4000);
            // Three steps up the range, on a high priority so ambient layers can't mask them.
            var steps = new List<HapticPulseStep>
            {
                new(0,    new HapticPulse(0.3, 60, 440, 100, 5)),
                new(1100, new HapticPulse(0.6, 60, 440, 100, 5)),
                new(2200, new HapticPulse(1.0, 60, 740, 120, 5)),
            };
            await _mixer.Play(steps).Completion;
            App.Logger?.Information("TestAsync: Test pattern completed");
            return HapticTestResult.Success;
        }

        /// <summary>Stop everything currently playing (transients AND continuous layers).</summary>
        public Task StopAsync()
        {
            _mixer.ClearAll();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Live intensity control from the slider. Holds the Manual layer at the requested level
        /// and self-clears after 1.5s, matching the old "send a 1.5s vibrate" feel without
        /// leaving the toy running if the user walks away mid-drag.
        /// </summary>
        public Task LiveIntensityUpdateAsync(double intensity)
        {
            if (intensity <= 0)
            {
                _mixer.SetLayer(HapticLayer.Manual, 0);
                Announce("Live", 0);
                return Task.CompletedTask;
            }
            var clamped = Math.Clamp(intensity, 0.01, 1.0);
            _mixer.SetLayer(HapticLayer.Manual, clamped, autoZeroMs: 1500);
            Announce("Live", clamped);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Continuous intensity for audio-synced playback. Called at 20-50 Hz by AudioSyncService;
        /// this now costs a lock and an array write — the 10 Hz mixer loop does the sending.
        /// </summary>
        public Task SetSyncIntensityAsync(double intensity)
        {
            if (!Settings.AudioSync.Enabled)
            {
                _mixer.SetLayer(HapticLayer.AudioSync, 0);
                return Task.CompletedTask;
            }
            var clamped = Math.Clamp(intensity, Settings.AudioSync.MinIntensity, Settings.AudioSync.MaxIntensity);
            _mixer.SetLayer(HapticLayer.AudioSync, clamped);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Play an authored keyframe envelope (Deeper runtime + its editor preview).
        /// Deliberately NOT gated on Settings.AudioSync.Enabled — that default-OFF toggle belongs
        /// to a different feature and would drop every Deeper haptic.
        /// </summary>
        public Task SetSyncPatternAsync(float[] intensities, int totalDurationMs)
        {
            if (intensities == null || intensities.Length == 0 || totalDurationMs <= 0) return Task.CompletedTask;

            var values = new double[intensities.Length];
            for (int i = 0; i < intensities.Length; i++)
                values[i] = Math.Clamp(intensities[i], 0f, 1f);

            // Authored intensities are used as-is: the audio-sync min/max sliders tune a different
            // feature and would distort the pattern.
            _mixer.PlayLayerEnvelope(HapticLayer.Pattern, values, totalDurationMs);
            return Task.CompletedTask;
        }

        // === SPECIAL PATTERNS ===

        public Task LevelUpPatternAsync()
        {
            var rule = Settings.V2.Rule(HapticEventKind.LevelUp);
            if (!rule.Enabled) return Task.CompletedTask;

            var intensity = GetSliderIntensity(rule.Intensity);
            // Celebration: same intensity, building durations (the original ladder).
            var seq = PlayLadder(HapticEventKind.LevelUp, intensity, rule.Mode,
                new[] { (0, 100), (250, 150), (600, 200), (1050, 300), (1700, 150), (2050, 100) });
            Announce("LevelUp", intensity);
            return seq.Completion;
        }

        public Task AchievementPatternAsync()
        {
            var rule = Settings.V2.Rule(HapticEventKind.Achievement);
            if (!rule.Enabled) return Task.CompletedTask;

            var intensity = GetSliderIntensity(rule.Intensity);
            var seq = PlayLadder(HapticEventKind.Achievement, intensity, rule.Mode,
                new[] { (0, 100), (250, 200), (700, 100), (950, 300), (1600, 150) });
            Announce("Achievement", intensity);
            return seq.Completion;
        }

        private HapticSequence PlayLadder(HapticEventKind kind, double intensity, VibrationMode mode,
                                          (int offsetMs, int durationMs)[] rungs, int priority = 2)
        {
            var steps = new List<HapticPulseStep>();
            var target = Settings.V2.Rule(kind).Target;
            foreach (var (offset, duration) in rungs)
                HapticPatterns.Append(steps, HapticPatterns.Render(mode, intensity, duration, priority, target), offset);
            var seq = _mixer.Play(steps);
            TrackKind(kind, seq);
            return seq;
        }

        /// <summary>
        /// Ramp the video background layer between two fractions of the Video slider.
        /// No production caller as of v6.7 — kept so nothing breaks if one returns.
        /// </summary>
        public Task RampUpAsync(double startPercent, double endPercent, int totalDurationMs, int steps = 5)
        {
            if (!Settings.VideoEnabled) return Task.CompletedTask;

            var maxIntensity = GetSliderIntensity(Settings.VideoIntensity);
            steps = Math.Clamp(steps, 1, 64);
            var values = new double[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                var percent = startPercent + (endPercent - startPercent) * (i / (double)steps);
                values[i] = Math.Clamp(maxIntensity * percent, 0, 1);
            }
            _mixer.PlayLayerEnvelope(HapticLayer.Video, values, Math.Max(1, totalDurationMs));
            return Task.CompletedTask;
        }

        // === FLASH DECAY SYSTEM ===

        /// <summary>Vibe that decays over ~2s. The slider sets the starting intensity; the decay
        /// curve (0.7^n over 8 rungs) is ours and is preserved exactly.</summary>
        public Task FlashDecayVibeAsync() => PlayDecayLadder(HapticEventKind.FlashDecay);

        /// <summary>Flash click — same decay shape, its own routing row. Refreshes (replaces) any
        /// decay already running so a click ladder can't stack on itself.</summary>
        public Task FlashClickVibeAsync() => PlayDecayLadder(HapticEventKind.FlashClick);

        private Task PlayDecayLadder(HapticEventKind kind)
        {
            var rule = Settings.V2.Rule(kind);
            if (!rule.Enabled) return Task.CompletedTask;

            // Replace whatever decay is running (both flash kinds share the visual moment).
            CancelKind(HapticEventKind.FlashDecay);
            CancelKind(HapticEventKind.FlashClick);

            var start = GetSliderIntensity(rule.Intensity);
            var steps = new List<HapticPulseStep>();
            for (int i = 0; i < 8; i++)
            {
                var intensity = Math.Max(start * Math.Pow(0.7, i), MinPerceptibleIntensity);
                HapticPatterns.Append(steps,
                    HapticPatterns.Render(rule.Mode, intensity, 250, priority: 1, target: rule.Target),
                    offsetMs: i * 450);
            }
            var seq = _mixer.Play(steps);
            TrackKind(kind, seq);
            Announce(kind == HapticEventKind.FlashClick ? "Flash Click" : "Flash", start);
            return seq.Completion;
        }

        // === BUBBLE COMBO SYSTEM ===
        private DateTime _lastBubblePop = DateTime.MinValue;
        private int _bubbleCombo = 0;

        public Task BubblePopAsync()
        {
            var rule = Settings.V2.Rule(HapticEventKind.BubblePop);
            if (!rule.Enabled) return Task.CompletedTask;

            var now = DateTime.Now;
            lock (_liveGate)
            {
                if ((now - _lastBubblePop).TotalMilliseconds > 2000) _bubbleCombo = 0;   // 2s combo window
                _bubbleCombo++;
                _lastBubblePop = now;
            }

            var seq = PostEvent(HapticEventKind.BubblePop, GetSliderIntensity(rule.Intensity), 100, null, 1);
            HapticTriggered?.Invoke(this, $"Bubble: {_bubbleCombo}x ({(int)(rule.Intensity * 100)}%)");
            return seq.Completion;
        }

        // === BOUNCING TEXT ===

        /// <summary>Brief sharp pulse when bouncing text hits a screen edge.</summary>
        public Task BouncingTextBounceAsync()
            => PostEvent(HapticEventKind.BouncingTextBounce, null, 60, null, 0).Completion;

        // === BLINK TRAINER ===

        /// <summary>Short pulse on each blink detected by the Lab "Blink Trainer".</summary>
        public Task BlinkPulseAsync()
            => PostEvent(HapticEventKind.BlinkPulse, null, 150, null, 0).Completion;

        // === VIDEO BACKGROUND VIBE ===
        private int _videoTargetHits = 0;

        public Task StartVideoBackgroundVibeAsync()
        {
            if (!Settings.VideoEnabled) { _mixer.SetLayer(HapticLayer.Video, 0); return Task.CompletedTask; }

            // Slider at 0 means "no background vibe, target-hit spikes only" — the perceptible
            // floor must not turn 0 into a constant buzz.
            if (Settings.VideoIntensity <= 0)
            {
                _mixer.SetLayer(HapticLayer.Video, 0);
                _videoTargetHits = 0;
                return Task.CompletedTask;
            }

            _videoTargetHits = 0;
            // Background vibe is 10% of the slider so target hits feel impactful.
            var level = Math.Max(Settings.VideoIntensity * 0.1, MinPerceptibleIntensity);
            _mixer.SetLayer(HapticLayer.Video, level);
            Announce("Video Background", level);
            return Task.CompletedTask;
        }

        public Task StopVideoBackgroundVibeAsync()
        {
            _videoTargetHits = 0;
            _mixer.SetLayer(HapticLayer.Video, 0);
            return Task.CompletedTask;
        }

        public Task VideoTargetHitAsync()
        {
            var rule = Settings.V2.Rule(HapticEventKind.VideoTargetHit);
            if (!rule.Enabled) return Task.CompletedTask;

            var hit = ++_videoTargetHits;
            // Priority 3: the spike rides OVER the video floor instead of replacing it, so the
            // old "resume the background afterwards" dance (and the race between overlapping
            // hits that flattened each other, #516) is gone.
            var seq = PostEvent(HapticEventKind.VideoTargetHit, GetSliderIntensity(rule.Intensity), 100, null, 3);
            HapticTriggered?.Invoke(this, $"Target Hit #{hit}: {(int)(rule.Intensity * 100)}%");
            return seq.Completion;
        }

        // === SUBLIMINAL PATTERN SYSTEM ===

        /// <summary>Short pulse for subliminal text; duration is keyed off the trigger's wording.</summary>
        public Task TriggerSubliminalPatternAsync(string triggerText)
        {
            var duration = TriggerDurationMs(triggerText);
            return PostEvent(HapticEventKind.SubliminalTrigger, null, duration, null, 1).Completion;
        }

        // === AWARENESS KEYWORD PATTERN ===

        /// <summary>
        /// Pulse for an Awareness keyword hit. Intensity comes from the trigger's own action config
        /// (the per-action slider in the preset editor), NOT a global slider, and it must not depend
        /// on the Subliminal feature's toggle.
        /// </summary>
        public Task TriggerKeywordPatternAsync(string triggerText, double intensity)
        {
            var duration = TriggerDurationMs(triggerText);
            return PostEvent(HapticEventKind.KeywordTrigger, GetSliderIntensity(intensity), duration, null, 1).Completion;
        }

        private int TriggerDurationMs(string triggerText)
        {
            var textLower = (triggerText ?? "").ToLowerInvariant();
            // Buttplug.io needs longer durations due to protocol overhead.
            var multiplier = IsButtplugProvider ? 2.0 : 1.0;
            if (textLower.Contains("cum") || textLower.Contains("collapse") || textLower.Contains("drop"))
                return (int)(250 * multiplier);      // slightly longer for intense triggers
            if (textLower.Contains("freeze") || textLower.Contains("zap"))
                return (int)(120 * multiplier);      // sharp quick burst
            return (int)(150 * multiplier);          // default: quick pulse
        }

        // === AVATAR EASTER EGG PATTERN ===

        /// <summary>Long vibe (~8s) for the avatar 20-click easter egg.</summary>
        public Task AvatarEasterEggPatternAsync()
        {
            var rule = Settings.V2.Rule(HapticEventKind.AvatarEasterEgg);
            if (!rule.Enabled) return Task.CompletedTask;

            var intensity = GetSliderIntensity(rule.Intensity);
            var steps = new List<HapticPulseStep>();
            for (int i = 0; i < 16; i++)
                steps.Add(new HapticPulseStep(i * 500, new HapticPulse(intensity, 20, 430, 50, 2, rule.Target)));
            steps.Add(new HapticPulseStep(8000, new HapticPulse(intensity, 20, 280, 50, 2, rule.Target)));
            steps.Add(new HapticPulseStep(8400, new HapticPulse(intensity, 20, 380, 60, 2, rule.Target)));

            var seq = _mixer.Play(steps);
            TrackKind(HapticEventKind.AvatarEasterEgg, seq);
            HapticTriggered?.Invoke(this, $"Avatar: Easter Egg! {(int)(intensity * 100)}%");
            return seq.Completion;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Settings.PropertyChanged -= OnSettingsChanged;
            StopPingTimer();
            // Never blocks: the mixer zeroes state synchronously and flushes the all-stop on a
            // background task with a hard 2s cap (plus a ProcessExit watchdog).
            _mixer.Dispose();
            _deviceManager.Dispose();
        }
    }
}
