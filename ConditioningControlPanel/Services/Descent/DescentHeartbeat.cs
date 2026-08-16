using System;
using System.IO;
using NAudio.Wave;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE HEARTBEAT (CONTRACT-FUSE-0816 §2.4) — the low loop under the final ten minutes, IF the
    /// asset ever ships.
    ///
    /// <para><b>The asset does not exist and this class does not create one.</b>
    /// <c>Resources/descent/heartbeat.wav</c> is an open owner call; until a file is put at that
    /// path this whole class is a single <see cref="File.Exists"/> that returns false, logs one
    /// Debug line for the life of the process, and never allocates an audio device. Fabricating a
    /// placeholder would be worse than silence: a wrong heartbeat under the last ten minutes of the
    /// countdown is a note the owner did not write, playing at the loudest moment of the year.</para>
    ///
    /// <para><b>Every gate is a reason not to play.</b> The subject's audio flag
    /// (<c>DescentCountdownAudio</c>, which exists precisely so this can be turned off), a master
    /// volume of zero — the app's mute — and the file's absence each independently mean silence.
    /// The volume is a fraction of master, so every later change the subject makes to the app's
    /// volume is respected the next time the loop is started.</para>
    ///
    /// <para><b>It stops at zero, always.</b> The show has its own sound design and the countdown
    /// must not be playing underneath it. <see cref="Stop"/> is idempotent and is called from the
    /// director on the phase leaving Terminal, on zero, and on teardown.</para>
    /// </summary>
    public sealed class DescentHeartbeat : IDisposable
    {
        /// <summary>A bed, not a wall of sound — the same fraction ChaosTunnelService's ambient uses.</summary>
        private const float VolumeFraction = 0.22f;

        private static bool _missingLogged;

        private WaveOutEvent? _out;
        private AudioFileReader? _reader;
        private bool _disposed;

        /// <summary>
        /// Where the asset would live. Off <c>BaseDirectory</c> like every other on-disk sound in
        /// the app, so a content pack or a hand-drop can supply it without a rebuild.
        /// </summary>
        public static string AssetPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "descent", "heartbeat.wav");

        /// <summary>True while the loop is running.</summary>
        public bool IsPlaying => _out != null;

        /// <summary>
        /// The gate, as pure arithmetic so the "it must be silent by default" claim is testable
        /// without an audio device or a settings file. All three have to agree.
        /// </summary>
        public static bool ShouldPlay(bool audioEnabled, int masterVolume, bool assetExists) =>
            audioEnabled && masterVolume > 0 && assetExists;

        /// <summary>
        /// Start the loop, if all three gates agree. Idempotent — a second call while playing does
        /// nothing rather than opening a second device.
        /// </summary>
        public void Start()
        {
            if (_disposed || _out != null) return;

            try
            {
                var enabled = App.DescentCountdown?.AudioEnabled ?? true;
                var master = App.Settings?.Current?.MasterVolume ?? 0;
                var exists = File.Exists(AssetPath);

                if (!exists && !_missingLogged)
                {
                    // ONE line, for the life of the process. The contract is explicit that a missing
                    // asset is a silent no-op with no log spam, and this runs at a phase boundary
                    // that a long-running install crosses exactly once anyway.
                    _missingLogged = true;
                    Log.Debug("[Fuse] No heartbeat asset at {Path} — the terminal phase stays silent.", AssetPath);
                }

                if (!ShouldPlay(enabled, master, exists)) return;

                _reader = new AudioFileReader(AssetPath) { Volume = Math.Clamp(master / 100f * VolumeFraction, 0f, 1f) };
                _out = new WaveOutEvent();
                App.Audio?.ApplyPreferredDevice(_out);
                _out.Init(new LoopStream(_reader));
                _out.Play();

                Log.Information("[Fuse] Heartbeat started for the terminal phase.");
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Heartbeat could not start: {Error}", ex.Message);
                Stop();
            }
        }

        /// <summary>Stop and free the device. Idempotent, and safe from any thread.</summary>
        public void Stop()
        {
            try { _out?.Stop(); } catch { /* a device already gone is not news */ }
            try { _out?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            _out = null;
            _reader = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        /// <summary>Loops a WaveStream forever (NAudio has no built-in looper). Same shape as
        /// ChaosTunnelService's and QuizWindow's — deliberately, so there is one idiom to audit.</summary>
        private sealed class LoopStream : WaveStream
        {
            private readonly WaveStream _src;
            public LoopStream(WaveStream src) { _src = src; }
            public override WaveFormat WaveFormat => _src.WaveFormat;
            public override long Length => _src.Length;
            public override long Position { get => _src.Position; set => _src.Position = value; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int total = 0;
                while (total < count)
                {
                    int n = _src.Read(buffer, offset + total, count - total);
                    if (n == 0)
                    {
                        if (_src.Position == 0) break;   // empty source — avoid a busy loop
                        _src.Position = 0;
                    }
                    total += n;
                }
                return total;
            }
        }
    }
}
