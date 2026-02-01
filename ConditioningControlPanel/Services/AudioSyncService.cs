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
        private TimeSpan _lastPlaybackPosition;
        private DateTime _lastSyncTime;
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
                Log.Debug("AudioSyncService: Playback resumed at {Time}", currentTime);
            }

            if (paused)
                return;

            _isPlaying = true;
            _lastPlaybackPosition = currentTime;
            _lastSyncTime = DateTime.UtcNow;

            // Check buffer and trigger next chunk download if needed
            _chunkManager.CheckBufferAndProcess(currentTime);

            // Calculate look-ahead time with latency compensation
            var latencyMs = _hapticService.SubliminalAnticipationMs + _settings.ManualLatencyOffsetMs;
            var lookAheadTime = currentTime + TimeSpan.FromMilliseconds(latencyMs);

            // Get intensity from track
            var track = _chunkManager.Track;
            if (!track.HasDataForTime(lookAheadTime))
            {
                // No data yet - this shouldn't happen if buffer management is working
                Log.Warning("AudioSyncService: No haptic data for time {Time}", lookAheadTime);
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

            // Check if we have data for this position
            var track = _chunkManager.Track;
            if (!track.HasDataForTime(newTime))
            {
                // Need to process this chunk - this will be handled by buffer check
                _chunkManager.CheckBufferAndProcess(newTime);
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
                // Use short duration since we're sending continuously
                await _hapticService.SetSyncIntensityAsync(intensity);
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
        }
    }
}
