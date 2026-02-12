using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using NAudio.Wave;
using Serilog;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// State of a single audio chunk
    /// </summary>
    public enum ChunkState
    {
        NotStarted,
        Downloading,
        Extracting,
        Analyzing,
        Ready,
        Failed
    }

    /// <summary>
    /// Represents a single chunk of audio to be processed
    /// </summary>
    public class AudioChunk
    {
        public int Index { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public ChunkState State { get; set; } = ChunkState.NotStarted;
        public float[]? IntensityData { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Manages chunked downloading and processing of video audio for haptic generation.
    /// Downloads video once, then processes audio in 5-minute chunks progressively.
    /// </summary>
    public class ChunkManager : IDisposable
    {
        private readonly VideoDownloader _downloader;
        private readonly AudioAnalyzer _analyzer;
        private readonly AudioSyncSettings _settings;
        private readonly HapticTrack _track;

        private readonly List<AudioChunk> _chunks = new();
        private readonly object _lock = new();
        private readonly SemaphoreSlim _processingSemaphore = new(1, 1);

        private string? _videoUrl;
        private string? _cachedVideoPath;  // Keep video file for all chunks
        private TimeSpan _videoDuration;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;
        private bool _disposed;
        private int _currentlyProcessingChunk = -1;

        /// <summary>
        /// Event fired when a chunk finishes processing
        /// </summary>
        public event EventHandler<AudioChunk>? ChunkReady;

        /// <summary>
        /// Event fired when processing encounters an error
        /// </summary>
        public event EventHandler<string>? Error;

        /// <summary>
        /// Event fired to report download/processing progress
        /// </summary>
        public event EventHandler<ChunkProgressEventArgs>? Progress;

        /// <summary>
        /// Event fired when a chunk needs to be loaded (for seek handling)
        /// </summary>
        public event EventHandler<int>? ChunkLoadingStarted;

        /// <summary>
        /// The haptic track containing all processed intensity data
        /// </summary>
        public HapticTrack Track => _track;

        /// <summary>
        /// Whether the first chunk is ready for playback
        /// </summary>
        public bool IsFirstChunkReady
        {
            get
            {
                lock (_lock)
                {
                    return _chunks.Count > 0 && _chunks[0].State == ChunkState.Ready;
                }
            }
        }

        /// <summary>
        /// Check if a specific chunk is ready
        /// </summary>
        public bool IsChunkReady(int chunkIndex)
        {
            lock (_lock)
            {
                return chunkIndex < _chunks.Count && _chunks[chunkIndex].State == ChunkState.Ready;
            }
        }

        /// <summary>
        /// Get chunk index for a given time
        /// </summary>
        public int GetChunkIndexForTime(TimeSpan time)
        {
            return (int)(time.TotalSeconds / _settings.ChunkDurationSeconds);
        }

        public ChunkManager(AudioSyncSettings settings)
        {
            _settings = settings;
            _downloader = new VideoDownloader();
            _analyzer = new AudioAnalyzer();
            _track = new HapticTrack(_analyzer.OutputSampleRate, settings.ChunkDurationSeconds);

            _downloader.ProgressChanged += OnDownloadProgress;
        }

        /// <summary>
        /// Initialize chunk processing for a video
        /// </summary>
        public void Initialize(string videoUrl, TimeSpan? estimatedDuration = null)
        {
            lock (_lock)
            {
                Stop();

                // Clean up previous cached video file before losing the reference
                if (!string.IsNullOrEmpty(_cachedVideoPath))
                {
                    try
                    {
                        if (File.Exists(_cachedVideoPath))
                            File.Delete(_cachedVideoPath);
                    }
                    catch { }
                }

                _videoUrl = videoUrl;
                _videoDuration = estimatedDuration ?? TimeSpan.FromMinutes(30);
                _chunks.Clear();
                _track.Clear();
                _cachedVideoPath = null;

                // Create chunk definitions
                var chunkDuration = TimeSpan.FromSeconds(_settings.ChunkDurationSeconds);
                var currentTime = TimeSpan.Zero;
                int index = 0;

                while (currentTime < _videoDuration)
                {
                    var endTime = currentTime + chunkDuration;
                    if (endTime > _videoDuration)
                        endTime = _videoDuration;

                    _chunks.Add(new AudioChunk
                    {
                        Index = index++,
                        StartTime = currentTime,
                        EndTime = endTime,
                        State = ChunkState.NotStarted
                    });

                    currentTime = endTime;
                }

                App.Logger?.Information("ChunkManager: Initialized with {ChunkCount} chunks for video duration {Duration}",
                    _chunks.Count, _videoDuration);
            }
        }

        /// <summary>
        /// Start processing the first chunk (downloads video and processes first 5 min)
        /// </summary>
        public async Task StartFirstChunkAsync(CancellationToken ct = default)
        {
            if (_chunks.Count == 0 || string.IsNullOrEmpty(_videoUrl))
            {
                Error?.Invoke(this, "No video initialized");
                return;
            }

            await ProcessChunkAsync(0, ct);
        }

        /// <summary>
        /// Ensure a specific chunk is ready (for seek handling)
        /// Returns when the chunk is processed
        /// </summary>
        public async Task EnsureChunkReadyAsync(int chunkIndex, CancellationToken ct = default)
        {
            if (chunkIndex < 0 || chunkIndex >= _chunks.Count)
                return;

            // Check if already ready
            lock (_lock)
            {
                if (_chunks[chunkIndex].State == ChunkState.Ready)
                    return;
            }

            // Signal that we're loading this chunk
            ChunkLoadingStarted?.Invoke(this, chunkIndex);

            // Process the chunk (will wait if another chunk is being processed)
            await ProcessChunkAsync(chunkIndex, ct);
        }

        /// <summary>
        /// Check buffer and trigger next chunk processing if needed
        /// </summary>
        public void CheckBufferAndProcess(TimeSpan currentPlaybackTime)
        {
            lock (_lock)
            {
                // Find the chunk for current time
                var currentChunkIndex = GetChunkIndexForTime(currentPlaybackTime);

                // Check if we need to process upcoming chunks
                for (int i = currentChunkIndex; i < _chunks.Count && i <= currentChunkIndex + 1; i++)
                {
                    if (i < _chunks.Count && _chunks[i].State == ChunkState.NotStarted)
                    {
                        App.Logger?.Information("ChunkManager: Starting chunk {Index} (current time: {Time:F1}s)",
                            i, currentPlaybackTime.TotalSeconds);

                        // Start background processing
                        var chunkToProcess = i;
                        _cts?.Cancel();
                        _cts = new CancellationTokenSource();
                        _backgroundTask = Task.Run(() => ProcessChunkAsync(chunkToProcess, _cts.Token));
                        break; // Only start one at a time
                    }
                }
            }
        }

        /// <summary>
        /// Process a specific chunk
        /// </summary>
        private async Task ProcessChunkAsync(int chunkIndex, CancellationToken ct)
        {
            // Use semaphore to ensure only one chunk processes at a time
            await _processingSemaphore.WaitAsync(ct);

            try
            {
                AudioChunk chunk;
                lock (_lock)
                {
                    if (chunkIndex >= _chunks.Count)
                        return;
                    chunk = _chunks[chunkIndex];
                    if (chunk.State == ChunkState.Ready)
                        return; // Already done
                    if (chunk.State != ChunkState.NotStarted && chunk.State != ChunkState.Failed)
                        return; // Already processing

                    chunk.State = ChunkState.Downloading;
                    _currentlyProcessingChunk = chunkIndex;
                }

                App.Logger?.Information("ChunkManager: Processing chunk {Index} ({Start:F0}s - {End:F0}s)",
                    chunkIndex, chunk.StartTime.TotalSeconds, chunk.EndTime.TotalSeconds);

                // Download video if not already cached
                if (string.IsNullOrEmpty(_cachedVideoPath) || !File.Exists(_cachedVideoPath))
                {
                    Progress?.Invoke(this, new ChunkProgressEventArgs(chunkIndex, "Downloading video...", 0));
                    _cachedVideoPath = await _downloader.DownloadAsync(_videoUrl!, ct);
                    App.Logger?.Information("ChunkManager: Video downloaded to {Path}", _cachedVideoPath);
                }

                lock (_lock)
                {
                    chunk.State = ChunkState.Extracting;
                }

                Progress?.Invoke(this, new ChunkProgressEventArgs(chunkIndex, $"Extracting audio (chunk {chunkIndex + 1})...", 33));

                // Extract audio for THIS CHUNK ONLY
                var samples = await ExtractAudioRangeAsync(_cachedVideoPath, chunk.StartTime, chunk.EndTime, ct);

                if (samples.Length == 0)
                {
                    App.Logger?.Warning("ChunkManager: No audio samples extracted for chunk {Index}", chunkIndex);
                    lock (_lock)
                    {
                        chunk.State = ChunkState.Failed;
                        chunk.ErrorMessage = "No audio samples";
                    }
                    return;
                }

                lock (_lock)
                {
                    chunk.State = ChunkState.Analyzing;
                }

                Progress?.Invoke(this, new ChunkProgressEventArgs(chunkIndex, $"Analyzing audio (chunk {chunkIndex + 1})...", 66));

                // Reset analyzer for each chunk to get proper normalization
                _analyzer.Reset();

                // Analyze audio
                var intensities = _analyzer.Analyze(samples, _settings);

                // Store results
                lock (_lock)
                {
                    chunk.IntensityData = intensities;
                    chunk.State = ChunkState.Ready;

                    // Add chunk to track at the correct position
                    _track.SetChunk(chunkIndex, intensities);
                    _currentlyProcessingChunk = -1;
                }

                Progress?.Invoke(this, new ChunkProgressEventArgs(chunkIndex, "Ready", 100));
                ChunkReady?.Invoke(this, chunk);

                App.Logger?.Information("ChunkManager: Chunk {Index} ready with {Samples} intensity samples ({Duration:F1}s of audio)",
                    chunkIndex, intensities.Length, (double)intensities.Length / _analyzer.OutputSampleRate);
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    if (chunkIndex < _chunks.Count)
                        _chunks[chunkIndex].State = ChunkState.NotStarted;
                    _currentlyProcessingChunk = -1;
                }
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    if (chunkIndex < _chunks.Count)
                    {
                        _chunks[chunkIndex].State = ChunkState.Failed;
                        _chunks[chunkIndex].ErrorMessage = ex.Message;
                    }
                    _currentlyProcessingChunk = -1;
                }
                Error?.Invoke(this, $"Chunk {chunkIndex} failed: {ex.Message}");
                App.Logger?.Error(ex, "ChunkManager: Failed to process chunk {Index}", chunkIndex);
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        /// <summary>
        /// Extract audio samples for a specific time range
        /// </summary>
        private async Task<float[]> ExtractAudioRangeAsync(string videoPath, TimeSpan startTime, TimeSpan endTime, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var reader = new MediaFoundationReader(videoPath);

                    // Seek FIRST before creating sample provider
                    if (startTime > TimeSpan.Zero)
                    {
                        reader.CurrentTime = startTime;
                        App.Logger?.Debug("ChunkManager: Seeked to {Time:F1}s", startTime.TotalSeconds);
                    }

                    // Now create sample provider from seeked position
                    var sampleProvider = reader.ToSampleProvider();

                    // Convert to mono if stereo
                    if (sampleProvider.WaveFormat.Channels > 1)
                    {
                        sampleProvider = sampleProvider.ToMono();
                    }

                    var sampleRate = sampleProvider.WaveFormat.SampleRate;
                    var durationSeconds = (endTime - startTime).TotalSeconds;
                    var samplesToRead = (int)(durationSeconds * sampleRate);

                    // Read samples for this chunk only
                    var samples = new List<float>(samplesToRead);
                    var buffer = new float[4096];
                    int totalRead = 0;

                    while (totalRead < samplesToRead)
                    {
                        ct.ThrowIfCancellationRequested();

                        var toRead = Math.Min(buffer.Length, samplesToRead - totalRead);
                        var read = sampleProvider.Read(buffer, 0, toRead);

                        if (read == 0)
                            break; // End of file

                        for (int i = 0; i < read; i++)
                        {
                            samples.Add(buffer[i]);
                        }
                        totalRead += read;
                    }

                    App.Logger?.Information("ChunkManager: Extracted {Count} samples for range {Start:F1}s - {End:F1}s",
                        samples.Count, startTime.TotalSeconds, endTime.TotalSeconds);

                    return samples.ToArray();
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "ChunkManager: Failed to extract audio for range {Start:F1}s - {End:F1}s",
                        startTime.TotalSeconds, endTime.TotalSeconds);
                    return Array.Empty<float>();
                }
            }, ct);
        }

        /// <summary>
        /// Stop all processing
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _backgroundTask?.Wait(1000);
            }
            catch { }
            _cts?.Dispose();
            _cts = null;
            _backgroundTask = null;
            _currentlyProcessingChunk = -1;
        }

        /// <summary>
        /// Clean up and dispose
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();

            // Clean up cached video file
            if (!string.IsNullOrEmpty(_cachedVideoPath))
            {
                try
                {
                    if (File.Exists(_cachedVideoPath))
                        File.Delete(_cachedVideoPath);
                }
                catch { }
            }

            _downloader.Dispose();
            _processingSemaphore.Dispose();
        }

        private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
        {
            var percent = e.PercentComplete ?? 0;
            Progress?.Invoke(this, new ChunkProgressEventArgs(-1, $"Downloading... {percent:F0}%", (int)(percent * 0.33)));
        }
    }

    /// <summary>
    /// Progress event arguments for chunk processing
    /// </summary>
    public class ChunkProgressEventArgs : EventArgs
    {
        public int ChunkIndex { get; }
        public string Status { get; }
        public int PercentComplete { get; }

        public ChunkProgressEventArgs(int chunkIndex, string status, int percentComplete)
        {
            ChunkIndex = chunkIndex;
            Status = status;
            PercentComplete = percentComplete;
        }
    }
}
