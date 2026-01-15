using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using Serilog;

namespace ConditioningControlPanel.Services
{
    public class HapticService : IDisposable
    {
        private readonly MockHapticProvider _mockProvider;
        private readonly LovenseProvider _lovenseProvider;
        private readonly ButtplugProvider _buttplugProvider;
        private IHapticProvider? _activeProvider;
        private bool _disposed;
        private string? _currentEventType;

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<string>? DeviceDiscovered;
        public event EventHandler<string>? Error;
        public event EventHandler<string>? HapticTriggered;

        public HapticSettings Settings { get; }
        public bool IsConnected => _activeProvider?.IsConnected ?? false;
        public string ProviderName => _activeProvider?.Name ?? "None";
        public bool IsButtplugProvider => Settings.Provider == HapticProviderType.Buttplug;

        /// <summary>
        /// Buttplug.io has ~1.3s latency, so we need to trigger haptics earlier
        /// </summary>
        public int SubliminalAnticipationMs => IsButtplugProvider ? 1300 : 250;

        public System.Collections.Generic.List<string> ConnectedDevices =>
            _activeProvider?.ConnectedDevices ?? new System.Collections.Generic.List<string>();

        public HapticService(HapticSettings settings)
        {
            Settings = settings;
            _mockProvider = new MockHapticProvider();
            _lovenseProvider = new LovenseProvider();
            _buttplugProvider = new ButtplugProvider();

            // Wire up events from all providers
            WireProviderEvents(_mockProvider);
            WireProviderEvents(_lovenseProvider);
            WireProviderEvents(_buttplugProvider);

            // Listen for settings changes to enable live stop
            Settings.PropertyChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            // If master enabled is turned off, stop immediately
            if (e.PropertyName == nameof(HapticSettings.Enabled) && !Settings.Enabled)
            {
                _ = StopAsync();
                return;
            }

            // If a specific feature is turned off and it's currently running, stop
            if (_currentEventType != null)
            {
                var shouldStop = e.PropertyName switch
                {
                    nameof(HapticSettings.BubblePopEnabled) when _currentEventType == "BubblePop" => !Settings.BubblePopEnabled,
                    nameof(HapticSettings.FlashDisplayEnabled) when _currentEventType == "FlashDisplay" => !Settings.FlashDisplayEnabled,
                    nameof(HapticSettings.FlashClickEnabled) when _currentEventType == "FlashClick" => !Settings.FlashClickEnabled,
                    nameof(HapticSettings.VideoEnabled) when _currentEventType == "Video" => !Settings.VideoEnabled,
                    nameof(HapticSettings.TargetHitEnabled) when _currentEventType == "TargetHit" => !Settings.TargetHitEnabled,
                    nameof(HapticSettings.SubliminalEnabled) when _currentEventType == "Subliminal" => !Settings.SubliminalEnabled,
                    nameof(HapticSettings.LevelUpEnabled) when _currentEventType == "LevelUp" => !Settings.LevelUpEnabled,
                    nameof(HapticSettings.AchievementEnabled) when _currentEventType == "Achievement" => !Settings.AchievementEnabled,
                    nameof(HapticSettings.BouncingTextEnabled) when _currentEventType == "BouncingText" => !Settings.BouncingTextEnabled,
                    _ => false
                };

                if (shouldStop)
                {
                    _ = StopAsync();
                }
            }
        }

        private void WireProviderEvents(IHapticProvider provider)
        {
            provider.ConnectionChanged += (s, connected) => ConnectionChanged?.Invoke(this, connected);
            provider.DeviceDiscovered += (s, device) => DeviceDiscovered?.Invoke(this, device);
            provider.Error += (s, error) => Error?.Invoke(this, error);
        }

        public async Task<bool> ConnectAsync()
        {
            // Disconnect any existing provider
            await DisconnectAsync();

            // Select the provider based on settings
            _activeProvider = Settings.Provider switch
            {
                HapticProviderType.Mock => _mockProvider,
                HapticProviderType.Lovense => _lovenseProvider,
                HapticProviderType.Buttplug => _buttplugProvider,
                _ => null
            };

            if (_activeProvider == null)
            {
                Error?.Invoke(this, "No provider selected");
                return false;
            }

            // Set URLs for providers that need them
            if (_activeProvider is LovenseProvider lovense)
            {
                lovense.SetUrl(Settings.LovenseUrl);
            }
            else if (_activeProvider is ButtplugProvider buttplug)
            {
                buttplug.SetUrl(Settings.ButtplugUrl);
            }

            Log.Information("Connecting to haptic provider: {Provider}", _activeProvider.Name);
            var result = await _activeProvider.ConnectAsync();
            if (result)
            {
                // Auto-enable haptics when successfully connected
                Settings.Enabled = true;
            }
            return result;
        }

        public async Task DisconnectAsync()
        {
            if (_activeProvider != null)
            {
                await _activeProvider.DisconnectAsync();
                _activeProvider = null;
            }
        }

        /// <summary>
        /// Get slider intensity with minimum floor (devices need ~5% to respond)
        /// Slider value directly controls device power: 1% = min, 100% = max
        /// </summary>
        private double GetSliderIntensity(double sliderValue)
        {
            // Minimum 5% floor so device always responds, max 100%
            return Math.Clamp(sliderValue, 0.05, 1.0);
        }

        /// <summary>
        /// Apply a vibration pattern based on the selected mode
        /// Duration stays the same, pattern changes how vibration feels within that duration
        /// </summary>
        private async Task ApplyVibrationModeAsync(double intensity, int durationMs, VibrationMode mode, System.Threading.CancellationToken? token = null)
        {
            if (_activeProvider == null || !_activeProvider.IsConnected) return;

            switch (mode)
            {
                case VibrationMode.Constant:
                    // Simple continuous vibration
                    await _activeProvider.VibrateAsync(intensity, durationMs);
                    break;

                case VibrationMode.Pulse:
                    // Quick on/off pulses - 50ms on, 30ms off
                    var pulseCount = Math.Max(1, durationMs / 80);
                    for (int i = 0; i < pulseCount; i++)
                    {
                        if (token?.IsCancellationRequested == true) break;
                        await _activeProvider.VibrateAsync(intensity, 50);
                        if (i < pulseCount - 1) await Task.Delay(30);
                    }
                    break;

                case VibrationMode.Wave:
                    // Smooth ramp up then down
                    var waveSteps = 6;
                    var waveStepDuration = durationMs / (waveSteps * 2);
                    // Ramp up
                    for (int i = 1; i <= waveSteps; i++)
                    {
                        if (token?.IsCancellationRequested == true) break;
                        var stepIntensity = intensity * (i / (double)waveSteps);
                        await _activeProvider.VibrateAsync(Math.Max(stepIntensity, 0.05), waveStepDuration);
                    }
                    // Ramp down
                    for (int i = waveSteps - 1; i >= 0; i--)
                    {
                        if (token?.IsCancellationRequested == true) break;
                        var stepIntensity = intensity * (i / (double)waveSteps);
                        await _activeProvider.VibrateAsync(Math.Max(stepIntensity, 0.05), waveStepDuration);
                    }
                    break;

                case VibrationMode.Heartbeat:
                    // Double pulse pattern (ba-bump, pause, repeat)
                    var heartbeatCount = Math.Max(1, durationMs / 400);
                    for (int i = 0; i < heartbeatCount; i++)
                    {
                        if (token?.IsCancellationRequested == true) break;
                        // First beat (strong)
                        await _activeProvider.VibrateAsync(intensity, 80);
                        await Task.Delay(60);
                        // Second beat (lighter)
                        await _activeProvider.VibrateAsync(intensity * 0.7, 60);
                        if (i < heartbeatCount - 1) await Task.Delay(200);
                    }
                    break;

                case VibrationMode.Escalate:
                    // Ramps up from low to full intensity
                    var escalateSteps = 8;
                    var escalateStepDuration = durationMs / escalateSteps;
                    for (int i = 1; i <= escalateSteps; i++)
                    {
                        if (token?.IsCancellationRequested == true) break;
                        var stepIntensity = intensity * (0.2 + 0.8 * (i / (double)escalateSteps));
                        await _activeProvider.VibrateAsync(stepIntensity, escalateStepDuration);
                    }
                    break;

                case VibrationMode.Earthquake:
                    // Random intensity variations
                    var quakeSteps = Math.Max(2, durationMs / 100);
                    var random = new Random();
                    for (int i = 0; i < quakeSteps; i++)
                    {
                        if (token?.IsCancellationRequested == true) break;
                        // Random between 30% and 100% of set intensity
                        var randomIntensity = intensity * (0.3 + random.NextDouble() * 0.7);
                        await _activeProvider.VibrateAsync(randomIntensity, 80);
                        await Task.Delay(20);
                    }
                    break;
            }
        }

        /// <summary>
        /// Check if a feature is enabled
        /// </summary>
        private bool IsFeatureEnabled(string eventType)
        {
            return eventType switch
            {
                "BubblePop" => Settings.BubblePopEnabled,
                "FlashDisplay" => Settings.FlashDisplayEnabled,
                "FlashClick" => Settings.FlashClickEnabled,
                "Video" => Settings.VideoEnabled,
                "TargetHit" => Settings.TargetHitEnabled,
                "Subliminal" => Settings.SubliminalEnabled,
                "LevelUp" => Settings.LevelUpEnabled,
                "Achievement" => Settings.AchievementEnabled,
                "BouncingText" => Settings.BouncingTextEnabled,
                _ => true
            };
        }

        public async Task TriggerAsync(string eventType, double sliderIntensity, int durationMs)
        {
            Log.Debug("TriggerAsync called: {Event}, Enabled={Enabled}, Provider={Provider}, Connected={Connected}",
                eventType, Settings.Enabled, _activeProvider?.Name ?? "null", _activeProvider?.IsConnected ?? false);

            if (!Settings.Enabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            if (!IsFeatureEnabled(eventType)) return;

            _currentEventType = eventType;

            // Slider directly controls device power
            var intensity = GetSliderIntensity(sliderIntensity);

            Log.Debug("Haptic trigger: {Event} at {Intensity}% for {Duration}ms",
                eventType, (int)(intensity * 100), durationMs);

            HapticTriggered?.Invoke(this, $"{eventType}: {(int)(intensity * 100)}%");

            await _activeProvider.VibrateAsync(intensity, durationMs);
            _currentEventType = null;
        }

        public async Task TestAsync()
        {
            if (_activeProvider == null || !_activeProvider.IsConnected)
            {
                Error?.Invoke(this, "Not connected to any device");
                return;
            }

            // Run a test pattern at fixed levels
            var g = 1.0;
            await _activeProvider.VibrateAsync(0.3 * g, 200);
            await Task.Delay(300);
            await _activeProvider.VibrateAsync(0.6 * g, 200);
            await Task.Delay(300);
            await _activeProvider.VibrateAsync(1.0 * g, 400);
        }

        public async Task StopAsync()
        {
            _currentEventType = null;
            if (_activeProvider != null)
            {
                await _activeProvider.StopAsync();
            }
        }

        /// <summary>
        /// Live intensity control from slider - directly sets vibration level
        /// 0% = stop, 1% = minimum device capability, 100% = maximum
        /// </summary>
        public async Task LiveIntensityUpdateAsync(double intensity)
        {
            if (_activeProvider == null || !_activeProvider.IsConnected)
                return;

            // Stop if intensity is 0
            if (intensity <= 0)
            {
                await _activeProvider.StopAsync();
                HapticTriggered?.Invoke(this, "Live: Stopped");
                return;
            }

            // Clamp to valid range (1-100%)
            var clampedIntensity = Math.Clamp(intensity, 0.01, 1.0);

            // Send 1.5 second vibration - just enough to feel the level
            await _activeProvider.VibrateAsync(clampedIntensity, 1500);

            HapticTriggered?.Invoke(this, $"Live: {(int)(clampedIntensity * 100)}%");
        }

        // === SPECIAL PATTERNS ===

        public async Task LevelUpPatternAsync()
        {
            if (!Settings.Enabled || !Settings.LevelUpEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _currentEventType = "LevelUp";
            // Slider directly controls device power
            var intensity = GetSliderIntensity(Settings.LevelUpIntensity);
            var mode = Settings.LevelUpMode;
            HapticTriggered?.Invoke(this, $"LevelUp: {(int)(intensity * 100)}%");

            // Celebration pattern - same intensity, building duration, using selected mode
            await ApplyVibrationModeAsync(intensity, 100, mode);
            if (!Settings.LevelUpEnabled) { _currentEventType = null; return; }
            await Task.Delay(150);

            await ApplyVibrationModeAsync(intensity, 150, mode);
            if (!Settings.LevelUpEnabled) { _currentEventType = null; return; }
            await Task.Delay(200);

            await ApplyVibrationModeAsync(intensity, 200, mode);
            if (!Settings.LevelUpEnabled) { _currentEventType = null; return; }
            await Task.Delay(250);

            await ApplyVibrationModeAsync(intensity, 300, mode);
            if (!Settings.LevelUpEnabled) { _currentEventType = null; return; }
            await Task.Delay(350);

            await ApplyVibrationModeAsync(intensity, 150, mode);
            if (!Settings.LevelUpEnabled) { _currentEventType = null; return; }
            await Task.Delay(200);

            await ApplyVibrationModeAsync(intensity, 100, mode);
            _currentEventType = null;
        }

        public async Task AchievementPatternAsync()
        {
            if (!Settings.Enabled || !Settings.AchievementEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _currentEventType = "Achievement";
            // Slider directly controls device power
            var intensity = GetSliderIntensity(Settings.AchievementIntensity);
            var mode = Settings.AchievementMode;
            HapticTriggered?.Invoke(this, $"Achievement: {(int)(intensity * 100)}%");

            // Achievement pattern - triumphant pulses, using selected mode
            await ApplyVibrationModeAsync(intensity, 100, mode);
            if (!Settings.AchievementEnabled) { _currentEventType = null; return; }
            await Task.Delay(150);

            await ApplyVibrationModeAsync(intensity, 200, mode);
            if (!Settings.AchievementEnabled) { _currentEventType = null; return; }
            await Task.Delay(250);

            await ApplyVibrationModeAsync(intensity, 100, mode);
            if (!Settings.AchievementEnabled) { _currentEventType = null; return; }
            await Task.Delay(150);

            await ApplyVibrationModeAsync(intensity, 300, mode);
            if (!Settings.AchievementEnabled) { _currentEventType = null; return; }
            await Task.Delay(350);

            await ApplyVibrationModeAsync(intensity, 150, mode);
            _currentEventType = null;
        }

        public async Task RampUpAsync(double startPercent, double endPercent, int totalDurationMs, int steps = 5)
        {
            if (!Settings.Enabled || !Settings.VideoEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _currentEventType = "Video";
            // Slider controls the max intensity, start/end are percentages of that
            var maxIntensity = GetSliderIntensity(Settings.VideoIntensity);
            var stepDuration = totalDurationMs / steps;
            var intensityStep = (endPercent - startPercent) / steps;

            for (int i = 0; i <= steps; i++)
            {
                if (!Settings.VideoEnabled) { _currentEventType = null; return; }
                var percent = startPercent + (intensityStep * i);
                var intensity = maxIntensity * percent;
                await _activeProvider.VibrateAsync(Math.Clamp(intensity, 0.05, 1), stepDuration);
                if (i < steps) await Task.Delay(stepDuration);
            }
            _currentEventType = null;
        }

        // === FLASH DECAY SYSTEM ===
        private System.Threading.CancellationTokenSource? _flashDecayCts;

        /// <summary>
        /// Start a vibe that rapidly decays over 2 seconds.
        /// Slider controls starting intensity, we control the decay pattern.
        /// </summary>
        public async Task FlashDecayVibeAsync()
        {
            if (!Settings.Enabled || !Settings.FlashDisplayEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            // Cancel any existing decay
            _flashDecayCts?.Cancel();
            _flashDecayCts = new System.Threading.CancellationTokenSource();
            var token = _flashDecayCts.Token;

            _currentEventType = "FlashDisplay";
            // Slider directly controls starting intensity
            var startIntensity = GetSliderIntensity(Settings.FlashDisplayIntensity);
            var mode = Settings.FlashDisplayMode;
            HapticTriggered?.Invoke(this, $"Flash: {(int)(startIntensity * 100)}%");

            try
            {
                // Decay over 2 seconds in 8 steps (250ms each), using selected mode
                for (int i = 0; i < 8; i++)
                {
                    if (token.IsCancellationRequested || !Settings.FlashDisplayEnabled) break;

                    // Exponential decay from slider intensity
                    var decayFactor = Math.Pow(0.7, i);
                    var intensity = Math.Max(startIntensity * decayFactor, 0.05);

                    await ApplyVibrationModeAsync(intensity, 250, mode, token);
                    await Task.Delay(200, token);
                }

                // Final stop
                if (!token.IsCancellationRequested)
                    await _activeProvider.StopAsync();
            }
            catch (OperationCanceledException) { }
            finally
            {
                _currentEventType = null;
            }
        }

        /// <summary>
        /// Flash click - refreshes the decay with click intensity
        /// </summary>
        public async Task FlashClickVibeAsync()
        {
            if (!Settings.Enabled || !Settings.FlashClickEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _flashDecayCts?.Cancel();
            _flashDecayCts = new System.Threading.CancellationTokenSource();
            var token = _flashDecayCts.Token;

            _currentEventType = "FlashClick";
            // Slider directly controls starting intensity
            var startIntensity = GetSliderIntensity(Settings.FlashClickIntensity);
            var mode = Settings.FlashClickMode;
            HapticTriggered?.Invoke(this, $"Flash Click: {(int)(startIntensity * 100)}%");

            try
            {
                // Decay over 2 seconds in 8 steps, using selected mode
                for (int i = 0; i < 8; i++)
                {
                    if (token.IsCancellationRequested || !Settings.FlashClickEnabled) break;

                    var decayFactor = Math.Pow(0.7, i);
                    var intensity = Math.Max(startIntensity * decayFactor, 0.05);

                    await ApplyVibrationModeAsync(intensity, 250, mode, token);
                    await Task.Delay(200, token);
                }

                if (!token.IsCancellationRequested)
                    await _activeProvider.StopAsync();
            }
            catch (OperationCanceledException) { }
            finally
            {
                _currentEventType = null;
            }
        }

        // === BUBBLE COMBO SYSTEM ===
        private DateTime _lastBubblePop = DateTime.MinValue;
        private int _bubbleCombo = 0;

        public async Task BubblePopAsync()
        {
            Log.Debug("BubblePopAsync called: Enabled={Enabled}, BubbleEnabled={BubbleEnabled}, Connected={Connected}",
                Settings.Enabled, Settings.BubblePopEnabled, _activeProvider?.IsConnected ?? false);

            if (!Settings.Enabled || !Settings.BubblePopEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _currentEventType = "BubblePop";
            var now = DateTime.Now;
            // 2 second combo window
            if ((now - _lastBubblePop).TotalMilliseconds > 2000) _bubbleCombo = 0;
            _bubbleCombo++;
            _lastBubblePop = now;

            // Slider directly controls device power
            var intensity = GetSliderIntensity(Settings.BubblePopIntensity);

            HapticTriggered?.Invoke(this, $"Bubble: {_bubbleCombo}x ({(int)(intensity * 100)}%)");
            // 100ms pattern using selected mode
            await ApplyVibrationModeAsync(intensity, 100, Settings.BubblePopMode);
            _currentEventType = null;
        }

        // === BOUNCING TEXT ===

        /// <summary>
        /// Brief sharp pulse when bouncing text hits screen edge
        /// </summary>
        public async Task BouncingTextBounceAsync()
        {
            if (!Settings.Enabled || !Settings.BouncingTextEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _currentEventType = "BouncingText";

            // Slider directly controls device power
            var intensity = GetSliderIntensity(Settings.BouncingTextIntensity);
            HapticTriggered?.Invoke(this, $"Bounce: {(int)(intensity * 100)}%");

            // Quick 60ms pattern using selected mode
            await ApplyVibrationModeAsync(intensity, 60, Settings.BouncingTextMode);
            _currentEventType = null;
        }

        // === VIDEO BACKGROUND VIBE ===
        private System.Threading.CancellationTokenSource? _videoVibeCts;
        private int _videoTargetHits = 0;
        private DateTime _videoStartTime;
        private double _currentVideoIntensity = 0;

        public async Task StartVideoBackgroundVibeAsync()
        {
            if (!Settings.Enabled || !Settings.VideoEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _videoVibeCts?.Cancel();
            _videoVibeCts = new System.Threading.CancellationTokenSource();
            var token = _videoVibeCts.Token;
            _videoTargetHits = 0;
            _videoStartTime = DateTime.Now;

            _currentEventType = "Video";
            // Background vibe is 10% of slider so target hits feel impactful
            _currentVideoIntensity = Math.Max(Settings.VideoIntensity * 0.1, 0.05);
            HapticTriggered?.Invoke(this, $"Video: Background {(int)(_currentVideoIntensity * 100)}%");

            try
            {
                while (!token.IsCancellationRequested && Settings.VideoEnabled)
                {
                    // Send long duration command (30 sec) - will be overridden by target hits
                    await _activeProvider.VibrateAsync(_currentVideoIntensity, 30000);

                    // Check less frequently since intensity is constant
                    await Task.Delay(5000, token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _currentEventType = null;
            }
        }

        public async Task StopVideoBackgroundVibeAsync()
        {
            _videoVibeCts?.Cancel();
            _videoVibeCts = null;
            _videoTargetHits = 0;
            _currentVideoIntensity = 0;
            await StopAsync();
        }

        public async Task VideoTargetHitAsync()
        {
            // Check if target hit haptics are enabled (separate from video background)
            if (!Settings.Enabled || !Settings.TargetHitEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _videoTargetHits++;

            // Use target hit intensity (not video intensity) for the spike
            var spikeIntensity = GetSliderIntensity(Settings.TargetHitIntensity);
            HapticTriggered?.Invoke(this, $"Target Hit #{_videoTargetHits}: {(int)(spikeIntensity * 100)}%");

            // Quick intensity spike using target hit mode - short 100ms burst
            // This replaces the background vibe briefly with higher intensity
            await ApplyVibrationModeAsync(spikeIntensity, 100, Settings.TargetHitMode);

            // Immediately resume background vibe without delay (no pause!)
            if (_currentVideoIntensity > 0 && Settings.VideoEnabled)
            {
                await _activeProvider.VibrateAsync(_currentVideoIntensity, 30000);
            }
        }

        // === SUBLIMINAL PATTERN SYSTEM ===
        private static readonly Random _random = new Random();

        /// <summary>
        /// Trigger short haptic pulse for subliminal text
        /// Slider directly controls intensity, we control duration
        /// </summary>
        public async Task TriggerSubliminalPatternAsync(string triggerText)
        {
            if (!Settings.Enabled || !Settings.SubliminalEnabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _currentEventType = "Subliminal";
            // Slider directly controls device power
            var intensity = GetSliderIntensity(Settings.SubliminalIntensity);
            var textLower = triggerText.ToLowerInvariant();

            HapticTriggered?.Invoke(this, $"Subliminal: {(int)(intensity * 100)}%");

            try
            {
                // Short patterns based on trigger type, using selected mode
                // Buttplug.io needs longer durations due to protocol overhead
                int durationMs;
                var durationMultiplier = IsButtplugProvider ? 2.0 : 1.0;

                if (textLower.Contains("cum") || textLower.Contains("collapse") || textLower.Contains("drop"))
                {
                    // Slightly longer for intense triggers
                    durationMs = (int)(250 * durationMultiplier);
                }
                else if (textLower.Contains("freeze") || textLower.Contains("zap"))
                {
                    // Sharp quick burst
                    durationMs = (int)(120 * durationMultiplier);
                }
                else
                {
                    // Default: quick pulse
                    durationMs = (int)(150 * durationMultiplier);
                }
                await ApplyVibrationModeAsync(intensity, durationMs, Settings.SubliminalMode);
            }
            finally
            {
                _currentEventType = null;
            }
        }

        // === AVATAR EASTER EGG PATTERN ===

        /// <summary>
        /// Long vibe (~8 seconds) for the avatar 20-click easter egg
        /// Slider directly controls intensity
        /// </summary>
        public async Task AvatarEasterEggPatternAsync()
        {
            if (!Settings.Enabled || _activeProvider == null || !_activeProvider.IsConnected)
                return;

            _currentEventType = "Achievement";
            // Slider directly controls device power
            var intensity = GetSliderIntensity(Settings.AchievementIntensity);
            HapticTriggered?.Invoke(this, $"Avatar: Easter Egg! {(int)(intensity * 100)}%");

            try
            {
                // ~8 second pattern
                for (int i = 0; i < 16; i++)
                {
                    if (!Settings.AchievementEnabled) break;
                    await _activeProvider.VibrateAsync(intensity, 450);
                    await Task.Delay(50);
                }

                // Final pulses
                await _activeProvider.VibrateAsync(intensity, 300);
                await Task.Delay(100);
                await _activeProvider.VibrateAsync(intensity, 400);
            }
            finally
            {
                _currentEventType = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Settings.PropertyChanged -= OnSettingsChanged;
            DisconnectAsync().Wait(1000);
        }
    }
}
