using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--audio-probe`: the live counterpart of the smoke's dummy-output check. Plays the bundled
    /// clip on the default output while listing sink-inputs, then ducks and unducks other apps and
    /// fails unless their volumes moved on Duck and came back on Unduck.
    /// </summary>
    internal static class AudioProbe
    {
        public static int Run()
        {
            new LibVlcAudio().Seed();
            var clip = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "faucet_charge_drop.wav");
            var started = new ManualResetEventSlim();
            var finished = new ManualResetEventSlim();
            var sw = Stopwatch.StartNew();
            CoreAudio.PlayOneShot(clip, 0.5f, "probe",
                d => { Console.WriteLine($"started at {sw.ElapsedMilliseconds} ms, clip {d.TotalMilliseconds} ms"); started.Set(); },
                () => { Console.WriteLine($"finished at {sw.ElapsedMilliseconds} ms"); finished.Set(); });
            started.Wait(2000);
            Thread.Sleep(50); // the volume is applied just after Playing, off libvlc's thread
            Console.WriteLine($"-- sink-inputs while playing (own pid {Environment.ProcessId}):\n{SinkInputs()}");
            var played = started.IsSet && finished.Wait(3000);

            var before = Volumes();
            var gen = CoreAudio.DuckGeneration;
            CoreAudio.Duck(80);
            Thread.Sleep(1000);
            var ducked = Volumes();
            Console.WriteLine($"-- after Duck(80), generation {gen}:\n{SinkInputs()}");
            CoreAudio.Unduck(gen);
            Thread.Sleep(1000);
            var restored = Volumes();
            Console.WriteLine($"-- after Unduck({gen}):\n{SinkInputs()}");

            var duckOk = before.Length > 0 && ducked != before && restored == before;
            Console.WriteLine($"play: {(played ? "PASS" : "FAIL")}  duck/unduck: {(duckOk ? "PASS" : before.Length == 0 ? "FAIL (no other stream playing)" : "FAIL")}");
            return played && duckOk ? 0 : 1;
        }

        private static string SinkInputs() => string.Join('\n', LibVlcAudio.Pactl("list sink-inputs").Split('\n')
            .Where(l => l.StartsWith("Sink Input") || l.Contains("Volume:") || l.Contains("application.name") || l.Contains("process.id")));

        private static string Volumes() => string.Join('\n', LibVlcAudio.Pactl("list sink-inputs").Split('\n')
            .Where(l => l.TrimStart().StartsWith("Volume:")));
    }
}
