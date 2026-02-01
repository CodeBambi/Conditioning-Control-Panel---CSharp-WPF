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
            App.Logger?.Information("AudioSyncService.OnVideoDetectedAsync called with URL length={UrlLen}", videoUrl?.Length ?? 0);

            if (!_settings.Enabled || !_hapticService.IsConnected)
            {
                App.Logger?.Warning("AudioSyncService: Skipping - Enabled={Enabled}, Connected={Connected}",
                    _settings.Enabled, _hapticService.IsConnected);
                return;
            }

            if (!VideoDownloader.IsLikelyVideoUrl(videoUrl))
            {
                App.Logger?.Warning("AudioSyncService: URL doesn't look like a video: {Url}", videoUrl);
                return;
            }

            App.Logger?.Information("AudioSyncService: Video detected, starting processing: {Url}", videoUrl);

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

                App.Logger?.Information("AudioSyncService: ChunkManager created, initializing...");

                // Initialize and start first chunk
                _chunkManager.Initialize(videoUrl);
                App.Logger?.Information("AudioSyncService: ChunkManager initialized, starting first chunk...");
                await _chunkManager.StartFirstChunkAsync();
                App.Logger?.Information("AudioSyncService: StartFirstChunkAsync completed, waiting for first chunk...");

                // Wait for first chunk to be ready (with timeout)
                var timeout = TimeSpan.FromMinutes(2);
                var startWait = DateTime.UtcNow;

                while (!_chunkManager.IsFirstChunkReady && (DateTime.UtcNow - startWait) < timeout)
                {
                    await Task.Delay(100);
                }

                if (!_chunkManager.IsFirstChunkReady)
                {
                    App.Logger?.Error("AudioSyncService: Timeout waiting for first chunk!");
                    throw new AudioSyncException("Timeout waiting for first chunk to process");
                }

                _isProcessing = false;
                ProcessingCompleted?.Invoke(this, EventArgs.Empty);

                App.Logger?.Information("AudioSyncService: First chunk ready! Track has {ChunkCount} chunks, duration {Duration}",
                    _chunkManager.Track.ChunkCount, _chunkManager.Track.Duration);
            }
            catch (Exception ex)
            {
                _isProcessing = false;
                App.Logger?.Error(ex, "AudioSyncService: Failed to process video");
                Error?.Invoke(this, $"Failed to prepare haptic sync: {ex.Message}");

                // Allow video to play without haptics
                ProcessingCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called when JS reports video playback state (currentTime, paused)
        /// Sends individual intensity commands for responsive haptic feedback.
        /// </summary>
        public void OnPlaybackStateUpdate(double currentTimeSeconds, bool paused)
        {
            // Log every 5 seconds
            if ((int)(currentTimeSeconds * 10) % 50 == 0)
            {
                App.Logger?.Information("AudioSyncService.OnPlaybackStateUpdate: time={Time:F1}s, paused={Paused}, enabled={Enabled}, chunkMgr={HasChunkMgr}",
                    currentTimeSeconds, paused, _settings.Enabled, _chunkManager != null);
            }

            if (!_settings.Enabled || _chunkManager == null)
                return;

            var currentTime = TimeSpan.FromSeconds(currentTimeSeconds);

            // Handle pause/resume
            if (paused && !_isPaused)
            {
                _isPaused = true;
                _isPlaying = false;
                _ = _hapticService.StopAsync();
                App.Logger?.Debug("AudioSyncService: Playback paused at {Time}", currentTime);
                return;
            }

            if (!paused && _isPaused)
            {
                _isPaused = false;
                App.Logger?.Information("AudioSyncService: Playback resumed at {Time}", currentTime);
            }

            if (paused)
                return;

            _isPlaying = true;
            _lastPlaybackPosition = currentTime;
            _lastSyncTime = DateTime.UtcNow;

            // Check buffer and trigger next chunk download if needed
            _chunkManager.CheckBufferAndProcess(currentTime);

            // Calculate look-ahead time with latency compensation
            // Total ~1600ms to account for device latency + network + processing + 200ms earlier
            var latencyMs = _hapticService.SubliminalAnticipationMs + 1350 + _settings.ManualLatencyOffsetMs;
            var lookAheadTime = currentTime + TimeSpan.FromMilliseconds(latencyMs);

            // Get intensity from track
            var track = _chunkManager.Track;
            if (!track.HasDataForTime(lookAheadTime))
            {
                if ((int)currentTime.TotalSeconds % 5 == 0)
                {
                    App.Logger?.Warning("AudioSyncService: No haptic data for time {Time}", lookAheadTime);
                }
                return;
            }

            var intensity = track.GetIntensityAt(lookAheadTime);

            // Log every 5 seconds
            if ((int)(currentTime.TotalSeconds * 2) % 10 == 0)
            {
                App.Logger?.Information("AudioSyncService: Sending intensity {Intensity:F3} at time {Time:F1}s",
                    intensity, currentTime.TotalSeconds);
            }

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
            App.Logger?.Information("AudioSyncService: User seeked to {Time}", newTime);

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
            App.Logger?.Information("AudioSyncService: Video ended");
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
                App.Logger?.Debug("AudioSyncService: Failed to send haptic: {Error}", ex.Message);
            }
        }


        private void OnChunkReady(object? sender, AudioChunk chunk)
        {
            App.Logger?.Debug("AudioSyncService: Chunk {Index} ready", chunk.Index);
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
