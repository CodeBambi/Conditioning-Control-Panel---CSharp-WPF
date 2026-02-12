using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Models;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Intercepts typed text system-wide and fires multi-modal responses (audio, visual, haptic, XP)
    /// when configured keyword triggers are detected.
    /// Requires Patreon Tier 2 or whitelist.
    /// </summary>
    public class KeywordTriggerService : IDisposable
    {
        #region Fields

        private readonly StringBuilder _buffer = new(200);
        private DateTime _lastKeyTime = DateTime.MinValue;
        private DateTime _lastGlobalTriggerTime = DateTime.MinValue;
        private bool _isActive;
        private bool _disposed;

        // Own audio player to avoid conflicting with AudioService/SubliminalService
        private WaveOutEvent? _triggerPlayer;
        private AudioFileReader? _triggerAudioFile;
        private readonly object _audioLock = new();

        // Audio file search cache
        private string[]? _audioFilesCache;
        private DateTime _audioFilesCacheTime = DateTime.MinValue;
        private readonly string _audioPath;

        // Session awareness callback
        private Func<bool>? _isSessionActive;

        /// <summary>Fired when a keyword trigger activates</summary>
        public event EventHandler<KeywordTrigger>? TriggerFired;

        #endregion

        #region Constructor

        public KeywordTriggerService()
        {
            _audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sub_audio");
            App.Logger?.Information("KeywordTriggerService initialized");
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start listening for keyword triggers.
        /// </summary>
        public void Start()
        {
            if (_isActive) return;
            if (!HasAccess())
            {
                App.Logger?.Debug("KeywordTriggerService: No access (requires T2 or whitelist)");
                return;
            }

            _isActive = true;
            _buffer.Clear();
            App.Logger?.Information("KeywordTriggerService started");
        }

        /// <summary>
        /// Stop listening for keyword triggers.
        /// </summary>
        public void Stop()
        {
            if (!_isActive) return;
            _isActive = false;
            _buffer.Clear();
            StopTriggerAudio();
            App.Logger?.Information("KeywordTriggerService stopped");
        }

        /// <summary>
        /// Set a callback to check if a session engine is currently running.
        /// </summary>
        public void SetSessionActiveCallback(Func<bool> callback)
        {
            _isSessionActive = callback;
        }

        /// <summary>
        /// Called from the keyboard hook on every key press.
        /// Translates vkCode → character, appends to buffer, checks for matches.
        /// </summary>
        public void OnKeyPressed(Key key, int vkCode)
        {
            if (!_isActive || _disposed) return;

            var settings = App.Settings?.Current;
            if (settings == null || !settings.KeywordTriggersEnabled) return;

            // Check buffer timeout — reset if too much time has passed
            var now = DateTime.Now;
            var timeoutMs = settings.KeywordBufferTimeoutMs;
            if ((now - _lastKeyTime).TotalMilliseconds > timeoutMs && _buffer.Length > 0)
            {
                _buffer.Clear();
            }
            _lastKeyTime = now;

            // Handle special keys
            if (key == Key.Return || key == Key.Escape || key == Key.Tab)
            {
                _buffer.Clear();
                return;
            }

            if (key == Key.Back)
            {
                if (_buffer.Length > 0)
                    _buffer.Remove(_buffer.Length - 1, 1);
                return;
            }

            if (key == Key.Space)
            {
                _buffer.Append(' ');
                CheckForMatches();
                return;
            }

            // Translate vkCode → actual character using Win32
            var ch = TranslateVkCode(vkCode);
            if (ch == null) return;

            _buffer.Append(ch.Value);

            // Cap buffer length
            if (_buffer.Length > 200)
                _buffer.Remove(0, _buffer.Length - 200);

            CheckForMatches();
        }

        /// <summary>
        /// Check if user has access to keyword triggers (T2 or whitelisted).
        /// </summary>
        public static bool HasAccess()
        {
            var patreon = App.Patreon;
            if (patreon == null) return false;
            return patreon.CurrentTier >= PatreonTier.Level2 || patreon.IsWhitelisted;
        }

        /// <summary>
        /// Find a matching audio file for a keyword in the sub_audio folder.
        /// Uses the same logic as SubliminalService.FindLinkedAudio.
        /// </summary>
        public string? FindLinkedAudio(string keyword)
        {
            var cleanText = keyword.Trim();
            var extensions = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

            var textVariants = new[]
            {
                cleanText,
                cleanText.ToUpper(),
                cleanText.ToLower(),
                cleanText.Replace("\u2019", "'"),
                cleanText.Replace("'", "\u2019"),
                cleanText.ToUpper().Replace("\u2019", "'"),
            };

            foreach (var textVar in textVariants)
            {
                foreach (var ext in extensions)
                {
                    var path = Path.Combine(_audioPath, textVar + ext);
                    if (File.Exists(path)) return path;
                }
            }

            // Fallback: case-insensitive directory search (cached)
            try
            {
                if (Directory.Exists(_audioPath))
                {
                    if (_audioFilesCache == null || (DateTime.UtcNow - _audioFilesCacheTime).TotalSeconds > 60)
                    {
                        _audioFilesCache = Directory.GetFiles(_audioPath);
                        _audioFilesCacheTime = DateTime.UtcNow;
                    }

                    var normalizedText = cleanText.ToUpperInvariant().Replace("\u2019", "'");
                    foreach (var file in _audioFilesCache)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant().Replace("\u2019", "'");
                        if (fileName == normalizedText)
                            return file;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("KeywordTriggerService: Error searching audio files: {Error}", ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Import triggers from the existing CustomTriggers list, auto-matching audio files.
        /// </summary>
        public List<KeywordTrigger> ImportFromCustomTriggers()
        {
            var customTriggers = App.Settings?.Current?.CustomTriggers;
            if (customTriggers == null || customTriggers.Count == 0)
                return new List<KeywordTrigger>();

            var existing = App.Settings?.Current?.KeywordTriggers ?? new List<KeywordTrigger>();
            var existingKeywords = new HashSet<string>(
                existing.Select(t => t.Keyword.ToUpperInvariant()));

            var imported = new List<KeywordTrigger>();

            foreach (var trigger in customTriggers)
            {
                if (string.IsNullOrWhiteSpace(trigger)) continue;
                if (existingKeywords.Contains(trigger.ToUpperInvariant())) continue;

                var kt = new KeywordTrigger
                {
                    Keyword = trigger,
                    MatchType = KeywordMatchType.PlainText,
                    Enabled = true,
                    CooldownSeconds = 30,
                    AudioFilePath = FindLinkedAudio(trigger),
                    AudioVolume = 80,
                    VisualEffect = KeywordVisualEffect.SubliminalFlash,
                    HapticEnabled = true,
                    HapticIntensity = 0.5,
                    DuckAudio = true,
                    XPAward = 10
                };

                imported.Add(kt);
            }

            return imported;
        }

        #endregion

        /// <summary>
        /// Check externally-provided text (e.g. from OCR) for keyword matches.
        /// Reuses the same matching, cooldown, and dispatch logic as the keyboard buffer path.
        /// </summary>
        public void CheckTextForMatches(string text)
        {
            if (!_isActive || _disposed) return;
            if (string.IsNullOrEmpty(text)) return;

            var settings = App.Settings?.Current;
            if (settings == null || !settings.KeywordTriggersEnabled) return;

            var triggers = settings.KeywordTriggers;
            if (triggers == null || triggers.Count == 0) return;

            // Check global cooldown
            var now = DateTime.Now;
            if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds)
                return;

            foreach (var trigger in triggers)
            {
                if (!trigger.Enabled || string.IsNullOrEmpty(trigger.Keyword)) continue;
                if (trigger.IsOnCooldown) continue;

                bool matched = false;

                if (trigger.MatchType == KeywordMatchType.Regex)
                {
                    matched = TryRegexMatch(text, trigger.Keyword);
                }
                else
                {
                    matched = text.Contains(trigger.Keyword, StringComparison.OrdinalIgnoreCase);
                }

                if (matched)
                {
                    trigger.LastTriggeredAt = now;
                    _lastGlobalTriggerTime = now;

                    App.Logger?.Information("Keyword trigger fired (OCR): '{Keyword}'", trigger.Keyword);

                    _ = DispatchResponseAsync(trigger);
                    TriggerFired?.Invoke(this, trigger);
                    break;
                }
            }
        }

        #region Matching

        private void CheckForMatches()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var triggers = settings.KeywordTriggers;
            if (triggers == null || triggers.Count == 0) return;

            // Check global cooldown
            var now = DateTime.Now;
            if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds)
                return;

            var bufferText = _buffer.ToString();
            if (string.IsNullOrEmpty(bufferText)) return;

            foreach (var trigger in triggers)
            {
                if (!trigger.Enabled || string.IsNullOrEmpty(trigger.Keyword)) continue;
                if (trigger.IsOnCooldown) continue;

                bool matched = false;

                if (trigger.MatchType == KeywordMatchType.Regex)
                {
                    matched = TryRegexMatch(bufferText, trigger.Keyword);
                }
                else
                {
                    // Case-insensitive contains match
                    matched = bufferText.Contains(trigger.Keyword, StringComparison.OrdinalIgnoreCase);
                }

                if (matched)
                {
                    trigger.LastTriggeredAt = now;
                    _lastGlobalTriggerTime = now;
                    _buffer.Clear(); // Prevent re-triggering on same text

                    App.Logger?.Information("Keyword trigger fired: '{Keyword}'", trigger.Keyword);

                    // Dispatch response asynchronously
                    _ = DispatchResponseAsync(trigger);

                    TriggerFired?.Invoke(this, trigger);
                    break; // Only fire one trigger per match cycle
                }
            }
        }

        private static bool TryRegexMatch(string input, string pattern)
        {
            try
            {
                return Regex.IsMatch(input, pattern,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100));
            }
            catch
            {
                // Bad regex pattern — silently fail
                return false;
            }
        }

        #endregion

        #region Response Dispatch

        private async Task DispatchResponseAsync(KeywordTrigger trigger)
        {
            try
            {
                // 1. Duck audio (if enabled)
                if (trigger.DuckAudio && App.Settings?.Current?.AudioDuckingEnabled == true)
                {
                    App.Audio?.Duck(App.Settings?.Current?.DuckingLevel ?? 80);
                }

                // 2. Play audio
                double audioDuration = 0;
                if (!string.IsNullOrEmpty(trigger.AudioFilePath) && File.Exists(trigger.AudioFilePath))
                {
                    for (int i = 0; i < trigger.AudioPlayCount; i++)
                    {
                        if (i > 0 && trigger.AudioDelayBetweenMs > 0)
                            await Task.Delay(trigger.AudioDelayBetweenMs);

                        audioDuration = PlayTriggerAudio(trigger.AudioFilePath, trigger.AudioVolume);
                    }
                }

                // 3. Fire visual effect (on UI thread)
                Application.Current?.Dispatcher?.Invoke(() => FireVisualEffect(trigger));

                // 4. Fire haptic pattern
                if (trigger.HapticEnabled)
                {
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(trigger.Keyword);
                }

                // 5. Award XP
                if (trigger.XPAward > 0)
                {
                    var xpAmount = (double)trigger.XPAward;

                    // Apply session multiplier
                    if (_isSessionActive?.Invoke() == true)
                    {
                        var multiplier = App.Settings?.Current?.KeywordSessionMultiplier ?? 1.5;
                        xpAmount *= multiplier;
                    }

                    App.Progression?.AddXP(xpAmount, XPSource.KeywordTrigger,
                        XPContext.FromCurrentSettings());
                }

                // 6. Wait for audio to finish, then unduck
                if (trigger.DuckAudio && audioDuration > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(audioDuration + 0.5));
                    App.Audio?.Unduck();
                }
                else if (trigger.DuckAudio)
                {
                    // No audio but ducking was enabled — unduck after a brief delay
                    await Task.Delay(500);
                    App.Audio?.Unduck();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("KeywordTriggerService: Error dispatching response: {Error}", ex.Message);

                // Make sure we unduck on error
                if (trigger.DuckAudio)
                    App.Audio?.Unduck();
            }
        }

        private void FireVisualEffect(KeywordTrigger trigger)
        {
            try
            {
                switch (trigger.VisualEffect)
                {
                    case KeywordVisualEffect.SubliminalFlash:
                        // Flash a subliminal from the user's configured pool
                        App.Subliminal?.FlashSubliminal();
                        break;

                    case KeywordVisualEffect.ImageFlash:
                        // Trigger a single image flash
                        App.Flash?.TriggerFlashOnce();
                        break;

                    case KeywordVisualEffect.OverlayPulse:
                        // Refresh overlays to create a pulse effect
                        App.Overlay?.RefreshOverlays();
                        break;

                    case KeywordVisualEffect.None:
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("KeywordTriggerService: Error firing visual effect: {Error}", ex.Message);
            }
        }

        #endregion

        #region Audio

        private double PlayTriggerAudio(string path, int volumePercent)
        {
            lock (_audioLock)
            {
                try
                {
                    StopTriggerAudio();

                    if (!File.Exists(path)) return 0;

                    _triggerAudioFile = new AudioFileReader(path);
                    _triggerPlayer = new WaveOutEvent();

                    // Apply volume curve (same as AudioService)
                    var volume = volumePercent / 100.0f;
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100.0f;
                    var curvedVolume = Math.Max(0.05f, (float)Math.Pow(volume * masterVolume, 1.5));
                    _triggerAudioFile.Volume = curvedVolume;

                    _triggerPlayer.Init(_triggerAudioFile);
                    _triggerPlayer.Play();

                    return _triggerAudioFile.TotalTime.TotalSeconds;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("KeywordTriggerService: Error playing audio: {Error}", ex.Message);
                    return 0;
                }
            }
        }

        private void StopTriggerAudio()
        {
            lock (_audioLock)
            {
                try
                {
                    _triggerPlayer?.Stop();
                    _triggerPlayer?.Dispose();
                    _triggerAudioFile?.Dispose();
                }
                catch { }

                _triggerPlayer = null;
                _triggerAudioFile = null;
            }
        }

        #endregion

        #region Win32 Key Translation

        /// <summary>
        /// Translate a virtual key code to the actual typed character,
        /// accounting for Shift, CapsLock, and keyboard layout.
        /// </summary>
        private static char? TranslateVkCode(int vkCode)
        {
            try
            {
                var keyboardState = new byte[256];
                if (!GetKeyboardState(keyboardState))
                    return null;

                var scanCode = MapVirtualKey((uint)vkCode, 0);
                var chars = new StringBuilder(4);

                var result = ToUnicode(
                    (uint)vkCode, scanCode, keyboardState,
                    chars, chars.Capacity, 0);

                if (result == 1)
                    return chars[0];

                return null;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern int ToUnicode(
            uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
            int cchBuff, uint wFlags);

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            StopTriggerAudio();
        }

        #endregion
    }
}
