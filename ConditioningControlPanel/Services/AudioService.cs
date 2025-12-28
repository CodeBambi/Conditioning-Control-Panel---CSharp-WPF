using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;
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
        
        private WaveOutEvent? _musicPlayer;
        private AudioFileReader? _musicFile;
        private WaveOutEvent? _soundPlayer;
        private AudioFileReader? _soundFile;
        
        private MMDeviceEnumerator? _deviceEnumerator;
        private bool _isDucked;
        private float _duckAmount = 0.8f; // Default: reduce to 20%
        
        private readonly string _soundsPath;
        private readonly string _musicPath;
        
        private bool _disposed;

        #endregion

        #region Constructor

        public AudioService()
        {
            var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            _soundsPath = Path.Combine(assetsPath, "sounds");
            _musicPath = Path.Combine(assetsPath, "backgrounds");
            
            Directory.CreateDirectory(_soundsPath);
            Directory.CreateDirectory(_musicPath);

            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                App.Logger?.Information("Audio service initialized with ducking support");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Audio ducking not available: {Error}", ex.Message);
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
            catch { }
            
            _soundPlayer = null;
            _soundFile = null;
        }

        /// <summary>
        /// Play background music on loop
        /// </summary>
        public void PlayBackgroundMusic(string? path = null, int volumePercent = 15)
        {
            try
            {
                StopBackgroundMusic();

                if (string.IsNullOrEmpty(path))
                {
                    // Find random music file
                    if (!Directory.Exists(_musicPath)) return;
                    
                    var files = Directory.GetFiles(_musicPath, "*.mp3");
                    if (files.Length == 0)
                        files = Directory.GetFiles(_musicPath, "*.wav");
                    if (files.Length == 0) return;
                    
                    path = files[new Random().Next(files.Length)];
                }

                if (!File.Exists(path)) return;

                _musicFile = new AudioFileReader(path);
                _musicPlayer = new WaveOutEvent();
                
                var volume = volumePercent / 100.0f;
                _musicFile.Volume = Math.Max(0.01f, volume);
                
                // Loop using event
                _musicPlayer.PlaybackStopped += (s, e) =>
                {
                    if (_musicFile != null && _musicPlayer != null)
                    {
                        try
                        {
                            _musicFile.Position = 0;
                            _musicPlayer.Play();
                        }
                        catch { }
                    }
                };
                
                _musicPlayer.Init(_musicFile);
                _musicPlayer.Play();
                
                App.Logger?.Debug("Playing background music: {Path}", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not play background music: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Stop background music
        /// </summary>
        public void StopBackgroundMusic()
        {
            try
            {
                _musicPlayer?.Stop();
                _musicPlayer?.Dispose();
                _musicFile?.Dispose();
            }
            catch { }
            
            _musicPlayer = null;
            _musicFile = null;
        }

        /// <summary>
        /// Pause background music (for video playback)
        /// </summary>
        public void PauseBackgroundMusic()
        {
            try
            {
                _musicPlayer?.Pause();
                App.Logger?.Debug("Background music paused");
            }
            catch { }
        }

        /// <summary>
        /// Resume background music after pause
        /// </summary>
        public void ResumeBackgroundMusic()
        {
            try
            {
                _musicPlayer?.Play();
                App.Logger?.Debug("Background music resumed");
            }
            catch { }
        }

        #endregion

        #region Audio Ducking

        /// <summary>
        /// Lower the volume of other applications
        /// </summary>
        /// <param name="strength">0-100 (0 = no ducking, 100 = full mute)</param>
        public void Duck(int strength = 80)
        {
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

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            var processId = (int)session.GetProcessID;
                            
                            // Skip our own process
                            if (processId == currentProcessId || processId == 0) continue;
                            
                            var currentVolume = session.SimpleAudioVolume.Volume;
                            
                            // Store original volume
                            _originalVolumes[processId] = currentVolume;
                            
                            // Calculate ducked volume
                            var newVolume = currentVolume * (1.0f - _duckAmount);
                            session.SimpleAudioVolume.Volume = Math.Max(0.0f, newVolume);
                        }
                        catch
                        {
                            // Session may have ended
                        }
                    }

                    _isDucked = true;
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
                        catch
                        {
                            // Session may have ended
                        }
                    }

                    _originalVolumes.Clear();
                    _isDucked = false;
                    App.Logger?.Debug("Audio unducked");
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Audio unducking failed: {Error}", ex.Message);
                    _originalVolumes.Clear();
                    _isDucked = false;
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
            StopBackgroundMusic();
            
            _deviceEnumerator?.Dispose();
        }

        #endregion
    }
}
