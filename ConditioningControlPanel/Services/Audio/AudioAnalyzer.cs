using System;
using System.Collections.Generic;
using System.Numerics;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// Analyzes audio for haptic generation with improved bass handling.
    ///
    /// KEY FEATURES:
    /// 1. Bass = smooth low vibration (continuous, follows bass energy)
    /// 2. Fast beats (>140 BPM) are halved for comfort
    /// 3. High energy moments = stronger haptics
    /// 4. Bass drops trigger strong synced haptics
    /// </summary>
    public class AudioAnalyzer
    {
        private const int FFT_SIZE = 2048;
        private const int HOP_SIZE = 512;         // ~11.6ms per frame
        private const int SAMPLE_RATE = 44100;

        // Bass range for smooth vibration (20-120Hz)
        private const int BASS_LOW_BIN = 1;       // ~21Hz
        private const int BASS_HIGH_BIN = 6;      // ~129Hz

        // Sub-bass for drop detection (20-60Hz)
        private const int SUB_BASS_LOW_BIN = 1;   // ~21Hz
        private const int SUB_BASS_HIGH_BIN = 3;  // ~65Hz

        // Mid range for energy detection
        private const int MID_LOW_BIN = 7;        // ~150Hz
        private const int MID_HIGH_BIN = 50;      // ~1000Hz

        // Voice/high range for spike detection
        private const int VOICE_LOW_BIN = 20;     // ~430Hz
        private const int VOICE_HIGH_BIN = 186;   // ~4000Hz

        private readonly float[] _window;

        // Bass tracking for smooth vibration
        private float _smoothedBass = 0f;
        private const float BASS_SMOOTHING = 0.85f;  // High smoothing for constant feel
        private float _bassBaseline = 0.001f;

        // Beat detection - no halving, every beat gets a pulse
        private readonly Queue<float> _beatTimes = new();  // Timestamps of recent beats
        private const int BEAT_HISTORY_SIZE = 8;
        private float _lastBeatTime = -1f;
        private float _prevBassEnergy = 0f;
        private int _frameIndex = 0;
        private bool _onBeat = false;  // Current frame is on a beat

        // Energy tracking for intensity scaling
        private float _avgEnergy = 0.001f;
        private float _maxEnergy = 0.001f;
        private readonly Queue<float> _energyHistory = new();
        private const int ENERGY_HISTORY_SIZE = 20;

        // Bass drop detection - longer window for better quiet→loud detection
        private readonly Queue<float> _bassDropHistory = new();
        private const int BASS_DROP_HISTORY_SIZE = 60;  // ~700ms window
        private float _bassDropPulse = 0f;
        private const float BASS_DROP_DECAY = 0.85f;

        // Overall loudness tracking for dynamic range
        private readonly Queue<float> _loudnessHistory = new();
        private const int LOUDNESS_HISTORY_SIZE = 100;  // ~1.2 second window
        private float _recentLoudnessMin = float.MaxValue;
        private float _recentLoudnessMax = 0f;

        // Voice spike detection (unchanged)
        private float _voiceSpike = 0f;
        private const float VOICE_SPIKE_DECAY = 0.65f;
        private const float VOICE_SPIKE_LEVEL = 0.90f;
        private float _prevVoiceEnergy = 0f;
        private float _avgVoice = 0.001f;
        private readonly Queue<float> _voiceHistory = new();
        private const int VOICE_HISTORY_SIZE = 6;

        // Frame timing
        private const float FRAME_DURATION_SEC = (float)HOP_SIZE / SAMPLE_RATE;

        public int OutputSampleRate => SAMPLE_RATE / HOP_SIZE;

        public AudioAnalyzer()
        {
            _window = new float[FFT_SIZE];
            for (int i = 0; i < FFT_SIZE; i++)
            {
                _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FFT_SIZE - 1)));
            }
        }

        public float[] Analyze(float[] samples, AudioSyncSettings settings)
        {
            if (samples.Length < FFT_SIZE)
            {
                App.Logger?.Warning("AudioAnalyzer: Not enough samples ({Count})", samples.Length);
                return Array.Empty<float>();
            }

            var windowCount = (samples.Length - FFT_SIZE) / HOP_SIZE + 1;
            var intensities = new float[windowCount];
            var fftBuffer = new Complex[FFT_SIZE];
            var spectrum = new float[FFT_SIZE / 2];

            int beatCount = 0, dropCount = 0, spikeCount = 0;

            for (int i = 0; i < windowCount; i++)
            {
                var offset = i * HOP_SIZE;
                _frameIndex++;
                float currentTimeSec = _frameIndex * FRAME_DURATION_SEC;

                // Apply window and FFT
                for (int j = 0; j < FFT_SIZE; j++)
                {
                    fftBuffer[j] = new Complex(samples[offset + j] * _window[j], 0);
                }
                FFT(fftBuffer);

                for (int j = 0; j < FFT_SIZE / 2; j++)
                {
                    spectrum[j] = (float)fftBuffer[j].Magnitude;
                }

                // === BASS ENERGY (for smooth vibration) ===
                float bassEnergy = 0f;
                for (int j = BASS_LOW_BIN; j <= BASS_HIGH_BIN && j < spectrum.Length; j++)
                {
                    bassEnergy += spectrum[j];
                }

                // === SUB-BASS (for drop detection) ===
                float subBassEnergy = 0f;
                for (int j = SUB_BASS_LOW_BIN; j <= SUB_BASS_HIGH_BIN && j < spectrum.Length; j++)
                {
                    subBassEnergy += spectrum[j];
                }

                // === MID ENERGY (for overall energy) ===
                float midEnergy = 0f;
                for (int j = MID_LOW_BIN; j <= MID_HIGH_BIN && j < spectrum.Length; j++)
                {
                    midEnergy += spectrum[j];
                }

                // === TOTAL ENERGY ===
                float totalEnergy = bassEnergy + midEnergy * 0.5f;
                _energyHistory.Enqueue(totalEnergy);
                if (_energyHistory.Count > ENERGY_HISTORY_SIZE)
                    _energyHistory.Dequeue();

                // Update energy statistics
                float localAvgEnergy = 0f;
                foreach (var e in _energyHistory) localAvgEnergy += e;
                localAvgEnergy /= _energyHistory.Count;

                float alpha = 1f / MathF.Min(_frameIndex, 500);
                _avgEnergy = _avgEnergy * (1 - alpha) + totalEnergy * alpha;
                _maxEnergy = MathF.Max(_maxEnergy * 0.9999f, totalEnergy);

                // Update bass baseline
                _bassBaseline = _bassBaseline * (1 - alpha) + bassEnergy * alpha;

                // === SIMPLE TWO-LEVEL APPROACH ===
                // Normal music = LOW and constant
                // Bass drops = HIGH
                // This gives clear, perceptible contrast

                // Calculate normalized loudness for bass drop detection
                float normalizedLoudness = totalEnergy / MathF.Max(_avgEnergy * 0.5f, 0.001f);
                normalizedLoudness = MathF.Min(normalizedLoudness, 3f) / 3f;

                // Smoothing for stability
                _smoothedBass = _smoothedBass * 0.2f + normalizedLoudness * 0.8f;

                // Base intensity: constant LOW level (25%) for all normal audio
                // This provides subtle background that follows music existence
                // Quiet parts (below threshold) get OFF
                float baseIntensity;
                if (_smoothedBass < 0.1f)
                    baseIntensity = 0.0f;  // Quiet = off
                else
                    baseIntensity = 0.25f;  // Normal music = constant low

                // Track beats for logging only
                _onBeat = bassEnergy > _prevBassEnergy * 1.2f && bassEnergy > _bassBaseline * 1.2f;
                if (_onBeat) beatCount++;
                _prevBassEnergy = bassEnergy;

                // === OVERALL LOUDNESS TRACKING ===
                _loudnessHistory.Enqueue(totalEnergy);
                if (_loudnessHistory.Count > LOUDNESS_HISTORY_SIZE)
                    _loudnessHistory.Dequeue();

                // Track min/max loudness over recent history
                if (_loudnessHistory.Count >= 10)
                {
                    int histCount = 0;
                    float firstHalfAvg = 0f, secondHalfAvg = 0f;
                    int halfPoint = _loudnessHistory.Count / 2;

                    foreach (var loud in _loudnessHistory)
                    {
                        if (histCount < halfPoint)
                            firstHalfAvg += loud;
                        else
                            secondHalfAvg += loud;
                        histCount++;
                    }
                    firstHalfAvg /= halfPoint;
                    secondHalfAvg /= (_loudnessHistory.Count - halfPoint);

                    _recentLoudnessMin = firstHalfAvg;
                    _recentLoudnessMax = secondHalfAvg;
                }

                // === BASS DROP DETECTION ===
                _bassDropHistory.Enqueue(subBassEnergy + bassEnergy * 0.5f);  // Include some bass, not just sub
                if (_bassDropHistory.Count > BASS_DROP_HISTORY_SIZE)
                    _bassDropHistory.Dequeue();

                // Track recent min/max for drop detection
                float recentMin = float.MaxValue, recentMax = 0f;
                int count = 0;
                foreach (var b in _bassDropHistory)
                {
                    count++;
                    if (count < BASS_DROP_HISTORY_SIZE / 2)
                    {
                        // First half = "quiet" period
                        recentMin = MathF.Min(recentMin, b);
                    }
                    else
                    {
                        // Second half = potential drop
                        recentMax = MathF.Max(recentMax, b);
                    }
                }

                // Detect bass drop: quiet then sudden loud - more sensitive now
                // Also detect overall loudness jumps
                bool isBassDrop = (recentMax > recentMin * 2.5f &&
                                  recentMax > _bassBaseline * 1.5f &&
                                  _bassDropPulse < 0.2f) ||  // Bass-based detection
                                  (_recentLoudnessMax > _recentLoudnessMin * 2f &&
                                   totalEnergy > _avgEnergy * 2f &&
                                   _bassDropPulse < 0.2f);   // Loudness-based detection

                if (isBassDrop)
                {
                    _bassDropPulse = 1.0f;  // Full intensity for drop
                    dropCount++;
                }
                else
                {
                    _bassDropPulse *= BASS_DROP_DECAY;
                    if (_bassDropPulse < 0.01f) _bassDropPulse = 0f;
                }

                // === ENERGY-BASED INTENSITY SCALING ===
                // Scale up when energy is high relative to average
                float energyMultiplier = 1f;
                if (localAvgEnergy > _avgEnergy * 1.5f)
                {
                    // High energy section - boost intensity
                    energyMultiplier = 1f + MathF.Min((localAvgEnergy / _avgEnergy - 1f) * 0.3f, 0.5f);
                }

                // === VOICE/HIGH SPIKE DETECTION ===
                float voiceEnergy = 0f;
                for (int j = VOICE_LOW_BIN; j <= VOICE_HIGH_BIN && j < spectrum.Length; j++)
                {
                    voiceEnergy += spectrum[j];
                }

                _voiceHistory.Enqueue(voiceEnergy);
                if (_voiceHistory.Count > VOICE_HISTORY_SIZE)
                    _voiceHistory.Dequeue();

                float voiceAvg = 0f;
                foreach (var v in _voiceHistory) voiceAvg += v;
                voiceAvg /= _voiceHistory.Count;

                _avgVoice = _avgVoice * (1 - alpha) + voiceEnergy * alpha;

                bool isVoiceSpike = voiceEnergy > voiceAvg * 1.6f &&
                                    voiceEnergy > _prevVoiceEnergy * 1.4f &&
                                    voiceEnergy > _avgVoice * 1.0f &&
                                    _voiceSpike < 0.4f;

                if (isVoiceSpike)
                {
                    _voiceSpike = VOICE_SPIKE_LEVEL;
                    spikeCount++;
                }
                else
                {
                    _voiceSpike *= VOICE_SPIKE_DECAY;
                    if (_voiceSpike < 0.01f) _voiceSpike = 0f;
                }
                _prevVoiceEnergy = voiceEnergy;

                // === COMBINE ALL COMPONENTS ===
                // Optimized for bass drop pattern: low → pause → HIGH
                float intensity = baseIntensity;

                // Bass drop = FULL MAX for sharp contrast
                if (_bassDropPulse > 0.4f)
                {
                    intensity = 1.0f;  // Max intensity for bass drops
                }
                // Very quiet (the "pause" before drop) = off
                else if (normalizedLoudness < 0.08f)
                {
                    intensity = 0.0f;  // Off for quiet/pause
                }
                // Low-medium stays compressed due to curve
                // Only loud parts break through

                // Apply sensitivity
                if (settings.Sensitivity != 1.0 && intensity > 0)
                {
                    intensity = MathF.Pow(intensity, 1f / (float)settings.Sensitivity);
                }

                // Clamp to settings range
                intensity = Math.Clamp(intensity, (float)settings.MinIntensity, (float)settings.MaxIntensity);

                intensities[i] = intensity;
            }

            // Stats before normalization
            float minI = float.MaxValue, maxI = float.MinValue, avgI = 0;
            int zeroCount = 0;
            for (int i = 0; i < windowCount; i++)
            {
                minI = MathF.Min(minI, intensities[i]);
                maxI = MathF.Max(maxI, intensities[i]);
                avgI += intensities[i];
                if (intensities[i] < 0.01f) zeroCount++;
            }
            avgI /= windowCount;

            App.Logger?.Information("AudioAnalyzer: {Samples} -> {Values} | Beats: {Beats}, Drops: {Drops}, Spikes: {Spikes}",
                samples.Length, windowCount, beatCount, dropCount, spikeCount);
            App.Logger?.Information("AudioAnalyzer: Pre-norm Intensity Min={Min:F3}, Max={Max:F3}, Avg={Avg:F3}",
                minI, maxI, avgI);

            // === LIGHT NORMALIZATION PASS ===
            // Only boost the highs slightly, don't compress the dynamics
            float settingsMin = (float)settings.MinIntensity;
            float settingsMax = (float)settings.MaxIntensity;

            // Just ensure values are clamped to settings range
            for (int i = 0; i < windowCount; i++)
            {
                intensities[i] = Math.Clamp(intensities[i], settingsMin, settingsMax);
            }

            // Log final stats
            float minPost = float.MaxValue, maxPost = float.MinValue, avgPost = 0;
            for (int i = 0; i < windowCount; i++)
            {
                minPost = MathF.Min(minPost, intensities[i]);
                maxPost = MathF.Max(maxPost, intensities[i]);
                avgPost += intensities[i];
            }
            avgPost /= windowCount;
            App.Logger?.Information("AudioAnalyzer: Final Intensity Min={Min:F3}, Max={Max:F3}, Avg={Avg:F3}",
                minPost, maxPost, avgPost);

            return intensities;
        }

        /// <summary>
        /// Calculate average interval between recent beats
        /// </summary>
        private float CalculateAverageBeatInterval()
        {
            if (_beatTimes.Count < 2) return 0f;

            var times = new List<float>(_beatTimes);
            float totalInterval = 0f;
            int intervalCount = 0;

            for (int i = 1; i < times.Count; i++)
            {
                totalInterval += times[i] - times[i - 1];
                intervalCount++;
            }

            return intervalCount > 0 ? totalInterval / intervalCount : 0f;
        }

        public void Reset()
        {
            _beatTimes.Clear();
            _voiceHistory.Clear();
            _energyHistory.Clear();
            _bassDropHistory.Clear();
            _loudnessHistory.Clear();
            _smoothedBass = 0f;
            _bassBaseline = 0.001f;
            _prevBassEnergy = 0f;
            _prevVoiceEnergy = 0f;
            _voiceSpike = 0f;
            _bassDropPulse = 0f;
            _avgEnergy = 0.001f;
            _maxEnergy = 0.001f;
            _avgVoice = 0.001f;
            _frameIndex = 0;
            _lastBeatTime = -1f;
            _onBeat = false;
            _recentLoudnessMin = float.MaxValue;
            _recentLoudnessMax = 0f;
        }

        private static void FFT(Complex[] buffer)
        {
            int n = buffer.Length;
            int bits = (int)Math.Log2(n);

            for (int i = 0; i < n; i++)
            {
                int j = BitReverse(i, bits);
                if (j > i)
                    (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }

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
