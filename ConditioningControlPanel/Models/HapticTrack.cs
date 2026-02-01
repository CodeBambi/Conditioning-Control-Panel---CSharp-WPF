using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a haptic intensity track generated from audio analysis.
    /// Stores intensity values indexed by time for synchronized playback.
    /// </summary>
    public class HapticTrack
    {
        /// <summary>
        /// Number of intensity samples per second (typically ~86 for 512 hop size at 44.1kHz)
        /// </summary>
        public int SamplesPerSecond { get; }

        /// <summary>
        /// Total duration of the track
        /// </summary>
        public TimeSpan Duration { get; private set; }

        /// <summary>
        /// Intensity data organized by chunk index
        /// Each chunk contains intensity values for its time range
        /// </summary>
        private readonly List<float[]> _chunks = new();

        /// <summary>
        /// Duration of each chunk
        /// </summary>
        public TimeSpan ChunkDuration { get; }

        /// <summary>
        /// Number of chunks currently loaded
        /// </summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>
        /// Creates a new haptic track with specified parameters
        /// </summary>
        /// <param name="samplesPerSecond">Intensity values per second</param>
        /// <param name="chunkDurationSeconds">Duration of each chunk in seconds</param>
        public HapticTrack(int samplesPerSecond, int chunkDurationSeconds = 300)
        {
            SamplesPerSecond = samplesPerSecond;
            ChunkDuration = TimeSpan.FromSeconds(chunkDurationSeconds);
            Duration = TimeSpan.Zero;
        }

        /// <summary>
        /// Adds a new chunk of intensity data
        /// </summary>
        /// <param name="intensities">Array of intensity values (0.0 to 1.0)</param>
        public void AddChunk(float[] intensities)
        {
            _chunks.Add(intensities);
            // Update total duration based on chunk data
            var chunkDuration = TimeSpan.FromSeconds((double)intensities.Length / SamplesPerSecond);
            Duration = TimeSpan.FromSeconds((_chunks.Count - 1) * ChunkDuration.TotalSeconds) + chunkDuration;
        }

        /// <summary>
        /// Gets the intensity value at a specific time with linear interpolation
        /// </summary>
        /// <param name="time">Time position in the track</param>
        /// <returns>Intensity value between 0.0 and 1.0, or 0 if time is out of range</returns>
        public float GetIntensityAt(TimeSpan time)
        {
            if (time < TimeSpan.Zero || _chunks.Count == 0)
                return 0f;

            // Determine which chunk contains this time
            var chunkIndex = (int)(time.TotalSeconds / ChunkDuration.TotalSeconds);

            if (chunkIndex >= _chunks.Count)
            {
                // Past the end of analyzed data - return last known value
                var lastChunk = _chunks[^1];
                return lastChunk.Length > 0 ? lastChunk[^1] : 0f;
            }

            var chunk = _chunks[chunkIndex];
            if (chunk.Length == 0)
                return 0f;

            // Calculate position within the chunk
            var chunkStartTime = TimeSpan.FromSeconds(chunkIndex * ChunkDuration.TotalSeconds);
            var timeInChunk = time - chunkStartTime;

            // Convert to sample index with interpolation
            var exactSampleIndex = timeInChunk.TotalSeconds * SamplesPerSecond;
            var sampleIndex = (int)exactSampleIndex;
            var fraction = (float)(exactSampleIndex - sampleIndex);

            if (sampleIndex >= chunk.Length - 1)
            {
                return chunk[^1];
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
        /// <returns>True if data exists for this time</returns>
        public bool HasDataForTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero || _chunks.Count == 0)
                return false;

            var chunkIndex = (int)(time.TotalSeconds / ChunkDuration.TotalSeconds);
            return chunkIndex < _chunks.Count;
        }

        /// <summary>
        /// Gets the amount of analyzed time ahead of the specified position
        /// </summary>
        /// <param name="currentTime">Current playback position</param>
        /// <returns>Buffer duration ahead of current position</returns>
        public TimeSpan GetBufferAhead(TimeSpan currentTime)
        {
            if (_chunks.Count == 0)
                return TimeSpan.Zero;

            var analyzedDuration = Duration;
            var bufferAhead = analyzedDuration - currentTime;
            return bufferAhead > TimeSpan.Zero ? bufferAhead : TimeSpan.Zero;
        }

        /// <summary>
        /// Clears all chunk data (for cleanup or reset)
        /// </summary>
        public void Clear()
        {
            _chunks.Clear();
            Duration = TimeSpan.Zero;
        }

        /// <summary>
        /// Gets the chunk index for a given time
        /// </summary>
        public int GetChunkIndexForTime(TimeSpan time)
        {
            return (int)(time.TotalSeconds / ChunkDuration.TotalSeconds);
        }
    }
}
