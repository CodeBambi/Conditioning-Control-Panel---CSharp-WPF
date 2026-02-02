using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a haptic intensity track generated from audio analysis.
    /// Stores intensity values indexed by time for synchronized playback.
    /// Supports progressive chunk loading and sparse chunk storage.
    /// </summary>
    public class HapticTrack
    {
        /// <summary>
        /// Number of intensity samples per second (typically ~86 for 512 hop size at 44.1kHz)
        /// </summary>
        public int SamplesPerSecond { get; }

        /// <summary>
        /// Total duration of loaded data
        /// </summary>
        public TimeSpan Duration { get; private set; }

        /// <summary>
        /// Intensity data organized by chunk index
        /// Each chunk contains intensity values for its time range
        /// Chunks can be null if not yet loaded (sparse loading)
        /// </summary>
        private readonly Dictionary<int, float[]> _chunks = new();

        /// <summary>
        /// Duration of each chunk in seconds
        /// </summary>
        public int ChunkDurationSeconds { get; }

        /// <summary>
        /// Duration of each chunk
        /// </summary>
        public TimeSpan ChunkDuration => TimeSpan.FromSeconds(ChunkDurationSeconds);

        /// <summary>
        /// Number of chunks currently loaded
        /// </summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>
        /// Highest chunk index that has been loaded
        /// </summary>
        public int HighestChunkIndex { get; private set; } = -1;

        /// <summary>
        /// Creates a new haptic track with specified parameters
        /// </summary>
        /// <param name="samplesPerSecond">Intensity values per second</param>
        /// <param name="chunkDurationSeconds">Duration of each chunk in seconds</param>
        public HapticTrack(int samplesPerSecond, int chunkDurationSeconds = 300)
        {
            SamplesPerSecond = samplesPerSecond;
            ChunkDurationSeconds = chunkDurationSeconds;
            Duration = TimeSpan.Zero;
        }

        /// <summary>
        /// Adds a new chunk of intensity data (appends to end)
        /// </summary>
        public void AddChunk(float[] intensities)
        {
            var nextIndex = HighestChunkIndex + 1;
            SetChunk(nextIndex, intensities);
        }

        /// <summary>
        /// Sets a chunk at a specific index (supports out-of-order loading)
        /// </summary>
        public void SetChunk(int chunkIndex, float[] intensities)
        {
            _chunks[chunkIndex] = intensities;

            if (chunkIndex > HighestChunkIndex)
            {
                HighestChunkIndex = chunkIndex;
            }

            // Recalculate duration based on highest chunk
            var chunkEndTime = TimeSpan.FromSeconds((chunkIndex + 1) * ChunkDurationSeconds);
            if (chunkEndTime > Duration)
            {
                Duration = chunkEndTime;
            }
        }

        /// <summary>
        /// Checks if a specific chunk is loaded
        /// </summary>
        public bool IsChunkLoaded(int chunkIndex)
        {
            return _chunks.ContainsKey(chunkIndex);
        }

        /// <summary>
        /// Gets the intensity value at a specific time with linear interpolation
        /// </summary>
        /// <param name="time">Time position in the track</param>
        /// <returns>Intensity value between 0.0 and 1.0, or 0 if time is out of range or chunk not loaded</returns>
        public float GetIntensityAt(TimeSpan time)
        {
            if (time < TimeSpan.Zero || _chunks.Count == 0)
                return 0f;

            // Determine which chunk contains this time
            var chunkIndex = (int)(time.TotalSeconds / ChunkDurationSeconds);

            // Check if this chunk is loaded
            if (!_chunks.TryGetValue(chunkIndex, out var chunk) || chunk == null || chunk.Length == 0)
            {
                return 0f; // Chunk not loaded yet
            }

            // Calculate position within the chunk
            var chunkStartTime = TimeSpan.FromSeconds(chunkIndex * ChunkDurationSeconds);
            var timeInChunk = time - chunkStartTime;

            // Convert to sample index with interpolation
            var exactSampleIndex = timeInChunk.TotalSeconds * SamplesPerSecond;
            var sampleIndex = (int)exactSampleIndex;
            var fraction = (float)(exactSampleIndex - sampleIndex);

            if (sampleIndex >= chunk.Length - 1)
            {
                return chunk[^1];
            }

            if (sampleIndex < 0)
            {
                return chunk[0];
            }

            // Linear interpolation between samples for smooth output
            var current = chunk[sampleIndex];
            var next = chunk[Math.Min(sampleIndex + 1, chunk.Length - 1)];
            return current + (next - current) * fraction;
        }

        /// <summary>
        /// Checks if we have analyzed data for the specified time
        /// </summary>
        /// <param name="time">Time to check</param>
        /// <returns>True if chunk for this time is loaded</returns>
        public bool HasDataForTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
                return false;

            var chunkIndex = (int)(time.TotalSeconds / ChunkDurationSeconds);
            return _chunks.ContainsKey(chunkIndex);
        }

        /// <summary>
        /// Gets the amount of contiguous analyzed time ahead of the specified position
        /// </summary>
        public TimeSpan GetBufferAhead(TimeSpan currentTime)
        {
            if (_chunks.Count == 0)
                return TimeSpan.Zero;

            var currentChunkIndex = (int)(currentTime.TotalSeconds / ChunkDurationSeconds);

            // Find how many contiguous chunks we have from current position
            int contiguousChunks = 0;
            for (int i = currentChunkIndex; i <= HighestChunkIndex; i++)
            {
                if (_chunks.ContainsKey(i))
                    contiguousChunks++;
                else
                    break; // Gap found
            }

            if (contiguousChunks == 0)
                return TimeSpan.Zero;

            var bufferEndTime = TimeSpan.FromSeconds((currentChunkIndex + contiguousChunks) * ChunkDurationSeconds);
            var bufferAhead = bufferEndTime - currentTime;
            return bufferAhead > TimeSpan.Zero ? bufferAhead : TimeSpan.Zero;
        }

        /// <summary>
        /// Clears all chunk data (for cleanup or reset)
        /// </summary>
        public void Clear()
        {
            _chunks.Clear();
            Duration = TimeSpan.Zero;
            HighestChunkIndex = -1;
        }

        /// <summary>
        /// Gets the chunk index for a given time
        /// </summary>
        public int GetChunkIndexForTime(TimeSpan time)
        {
            return (int)(time.TotalSeconds / ChunkDurationSeconds);
        }
    }
}
