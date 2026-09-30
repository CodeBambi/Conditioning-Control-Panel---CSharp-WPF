using System;
using System.IO;
using System.Threading;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The mantra window's synthesised sounds as WAV files, because this head's audio path (LibVLC)
    /// plays files, not NAudio SignalGenerators. Sine waves at full scale; the caller sets the gain.
    /// Written once per process into a private temp directory.
    /// ponytail: the directory is not deleted on exit (a few small files); clean up if it ever matters.
    /// </summary>
    internal static class ToneWav
    {
        private const int Rate = 22050;

        /// <summary>Peak of the drone file relative to its 90 Hz fundamental (1 + 0.4 harmonic, scaled into range).</summary>
        internal const double DronePeak = 1.4;

        private static readonly Lazy<string> Dir = new(() => Directory.CreateTempSubdirectory("ccp-mantra-").FullName,
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>WPF PlayTone: one sine of <paramref name="hz"/> for <paramref name="ms"/>.</summary>
        internal static string Tone(double hz, int ms) =>
            Write($"tone-{hz:0}-{ms}.wav", ms / 1000.0, t => Math.Sin(2 * Math.PI * hz * t));

        /// <summary>WPF StartDrone: 90 Hz + 180 Hz at 0.4, divided by <see cref="DronePeak"/>. 10 s of whole
        /// cycles of both, so the loop is seamless.</summary>
        internal static string Drone() =>
            Write("drone.wav", 10, t => (Math.Sin(2 * Math.PI * 90 * t) + 0.4 * Math.Sin(2 * Math.PI * 180 * t)) / DronePeak);

        /// <summary>Linear sample gain -> LibVLC's cubic 0..100 volume, as LibVlcAudio maps it.</summary>
        internal static int VlcVolume(double gain) => (int)Math.Round(Math.Cbrt(Math.Clamp(gain, 0, 1)) * 100);

        private static string Write(string name, double seconds, Func<double, double> wave)
        {
            var path = Path.Combine(Dir.Value, name);
            lock (Dir)
            {
                if (File.Exists(path)) return path;
                var n = (int)(Rate * seconds);
                using var w = new BinaryWriter(File.Create(path));
                w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
                w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data"u8); w.Write(n * 2);
                for (var i = 0; i < n; i++)
                    w.Write((short)Math.Round(Math.Clamp(wave((double)i / Rate), -1, 1) * short.MaxValue));
            }
            return path;
        }
    }
}
