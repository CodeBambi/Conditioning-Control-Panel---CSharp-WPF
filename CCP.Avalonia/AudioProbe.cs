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

        /// <summary>`--layers-probe`: two looping Audio Layers on the real output. Fails unless pactl
        /// shows two of our streams, one moves on a live volume change, and one is left after it is
        /// disabled (the window's toggle path: Restart).</summary>
        public static int RunLayers()
        {
            new LibVlcAudio().Seed();
            var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds");
            var s = CoreSettings.Current;
            var a = new Models.AudioLayerTrack { Path = Path.Combine(dir, "faucet_charge_drop.wav"), Volume = 100 };
            var b = new Models.AudioLayerTrack { Path = Path.Combine(dir, "00 Bimbo Drone.mp3"), Volume = 50 };
            s.AudioLayers = new() { a, b };
            s.AudioLayersEnabled = true;
            s.AudioLayersMasterVolume = 100;
            s.MasterVolume = 100;
            var layers = LayeredAudio.Instance!;
            layers.Start();
            Thread.Sleep(1500);
            var started = Ours();
            Console.WriteLine($"-- 2 layers started (100%, 50%):\n{string.Join('\n', started)}");
            b.Volume = 20; // as the window: the setting, then the live call
            layers.SetTrackVolumeLive(b, 20);
            Thread.Sleep(500);
            var changed = Ours();
            Console.WriteLine($"-- layer 2 set to 20%:\n{string.Join('\n', changed)}");
            a.Enabled = false;
            layers.Restart();
            Thread.Sleep(1500);
            var one = Ours();
            Console.WriteLine($"-- layer 1 disabled:\n{string.Join('\n', one)}");
            layers.Stop();
            Thread.Sleep(1000);
            var ok = started.Length == 2 && changed.Length == 2 && !changed.SequenceEqual(started) && one.Length == 1 && Ours().Length == 0;
            Console.WriteLine($"layers: {(ok ? "PASS" : "FAIL")}");
            return ok ? 0 : 1;
        }

        /// <summary>Our own sink-inputs as "name volume%".</summary>
        private static string[] Ours()
        {
            using var doc = System.Text.Json.JsonDocument.Parse(LibVlcAudio.Pactl("-f json list sink-inputs"));
            return doc.RootElement.EnumerateArray()
                .Where(si => si.GetProperty("properties").TryGetProperty("application.process.id", out var pid) && pid.GetString() == Environment.ProcessId.ToString())
                .Select(si => $"{si.GetProperty("properties").GetProperty("application.name").GetString()} {si.GetProperty("volume").EnumerateObject().First().Value.GetProperty("value_percent").GetString()}")
                .ToArray();
        }

        private static string SinkInputs() => string.Join('\n', LibVlcAudio.Pactl("list sink-inputs").Split('\n')
            .Where(l => l.StartsWith("Sink Input") || l.Contains("Volume:") || l.Contains("application.name") || l.Contains("process.id")));

        private static string Volumes() => string.Join('\n', LibVlcAudio.Pactl("list sink-inputs").Split('\n')
            .Where(l => l.TrimStart().StartsWith("Volume:")));
    }
}
