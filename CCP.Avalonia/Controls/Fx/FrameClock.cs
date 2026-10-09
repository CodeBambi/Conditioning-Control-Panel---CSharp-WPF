using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Motion;

namespace ConditioningControlPanel.Avalonia.Controls.Fx
{
    /// <summary>
    /// Where frame callbacks come from. The live one is <see cref="TopLevelFrameSource"/>
    /// (Avalonia's own animation frame, one per composed frame); tests inject a fake and step it.
    /// The timestamp handed to a subscriber is the frame's render time.
    /// </summary>
    public interface IFrameSource
    {
        void Subscribe(Action<TimeSpan> onFrame);
        void Unsubscribe(Action<TimeSpan> onFrame);
    }

    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/FrameClock.cs (perf pass, 2026-10-07).
    ///
    /// <para>A frame-locked replacement for a DispatcherTimer driving an effect. WPF ticked from
    /// CompositionTarget.Rendering; the Avalonia twin rides <see cref="TopLevel.RequestAnimationFrame"/>
    /// through one shared <see cref="TopLevelFrameSource"/> per window, so every tick lands right
    /// before a frame is composed, and it skips whole frames to hold <see cref="Interval"/>: at
    /// 30 fps on a 60 Hz screen that is exactly every second frame, where a free-running 33 ms timer
    /// drifts against the refresh and shows as an uneven step (judder) however fast the PC is.</para>
    ///
    /// <para>Same surface as the timer it replaces: Interval, IsEnabled, Start, Stop, Tick. The
    /// window's frame request stops as soon as no clock listens, so a parked effect costs nothing.</para>
    ///
    /// <para>Off a window (owner not attached yet, or no owner given) it falls back to a 16 ms
    /// dispatcher pump: still ticks, never frame-locked. Everything that matters starts its clock
    /// from Loaded, so the fallback is a safety net, not a path.</para>
    /// </summary>
    public sealed class FrameClock
    {
        private readonly FrameGate _gate = new();
        private readonly Visual? _owner;
        private readonly IFrameSource? _injected;
        private IFrameSource? _source;
        private readonly Action<TimeSpan> _onFrame;

        /// <summary>A clock that finds its window from <paramref name="owner"/> at every Start.</summary>
        public FrameClock(Visual owner) : this() => _owner = owner;

        /// <summary>A clock on an explicit frame source (tests, benches).</summary>
        public FrameClock(IFrameSource source) : this() => _injected = source;

        private FrameClock() => _onFrame = OnFrame;

        public TimeSpan Interval
        {
            get => _gate.Interval;
            set => _gate.Interval = value;
        }

        public bool IsEnabled { get; private set; }

        public event EventHandler? Tick;

        /// <summary>True when the last Start found a window to lock to (tests, bench).</summary>
        internal bool IsFrameLocked => _source is TopLevelFrameSource;

        public void Start()
        {
            if (IsEnabled) return;
            IsEnabled = true;
            _gate.Reset();
            _source = _injected ?? Resolve();
            _source.Subscribe(_onFrame);
        }

        public void Stop()
        {
            if (!IsEnabled) return;
            IsEnabled = false;
            _source?.Unsubscribe(_onFrame);
            _source = null;
        }

        private IFrameSource Resolve()
        {
            var top = _owner == null ? null : TopLevel.GetTopLevel(_owner);
            return top != null ? TopLevelFrameSource.For(top) : DispatcherFrameSource.Shared;
        }

        private void OnFrame(TimeSpan now)
        {
            if (!IsEnabled) return;
            bool due = _source is TopLevelFrameSource top ? top.BeatDue(_gate, now) : _gate.Due(now);
            if (!due) return;
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// One per window: asks the window for the next animation frame while anybody listens, and
    /// fans the frame out to every subscribed <see cref="FrameClock"/>. The request is one-shot in
    /// Avalonia, so it is re-armed after each frame only while the list is not empty: the last
    /// Unsubscribe lets the window go idle.
    /// </summary>
    public sealed class TopLevelFrameSource : IFrameSource
    {
        private static readonly ConditionalWeakTable<TopLevel, TopLevelFrameSource> Table = new();

        private readonly WeakReference<TopLevel> _top;
        private readonly List<Action<TimeSpan>> _subs = new();
        private Action<TimeSpan>[] _snapshot = Array.Empty<Action<TimeSpan>>();
        private bool _requested;
        private readonly Action<TimeSpan> _callback;

        private TopLevelFrameSource(TopLevel top)
        {
            _top = new WeakReference<TopLevel>(top);
            _callback = OnFrame;
        }

        public static TopLevelFrameSource For(TopLevel top) => Table.GetValue(top, t => new TopLevelFrameSource(t));

        /// <summary>Clocks listening right now (tests).</summary>
        internal int SubscriberCount => _subs.Count;

        /// <summary>True while a frame request is outstanding (tests).</summary>
        internal bool IsRequesting => _requested;

        public void Subscribe(Action<TimeSpan> onFrame)
        {
            if (_subs.Contains(onFrame)) return;
            _subs.Add(onFrame);
            _snapshot = _subs.ToArray();
            Request();
        }

        public void Unsubscribe(Action<TimeSpan> onFrame)
        {
            if (_subs.Remove(onFrame)) _snapshot = _subs.ToArray();
        }

        private void Request()
        {
            if (_requested || _subs.Count == 0) return;
            if (!_top.TryGetTarget(out var top)) return;
            _requested = true;
            top.RequestAnimationFrame(_callback);
        }

        // ---- the shared beat (Home lag, 2026-10-09) -------------------------------------------
        //
        // Every clock used to gate on its own FrameGate, so two 30 fps clocks on one window (the
        // Home fog and the logo dial) could tick on ALTERNATE 60 Hz frames: the compositor then ran
        // 60 times a second and each frame repainted the whole dashboard under the fog. Clocks of
        // the same rate on one window now share one gate, so they tick on the same frames and the
        // window composes 30 frames a second. A clock that starts mid-beat waits for the next one
        // (at most one interval); a clock alone behaves exactly as before.

        private readonly Dictionary<long, (FrameGate Gate, TimeSpan Frame, bool Due)> _beats = new();

        /// <summary>True when the beat for <paramref name="own"/>'s interval is due on this frame.</summary>
        internal bool BeatDue(FrameGate own, TimeSpan now)
        {
            long key = (long)Math.Round(own.Interval.TotalMilliseconds);
            if (!_beats.TryGetValue(key, out var beat))
                beat = (new FrameGate { Interval = own.Interval }, TimeSpan.MinValue, false);
            if (beat.Frame != now)
                beat = (beat.Gate, now, beat.Gate.Due(now));
            _beats[key] = beat;
            return beat.Due;
        }

        private void OnFrame(TimeSpan now)
        {
            foreach (var s in _snapshot)
            {
                try { s(now); }
                catch (Exception ex) { Serilog.Log.Debug("FrameClock subscriber: {E}", ex.Message); }
            }
            // Re-armed from inside the callback, the way Avalonia means it: MediaContext queues it for
            // the next pulse and its animation timer (or the compositor) brings that pulse round.
            // Posting the re-arm instead schedules an immediate render op and spins the dispatcher.
            _requested = false;
            Request();
        }
    }

    /// <summary>
    /// The off-window fallback: a 16 ms dispatcher pump stamped with a stopwatch. Shared, and
    /// stopped when the last clock leaves, like the window source.
    /// </summary>
    internal sealed class DispatcherFrameSource : IFrameSource
    {
        public static readonly DispatcherFrameSource Shared = new();

        private readonly List<Action<TimeSpan>> _subs = new();
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        private DispatcherTimer? _timer;

        public void Subscribe(Action<TimeSpan> onFrame)
        {
            if (_subs.Contains(onFrame)) return;
            _subs.Add(onFrame);
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
                _timer.Tick += (_, _) =>
                {
                    var now = _sw.Elapsed;
                    foreach (var s in _subs.ToArray())
                    {
                        try { s(now); }
                        catch (Exception ex) { Serilog.Log.Debug("FrameClock fallback subscriber: {E}", ex.Message); }
                    }
                };
            }
            if (!_timer.IsEnabled) _timer.Start();
        }

        public void Unsubscribe(Action<TimeSpan> onFrame)
        {
            _subs.Remove(onFrame);
            if (_subs.Count == 0) _timer?.Stop();
        }
    }
}
