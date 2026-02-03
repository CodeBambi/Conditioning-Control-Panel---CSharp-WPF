using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Audio;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Orchestrates audio-synced haptics for web video playback.
    /// Coordinates video URL detection, audio processing, and haptic playback.
    /// Supports progressive chunk loading for long videos.
    /// </summary>
    public class AudioSyncService : IDisposable
    {
        private readonly HapticService _hapticService;
        private readonly AudioSyncSettings _settings;
        private ChunkManager? _chunkManager;

        private bool _disposed;
        private bool _isProcessing;
        private bool _isPaused;
        private bool _isPlaying;
        private bool _isWaitingForChunk;
        private TimeSpan _lastPlaybackPosition;
        private DateTime _lastSyncTime;
        private DateTime _lastResyncTime;
        private const int RESYNC_INTERVAL_MS = 5000; // Force resync every 5 seconds
        private CancellationTokenSource? _syncCts;

        /// <summary>
        /// Event fired when processing starts (show overlay)
        /// </summary>
        public event EventHandler<string>? ProcessingStarted;

        /// <summary>
        /// Event fired with processing progress updates
        /// </summary>
        public event EventHandler<ChunkProgressEventArgs>? ProcessingProgress;

        /// <summary>
        /// Event fired when processing completes and video can play
        /// </summary>
        public event EventHandler? ProcessingCompleted;

        /// <summary>
        /// Event fired when an error occurs
        /// </summary>
        public event EventHandler<string>? Error;

        /// <summary>
        /// Event fired when waiting for a chunk to load (pause video, show loading)
        /// </summary>
        public event EventHandler<int>? ChunkLoadingRequired;

        /// <summary>
        /// Event fired when chunk finished loading (resume video)
        /// </summary>
        public event EventHandler? ChunkLoadingCompleted;

        /// <summary>
        /// Whether audio sync is currently enabled
        /// </summary>
        public bool IsEnabled => _settings.Enabled;

        /// <summary>
        /// Whether we're currently processing a video
        /// </summary>
        public bool IsProcessing => _isProcessing;

        /// <summary>
        /// Whether first chunk is ready and video can start playing
        /// </summary>
        public bool IsReadyToPlay => _chunkManager?.IsFirstChunkReady ?? false;

        /// <summary>
        /// Whether we're waiting for a chunk to load
        /// </summary>
        public bool IsWaitingForChunk => _isWaitingForChunk;

        public AudioSyncService(HapticService hapticService, AudioSyncSettings settings)
        {
            _hapticService = hapticService;
            _settings = settings;
        }

        /// <summary>
        /// Called when a video URL is detected in WebView2
        /// Starts the download and analysis process
        /// </summary>
        public async Task OnVideoDetectedAsync(string videoUrl)
        {
            if (!_settings.Enabled || !_hapticService.IsConnected)
            {
                Log.Debug("AudioSyncService: Skipping - Enabled={Enabled}, Connected={Connected}",
                    _settings.Enabled, _hapticService.IsConnected);
                return;
            }

            if (!VideoDownloader.IsLikelyVideoUrl(videoUrl))
            {
                Log.Debug("AudioSyncService: URL doesn't look like a video: {Url}", videoUrl);
                return;
            }

            Log.Information("AudioSyncService: Video detected, starting processing: {Url}", videoUrl);

            try
            {
                _isProcessing = true;
                ProcessingStarted?.Invoke(this, "Preparing haptic sync...");

                // Clean up previous session
                _chunkManager?.Dispose();
                _chunkManager = new ChunkManager(_settings);
                _chunkManager.ChunkReady += OnChunkReady;
                _chunkManager.Progress += OnChunkProgress;
                _chunkManager.Error += OnChunkError;
                _chunkManager.ChunkLoadingStarted += OnChunkLoadingStarted;

                // Initialize and start first chunk
                _chunkManager.Initialize(videoUrl);
                await _chunkManager.StartFirstChunkAsync();

                // Wait for first chunk to be ready (with timeout)
                var timeout = TimeSpan.FromMinutes(2);
                var startWait = DateTime.UtcNow;

                while (!_chunkManager.IsFirstChunkReady && (DateTime.UtcNow - startWait) < timeout)
                {
                    await Task.Delay(100);
                }

                if (!_chunkManager.IsFirstChunkReady)
                {
                    throw new AudioSyncException("Timeout waiting for first chunk to process");
                }

                _isProcessing = false;
                ProcessingCompleted?.Invoke(this, EventArgs.Empty);

                Log.Information("AudioSyncService: First chunk ready, video can start playing");
            }
            catch (Exception ex)
            {
                _isProcessing = false;
                Log.Error(ex, "AudioSyncService: Failed to process video");
                Error?.Invoke(this, $"Failed to prepare haptic sync: {ex.Message}");

                // Allow video to play without haptics
                ProcessingCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called when JS reports video playback state (currentTime, paused)
        /// </summary>
        public void OnPlaybackStateUpdate(double currentTimeSeconds, bool paused)
        {
            if (!_settings.Enabled || _chunkManager == null)
                return;

            var currentTime = TimeSpan.FromSeconds(currentTimeSeconds);

            // Handle pause/resume
            if (paused && !_isPaused)
            {
                _isPaused = true;
                _isPlaying = false;
                _ = _hapticService.StopAsync();
                Log.Debug("AudioSyncService: Playback paused at {Time}", currentTime);
                return;
            }

            if (!paused && _isPaused)
            {
                _isPaused = false;
                _lastResyncTime = DateTime.UtcNow; // Reset resync timer on resume
                Log.Debug("AudioSyncService: Playback resumed at {Time}", currentTime);
            }

            if (paused || _isWaitingForChunk)
                return;

            _isPlaying = true;
            _lastPlaybackPosition = currentTime;
            _lastSyncTime = DateTime.UtcNow;

            // Check buffer and trigger next chunk processing if needed
            _chunkManager.CheckBufferAndProcess(currentTime);

            // Check if we need a forced resync (every 5 seconds)
            // This prevents drift between haptics and video over time
            var now = DateTime.UtcNow;
            var needsResync = (now - _lastResyncTime).TotalMilliseconds >= RESYNC_INTERVAL_MS;

            TimeSpan lookAheadTime;
            if (needsResync)
            {
                // Force resync: use exact current time (no latency compensation)
                // This snaps haptics back to precise video position
                lookAheadTime = currentTime;
                _lastResyncTime = now;
                Log.Debug("AudioSyncService: Forced resync at {Time}", currentTime);
            }
            else
            {
                // Normal operation: calculate look-ahead time with latency compensation
                // Base 300ms for device response + network + processing latency
                var latencyMs = 300 + _hapticService.SubliminalAnticipationMs + _settings.ManualLatencyOffsetMs;
                lookAheadTime = currentTime + TimeSpan.FromMilliseconds(latencyMs);
            }

            // Get intensity from track
            var track = _chunkManager.Track;
            if (!track.HasDataForTime(lookAheadTime))
            {
                // No data yet - chunk not loaded
                Log.Debug("AudioSyncService: No haptic data for time {Time} - chunk not loaded", lookAheadTime);
                return;
            }

            var intensity = track.GetIntensityAt(lookAheadTime);

            // Send to haptic device
            _ = SendHapticAsync(intensity);
        }

        /// <summary>
        /// Called when user seeks to a new position
        /// </summary>
        public void OnVideoSeek(double newTimeSeconds)
        {
            if (!_settings.Enabled || _chunkManager == null)
                return;

            var newTime = TimeSpan.FromSeconds(newTimeSeconds);
            Log.Information("AudioSyncService: User seeked to {Time}", newTime);

            _lastPlaybackPosition = newTime;
            _lastResyncTime = DateTime.UtcNow; // Reset resync timer on seek

            // Check if we have data for this position
            var chunkIndex = _chunkManager.GetChunkIndexForTime(newTime);

            if (!_chunkManager.IsChunkReady(chunkIndex))
            {
                // Need to load this chunk - do it in background
                _ = LoadChunkForSeekAsync(chunkIndex);
            }
        }

        /// <summary>
        /// Load a chunk after user seeks to unloaded section
        /// </summary>
        private async Task LoadChunkForSeekAsync(int chunkIndex)
        {
            if (_chunkManager == null) return;

            // Signal to pause video
            _isWaitingForChunk = true;
            ChunkLoadingRequired?.Invoke(this, chunkIndex);

            try
            {
                // Wait for chunk to be ready
                await _chunkManager.EnsureChunkReadyAsync(chunkIndex);

                Log.Information("AudioSyncService: Chunk {Index} loaded after seek", chunkIndex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AudioSyncService: Failed to load chunk {Index} after seek", chunkIndex);
                Error?.Invoke(this, $"Failed to load haptic data: {ex.Message}");
            }
            finally
            {
                _isWaitingForChunk = false;
                ChunkLoadingCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called when video ends
        /// </summary>
        public void OnVideoEnded()
        {
            Log.Information("AudioSyncService: Video ended");
            StopSync();
        }

        /// <summary>
        /// Stop haptic sync
        /// </summary>
        public void StopSync()
        {
            _isPlaying = false;
            _isPaused = false;
            _isWaitingForChunk = false;
            _syncCts?.Cancel();
            _ = _hapticService.StopAsync();

            _chunkManager?.Stop();
        }

        /// <summary>
        /// Clean up resources when navigating away
        /// </summary>
        public void Reset()
        {
            StopSync();
            _chunkManager?.Dispose();
            _chunkManager = null;
            _isProcessing = false;
        }

        private async Task SendHapticAsync(float intensity)
        {
            try
            {
                // Device needs at least ~8% to produce perceptible vibration
                const float minDeviceIntensity = 0.08f;
                var liveIntensity = (float)_settings.LiveIntensity;
                float adjustedIntensity;

                if (intensity <= 0.01f || liveIntensity <= 0)
                {
                    // Track says quiet or power is off
                    adjustedIntensity = 0;
                }
                else if (liveIntensity <= minDeviceIntensity)
                {
                    // Very low power - just multiply, user wants minimal
                    adjustedIntensity = intensity * liveIntensity;
                }
                else
                {
                    // Map track intensity [0, 1] to device range [minDevice, liveIntensity]
                    // This preserves dynamics while ensuring device responds even at low power
                    adjustedIntensity = minDeviceIntensity + intensity * (liveIntensity - minDeviceIntensity);
                }

                await _hapticService.SetSyncIntensityAsync(adjustedIntensity);
            }
            catch (Exception ex)
            {
                Log.Debug("AudioSyncService: Failed to send haptic: {Error}", ex.Message);
            }
        }

        private void OnChunkReady(object? sender, AudioChunk chunk)
        {
            Log.Debug("AudioSyncService: Chunk {Index} ready", chunk.Index);
        }

        private void OnChunkProgress(object? sender, ChunkProgressEventArgs e)
        {
            ProcessingProgress?.Invoke(this, e);
        }

        private void OnChunkError(object? sender, string error)
        {
            Error?.Invoke(this, error);
        }

        private void OnChunkLoadingStarted(object? sender, int chunkIndex)
        {
            Log.Information("AudioSyncService: Chunk {Index} loading started", chunkIndex);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
        }
    }
}
