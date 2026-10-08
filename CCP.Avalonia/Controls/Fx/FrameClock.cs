using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

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
    /// The pure half of <see cref="FrameClock"/>: given frame times, say which frames are due so
    /// the clock holds its interval on WHOLE frames (skip-to-interval). Twin of the WPF
    /// FrameClock.OnRendering body, numbers exact: 4 ms slack, a repeated frame time is ignored.
    /// </summary>
    public sealed class FrameGate
    {
        /// <summary>Slack under the interval that still counts as due, so a tick a few ms early
        /// on a jittery frame is taken instead of slipping a whole frame late.</summary>
        public const double SlackMs = 4.0;

        private TimeSpan _lastTick = TimeSpan.MinValue;
        private TimeSpan _lastFrame = TimeSpan.MinValue;

        public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(33);

        public void Reset()
        {
            _lastTick = TimeSpan.MinValue;
            _lastFrame = TimeSpan.MinValue;
        }

        /// <summary>True when the frame at <paramref name="now"/> should tick.</summary>
        public bool Due(TimeSpan now)
        {
            // A frame can be reported more than once for the same render time.
            if (now == _lastFrame) return false;
            _lastFrame = now;
            if (_lastTick != TimeSpan.MinValue &&
                (now - _lastTick).TotalMilliseconds < Interval.TotalMilliseconds - SlackMs) return false;
            _lastTick = now;
            return true;
        }
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
            if (!_gate.Due(now)) return;
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
