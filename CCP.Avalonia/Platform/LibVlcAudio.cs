using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The Avalonia head's answer to <see cref="CoreAudio"/>: one shared LibVLC, one
    /// MediaPlayer per one-shot, disposed when the clip ends. Ducking lowers OTHER apps'
    /// PulseAudio/PipeWire sink-inputs through <c>pactl</c> on Linux and restores them on
    /// Unduck, with WPF's ref count and generation rules.
    /// </summary>
    internal sealed class LibVlcAudio
    {
        private readonly LibVLC _vlc;

        /// <summary>Throws when libvlc is not installed; the caller then leaves CoreAudio unseeded.</summary>
        public LibVlcAudio(params string[] options)
        {
            Core.Initialize();
            _vlc = new LibVLC(options.Append("--no-video").Append("--quiet").ToArray());
        }

        public void Seed()
        {
            CoreAudio.PlayOneShotProvider = PlayOneShot;
            // ponytail: Windows ducking stays unseeded (no-op) until AudioService's WASAPI sweep is ported.
            if (!OperatingSystem.IsLinux()) return;
            CoreAudio.DuckProvider = Duck;
            CoreAudio.UnduckProvider = Unduck;
            CoreAudio.DuckGenerationProvider = () => { lock (_duckLock) return _duckGeneration; };
        }

        /// <summary>Same contract as WPF's AudioService.PlayOneShot: onStarted gets the clip length
        /// once playing; onFinished fires exactly once, off the UI thread, on end, error or refusal.</summary>
        public void PlayOneShot(string path, float volume, string tag, Action<TimeSpan>? onStarted, Action? onFinished)
        {
            if (volume <= 0f || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Fire(onFinished); return; }

            var media = new Media(_vlc, path, FromType.FromPath);
            var player = new MediaPlayer(media);
            var vol = (int)(Math.Clamp(volume, 0f, 1f) * 100);
            var done = 0;
            void Finish()
            {
                if (Interlocked.Exchange(ref done, 1) != 0) return;
                // Never dispose a player from inside its own libvlc event: that deadlocks.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { lock (player) player.Dispose(); media.Dispose(); } catch (Exception ex) { Log.Debug(ex, "[Audio] {Tag} dispose", tag); }
                    Fire(onFinished);
                });
            }
            player.Playing += (_, _) =>
            {
                // Setting volume before Play, or inside this libvlc event, is lost: the pulse
                // output only takes it once it exists, from a thread that is not libvlc's own.
                ThreadPool.QueueUserWorkItem(_ => { lock (player) if (done == 0) player.Volume = vol; });
                if (onStarted is null) return;
                try { onStarted(TimeSpan.FromMilliseconds(Math.Max(0, player.Length))); }
                catch (Exception ex) { Log.Debug(ex, "[Audio] {Tag}: onStarted threw", tag); }
            };
            player.EndReached += (_, _) => Finish();
            player.EncounteredError += (_, _) => Finish();
            player.Stopped += (_, _) => Finish();
            if (!player.Play()) Finish();
        }

        private static void Fire(Action? a) { try { a?.Invoke(); } catch { } }

        // ---- ducking (Linux, pactl) ----

        private readonly object _duckLock = new();
        private long _duckGeneration;
        private int _duckCount;
        private bool _isDucked;
        private readonly Dictionary<int, string[]> _originalVolumes = new(); // sink-input index -> raw channel volumes
        private Task _duckWork = Task.CompletedTask; // chained so a duck and its unduck run in order
        private bool _pactlWarned;
        private Timer? _duckWatchdog; // WPF's safety net for a leaked duck ref: force-unduck after 5 min

        public void Duck(int strength)
        {
            lock (_duckLock)
            {
                _duckCount++;
                if (_isDucked) return;
                _isDucked = true;
                _duckWatchdog = new Timer(_ => ForceUnduck(), null, 300_000, Timeout.Infinite);
                var keep = 1 - Math.Clamp(strength, 0, 100) / 100.0;
                Enqueue(() => DuckSweep(keep));
            }
        }

        public void Unduck(long generation)
        {
            lock (_duckLock)
            {
                if (generation >= 0 && generation != _duckGeneration) return; // stale callback
                _duckCount = Math.Max(0, _duckCount - 1);
                if (!_isDucked || _duckCount > 0) return;
                _isDucked = false;
                _duckWatchdog?.Dispose();
                Enqueue(Restore);
            }
        }

        /// <summary>As WPF: advancing the generation turns every pending Unduck into a stale no-op.</summary>
        private void ForceUnduck()
        {
            lock (_duckLock)
            {
                _duckGeneration++;
                _duckCount = 1;
                Unduck(_duckGeneration);
            }
        }

        private void Enqueue(Action work) => _duckWork = _duckWork.ContinueWith(_ =>
        {
            try { work(); }
            catch (Exception ex)
            {
                if (_pactlWarned) return;
                _pactlWarned = true;
                Log.Warning("[Audio] ducking via pactl unavailable: {E}", ex.Message);
            }
        }, TaskScheduler.Default);

        private void DuckSweep(double keep)
        {
            using var doc = JsonDocument.Parse(Pactl("-f json list sink-inputs"));
            foreach (var si in doc.RootElement.EnumerateArray())
            {
                var props = si.GetProperty("properties");
                if (props.TryGetProperty("application.process.id", out var pid) && pid.GetString() == Environment.ProcessId.ToString())
                    continue;
                var index = si.GetProperty("index").GetInt32();
                var raw = si.GetProperty("volume").EnumerateObject().Select(c => c.Value.GetProperty("value").GetInt32()).ToArray();
                lock (_duckLock)
                {
                    if (!_isDucked) return;
                    if (!_originalVolumes.TryAdd(index, raw.Select(v => v.ToString()).ToArray())) continue;
                }
                Pactl($"set-sink-input-volume {index} {string.Join(' ', raw.Select(v => (int)(v * keep)))}");
            }
        }

        private void Restore()
        {
            KeyValuePair<int, string[]>[] saved;
            lock (_duckLock) { saved = _originalVolumes.ToArray(); _originalVolumes.Clear(); }
            foreach (var (index, raw) in saved)
            {
                try { Pactl($"set-sink-input-volume {index} {string.Join(' ', raw)}"); }
                catch { } // the stream ended while ducked - nothing to restore
            }
        }

        internal static string Pactl(string args)
        {
            using var p = Process.Start(new ProcessStartInfo("pactl", args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            })!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException($"pactl {args}: {p.StandardError.ReadToEnd().Trim()}");
            return output;
        }
    }
}
