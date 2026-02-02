using System;
using System.Collections.Generic;
using System.Numerics;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// Analyzes audio samples using FFT to extract features for haptic intensity generation.
    /// Extracts RMS (volume), bass energy, and onset detection.
    /// </summary>
    public class AudioAnalyzer
    {
        // FFT parameters
        private const int FFT_SIZE = 2048;        // ~46ms window at 44.1kHz
        private const int HOP_SIZE = 512;         // ~11.6ms hop = ~86 samples/sec output
        private const int SAMPLE_RATE = 44100;

        // Frequency band definitions (bin indices for FFT_SIZE at 44.1kHz)
        // Each bin = SAMPLE_RATE / FFT_SIZE = ~21.5Hz
        private const int BASS_LOW_BIN = 1;       // ~21Hz
        private const int BASS_HIGH_BIN = 12;     // ~258Hz
        private const int MID_LOW_BIN = 12;       // ~258Hz
        private const int MID_HIGH_BIN = 50;      // ~1075Hz
        private const int HIGH_LOW_BIN = 93;      // ~2000Hz (snaps, claps, hi-hats)
        private const int HIGH_HIGH_BIN = 300;    // ~6450Hz

        // Transient detection parameters
        private const int TRANSIENT_HISTORY_SIZE = 43;  // ~0.5 sec rolling window at 86 samples/sec
        private const float TRANSIENT_THRESHOLD_MULT = 2.0f;  // Trigger when flux > 2x average
        private const int TRANSIENT_PULSE_FRAMES = 6;   // ~70ms pulse duration
        private const float TRANSIENT_INTENSITY = 1.0f; // Full intensity for transients

        // Adaptive baseline parameters (for filtering sustained audio like binaurals)
        // Baseline tracks slow-moving average; we only respond to energy ABOVE baseline
        private const int BASELINE_WINDOW_FRAMES = 258;     // ~3 seconds at 86 fps
        private const float BASELINE_RISE_RATE = 0.015f;    // How fast baseline rises to meet sustained energy
        private const float BASELINE_FALL_RATE = 0.003f;    // How fast baseline falls (slower = more memory)
        private const float BASELINE_SUBTRACT_FACTOR = 0.85f; // Subtract 85% of baseline from current

        // Bass-specific onset detection (for bass drops)
        private const int BASS_FLUX_HISTORY_SIZE = 43;      // ~0.5 sec
        private const float BASS_ONSET_THRESHOLD_MULT = 2.5f; // Trigger on significant bass increase
        private const int BASS_PULSE_FRAMES = 8;            // ~93ms for bass hits (slightly longer)
        private const float BASS_ONSET_INTENSITY = 0.95f;   // Strong but not quite max

        // Hanning window coefficients (pre-computed)
        private readonly float[] _window;

        // Previous spectrum for onset detection
        private float[]? _previousSpectrum;

        // Running statistics for normalization
        private float _maxRms = 0.001f;
        private float _maxBass = 0.001f;
        private float _maxOnset = 0.001f;

        // High-band transient detection state
        private readonly Queue<float> _highFluxHistory = new();
        private int _transientCooldown = 0;  // Frames until next transient can trigger

        // Bass onset detection state
        private readonly Queue<float> _bassFluxHistory = new();
        private int _bassCooldown = 0;

        /// <summary>
        /// Output samples per second based on hop size
        /// </summary>
        public int OutputSampleRate => SAMPLE_RATE / HOP_SIZE;

        public AudioAnalyzer()
        {
            // Pre-compute Hanning window
            _window = new float[FFT_SIZE];
            for (int i = 0; i < FFT_SIZE; i++)
            {
                _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FFT_SIZE - 1)));
            }
        }

        /// <summary>
        /// Analyzes mono audio samples and returns intensity values.
        /// </summary>
        /// <param name="samples">Mono float samples (-1 to 1)</param>
        /// <param name="settings">Analysis settings for weights and sensitivity</param>
        /// <returns>Array of intensity values (0 to 1) at OutputSampleRate</returns>
        public float[] Analyze(float[] samples, AudioSyncSettings settings)
        {
            if (samples.Length < FFT_SIZE)
            {
                Log.Warning("AudioAnalyzer: Not enough samples ({Count}) for FFT analysis", samples.Length);
                return Array.Empty<float>();
            }

            var windowCount = (samples.Length - FFT_SIZE) / HOP_SIZE + 1;
            var intensities = new float[windowCount];

            var fftBuffer = new Complex[FFT_SIZE];
            var spectrum = new float[FFT_SIZE / 2];
            float previousIntensity = 0f;

            // Reset max trackers for this chunk (adaptive normalization)
            _maxRms = 0.001f;
            _maxBass = 0.001f;
            _maxOnset = 0.001f;

            // First pass: compute features and track maximums
            var features = new (float rms, float bass, float onset, float highFlux, float bassFlux)[windowCount];

            for (int i = 0; i < windowCount; i++)
            {
                var offset = i * HOP_SIZE;

                // Apply window and compute RMS
                float sumSquares = 0f;
                for (int j = 0; j < FFT_SIZE; j++)
                {
                    var windowed = samples[offset + j] * _window[j];
                    fftBuffer[j] = new Complex(windowed, 0);
                    sumSquares += windowed * windowed;
                }

                var rms = MathF.Sqrt(sumSquares / FFT_SIZE);
                _maxRms = MathF.Max(_maxRms, rms);

                // Perform FFT
                FFT(fftBuffer);

                // Extract magnitude spectrum
                for (int j = 0; j < FFT_SIZE / 2; j++)
                {
                    spectrum[j] = (float)fftBuffer[j].Magnitude;
                }

                // Calculate bass energy (20-250Hz)
                float bassEnergy = 0f;
                for (int j = BASS_LOW_BIN; j <= BASS_HIGH_BIN && j < spectrum.Length; j++)
                {
                    bassEnergy += spectrum[j];
                }
                _maxBass = MathF.Max(_maxBass, bassEnergy);

                // Calculate onset (spectral flux) - per band
                float onset = 0f;
                float bassFlux = 0f;  // Bass-band flux for bass drop detection
                float highFlux = 0f;  // High-band flux for transient detection
                if (_previousSpectrum != null)
                {
                    for (int j = 0; j < spectrum.Length; j++)
                    {
                        var diff = spectrum[j] - _previousSpectrum[j];
                        if (diff > 0) // Only count increases (onsets, not offsets)
                        {
                            onset += diff;
                            // Track bass-band flux (20-250Hz) for bass drops
                            if (j >= BASS_LOW_BIN && j <= BASS_HIGH_BIN)
                            {
                                bassFlux += diff;
                            }
                            // Track high-band flux (2kHz+) for snaps/claps
                            if (j >= HIGH_LOW_BIN && j <= HIGH_HIGH_BIN)
                            {
                                highFlux += diff;
                            }
                        }
                    }
                }
                _maxOnset = MathF.Max(_maxOnset, onset);

                // Store features (including band-specific flux for onset detection)
                features[i] = (rms, bassEnergy, onset, highFlux, bassFlux);

                // Update previous spectrum
                _previousSpectrum ??= new float[spectrum.Length];
                Array.Copy(spectrum, _previousSpectrum, spectrum.Length);
            }

            // Second pass: normalize with adaptive baseline, detect transients
            // The key insight: subtract a slow-moving baseline to filter sustained audio (binaurals)
            // Only energy ABOVE the baseline triggers haptics
            var rawIntensities = new float[windowCount];
            var transientFrames = new bool[windowCount];  // Mark frames with high-band transients
            var bassOnsetFrames = new bool[windowCount];  // Mark frames with bass onsets

            _highFluxHistory.Clear();
            _bassFluxHistory.Clear();
            _transientCooldown = 0;
            _bassCooldown = 0;

            // Adaptive baselines - start at first frame's values
            float rmsBaseline = features[0].rms;
            float bassBaseline = features[0].bass;

            for (int i = 0; i < windowCount; i++)
            {
                var (rms, bass, onset, highFlux, bassFlux) = features[i];

                // Normalize features to 0-1 range
                var normRms = rms / _maxRms;
                var normBass = bass / _maxBass;
                var normOnset = _maxOnset > 0.001f ? onset / _maxOnset : 0f;

                // Normalize baselines too
                var normRmsBaseline = rmsBaseline / _maxRms;
                var normBassBaseline = bassBaseline / _maxBass;

                // Calculate delta above baseline (only positive = energy increase)
                // This filters out sustained energy (binaurals) which raises the baseline
                var rmsAboveBaseline = MathF.Max(0, normRms - normRmsBaseline * BASELINE_SUBTRACT_FACTOR);
                var bassAboveBaseline = MathF.Max(0, normBass - normBassBaseline * BASELINE_SUBTRACT_FACTOR);

                // Update baselines asymmetrically:
                // - Rise quickly to track sustained energy (filters it out)
                // - Fall slowly to remember recent levels
                if (rms > rmsBaseline)
                    rmsBaseline += (rms - rmsBaseline) * BASELINE_RISE_RATE;
                else
                    rmsBaseline += (rms - rmsBaseline) * BASELINE_FALL_RATE;

                if (bass > bassBaseline)
                    bassBaseline += (bass - bassBaseline) * BASELINE_RISE_RATE;
                else
                    bassBaseline += (bass - bassBaseline) * BASELINE_FALL_RATE;

                // Combine features with weights, using delta-above-baseline for RMS and bass
                // Onset (spectral flux) is already change-based so use it directly
                rawIntensities[i] =
                    rmsAboveBaseline * (float)settings.RmsWeight +
                    bassAboveBaseline * (float)settings.BassWeight +
                    normOnset * (float)settings.OnsetWeight;

                // ===== HIGH-BAND TRANSIENT DETECTION (snaps, claps, hi-hats) =====
                _highFluxHistory.Enqueue(highFlux);
                if (_highFluxHistory.Count > TRANSIENT_HISTORY_SIZE)
                    _highFluxHistory.Dequeue();

                if (_transientCooldown > 0)
                {
                    _transientCooldown--;
                }
                else if (_highFluxHistory.Count >= 10)
                {
                    float avgHighFlux = 0f;
                    foreach (var f in _highFluxHistory)
                        avgHighFlux += f;
                    avgHighFlux /= _highFluxHistory.Count;

                    if (avgHighFlux > 0.001f && highFlux > avgHighFlux * TRANSIENT_THRESHOLD_MULT)
                    {
                        transientFrames[i] = true;
                        _transientCooldown = TRANSIENT_PULSE_FRAMES;
                        Log.Debug("AudioAnalyzer: High transient at frame {Frame}", i);
                    }
                }

                // ===== BASS ONSET DETECTION (bass drops, kicks) =====
                _bassFluxHistory.Enqueue(bassFlux);
                if (_bassFluxHistory.Count > BASS_FLUX_HISTORY_SIZE)
                    _bassFluxHistory.Dequeue();

                if (_bassCooldown > 0)
                {
                    _bassCooldown--;
                }
                else if (_bassFluxHistory.Count >= 10)
                {
                    float avgBassFlux = 0f;
                    foreach (var f in _bassFluxHistory)
                        avgBassFlux += f;
                    avgBassFlux /= _bassFluxHistory.Count;

                    if (avgBassFlux > 0.001f && bassFlux > avgBassFlux * BASS_ONSET_THRESHOLD_MULT)
                    {
                        bassOnsetFrames[i] = true;
                        _bassCooldown = BASS_PULSE_FRAMES;
                        Log.Debug("AudioAnalyzer: Bass onset at frame {Frame}", i);
                    }
                }
            }

            Log.Debug("AudioAnalyzer: Final baselines - RMS={RmsBase:F4}, Bass={BassBase:F4}",
                rmsBaseline / _maxRms, bassBaseline / _maxBass);

            // Calculate median intensity for quiet threshold
            var sortedIntensities = new float[windowCount];
            Array.Copy(rawIntensities, sortedIntensities, windowCount);
            Array.Sort(sortedIntensities);
            var medianIntensity = sortedIntensities[windowCount / 2];
            var quietThreshold = medianIntensity * 0.3f; // 30% of median = silence

            Log.Debug("AudioAnalyzer: Median intensity={Median:F4}, quiet threshold={Threshold:F4}",
                medianIntensity, quietThreshold);

            // Third pass: apply threshold, sensitivity, smoothing, clamping, and onset pulses
            int highTransientRemaining = 0;   // Frames left in high-band transient pulse
            int bassOnsetRemaining = 0;       // Frames left in bass onset pulse
            for (int i = 0; i < windowCount; i++)
            {
                var intensity = rawIntensities[i];

                // Check if this frame starts onset pulses
                if (transientFrames[i])
                    highTransientRemaining = TRANSIENT_PULSE_FRAMES;
                if (bassOnsetFrames[i])
                    bassOnsetRemaining = BASS_PULSE_FRAMES;

                // Onset pulses override normal intensity (high transient takes priority)
                if (highTransientRemaining > 0)
                {
                    intensity = TRANSIENT_INTENSITY;
                    highTransientRemaining--;
                    bassOnsetRemaining = 0;  // High transient supersedes bass
                    previousIntensity = intensity;
                    intensities[i] = intensity;
                    continue;
                }

                if (bassOnsetRemaining > 0)
                {
                    intensity = BASS_ONSET_INTENSITY;
                    bassOnsetRemaining--;
                    previousIntensity = intensity;
                    intensities[i] = intensity;
                    continue;
                }

                // If below quiet threshold, set to 0 (no vibration during silence)
                if (intensity < quietThreshold)
                {
                    intensity = 0f;
                }
                else
                {
                    // Apply sensitivity curve (< 1 = more sensitive to quiet, > 1 = less sensitive)
                    if (settings.Sensitivity != 1.0 && intensity > 0)
                    {
                        intensity = MathF.Pow(intensity, 1f / (float)settings.Sensitivity);
                    }

                    // Clamp to min/max range (only for non-silent parts)
                    intensity = Math.Clamp(intensity, (float)settings.MinIntensity, (float)settings.MaxIntensity);
                }

                // Apply smoothing (exponential moving average)
                if (settings.Smoothing > 0)
                {
                    intensity = previousIntensity * (float)settings.Smoothing +
                                intensity * (1f - (float)settings.Smoothing);
                }
                previousIntensity = intensity;

                intensities[i] = intensity;
            }

            Log.Debug("AudioAnalyzer: Analyzed {Samples} samples into {Intensities} intensity values. MaxRMS={MaxRms:F4}, MaxBass={MaxBass:F4}, MaxOnset={MaxOnset:F4}",
                samples.Length, intensities.Length, _maxRms, _maxBass, _maxOnset);

            return intensities;
        }

        /// <summary>
        /// Resets the analyzer state (call when starting a new video)
        /// </summary>
        public void Reset()
        {
            _previousSpectrum = null;
            _maxRms = 0.001f;
            _maxBass = 0.001f;
            _maxOnset = 0.001f;
            _highFluxHistory.Clear();
            _bassFluxHistory.Clear();
            _transientCooldown = 0;
            _bassCooldown = 0;
        }

        /// <summary>
        /// In-place Cooley-Tukey FFT implementation
        /// </summary>
        private static void FFT(Complex[] buffer)
        {
            int n = buffer.Length;
            int bits = (int)Math.Log2(n);

            // Bit-reversal permutation
            for (int i = 0; i < n; i++)
            {
                int j = BitReverse(i, bits);
                if (j > i)
                {
                    (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
                }
            }

            // Cooley-Tukey iterative FFT
            for (int len = 2; len <= n; len *= 2)
            {
                double angle = -2.0 * Math.PI / len;
                var wn = new Complex(Math.Cos(angle), Math.Sin(angle));

                for (int i = 0; i < n; i += len)
                {
                    var w = Complex.One;
                    for (int j = 0; j < len / 2; j++)
                    {
                        var u = buffer[i + j];
                        var v = buffer[i + j + len / 2] * w;
                        buffer[i + j] = u + v;
                        buffer[i + j + len / 2] = u - v;
                        w *= wn;
                    }
                }
            }
        }

        /// <summary>
        /// Bit-reverse an integer for FFT
        /// </summary>
        private static int BitReverse(int x, int bits)
        {
            int result = 0;
            for (int i = 0; i < bits; i++)
            {
                result = (result << 1) | (x & 1);
                x >>= 1;
            }
            return result;
        }
    }
}
