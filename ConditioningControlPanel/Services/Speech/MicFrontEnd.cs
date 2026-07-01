using System;

namespace ConditioningControlPanel.Services.Speech
{
    /// <summary>
    /// First-order high-pass (DC-blocker) filter that strips low-frequency rumble — AC units, fans,
    /// desk vibration, 50/60 Hz mains hum — that concentrates below ~100 Hz. That energy otherwise
    /// inflates the RMS loudness reading (breaking the mic sensitivity calibration on a hot day when
    /// the AC is running) and smears the acoustic features the recognizer sees. One instance per
    /// capture session (it holds filter state); call it from the DataAvailable callback BEFORE
    /// measuring RMS and BEFORE handing samples to Vosk / sherpa.
    /// </summary>
    internal sealed class HighPassFilter
    {
        // y[n] = R * (y[n-1] + x[n] - x[n-1]);  pole at R. Cutoff rises as R falls.
        private readonly float _r;
        private float _prevIn;
        private float _prevOut;
        private bool _primed;

        public HighPassFilter(int sampleRate, double cutoffHz = 90.0)
        {
            // R = exp(-2*pi*fc/fs): ~0.965 for a 90 Hz cutoff at 16 kHz.
            _r = (float)Math.Exp(-2.0 * Math.PI * Math.Max(1.0, cutoffHz) / Math.Max(1, sampleRate));
        }

        /// <summary>Filter a single float sample in [-1,1].</summary>
        public float Process(float x)
        {
            if (!_primed) { _prevIn = x; _prevOut = 0f; _primed = true; return 0f; }
            float y = _r * (_prevOut + x - _prevIn);
            _prevIn = x;
            _prevOut = y;
            return y;
        }

        /// <summary>Filter a block of float samples in place.</summary>
        public void ProcessInPlace(float[] samples, int count)
        {
            for (int i = 0; i < count && i < samples.Length; i++)
                samples[i] = Process(samples[i]);
        }

        /// <summary>Filter 16-bit little-endian PCM inside a byte buffer, in place.</summary>
        public void ProcessPcm16(byte[] buffer, int bytes)
        {
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                short s = (short)(buffer[i] | (buffer[i + 1] << 8));
                float y = Process(s / 32768f);
                int iv = (int)MathF.Round(y * 32768f);
                if (iv > short.MaxValue) iv = short.MaxValue;
                else if (iv < short.MinValue) iv = short.MinValue;
                buffer[i] = (byte)(iv & 0xFF);
                buffer[i + 1] = (byte)((iv >> 8) & 0xFF);
            }
        }
    }

    /// <summary>
    /// Adaptive energy gate. Instead of a single fixed loudness threshold, it tracks a slowly-moving
    /// room-tone noise floor from quiet frames and declares a frame "voiced" only when its RMS clears
    /// that floor by an SNR margin. Because the floor rises to meet a steady hum, background noise
    /// (AC/fans) self-raises the trigger point rather than tripping it. Dual-threshold hysteresis plus a
    /// short hangover keeps a droning noise from chattering the gate on/off.
    ///
    /// This is the live-path promotion of the noise-floor logic that previously lived only inside the
    /// sherpa wake calibration sweep. One instance per capture session (holds floor + latch state).
    /// </summary>
    internal sealed class NoiseGate
    {
        private readonly double _triggerFactor; // enter-speech SNR multiple over the floor
        private readonly double _releaseFactor; // stay-in-speech multiple (< trigger => hysteresis)
        private readonly double _floorMin, _floorMax;
        private readonly int _hangoverFrames;

        private double _noiseFloor;
        private bool _voiced;
        private int _silenceFrames;

        /// <summary>Current estimated room-tone RMS.</summary>
        public double NoiseFloor => _noiseFloor;
        /// <summary>Absolute RMS a frame must exceed right now to count as speech onset.</summary>
        public double EnterGate => Math.Clamp(_noiseFloor * _triggerFactor, _floorMin, _floorMax);

        public NoiseGate(double triggerFactor = 4.0, double initialFloor = 0.006,
                         double floorMin = 1e-4, double floorMax = 0.05, int hangoverFrames = 3)
        {
            _triggerFactor = Math.Max(1.5, triggerFactor);
            _releaseFactor = Math.Max(1.2, _triggerFactor * 0.5);
            _noiseFloor = Math.Clamp(initialFloor, floorMin, floorMax);
            _floorMin = floorMin;
            _floorMax = floorMax;
            _hangoverFrames = Math.Max(0, hangoverFrames);
        }

        /// <summary>
        /// Feed one frame's RMS. Returns true while the gate considers audio "voiced". The floor only
        /// adapts while NOT voiced, so a spoken word can never drag the floor up under itself.
        /// </summary>
        public bool Update(double rms)
        {
            double enterGate = EnterGate;
            double exitGate = Math.Clamp(_noiseFloor * _releaseFactor, _floorMin, _floorMax);

            if (!_voiced)
            {
                if (rms < enterGate)
                {
                    // Creep up slowly toward a louder room; settle down quickly (EMA) when it quiets.
                    _noiseFloor = rms > _noiseFloor
                        ? Math.Min(_noiseFloor * 1.02 + 1e-5, rms)
                        : 0.9 * _noiseFloor + 0.1 * rms;
                    _noiseFloor = Math.Clamp(_noiseFloor, _floorMin, _floorMax);
                }
                else
                {
                    _voiced = true;
                    _silenceFrames = 0;
                }
            }
            else
            {
                if (rms >= exitGate) _silenceFrames = 0;
                else if (++_silenceFrames > _hangoverFrames) _voiced = false;
            }
            return _voiced;
        }
    }
}
