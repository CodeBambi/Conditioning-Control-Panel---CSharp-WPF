using System;
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

        // Hanning window coefficients (pre-computed)
        private readonly float[] _window;

        // Previous spectrum for onset detection
        private float[]? _previousSpectrum;

        // Running statistics for normalization
        private float _maxRms = 0.001f;
        private float _maxBass = 0.001f;
        private float _maxOnset = 0.001f;

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
            var features = new (float rms, float bass, float onset)[windowCount];

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

                // Calculate onset (spectral flux)
                float onset = 0f;
                if (_previousSpectrum != null)
                {
                    for (int j = 0; j < spectrum.Length; j++)
                    {
                        var diff = spectrum[j] - _previousSpectrum[j];
                        if (diff > 0) // Only count increases (onsets, not offsets)
                        {
                            onset += diff;
                        }
                    }
                }
                _maxOnset = MathF.Max(_maxOnset, onset);

                // Store features
                features[i] = (rms, bassEnergy, onset);

                // Update previous spectrum
                _previousSpectrum ??= new float[spectrum.Length];
                Array.Copy(spectrum, _previousSpectrum, spectrum.Length);
            }

            // Second pass: normalize and combine features
            for (int i = 0; i < windowCount; i++)
            {
                var (rms, bass, onset) = features[i];

                // Normalize features to 0-1 range
                var normRms = rms / _maxRms;
                var normBass = bass / _maxBass;
                var normOnset = _maxOnset > 0.001f ? onset / _maxOnset : 0f;

                // Combine with weights
                var intensity =
                    normRms * (float)settings.RmsWeight +
                    normBass * (float)settings.BassWeight +
                    normOnset * (float)settings.OnsetWeight;

                // Apply sensitivity curve (< 1 = more sensitive to quiet, > 1 = less sensitive)
                if (settings.Sensitivity != 1.0 && intensity > 0)
                {
                    intensity = MathF.Pow(intensity, 1f / (float)settings.Sensitivity);
                }

                // Apply smoothing (exponential moving average)
                if (settings.Smoothing > 0)
                {
                    intensity = previousIntensity * (float)settings.Smoothing +
                                intensity * (1f - (float)settings.Smoothing);
                }
                previousIntensity = intensity;

                // Clamp to min/max range
                intensity = Math.Clamp(intensity, (float)settings.MinIntensity, (float)settings.MaxIntensity);

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
