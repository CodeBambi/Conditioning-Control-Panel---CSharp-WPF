using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
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
        internal static LibVlcAudio? Instance { get; set; }

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
            CoreAudio.PlayOneShotProvider = (path, volume, tag, started, finished) => PlayOneShot(path, volume, tag, started, finished);
            CoreAudio.PlayStoppableProvider = PlayOneShot;
            // ponytail: Windows ducking stays unseeded (no-op) until AudioService's WASAPI sweep is ported.
            if (!OperatingSystem.IsLinux()) return;
            CoreAudio.DuckProvider = Duck;
            CoreAudio.UnduckProvider = Unduck;
            CoreAudio.DuckGenerationProvider = () => { lock (_duckLock) return _duckGeneration; };
        }

        /// <summary>Same contract as WPF's AudioService.PlayOneShot: onStarted gets the clip length
        /// once playing; onFinished fires exactly once - off the UI thread when the clip ends or
        /// errors, synchronously on the caller's thread when it is refused (muted, missing file).
        /// Returns the clip's stop (WPF handle.Stop): it ends the clip early, and onFinished still fires once.</summary>
        public Action PlayOneShot(string path, float volume, string tag, Action<TimeSpan>? onStarted, Action? onFinished)
        {
            if (volume <= 0f || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Fire(onFinished); return static () => { }; }

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
                ThreadPool.QueueUserWorkItem(_ => { lock (player) if (done == 0) { ApplyPreferredDevice(player); player.Volume = vol; } });
                if (onStarted is null) return;
                try { onStarted(TimeSpan.FromMilliseconds(Math.Max(0, player.Length))); }
                catch (Exception ex) { Log.Debug(ex, "[Audio] {Tag}: onStarted threw", tag); }
            };
            player.EndReached += (_, _) => Finish();
            player.EncounteredError += (_, _) => Finish();
            player.Stopped += (_, _) => Finish();
            if (!player.Play()) Finish();
            // Off the caller's thread: libvlc's Stop must never run inside one of its own events.
            return () => ThreadPool.QueueUserWorkItem(_ => { lock (player) if (done == 0) player.Stop(); });
        }

        /// <summary>A clip the caller can re-volume and stop (mind wipe). A loop is WPF's overlap
        /// (MindWipeService.cs:55): the next copy starts 120 ms before the current one ends, because
        /// LibVLC's own <c>:input-repeat</c> reopens the input and left a measured 64 ms of silence
        /// at every restart.</summary>
        internal MindWipePlayer.IVoice? PlayVoice(string path, double volume, bool loop, Action onEnded)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            if (loop)
            {
                var l = new LoopVoice(this, path, onEnded) { Volume = volume };
                return l.Start() ? l : null;
            }
            var voice = NewVoice(path, onEnded);
            voice.Volume = volume;
            return voice.Play() ? voice : null;
        }

        private Voice NewVoice(string path, Action onEnded)
        {
            var media = new Media(_vlc, path, FromType.FromPath);
            media.AddOption(NoVideo);
            return new Voice(new MediaPlayer(media), media, onEnded);
        }

        private sealed class Voice(MediaPlayer player, Media media, Action onEnded) : MindWipePlayer.IVoice
        {
            private int _vol, _done;
            /// <summary>Raised once with the clip length in ms (0 when unknown) when it starts playing.</summary>
            internal Action<long>? Started;
            internal volatile bool Failed;
            public double Volume
            {
                // Cubic like PlayOneShot; applied off libvlc's thread once the output exists.
                set { _vol = (int)Math.Round(Math.Cbrt(Math.Clamp(value, 0, 1)) * 100); Apply(); }
            }
            private void Apply() => ThreadPool.QueueUserWorkItem(_ => { lock (player) if (_done == 0 && player.IsPlaying) player.Volume = _vol; });
            public bool Play()
            {
                var started = 0;
                player.Playing += (_, _) =>
                {
                    Apply();
                    if (Interlocked.Exchange(ref started, 1) == 0) Fire(() => Started?.Invoke(Math.Max(0, player.Length)));
                };
                player.EndReached += (_, _) => Dispose();
                player.EncounteredError += (_, _) => { Failed = true; Dispose(); };
                if (player.Play()) return true;
                Failed = true;
                Dispose();
                return false;
            }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) != 0) return;
                // Never dispose a player from inside its own libvlc event: that deadlocks.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { lock (player) { player.Stop(); player.Dispose(); } media.Dispose(); }
                    catch (Exception ex) { Log.Debug(ex, "[Audio] mindwipe dispose"); }
                    Fire(onEnded);
                });
            }
        }

        /// <summary>Copies of one clip, each started <see cref="OverlapMs"/> before the previous ends.
        /// Ends (onEnded) when a copy fails and nothing else is playing.</summary>
        private sealed class LoopVoice(LibVlcAudio audio, string path, Action onEnded) : MindWipePlayer.IVoice
        {
            private const int OverlapMs = 120;   // WPF CROSSFADE_OVERLAP_SECONDS
            private readonly object _gate = new();
            private readonly List<Voice> _live = new();
            private Timer? _next;
            private bool _done;
            private double _vol;

            public double Volume { set { lock (_gate) { _vol = value; foreach (var v in _live) v.Volume = value; } } }

            public bool Start()
            {
                Voice v;
                lock (_gate)
                {
                    if (_done) return false;
                    v = null!;
                    v = audio.NewVoice(path, () => Retire(v));
                    v.Volume = _vol;
                    v.Started = Schedule;
                    _live.Add(v);
                }
                return v.Play();
            }

            /// <summary>WPF: interval = length - overlap, at least 100 ms. Unknown length: back to back.</summary>
            private void Schedule(long lengthMs)
            {
                if (lengthMs <= 0) return;
                lock (_gate)
                {
                    if (_done) return;
                    _next?.Dispose();
                    _next = new Timer(_ => Start(), null, Math.Max(100, lengthMs - OverlapMs), Timeout.Infinite);
                }
            }

            private void Retire(Voice v)
            {
                bool restart = false, dead = false;
                lock (_gate)
                {
                    _live.Remove(v);
                    if (_done || _live.Count > 0) return;
                    if (v.Failed) dead = _done = true; else restart = true;
                }
                if (dead) { Fire(onEnded); return; }
                if (restart && !Start()) { lock (_gate) _done = true; Fire(onEnded); }
            }

            public void Dispose()
            {
                Voice[] live;
                lock (_gate) { _done = true; _next?.Dispose(); _next = null; live = _live.ToArray(); _live.Clear(); }
                foreach (var v in live) v.Dispose();
                Fire(onEnded);
            }
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

        // ---- output device (WPF AudioService.EnumerateOutputDevices / ApplyPreferredDevice) ----

        /// <summary>One picker entry. Id is the PulseAudio sink name, which is also what LibVLC's
        /// pulse output takes in SetOutputDevice; empty Id is the system default.</summary>
        internal sealed record OutputDevice(string Id, string Name)
        {
            public override string ToString() => Name;
        }

        /// <summary>As WPF: the first entry is always the synthetic system default.</summary>
        internal static List<OutputDevice> EnumerateOutputDevices(Func<string, string>? pactl = null)
        {
            var list = new List<OutputDevice> { new("", Loc.Get("set2_mic_system_default")) };
            if (pactl is null && !OperatingSystem.IsLinux()) return list;
            try
            {
                using var doc = JsonDocument.Parse((pactl ?? Pactl)("-f json list sinks"));
                foreach (var sink in doc.RootElement.EnumerateArray())
                {
                    var id = sink.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (id.Length == 0) continue;
                    var name = sink.TryGetProperty("description", out var d) ? d.GetString() : null;
                    list.Add(new(id, string.IsNullOrWhiteSpace(name) ? id : name));
                }
            }
            catch (Exception ex) { Log.Warning("EnumerateOutputDevices failed: {Error}", ex.Message); }
            return list;
        }

        /// <summary>WPF's ApplyPreferredDevice rule: the saved device, only when the live player
        /// lists it (a stale id routes audio to nowhere), and not again when already there.</summary>
        internal static string? PreferredDevice(string? saved, string? current, IEnumerable<string> available)
            => string.IsNullOrEmpty(saved) || saved == current || !available.Contains(saved) ? null : saved;

        /// <summary>Call once the player is live (its Playing event), never before: like WPF's
        /// mmdevice, the output's device list is only meaningful once it exists.</summary>
        internal static void ApplyPreferredDevice(MediaPlayer player)
        {
            try
            {
                var id = PreferredDevice(CoreSettings.Current.AudioOutputDeviceId, player.OutputDevice,
                    player.AudioOutputDeviceEnum.Select(d => d.DeviceIdentifier));
                if (id != null) player.SetOutputDevice(id);
            }
            catch (Exception ex) { Log.Debug("ApplyPreferredDevice: {Error}", ex.Message); }
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
