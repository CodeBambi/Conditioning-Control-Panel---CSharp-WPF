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
        private readonly Func<string, string> _pactl = Pactl;

        /// <summary>The seeded instance, so App can restore ducked apps on exit.</summary>
        internal static LibVlcAudio? Instance { get; private set; }

        /// <summary>The process's one LibVLC, shared with video (MiniPlayerWindow), as WPF shares
        /// VideoService.SharedLibVLC. Null when libvlc did not load.</summary>
        internal static LibVLC? Shared { get; private set; }

        /// <summary>Throws when libvlc is not installed; the caller then leaves CoreAudio unseeded.</summary>
        public LibVlcAudio(params string[] options)
        {
            // Windows ships libvlc beside the exe; a single-file publish has no
            // assembly location for LibVLCSharp to probe from, so point it there as WPF's VideoService does. Linux: system libvlc.
            // publish\libvlc (CopyLibVLCAfterPublish), else the build output's libvlc\win-x64.
            var bundled = new[] { "", Environment.Is64BitProcess ? "win-x64" : "win-x86" }
                .Select(sub => System.IO.Path.Combine(AppContext.BaseDirectory, "libvlc", sub))
                .FirstOrDefault(d => System.IO.File.Exists(System.IO.Path.Combine(d, "libvlc.dll")));
            if (OperatingSystem.IsWindows() && bundled != null) Core.Initialize(bundled);
            else Core.Initialize();
            // No global --no-video: video shares this instance. Audio media opt out per item instead.
            _vlc = new LibVLC(options.Append("--quiet").ToArray());
            Shared ??= _vlc;
            _vlc.SetUserAgent("Conditioning Control Panel", "CCP"); // the name pactl shows for our streams
        }

        /// <summary>Ducking only, with pactl stubbed: for the state-machine test.</summary>
        internal LibVlcAudio(Func<string, string> pactl) { _vlc = null!; _pactl = pactl; }

        public void Seed()
        {
            Instance = this;
            LayeredAudio.Instance = new LayeredAudio(path => new LayeredAudio.VlcLayerPlayer(_vlc, path));
            CoreAudio.PlayOneShotProvider = PlayOneShot;
            // ponytail: Windows ducking stays unseeded (no-op) until AudioService's WASAPI sweep is ported.
            if (!OperatingSystem.IsLinux()) return;
            CoreAudio.DuckProvider = Duck;
            CoreAudio.UnduckProvider = Unduck;
            CoreAudio.DuckGenerationProvider = () => { lock (_duckLock) return _duckGeneration; };
        }

        /// <summary>Same contract as WPF's AudioService.PlayOneShot: onStarted gets the clip length
        /// once playing; onFinished fires exactly once - off the UI thread when the clip ends or
        /// errors, synchronously on the caller's thread when it is refused (muted, missing file).</summary>
        public void PlayOneShot(string path, float volume, string tag, Action<TimeSpan>? onStarted, Action? onFinished)
        {
            if (volume <= 0f || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Fire(onFinished); return; }

            var media = new Media(_vlc, path, FromType.FromPath);
            media.AddOption(NoVideo);
            var player = new MediaPlayer(media);
            // WPF's volume is linear sample gain; LibVLC's is cubic, so 0.5 must land at ~-6 dB, not -18.
            var vol = (int)Math.Round(Math.Cbrt(Math.Clamp(volume, 0f, 1f)) * 100);
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

        /// <summary>Media option for audio players: skip any video track (an mp3's cover art would open a window).</summary>
        internal const string NoVideo = ":no-video";

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
            if (CoreSettings.Current?.MasterVolume == 0) return; // as WPF: nothing of ours will play
            lock (_duckLock)
            {
                _duckCount++;
                if (_isDucked) return;
                _isDucked = true;
                _duckWatchdog = new Timer(_ => ForceUnduck(), null, 300_000, Timeout.Infinite);
                var keep = 1 - Math.Clamp(strength, 0, 100) / 100.0;
                // As WPF: our own layers are in-process, so the sweep skips them - duck them directly.
                LayeredAudio.Instance?.ApplyDuck((float)(1 - keep));
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
                LayeredAudio.Instance?.ReleaseDuck();
                Enqueue(Restore);
            }
        }

        /// <summary>As WPF: advancing the generation turns every pending Unduck into a stale no-op.</summary>
        internal void ForceUnduck()
        {
            lock (_duckLock)
            {
                _duckGeneration++;
                _duckCount = 1;
                Unduck(_duckGeneration);
            }
        }

        /// <summary>App exit: never leave other apps ducked behind us.</summary>
        internal void Shutdown()
        {
            lock (_duckLock) { if (_isDucked) ForceUnduck(); }
            Drain();
        }

        internal void Drain() => _duckWork.Wait(2000);

        internal bool IsDucked { get { lock (_duckLock) return _isDucked; } }

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
            using var doc = JsonDocument.Parse(_pactl("-f json list sink-inputs"));
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
                try { _pactl($"set-sink-input-volume {index} {string.Join(' ', raw.Select(v => (int)(v * keep)))}"); }
                catch { lock (_duckLock) _originalVolumes.Remove(index); } // the stream ended mid-sweep
            }
        }

        private void Restore()
        {
            KeyValuePair<int, string[]>[] saved;
            lock (_duckLock) { saved = _originalVolumes.ToArray(); _originalVolumes.Clear(); }
            foreach (var (index, raw) in saved)
            {
                try { _pactl($"set-sink-input-volume {index} {string.Join(' ', raw)}"); }
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
