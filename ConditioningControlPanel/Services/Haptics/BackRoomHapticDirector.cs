using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Haptics for the Back Room's stations (CONTRACT 10.23). The page has already decided WHAT a moment
    /// feels like and sends flat pulses <c>{level, ms}</c>; this director only decides whether the toy
    /// takes them. It does nothing when haptics are off or no device is connected, it rate limits again
    /// host-side because the page is untrusted input, and the newest accepted pulse replaces the one in
    /// play (the page arbitrated priority before it posted).
    ///
    /// Pulses are mixer transients, so they ride OVER video / audio-sync layers instead of replacing
    /// them, the user's master intensity and cap apply inside the mixer, and a stop cancels OUR pulse
    /// only. There is no waveform on this wire by design: a Lovense pattern is one flat level for its
    /// duration and a repeat of the same level inside a second is dropped, so the page sends distinct
    /// levels at about three a second and nothing here tries to be cleverer than that.
    /// </summary>
    internal static class BackRoomHapticDirector
    {
        /// <summary>
        /// The rate limit, with no clock and no WPF in it. At most one pulse per <see cref="MinGapMs"/>
        /// (under the page's own 100 ms interrupt floor, so a legitimate interrupt always passes) and at
        /// most <see cref="MaxPerWindow"/> per rolling <see cref="WindowMs"/>, which is double what an
        /// honest page sends and far under what a toy command channel chokes on.
        /// </summary>
        public sealed class Limiter
        {
            public const int MinGapMs = 80, WindowMs = 1000, MaxPerWindow = 6;
            private readonly Queue<long> _recent = new();
            private long _last = long.MinValue;

            public bool Accept(long nowMs)
            {
                while (_recent.Count > 0 && nowMs - _recent.Peek() >= WindowMs) _recent.Dequeue();
                if (_last != long.MinValue && nowMs - _last < MinGapMs) return false;
                if (_recent.Count >= MaxPerWindow) return false;
                _recent.Enqueue(nowMs);
                _last = nowMs;
                return true;
            }
        }

        /// <summary>Mixer priority from the level: the page's big moments are its strong ones.</summary>
        public static int PriorityFor(double level) => level >= 0.7 ? 3 : level >= 0.4 ? 2 : 1;

        /// <summary>Buttplug commands carry about a second of latency and a short pulse arrives as mush:
        /// stretch it, as the DtRH director does, but never past the wire's own ceiling.</summary>
        public static int DurationFor(int ms, bool buttplug)
            => buttplug ? Math.Min(BackRoomBridge.HapticMaxMs, ms * 2) : ms;

        private static readonly object Gate = new();
        private static readonly Limiter Limit = new();
        private static CancellationTokenSource? _playing;

        private static bool Ready
            => App.Settings?.Current?.Haptics is { Enabled: true } && App.Haptics is { IsConnected: true };

        /// <summary>The bridge's <c>Deps.Haptic</c>: one validated pulse, or a stop.</summary>
        public static void OnHaptic(BackRoomHaptic h)
        {
            if (h.IsStop) { Stop(); return; }
            var haptics = App.Haptics;
            if (haptics == null || !Ready) return;

            CancellationTokenSource cts;
            lock (Gate)
            {
                if (!Limit.Accept(Environment.TickCount64)) return;
                try { _playing?.Cancel(); } catch { }
                cts = _playing = new CancellationTokenSource();
            }
            var ms = DurationFor(h.Ms, haptics.IsButtplugProvider);
            App.Logger?.Debug("BackRoomHaptics: {Station}/{Tag} {Pct}% {Ms}ms", h.Station, h.Tag, (int)(h.Level * 100), ms);
            _ = PlayAsync(haptics, h.Level, ms, cts);
        }

        private static async System.Threading.Tasks.Task PlayAsync(HapticService haptics, double level, int ms, CancellationTokenSource cts)
        {
            try { await haptics.PlayPatternAsync(level, ms, VibrationMode.Constant, PriorityFor(level), ToyRole.All, cts.Token); }
            catch { /* a toy that dropped mid-pulse is not the room's problem */ }
            finally
            {
                lock (Gate) { if (_playing == cts) _playing = null; }
                cts.Dispose();
            }
        }

        /// <summary>Stop OUR pulse only (pause, suspend, station-close, window close). Whatever else is
        /// driving the toy underneath keeps going.</summary>
        public static void Stop()
        {
            CancellationTokenSource? cts;
            lock (Gate) { cts = _playing; _playing = null; }
            try { cts?.Cancel(); } catch { }
        }
    }
}
