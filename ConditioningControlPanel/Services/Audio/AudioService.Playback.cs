using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handle to one in-flight one-shot started by <see cref="AudioService.PlayOneShot"/>.
    /// Safe to Stop/Pause/Resume before the clip has actually opened (the request is replayed
    /// onto the device the moment it exists) and safe to keep after it finished (no-ops).
    /// </summary>
    public sealed class AudioPlaybackHandle
    {
        private readonly object _sync = new();
        private WaveOutEvent? _output;
        private bool _stopRequested;
        private bool _pauseRequested;
        private int _finished;

        /// <summary>True once the clip ended, was stopped, failed, or was force-torn-down.</summary>
        public bool IsFinished => Volatile.Read(ref _finished) != 0;

        /// <summary>Single-winner latch — everything that tears a one-shot down goes through this.</summary>
        internal bool TryFinish() => Interlocked.Exchange(ref _finished, 1) == 0;

        internal void Attach(WaveOutEvent output)
        {
            bool stop, pause;
            lock (_sync) { _output = output; stop = _stopRequested; pause = _pauseRequested; }
            // A Stop() that arrived while the clip was still queued must not be lost, or the
            // caller's "cut off the previous line" contract silently breaks.
            if (stop) { try { output.Stop(); } catch { } }
            else if (pause) { try { output.Pause(); } catch { } }
        }

        internal void Detach() { lock (_sync) { _output = null; } }

        public void Stop()
        {
            WaveOutEvent? o;
            lock (_sync) { _stopRequested = true; o = _output; }
            if (o != null) { try { o.Stop(); } catch { } }
        }

        public void Pause()
        {
            WaveOutEvent? o;
            lock (_sync) { _pauseRequested = true; o = _output; }
            if (o != null) { try { if (o.PlaybackState == PlaybackState.Playing) o.Pause(); } catch { } }
        }

        public void Resume()
        {
            WaveOutEvent? o;
            lock (_sync) { _pauseRequested = false; o = _output; }
            if (o != null) { try { if (o.PlaybackState == PlaybackState.Paused) o.Play(); } catch { } }
        }
    }

    public partial class AudioService
    {
        #region One-shot playback (#778/#779)

        // ── Why this file exists ──────────────────────────────────────────────────────────────
        // Every short clip in the app (giggle, pop, chime, whisper, bark voice, mantra prompt,
        // narrator cue, level-up) used to be played by its own copy of:
        //
        //     Task.Run(() => { using var r = new AudioFileReader(p);
        //                      using var o = new WaveOutEvent();
        //                      o.Init(r); o.Play();
        //                      while (o.PlaybackState == Playing) Thread.Sleep(50); });
        //
        // Three separate failure modes, all of which #778/#779 hit at once:
        //
        //  1. TWO blocked thread-pool threads per clip. NAudio's WaveOutEvent.Play() already
        //     queues its own PlaybackThread onto the pool for the clip's whole duration; the
        //     Thread.Sleep loop above burns a second one. Nothing capped how many ran at once.
        //  2. waveOutOpen (inside Init) does NOT fail fast when the Windows audio endpoint is
        //     gone — it blocks on the audio-service RPC. So every clip fired while the endpoint
        //     was dead parked another pair of pool threads, the pool injected ~1-2 more per
        //     second, and each parked frame pinned a half-built WaveOutEvent + AutoResetEvent +
        //     AudioFileReader. That is the reporter's +202 threads / +5,601 handles verbatim,
        //     and why the app "ground to a halt and died" the moment Audio Endpoint Builder
        //     came back and 200 blocked calls returned at once.
        //  3. WaveOutEvent captures SynchronizationContext.Current in its CONSTRUCTOR and posts
        //     PlaybackStopped to it. Constructed on the UI thread (the whisper/mind-wipe/level-up
        //     paths all did), cleanup was a dispatcher post — so a busy or wedged UI thread meant
        //     the disposal never ran at all.
        //
        // The replacement below: open/close serialized on one dedicated MTA thread (never the
        // dispatcher, never the pool, and no captured SynchronizationContext), no blocking wait
        // anywhere, a hard cap on concurrent clips, deterministic disposal from BOTH
        // PlaybackStopped and a safety timer, and a circuit breaker so a dead endpoint makes the
        // app go quiet instead of spinning. It recovers by itself when the endpoint comes back.

        /// <summary>Consecutive failures before new playback is suppressed.</summary>
        private const int OutputFailuresToTrip = 5;

        /// <summary>How long playback stays suppressed once the breaker trips.</summary>
        private static readonly TimeSpan OutputCooldown = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Hard ceiling on simultaneously-open one-shots. Overlapping short clips are normal
        /// (pop + bark + whisper), a hundred of them are not — that is a dead endpoint, and
        /// dropping the excess is what keeps the handle count flat instead of unbounded.
        /// </summary>
        private const int MaxConcurrentOneShots = 10;

        /// <summary>Queue ceiling for the open/close worker. Only reachable if it is wedged.</summary>
        private const int MaxQueuedPlaybackWork = 32;

        /// <summary>A single open/close taking longer than this means the audio stack is hung.</summary>
        private const int PlaybackWorkerWedgeMs = 4_000;

        private int _outputFailStreak;
        private long _outputSuppressedUntilTicks;   // DateTime.UtcNow.Ticks; 0 = healthy
        private int _outputRecoveryLogPending;      // 1 once we've logged a trip and owe a recovery line

        private int _oneShotsInFlight;

        private readonly BlockingCollection<Action> _playbackWork = new();
        private Thread? _playbackWorker;
        private int _playbackWorkerStarted;
        private int _playbackQueueDepth;
        private long _playbackWorkItemStartedTicks;  // 0 = idle

        private MMDeviceEnumerator? _notifyEnumerator;
        private EndpointWatcher? _endpointWatcher;
        private long _lastEndpointLogTicks;

        /// <summary>
        /// True while the circuit breaker is open — the audio endpoint failed repeatedly and we
        /// are deliberately silent for a cooldown instead of hammering a dead device. Clears
        /// itself when the cooldown expires or an endpoint change is observed.
        /// </summary>
        public bool IsOutputSuppressed
        {
            get
            {
                var until = Interlocked.Read(ref _outputSuppressedUntilTicks);
                if (until == 0) return false;
                if (DateTime.UtcNow.Ticks < until) return true;

                // Cooldown expired — re-arm and let the next clip probe the device for real.
                if (Interlocked.CompareExchange(ref _outputSuppressedUntilTicks, 0, until) == until)
                {
                    Interlocked.Exchange(ref _outputFailStreak, 0);
                    InvalidateOutputDeviceCache();   // never re-use a device number resolved against a dead endpoint
                    App.Logger?.Information("[Audio] output cooldown expired — retrying playback on a freshly resolved device");
                }
                return false;
            }
        }

        /// <summary>
        /// Play a short clip. Never blocks the caller, never blocks a thread for the clip's
        /// duration, and always disposes its device + reader exactly once.
        /// </summary>
        /// <param name="path">Absolute path to the clip.</param>
        /// <param name="volume">Final linear volume 0..1 (apply your own curve before calling).</param>
        /// <param name="tag">Short label for diagnostics ("bark", "pop", "whisper", ...).</param>
        /// <param name="onStarted">Invoked with the clip's duration once it is actually playing.</param>
        /// <param name="onFinished">
        /// Invoked exactly once, off the UI thread, when the clip ends OR when it was refused
        /// (missing file, breaker open, cap hit) — so caller state machines always unwind.
        /// </param>
        /// <returns>A handle, or null when nothing will play.</returns>
        public AudioPlaybackHandle? PlayOneShot(string path, float volume, string tag = "audio",
                                                Action<TimeSpan>? onStarted = null, Action? onFinished = null)
        {
            if (_disposed) { FireFinished(onFinished); return null; }

            if (string.IsNullOrWhiteSpace(path))
            {
                FireFinished(onFinished);
                return null;
            }

            if (volume <= 0f)
            {
                // Muted — don't touch the audio stack at all.
                FireFinished(onFinished);
                return null;
            }

            try
            {
                if (!File.Exists(path))
                {
                    App.Logger?.Debug("[Audio] {Tag}: clip not found at {Path}", tag, path);
                    FireFinished(onFinished);
                    return null;
                }
            }
            catch { FireFinished(onFinished); return null; }

            if (IsOutputSuppressed) { FireFinished(onFinished); return null; }

            if (IsPlaybackWorkerWedged())
            {
                // The open/close thread is stuck inside the audio stack. Every further clip would
                // just queue behind it, so refuse and treat the wedge itself as a device failure.
                NoteOutputFailure(tag, "playback worker wedged in the audio stack");
                FireFinished(onFinished);
                return null;
            }

            if (Interlocked.Increment(ref _oneShotsInFlight) > MaxConcurrentOneShots)
            {
                Interlocked.Decrement(ref _oneShotsInFlight);
                NoteOutputFailure(tag, $"concurrent one-shot cap ({MaxConcurrentOneShots}) reached");
                FireFinished(onFinished);
                return null;
            }

            var handle = new AudioPlaybackHandle();
            var clampedVolume = Math.Clamp(volume, 0f, 1f);

            if (!EnqueuePlaybackWork(() => OpenAndPlayOneShot(handle, path, clampedVolume, tag, onStarted, onFinished)))
            {
                Interlocked.Decrement(ref _oneShotsInFlight);
                handle.TryFinish();
                NoteOutputFailure(tag, "open/close queue full");
                FireFinished(onFinished);
                return null;
            }

            return handle;
        }

        /// <summary>
        /// Worker-thread half of <see cref="PlayOneShot"/>. Runs on the dedicated MTA playback
        /// thread so waveOutOpen can never block the dispatcher and WaveOutEvent captures no
        /// SynchronizationContext (PlaybackStopped then fires on NAudio's own thread, which is
        /// the only way cleanup stays reliable when the UI thread is busy).
        /// </summary>
        private void OpenAndPlayOneShot(AudioPlaybackHandle handle, string path, float volume, string tag,
                                        Action<TimeSpan>? onStarted, Action? onFinished)
        {
            AudioFileReader? reader = null;
            WaveOutEvent? output = null;
            System.Threading.Timer? safety = null;

            void Complete(bool ok, string reason)
            {
                if (!handle.TryFinish()) return;   // PlaybackStopped and the safety timer race — one wins
                handle.Detach();
                try { safety?.Dispose(); } catch { }
                ScheduleTeardown(output, reader);
                Interlocked.Decrement(ref _oneShotsInFlight);
                if (ok) NoteOutputSuccess();
                else NoteOutputFailure(tag, reason);
                FireFinished(onFinished);
            }

            try
            {
                reader = new AudioFileReader(path) { Volume = volume };
                var duration = reader.TotalTime;

                output = CreateWaveOut();
                if (output == null) throw new InvalidOperationException("no audio output device accepted waveOutOpen");

                output.Init(reader);

                // Created disarmed, then started right after Play() — created FIRST so a clip that
                // ends between Play() and here can still find a timer to dispose.
                var safetyMs = (int)Math.Clamp(reader.TotalTime.TotalMilliseconds + 5_000, 10_000, 20 * 60_000);
                safety = new System.Threading.Timer(_ =>
                {
                    // A deliberately paused clip (world-freeze) is not a stuck device — give it
                    // another window rather than tearing the line down mid-sentence.
                    try
                    {
                        if (output != null && output.PlaybackState == PlaybackState.Paused)
                        {
                            safety?.Change(safetyMs, Timeout.Infinite);
                            return;
                        }
                    }
                    catch { }
                    Complete(false, "playback never reported stopped");
                }, null, Timeout.Infinite, Timeout.Infinite);

                output.PlaybackStopped += (_, e) =>
                    Complete(e?.Exception == null, e?.Exception?.Message ?? "playback stopped with an error");

                handle.Attach(output);
                output.Play();

                // Safety net: if PlaybackStopped never arrives (endpoint yanked mid-clip, driver
                // stops pumping) this is what still frees the device, the reader and the slot.
                try { safety.Change(safetyMs, Timeout.Infinite); } catch { }

                NoteOutputSuccess();
                if (onStarted != null)
                {
                    try { onStarted(duration); }
                    catch (Exception ex) { App.Logger?.Debug("[Audio] {Tag}: onStarted threw: {E}", tag, ex.Message); }
                }
            }
            catch (Exception ex)
            {
                try { safety?.Dispose(); } catch { }
                try { output?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
                if (handle.TryFinish())
                {
                    handle.Detach();
                    Interlocked.Decrement(ref _oneShotsInFlight);
                    NoteOutputFailure(tag, ex.Message);
                    FireFinished(onFinished);
                }
            }
        }

        /// <summary>
        /// Free a finished clip's device + reader on the serialized worker, after a short grace
        /// period. The grace exists because disposing straight out of PlaybackStopped races
        /// NAudio's own unwind ("Handle is not initialized"); the worker exists because
        /// waveOutReset/waveOutClose block exactly as hard as waveOutOpen on a dead endpoint.
        /// </summary>
        private void ScheduleTeardown(WaveOutEvent? output, AudioFileReader? reader)
        {
            if (output == null && reader == null) return;

            void Teardown()
            {
                // teardown: true — never dropped by the queue cap, or the cap itself would leak.
                EnqueuePlaybackWork(() =>
                {
                    try { output?.Stop(); } catch { }
                    try { output?.Dispose(); } catch { }
                    try { reader?.Dispose(); } catch { }
                }, teardown: true);
            }

            try
            {
                System.Threading.Timer? t = null;
                t = new System.Threading.Timer(_ =>
                {
                    try { t?.Dispose(); } catch { }
                    Teardown();
                }, null, 50, Timeout.Infinite);
            }
            catch
            {
                Teardown();   // timer creation failed — free it now rather than never
            }
        }

        private static void FireFinished(Action? onFinished)
        {
            if (onFinished == null) return;
            try
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    try { onFinished(); }
                    catch (Exception ex) { App.Logger?.Debug("[Audio] one-shot completion callback threw: {E}", ex.Message); }
                }, null);
            }
            catch { }
        }

        #endregion

        #region Circuit breaker

        /// <summary>
        /// Record a successful open/play. Resets the failure streak and, if we were suppressed,
        /// logs the recovery once.
        /// </summary>
        internal void NoteOutputSuccess()
        {
            Interlocked.Exchange(ref _outputFailStreak, 0);
            Interlocked.Exchange(ref _outputSuppressedUntilTicks, 0);
            if (Interlocked.CompareExchange(ref _outputRecoveryLogPending, 0, 1) == 1)
                App.Logger?.Information("[Audio] output recovered — playback resumed");
        }

        /// <summary>
        /// Record a failed open/play. After <see cref="OutputFailuresToTrip"/> in a row the
        /// breaker opens for <see cref="OutputCooldown"/>: new playback is refused (silently,
        /// one log line) and the cached device resolution is dropped so the retry after the
        /// cooldown re-resolves the CURRENT default endpoint instead of the dead one.
        /// </summary>
        internal void NoteOutputFailure(string tag, string? reason)
        {
            var streak = Interlocked.Increment(ref _outputFailStreak);
            if (streak < OutputFailuresToTrip)
            {
                App.Logger?.Debug("[Audio] {Tag}: playback failed ({Reason}) — streak {Streak}/{Trip}",
                    tag, reason ?? "unknown", streak, OutputFailuresToTrip);
                return;
            }

            var until = DateTime.UtcNow.Add(OutputCooldown).Ticks;
            var previous = Interlocked.Exchange(ref _outputSuppressedUntilTicks, until);

            // Only the transition healthy -> suppressed gets a log line; a dead endpoint must
            // never be able to fill the log the way it used to fill the thread pool.
            if (previous == 0 || previous < DateTime.UtcNow.Ticks)
            {
                Interlocked.Exchange(ref _outputRecoveryLogPending, 1);
                App.Logger?.Warning(
                    "[Audio] output failed {Streak}x in a row (last: {Tag} — {Reason}). Suppressing playback for {Seconds}s; it will re-resolve the default device and retry. Windows audio may have restarted or the endpoint was removed.",
                    streak, tag, reason ?? "unknown", (int)OutputCooldown.TotalSeconds);
                InvalidateOutputDeviceCache();
            }
        }

        #endregion

        #region Playback worker + endpoint watcher

        private bool IsPlaybackWorkerWedged()
        {
            var started = Volatile.Read(ref _playbackWorkItemStartedTicks);
            if (started == 0) return false;
            return DateTime.UtcNow.Ticks - started > TimeSpan.TicksPerMillisecond * PlaybackWorkerWedgeMs;
        }

        private bool EnqueuePlaybackWork(Action work, bool teardown = false)
        {
            if (_disposed) return false;
            EnsurePlaybackWorker();

            if (!teardown && Volatile.Read(ref _playbackQueueDepth) >= MaxQueuedPlaybackWork)
                return false;

            Interlocked.Increment(ref _playbackQueueDepth);
            try
            {
                _playbackWork.Add(() =>
                {
                    Volatile.Write(ref _playbackWorkItemStartedTicks, DateTime.UtcNow.Ticks);
                    try { work(); }
                    catch (Exception ex) { App.Logger?.Debug("[Audio] playback work item failed: {E}", ex.Message); }
                    finally
                    {
                        Volatile.Write(ref _playbackWorkItemStartedTicks, 0);
                        Interlocked.Decrement(ref _playbackQueueDepth);
                    }
                });
                return true;
            }
            catch
            {
                Interlocked.Decrement(ref _playbackQueueDepth);   // collection completed (shutdown)
                return false;
            }
        }

        private void EnsurePlaybackWorker()
        {
            if (Interlocked.Exchange(ref _playbackWorkerStarted, 1) == 1) return;
            _playbackWorker = new Thread(PlaybackWorkerLoop)
            {
                IsBackground = true,
                Name = "AudioPlaybackWorker",
            };
            _playbackWorker.Start();
        }

        private void PlaybackWorkerLoop()
        {
            // MTA by default, and crucially NOT the dispatcher: every WaveOutEvent minted here
            // has SynchronizationContext.Current == null, so PlaybackStopped is raised on
            // NAudio's playback thread and cleanup no longer depends on a live UI thread.
            TryRegisterEndpointWatcher();

            try
            {
                foreach (var item in _playbackWork.GetConsumingEnumerable())
                {
                    try { item(); } catch { }
                }
            }
            catch { /* collection completed / disposed */ }

            TryUnregisterEndpointWatcher();
        }

        /// <summary>
        /// Watch for endpoint churn (default device changed, device added/removed/disabled, audio
        /// service restarted) so a cached WaveOut device number resolved against a device that no
        /// longer exists is dropped, and a tripped breaker re-arms immediately instead of waiting
        /// out its cooldown. Registered from the worker thread on purpose: MTA callbacks arrive on
        /// an RPC thread and never need the dispatcher to pump.
        /// </summary>
        private void TryRegisterEndpointWatcher()
        {
            try
            {
                _notifyEnumerator = new MMDeviceEnumerator();
                _endpointWatcher = new EndpointWatcher(this);
                _notifyEnumerator.RegisterEndpointNotificationCallback(_endpointWatcher);
                App.Logger?.Debug("[Audio] endpoint notification watcher registered");
            }
            catch (Exception ex)
            {
                // Purely an optimisation — the cooldown path recovers on its own without it.
                _endpointWatcher = null;
                App.Logger?.Debug("[Audio] could not register endpoint watcher: {E}", ex.Message);
            }
        }

        private void TryUnregisterEndpointWatcher()
        {
            try
            {
                if (_notifyEnumerator != null && _endpointWatcher != null)
                    _notifyEnumerator.UnregisterEndpointNotificationCallback(_endpointWatcher);
            }
            catch { }
            _endpointWatcher = null;
            try { _notifyEnumerator?.Dispose(); } catch { }
            _notifyEnumerator = null;
        }

        /// <summary>
        /// Called from a COM callback thread — must stay cheap and must never throw or block.
        /// </summary>
        internal void OnEndpointChanged(string reason)
        {
            try
            {
                if (_disposed) return;

                InvalidateOutputDeviceCache();
                Interlocked.Exchange(ref _outputFailStreak, 0);
                Interlocked.Exchange(ref _outputSuppressedUntilTicks, 0);

                // Endpoint changes arrive in bursts (one per role, per flow) — log at most one
                // line every couple of seconds.
                var now = DateTime.UtcNow.Ticks;
                var last = Interlocked.Read(ref _lastEndpointLogTicks);
                if (now - last > TimeSpan.TicksPerSecond * 2 &&
                    Interlocked.CompareExchange(ref _lastEndpointLogTicks, now, last) == last)
                {
                    App.Logger?.Information("[Audio] audio endpoint change ({Reason}) — device cache dropped, playback re-enabled", reason);
                }
            }
            catch { }
        }

        private sealed class EndpointWatcher : IMMNotificationClient
        {
            private readonly AudioService _service;
            public EndpointWatcher(AudioService service) => _service = service;

            public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _service.OnEndpointChanged("device state");
            public void OnDeviceAdded(string pwstrDeviceId) => _service.OnEndpointChanged("device added");
            public void OnDeviceRemoved(string deviceId) => _service.OnEndpointChanged("device removed");

            public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            {
                if (flow == DataFlow.Render) _service.OnEndpointChanged("default render device");
            }

            public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }

        /// <summary>Stop accepting new playback work and drop the endpoint watcher. Called from Dispose.</summary>
        private void ShutdownPlayback()
        {
            try { _playbackWork.CompleteAdding(); } catch { }
            if (Volatile.Read(ref _playbackWorkerStarted) == 0) TryUnregisterEndpointWatcher();
        }

        #endregion
    }
}
