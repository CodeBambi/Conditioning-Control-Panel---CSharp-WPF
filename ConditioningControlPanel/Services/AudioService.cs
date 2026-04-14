using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles audio playback and system audio ducking.
    /// Ported from Python utils.py AudioDucker.
    /// </summary>
    public class AudioService : IDisposable
    {
        #region Fields

        private readonly Dictionary<int, float> _originalVolumes = new();
        private readonly object _lockObj = new();

        private WaveOutEvent? _soundPlayer;
        private AudioFileReader? _soundFile;

        private MMDeviceEnumerator? _deviceEnumerator;
        private int _duckCount; // Reference count — unduck only when all duckers release
        private bool _isDucked;
        private float _duckAmount = 0.8f; // Default: reduce to 20%
        private long _duckGeneration; // Incremented on ForceUnduck to invalidate stale Unduck callbacks
        private System.Threading.Timer? _duckWatchdog; // Safety net: force-unduck if ducking exceeds max duration
        private const int DuckWatchdogMs = 300_000; // 5 minutes — safety net for leaked duck refs, must exceed longest video

        private bool _disposed;

        // Cached WebView2 process IDs to avoid slow Process.GetProcessById() on every duck
        private HashSet<int> _webView2Pids = new();
        private DateTime _webView2PidsCacheTime = DateTime.MinValue;
        private static readonly TimeSpan WebView2CacheExpiry = TimeSpan.FromSeconds(30);

        // Crash recovery file for ducking state
        private static readonly string DuckingRecoveryFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel", "ducking_recovery.json");

        #endregion

        #region Constructor

        public AudioService()
        {
            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();

                // Check for crash recovery - restore volumes if app was killed while ducked
                RecoverFromCrash();

                App.Logger?.Information("Audio service initialized with ducking support");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Audio ducking not available: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Run audio health diagnostics on startup. Logs warnings for any issues found.
        /// Call after construction to verify audio subsystem is functional.
        /// </summary>
        public void RunStartupDiagnostics()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var soundsDir = Path.Combine(baseDir, "Resources", "sounds");
                var subAudioDir = Path.Combine(baseDir, "Resources", "sub_audio");

                // Check sound directories exist and have files
                if (!Directory.Exists(soundsDir))
                    App.Logger?.Warning("[AudioDiag] Resources/sounds/ directory is MISSING at {Path}", soundsDir);
                else
                {
                    var soundCount = Directory.GetFiles(soundsDir, "*.*", SearchOption.AllDirectories).Length;
                    if (soundCount == 0)
                        App.Logger?.Warning("[AudioDiag] Resources/sounds/ directory exists but contains NO audio files");
                    else
                        App.Logger?.Information("[AudioDiag] Resources/sounds/: {Count} files found", soundCount);
                }

                if (!Directory.Exists(subAudioDir))
                    App.Logger?.Warning("[AudioDiag] Resources/sub_audio/ directory is MISSING at {Path}", subAudioDir);
                else
                {
                    var subCount = Directory.GetFiles(subAudioDir, "*.*").Length;
                    if (subCount == 0)
                        App.Logger?.Warning("[AudioDiag] Resources/sub_audio/ directory exists but contains NO audio files");
                    else
                        App.Logger?.Information("[AudioDiag] Resources/sub_audio/: {Count} files found", subCount);
                }

                // Check WaveOutEvent can be created (tests audio device availability)
                try
                {
                    using var testDevice = new WaveOutEvent();
                    App.Logger?.Information("[AudioDiag] WaveOutEvent: OK (audio device available)");
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("[AudioDiag] WaveOutEvent FAILED — no audio output device? Error: {Error}", ex.Message);
                }

                // Log current audio settings for diagnosis
                var settings = App.Settings?.Current;
                if (settings != null)
                {
                    App.Logger?.Information("[AudioDiag] Settings: MasterVolume={Master}%, SubAudioEnabled={SubEnabled}, SubAudioVolume={SubVol}%, FlashAudioEnabled={FlashEnabled}, AudioDuckingEnabled={DuckEnabled}",
                        settings.MasterVolume, settings.SubAudioEnabled, settings.SubAudioVolume, settings.FlashAudioEnabled, settings.AudioDuckingEnabled);

                    if (settings.MasterVolume == 0)
                        App.Logger?.Warning("[AudioDiag] MasterVolume is 0% — ALL audio will be silent");
                    if (!settings.SubAudioEnabled)
                        App.Logger?.Information("[AudioDiag] SubAudioEnabled is OFF — whisper/trigger audio will not play");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("[AudioDiag] Diagnostics failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Play a short test sound to verify audio output is working.
        /// Returns a diagnostic message string.
        /// </summary>
        public string TestAudioPlayback()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var soundsDir = Path.Combine(baseDir, "Resources", "sounds");
            var subAudioDir = Path.Combine(baseDir, "Resources", "sub_audio");

            var diagnostics = new System.Text.StringBuilder();
            diagnostics.AppendLine("=== Audio Diagnostics ===");

            // Check directories
            if (!Directory.Exists(soundsDir))
                diagnostics.AppendLine("WARNING: Resources/sounds/ directory MISSING");
            else
            {
                var count = Directory.GetFiles(soundsDir, "*.*", SearchOption.AllDirectories).Length;
                diagnostics.AppendLine($"Resources/sounds/: {count} files");
            }

            if (!Directory.Exists(subAudioDir))
                diagnostics.AppendLine("WARNING: Resources/sub_audio/ directory MISSING");
            else
            {
                var count = Directory.GetFiles(subAudioDir, "*.*").Length;
                diagnostics.AppendLine($"Resources/sub_audio/: {count} files");
            }

            // Check audio device
            try
            {
                using var testDevice = new WaveOutEvent();
                diagnostics.AppendLine("Audio device: OK");
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"Audio device: FAILED ({ex.Message})");
                return diagnostics.ToString();
            }

            // Try to play a sound
            var testFiles = new[]
            {
                Path.Combine(soundsDir, "chime1.mp3"),
                Path.Combine(soundsDir, "lvup.mp3"),
                Path.Combine(soundsDir, "bubbles", "Pop.mp3"),
            };

            string? playFile = null;
            foreach (var f in testFiles)
            {
                if (File.Exists(f)) { playFile = f; break; }
            }

            if (playFile == null)
            {
                diagnostics.AppendLine("WARNING: No test sound files found to play");
                return diagnostics.ToString();
            }

            try
            {
                StopSound();
                _soundFile = new AudioFileReader(playFile);
                _soundPlayer = new WaveOutEvent();
                _soundFile.Volume = 0.5f; // Fixed 50% for test — bypasses curve
                _soundPlayer.Init(_soundFile);
                _soundPlayer.Play();
                diagnostics.AppendLine($"Playing: {Path.GetFileName(playFile)} at 50% volume");
                diagnostics.AppendLine("If you can't hear this, check Windows volume mixer.");
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"Playback FAILED: {ex.Message}");
            }

            // Log settings
            var s = App.Settings?.Current;
            if (s != null)
            {
                diagnostics.AppendLine($"\nMaster Volume: {s.MasterVolume}%");
                diagnostics.AppendLine($"Whispers Enabled: {s.SubAudioEnabled}");
                diagnostics.AppendLine($"Whisper Volume: {s.SubAudioVolume}%");
                diagnostics.AppendLine($"Flash Audio Enabled: {s.FlashAudioEnabled}");
                var effectiveWhisperVol = Math.Pow((s.SubAudioVolume / 100.0) * (s.MasterVolume / 100.0), 1.5) * 100;
                diagnostics.AppendLine($"Effective Whisper Volume: {effectiveWhisperVol:F1}%");
            }

            return diagnostics.ToString();
        }

        #endregion

        #region Crash Recovery

        /// <summary>
        /// Check if the app crashed while audio was ducked and restore volumes.
        /// </summary>
        private void RecoverFromCrash()
        {
            try
            {
                if (!File.Exists(DuckingRecoveryFile)) return;

                App.Logger?.Information("Detected ducking recovery file - restoring audio from previous crash");

                var json = File.ReadAllText(DuckingRecoveryFile);
                var savedVolumes = JsonConvert.DeserializeObject<Dictionary<int, float>>(json);

                if (savedVolumes != null && savedVolumes.Count > 0 && _deviceEnumerator != null)
                {
                    var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    var sessionManager = device.AudioSessionManager;
                    var sessions = sessionManager.Sessions;

                    int restoredCount = 0;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            var processId = (int)session.GetProcessID;

                            if (savedVolumes.TryGetValue(processId, out var originalVolume))
                            {
                                session.SimpleAudioVolume.Volume = originalVolume;
                                restoredCount++;
                            }
                        }
                        catch { /* Session may have ended */ }
                    }

                    App.Logger?.Information("Restored {Count} audio sessions from crash recovery", restoredCount);
                }

                // Delete recovery file after restore
                File.Delete(DuckingRecoveryFile);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to recover ducking state: {Error}", ex.Message);
                // Try to delete the file anyway to avoid repeated failures
                try { File.Delete(DuckingRecoveryFile); } catch { }
            }
        }

        /// <summary>
        /// Save current ducking state for crash recovery.
        /// </summary>
        private void SaveDuckingState()
        {
            try
            {
                var dir = Path.GetDirectoryName(DuckingRecoveryFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(_originalVolumes);
                File.WriteAllText(DuckingRecoveryFile, json);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to save ducking state: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Clear crash recovery file (called on successful unduck).
        /// </summary>
        private void ClearDuckingState()
        {
            try
            {
                if (File.Exists(DuckingRecoveryFile))
                    File.Delete(DuckingRecoveryFile);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to clear ducking state: {Error}", ex.Message);
            }
        }

        #endregion

        #region Sound Playback

        /// <summary>
        /// Play a sound effect with volume control
        /// </summary>
        public double PlaySound(string path, int volumePercent)
        {
            try
            {
                StopSound();
                
                if (!File.Exists(path))
                {
                    App.Logger?.Debug("Sound file not found: {Path}", path);
                    return 0;
                }

                _soundFile = new AudioFileReader(path);
                _soundPlayer = new WaveOutEvent();
                
                // Apply volume curve (gentler, minimum 5%)
                var volume = volumePercent / 100.0f;
                var curvedVolume = Math.Max(0.05f, (float)Math.Pow(volume, 1.5));
                _soundFile.Volume = curvedVolume;
                
                _soundPlayer.Init(_soundFile);
                _soundPlayer.Play();
                
                var duration = _soundFile.TotalTime.TotalSeconds;
                App.Logger?.Debug("Playing sound: {Path}, duration: {Duration}s", Path.GetFileName(path), duration);
                
                return duration;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Could not play sound {Path}: {Error}", path, ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Stop currently playing sound
        /// </summary>
        public void StopSound()
        {
            try
            {
                _soundPlayer?.Stop();
                _soundPlayer?.Dispose();
                _soundFile?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Error stopping sound: {Error}", ex.Message);
            }

            _soundPlayer = null;
            _soundFile = null;
        }

        #endregion

        #region Audio Ducking

        /// <summary>
        /// Current duck generation — capture this when calling Duck() and pass to Unduck() to avoid stale callbacks.
        /// </summary>
        public long DuckGeneration
        {
            get { lock (_lockObj) { return _duckGeneration; } }
        }

        /// <summary>
        /// Lower the volume of other applications
        /// </summary>
        /// <param name="strength">0-100 (0 = no ducking, 100 = full mute)</param>
        public void Duck(int strength = 80)
        {
            // Don't duck if master volume is 0% - nothing to play anyway
            if ((App.Settings?.Current?.MasterVolume ?? 100) == 0) return;

            if (_deviceEnumerator == null) return;

            lock (_lockObj)
            {
                _duckCount++;
                if (_isDucked) return; // Already ducked — just bump the ref count
                
                _duckAmount = Math.Clamp(strength, 0, 100) / 100.0f;
                
                try
                {
                    var currentProcessId = Environment.ProcessId;
                    var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    var sessionManager = device.AudioSessionManager;
                    var sessions = sessionManager.Sessions;

                    // Check if we should exclude BambiCloud (WebView2) from ducking
                    var excludeWebView2 = App.Settings?.Current?.ExcludeBambiCloudFromDucking ?? true;

                    // Refresh WebView2 PID cache if expired (avoids slow Process.GetProcessById per session)
                    if (excludeWebView2 && DateTime.UtcNow - _webView2PidsCacheTime > WebView2CacheExpiry)
                    {
                        try
                        {
                            var newPids = new HashSet<int>();
                            foreach (var proc in Process.GetProcesses())
                            {
                                try
                                {
                                    var name = proc.ProcessName.ToLowerInvariant();
                                    if (name.Contains("msedgewebview2") || name.Contains("webview2"))
                                        newPids.Add(proc.Id);
                                }
                                catch { }
                                finally { proc.Dispose(); }
                            }
                            _webView2Pids = newPids;
                            _webView2PidsCacheTime = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Failed to refresh WebView2 PID cache: {Error}", ex.Message);
                        }
                    }

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            var processId = (int)session.GetProcessID;

                            // Skip our own process
                            if (processId == currentProcessId || processId == 0) continue;

                            // Skip WebView2 processes if setting is enabled (for BambiCloud audio)
                            if (excludeWebView2 && _webView2Pids.Contains(processId))
                                continue;

                            var currentVolume = session.SimpleAudioVolume.Volume;

                            // Store original volume
                            _originalVolumes[processId] = currentVolume;

                            // Calculate ducked volume
                            var newVolume = currentVolume * (1.0f - _duckAmount);
                            session.SimpleAudioVolume.Volume = Math.Max(0.0f, newVolume);
                        }
                        catch (Exception ex)
                        {
                            // Session may have ended
                            App.Logger?.Debug("Failed to duck audio session: {Error}", ex.Message);
                        }
                    }

                    _isDucked = true;

                    // Watchdog: force-unduck if ducking exceeds max duration.
                    // Catches leaked ref counts from cancelled Task.Delay callbacks,
                    // missing Unduck on audio failure, etc.
                    _duckWatchdog?.Dispose();
                    _duckWatchdog = new System.Threading.Timer(_ =>
                    {
                        if (_isDucked)
                        {
                            App.Logger?.Warning("[Ducking] Watchdog fired after {Ms}ms — force-unducking to prevent stuck volume", DuckWatchdogMs);
                            ForceUnduck();
                        }
                    }, null, DuckWatchdogMs, System.Threading.Timeout.Infinite);

                    // Save state for crash recovery
                    SaveDuckingState();

                    App.Logger?.Debug("Ducked {Count} audio sessions by {Amount}%", _originalVolumes.Count, strength);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Audio ducking failed: {Error}", ex.Message);
                    // Duck failed — compensate for the increment so ref count stays balanced
                    _duckCount = Math.Max(0, _duckCount - 1);
                }
            }
        }

        /// <summary>
        /// Restore the original volume of other applications.
        /// Pass the generation from DuckGeneration captured at Duck() time to prevent stale callbacks
        /// from interfering with newer ducking sessions.
        /// </summary>
        /// <param name="generation">Duck generation to validate against. Pass -1 to skip generation check (legacy callers).</param>
        public void Unduck(long generation = -1)
        {
            lock (_lockObj)
            {
                // If a generation was specified and doesn't match current, this is a stale callback — ignore
                if (generation >= 0 && generation != _duckGeneration)
                {
                    App.Logger?.Debug("Ignoring stale Unduck (gen {Old} vs current {Current})", generation, _duckGeneration);
                    return;
                }

                if (!_isDucked || _deviceEnumerator == null)
                {
                    _duckCount = Math.Max(0, _duckCount - 1);
                    return;
                }

                _duckCount = Math.Max(0, _duckCount - 1);
                if (_duckCount > 0) return; // Other consumers still need ducking

                try
                {
                    var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    var sessionManager = device.AudioSessionManager;
                    var sessions = sessionManager.Sessions;

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            var processId = (int)session.GetProcessID;

                            if (_originalVolumes.TryGetValue(processId, out var originalVolume))
                            {
                                session.SimpleAudioVolume.Volume = originalVolume;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Session may have ended
                            App.Logger?.Debug("Failed to unduck audio session: {Error}", ex.Message);
                        }
                    }

                    _originalVolumes.Clear();
                    _isDucked = false;
                    _duckWatchdog?.Dispose();
                    _duckWatchdog = null;

                    // Clear crash recovery file
                    ClearDuckingState();

                    App.Logger?.Debug("Audio unducked");
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Audio unducking failed, preserving state for retry: {Error}", ex.Message);
                    // CRITICAL: Do NOT clear _originalVolumes or set _isDucked=false here.
                    // If we do, the next Duck() will re-read the currently-ducked volumes as
                    // "originals", causing volumes to ratchet toward 0% over repeated cycles.
                    // Keep state intact so the next Unduck/ForceUnduck can retry restoration.
                    //
                    // Restore _duckCount to 1 (not 0) so the system can recover:
                    // If _duckCount=0 + _isDucked=true, Duck() silently returns and no future
                    // Unduck() can ever restore volumes — audio stays permanently ducked.
                    _duckCount = 1;
                    // Keep recovery file so crash recovery can restore if app exits
                }
            }
        }

        /// <summary>
        /// Force-unduck regardless of reference count. Used for panic key / app exit.
        /// Increments the duck generation to invalidate all pending stale Unduck callbacks.
        /// </summary>
        public void ForceUnduck()
        {
            long gen;
            lock (_lockObj)
            {
                _duckGeneration++; // Invalidate all pending stale Unduck callbacks
                _duckCount = 1; // Force next Unduck() to actually restore
                gen = _duckGeneration;
            }
            Unduck(gen);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Restore audio levels — force unduck regardless of ref count
            if (_isDucked)
            {
                ForceUnduck();
            }

            StopSound();
            _duckWatchdog?.Dispose();
            _deviceEnumerator?.Dispose();
        }

        #endregion
    }
}
