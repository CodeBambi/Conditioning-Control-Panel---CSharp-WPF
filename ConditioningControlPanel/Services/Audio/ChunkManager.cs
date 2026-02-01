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
        public string? TempVideoPath { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Manages chunked downloading and processing of video audio for haptic generation.
    /// Handles buffer management to ensure haptic data is always ready ahead of playback.
    /// </summary>
    public class ChunkManager : IDisposable
    {
        private readonly VideoDownloader _downloader;
        private readonly AudioAnalyzer _analyzer;
        private readonly AudioSyncSettings _settings;
        private readonly HapticTrack _track;

        private readonly List<AudioChunk> _chunks = new();
        private readonly object _lock = new();

        private string? _videoUrl;
        private TimeSpan _videoDuration;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;
        private bool _disposed;

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
        /// <param name="videoUrl">URL of the video</param>
        /// <param name="estimatedDuration">Estimated video duration (can be refined later)</param>
        public void Initialize(string videoUrl, TimeSpan? estimatedDuration = null)
        {
            lock (_lock)
            {
                Stop();

                _videoUrl = videoUrl;
                _videoDuration = estimatedDuration ?? TimeSpan.FromMinutes(30); // Default assumption
                _chunks.Clear();
                _track.Clear();
                _analyzer.Reset();

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
        /// Start processing the first chunk (call this to begin)
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
        /// Check buffer and trigger next chunk download if needed
        /// </summary>
        /// <param name="currentPlaybackTime">Current video playback position</param>
        public void CheckBufferAndProcess(TimeSpan currentPlaybackTime)
        {
            lock (_lock)
            {
                var bufferAhead = _track.GetBufferAhead(currentPlaybackTime);
                var minBuffer = TimeSpan.FromSeconds(_settings.MinBufferAheadSeconds);

                if (bufferAhead < minBuffer)
                {
                    // Find the next chunk that needs processing
                    var nextChunkIndex = _track.ChunkCount;
                    if (nextChunkIndex < _chunks.Count)
                    {
                        var chunk = _chunks[nextChunkIndex];
                        if (chunk.State == ChunkState.NotStarted)
                        {
                            App.Logger?.Information("ChunkManager: Buffer low ({Buffer:F1}s), starting chunk {Index}",
                                bufferAhead.TotalSeconds, nextChunkIndex);

                            // Start background processing
                            _cts?.Cancel();
                            _cts = new CancellationTokenSource();
                            _backgroundTask = Task.Run(() => ProcessChunkAsync(nextChunkIndex, _cts.Token));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Process a specific chunk
        /// </summary>
        private async Task ProcessChunkAsync(int chunkIndex, CancellationToken ct)
        {
            AudioChunk chunk;
            lock (_lock)
            {
                if (chunkIndex >= _chunks.Count)
                    return;
                chunk = _chunks[chunkIndex];
                if (chunk.State != ChunkState.NotStarted)
                    return;
                chunk.State = ChunkState.Downloading;
            }

            try
            {
                Progress?.Invoke(this, new ChunkProgressEventArgs(chunkIndex, "Downloading video...", 0));

                // Download video
                var tempPath = await _downloader.DownloadAsync(_videoUrl!, ct);
                chunk.TempVideoPath = tempPath;

                lock (_lock)
                {
                    chunk.State = ChunkState.Extracting;
                }

                Progress?.Invoke(this, new ChunkProgressEventArgs(chunkIndex, "Extracting audio...", 33));

                // Extract audio from video
                var samples = await ExtractAudioAsync(tempPath, ct);

                lock (_lock)
                {
                    chunk.State = ChunkState.Analyzing;
                }

                Progress?.Invoke(this, new ChunkProgressEventArgs(chunkIndex, "Analyzing audio...", 66));

                // Analyze audio
                var intensities = _analyzer.Analyze(samples, _settings);

                // Store results
                lock (_lock)
                {
                    chunk.IntensityData = intensities;
                    chunk.State = ChunkState.Ready;
                    _track.AddChunk(intensities);
                }

                // Clean up temp file
                CleanupTempFile(tempPath);

                Progress?.Invoke(this, new ChunkProgressEventArgs(chunkIndex, "Ready", 100));
                ChunkReady?.Invoke(this, chunk);

                App.Logger?.Information("ChunkManager: Chunk {Index} ready with {Samples} intensity samples",
                    chunkIndex, intensities.Length);
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    chunk.State = ChunkState.NotStarted; // Can retry later
                }
                CleanupTempFile(chunk.TempVideoPath);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    chunk.State = ChunkState.Failed;
                    chunk.ErrorMessage = ex.Message;
                }
                CleanupTempFile(chunk.TempVideoPath);
                Error?.Invoke(this, $"Chunk {chunkIndex} failed: {ex.Message}");
                App.Logger?.Error(ex, "ChunkManager: Failed to process chunk {Index}", chunkIndex);
            }
        }

        /// <summary>
        /// Extract audio samples from a video file using NAudio
        /// </summary>
        private async Task<float[]> ExtractAudioAsync(string videoPath, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                using var reader = new MediaFoundationReader(videoPath);
                var sampleProvider = reader.ToSampleProvider();

                // Convert to mono if stereo
                if (sampleProvider.WaveFormat.Channels > 1)
                {
                    sampleProvider = sampleProvider.ToMono();
                }

                // Read all samples
                var samples = new List<float>();
                var buffer = new float[4096];
                int read;

                while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    for (int i = 0; i < read; i++)
                    {
                        samples.Add(buffer[i]);
                    }
                }

                // Calculate some audio stats for debugging
                float minSample = float.MaxValue, maxSample = float.MinValue;
                foreach (var s in samples)
                {
                    minSample = MathF.Min(minSample, s);
                    maxSample = MathF.Max(maxSample, s);
                }

                App.Logger?.Information("ChunkManager: Extracted {Count} audio samples from {Path}",
                    samples.Count, videoPath);
                App.Logger?.Information("ChunkManager: Audio sample range: Min={Min:F4}, Max={Max:F4}",
                    minSample, maxSample);

                return samples.ToArray();
            }, ct);
        }

        /// <summary>
        /// Stop all processing
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _backgroundTask?.Wait(1000);
            _cts?.Dispose();
            _cts = null;
            _backgroundTask = null;

            // Clean up any temp files
            lock (_lock)
            {
                foreach (var chunk in _chunks)
                {
                    CleanupTempFile(chunk.TempVideoPath);
                }
            }
        }

        private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
        {
            var percent = e.PercentComplete ?? 0;
            Progress?.Invoke(this, new ChunkProgressEventArgs(-1, $"Downloading... {percent:F0}%", (int)(percent * 0.33)));
        }

        private static void CleanupTempFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _downloader.Dispose();
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
