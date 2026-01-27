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
        private bool _isDucked;
        private float _duckAmount = 0.8f; // Default: reduce to 20%

        private bool _disposed;

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
                App.Logger?.Debug("Could not play sound {Path}: {Error}", path, ex.Message);
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
        /// Lower the volume of other applications
        /// </summary>
        /// <param name="strength">0-100 (0 = no ducking, 100 = full mute)</param>
        public void Duck(int strength = 80)
        {
            // Don't duck if master volume is 0% - nothing to play anyway
            if ((App.Settings?.Current?.MasterVolume ?? 100) == 0) return;

            if (_isDucked || _deviceEnumerator == null) return;

            lock (_lockObj)
            {
                if (_isDucked) return;
                
                _duckAmount = Math.Clamp(strength, 0, 100) / 100.0f;
                
                try
                {
                    var currentProcessId = Environment.ProcessId;
                    var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    var sessionManager = device.AudioSessionManager;
                    var sessions = sessionManager.Sessions;

                    // Check if we should exclude BambiCloud (WebView2) from ducking
                    var excludeWebView2 = App.Settings?.Current?.ExcludeBambiCloudFromDucking ?? true;

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            var processId = (int)session.GetProcessID;

                            // Skip our own process
                            if (processId == currentProcessId || processId == 0) continue;

                            // Skip WebView2 processes if setting is enabled (for BambiCloud audio)
                            if (excludeWebView2 && processId > 0)
                            {
                                try
                                {
                                    var process = Process.GetProcessById(processId);
                                    var processName = process.ProcessName.ToLowerInvariant();
                                    if (processName.Contains("msedgewebview2") || processName.Contains("webview2"))
                                    {
                                        continue; // Don't duck WebView2 audio
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Process may have ended, continue with ducking
                                    App.Logger?.Debug("Could not check process {ProcessId}: {Error}", processId, ex.Message);
                                }
                            }

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

                    // Save state for crash recovery
                    SaveDuckingState();

                    App.Logger?.Debug("Ducked {Count} audio sessions by {Amount}%", _originalVolumes.Count, strength);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Audio ducking failed: {Error}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Restore the original volume of other applications
        /// </summary>
        public void Unduck()
        {
            if (!_isDucked || _deviceEnumerator == null) return;

            lock (_lockObj)
            {
                if (!_isDucked) return;
                
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

                    // Clear crash recovery file
                    ClearDuckingState();

                    App.Logger?.Debug("Audio unducked");
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Audio unducking failed: {Error}", ex.Message);
                    _originalVolumes.Clear();
                    _isDucked = false;
                    ClearDuckingState();
                }
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Restore audio levels
            if (_isDucked)
            {
                Unduck();
            }

            StopSound();
            _deviceEnumerator?.Dispose();
        }

        #endregion
    }
}
