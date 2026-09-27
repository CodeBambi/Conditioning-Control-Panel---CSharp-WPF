using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The one way a waveOut player is stopped and released (#1295).
    ///
    /// The hang dump behind #1295 had the one-shot worker parked inside
    /// <c>WaveOutBuffer.Dispose</c>, waiting on NAudio's waveOutLock, while the device's own
    /// playback thread was still inside the driver. Three shapes fed that:
    ///
    ///  1. Stop ran waveOutReset / waveOutClose on the UI thread (Flash, Mind Wipe, Brain Drain,
    ///     Audio Layers, whispers). A driver that does not answer then takes the window with it.
    ///  2. <c>WaveOutEvent.Dispose</c> does not wait for its playback thread. Disposing straight
    ///     after Stop frees the pinned buffers while that thread may still be filling one and
    ///     handing it to the driver.
    ///  3. Stop fires PlaybackStopped, and the services' own handlers dispose the same player
    ///     from the playback thread while the caller is still disposing it. Two threads in one
    ///     Dispose free the same GCHandles twice.
    ///
    /// So: every teardown runs on its own short-lived background thread (never the UI thread,
    /// never NAudio's callback thread, never the shared open worker, never under a caller's
    /// lock), a player is torn down at most once, Stop comes first, and Dispose waits for the
    /// playback thread to report out. If it has not reported out within <see cref="StopWaitMs"/>
    /// the device is ABANDONED: leaked, not disposed. One leaked handle on a broken driver is
    /// much cheaper than joining it.
    /// </summary>
    internal static class WaveOutTeardown
    {
        /// <summary>How long a playing device gets to report out after Stop.</summary>
        internal const int StopWaitMs = 2_000;

        /// <summary>Grace for a device that was already stopped, so its thread can unwind.</summary>
        internal const int StoppedGraceMs = 50;

        /// <summary>Teardown threads allowed at once. Past this the driver is not answering.</summary>
        internal const int MaxInFlight = 16;

        internal enum Outcome { Nothing, Disposed, Abandoned, Duplicate }

        // Identity claim: the first Release of a player wins, every later one is a no-op. Weak,
        // so a finished player does not stay reachable through this table.
        private static readonly ConditionalWeakTable<object, object> Claimed = new();
        private static readonly object ClaimMark = new();

        private static int _inFlight;
        private static int _abandoned;

        /// <summary>Devices left behind because the driver did not let go. Diagnostics.</summary>
        internal static int AbandonedCount => Volatile.Read(ref _abandoned);

        /// <summary>
        /// Stop and release a player and its sources off the calling thread. Returns at once.
        /// Safe from the UI thread, from inside PlaybackStopped, and with any lock held.
        /// </summary>
        public static void Release(IWavePlayer? output, IDisposable? source, string tag)
            => Release(output, source == null ? Array.Empty<IDisposable?>() : new[] { source }, tag);

        /// <inheritdoc cref="Release(IWavePlayer?, IDisposable?, string)"/>
        public static void Release(IWavePlayer? output, IReadOnlyList<IDisposable?> sources, string tag)
        {
            if (output == null && sources.Count == 0) return;
            if (!TryClaim(output, sources)) return;

            if (Interlocked.Increment(ref _inFlight) > MaxInFlight)
            {
                // Sixteen teardowns stuck at once means the audio stack is gone. Do not add a
                // seventeenth thread to the pile; drop the references and let them leak. Still
                // ask it to stop (off this thread), or a looping bed keeps playing after Stop.
                Interlocked.Decrement(ref _inFlight);
                StopOffThread(output);
                NoteAbandoned(tag, "too many teardowns already waiting on the driver");
                return;
            }

            try
            {
                var t = new Thread(() =>
                {
                    try { RunClaimed(output, sources, StopWaitMs, StoppedGraceMs, tag); }
                    finally { Interlocked.Decrement(ref _inFlight); }
                }, 256 * 1024)
                {
                    IsBackground = true,
                    Name = "AudioTeardown",
                };
                t.Start();
            }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref _inFlight);
                StopOffThread(output);
                Diag.Swallowed(ex, "could not start a teardown thread; the device leaks");
            }
        }

        /// <summary>
        /// A <see cref="WaveOutEvent"/> that raises PlaybackStopped on its own playback thread.
        /// WaveOutEvent captures <see cref="SynchronizationContext.Current"/> in its constructor
        /// and POSTS PlaybackStopped there, so one built on the UI thread reports out only when
        /// the dispatcher gets round to it. The teardown waits on that report, so a UI thread
        /// busy past <see cref="StopWaitMs"/> would leak a perfectly healthy device. Only for
        /// players with no PlaybackStopped handler that needs the UI thread.
        /// </summary>
        public static WaveOutEvent NewWaveOut()
        {
            var prev = SynchronizationContext.Current;
            if (prev == null) return new WaveOutEvent();
            SynchronizationContext.SetSynchronizationContext(null);
            try { return new WaveOutEvent(); }
            finally { SynchronizationContext.SetSynchronizationContext(prev); }
        }

        /// <summary>
        /// Stop a player off the calling thread without releasing it (its owner releases it when
        /// PlaybackStopped arrives). For the UI thread, where waveOutReset must never run.
        /// </summary>
        public static void StopOffThread(IWavePlayer? output)
        {
            if (output == null) return;
            try
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    try { output.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
                }, null);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>
        /// Synchronous core, on the calling thread. Claims the player first, so a second call
        /// for the same player answers <see cref="Outcome.Duplicate"/>. Tests drive this directly.
        /// </summary>
        internal static Outcome Run(IWavePlayer? output, IReadOnlyList<IDisposable?> sources,
                                    int waitMs, int graceMs, string tag = "test")
        {
            if (output == null && sources.Count == 0) return Outcome.Nothing;
            if (!TryClaim(output, sources)) return Outcome.Duplicate;
            return RunClaimed(output, sources, waitMs, graceMs, tag);
        }

        private static Outcome RunClaimed(IWavePlayer? output, IReadOnlyList<IDisposable?> sources,
                                          int waitMs, int graceMs, string tag)
        {
            if (output != null)
            {
                using var reported = new ManualResetEventSlim(false);
                EventHandler<StoppedEventArgs> onStopped = (_, _) =>
                {
                    try { reported.Set(); } catch (ObjectDisposedException) { }
                };

                bool wasRunning;
                try
                {
                    output.PlaybackStopped += onStopped;
                    wasRunning = output.PlaybackState != PlaybackState.Stopped;
                }
                catch (Exception ex)
                {
                    // Already disposed or half built. Nothing more to wait for.
                    Diag.Swallowed(ex);
                    wasRunning = false;
                }

                try { output.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }

                // A running device owes us a PlaybackStopped once its thread leaves the driver.
                // A stopped one only needs a moment to finish unwinding (the event may already
                // be behind us, so a missing signal there is not a failure).
                bool reportedOut = reported.Wait(wasRunning ? waitMs : graceMs) || !wasRunning;

                try { output.PlaybackStopped -= onStopped; } catch (Exception ex) { Diag.Swallowed(ex); }

                if (!reportedOut)
                {
                    NoteAbandoned(tag, $"no PlaybackStopped within {waitMs} ms of Stop");
                    return Outcome.Abandoned;   // sources stay too: the stuck thread may still read them
                }

                try { output.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }

            foreach (var s in sources)
            {
                try { s?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            return Outcome.Disposed;
        }

        private static bool TryClaim(IWavePlayer? output, IReadOnlyList<IDisposable?> sources)
        {
            object? key = output;
            if (key == null)
            {
                foreach (var s in sources) { if (s != null) { key = s; break; } }
            }
            if (key == null) return false;
            return Claimed.TryAdd(key, ClaimMark);
        }

        private static void NoteAbandoned(string tag, string why)
        {
            var n = Interlocked.Increment(ref _abandoned);
            // One line per abandon is plenty: this only happens on a driver that stopped answering.
            App.Logger?.Warning("[Audio] {Tag}: left an output device behind ({Why}); {Count} so far this run",
                tag, why, n);
        }
    }
}
