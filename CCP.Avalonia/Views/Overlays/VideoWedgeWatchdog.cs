// PORTED from WPF 7.1.5 Services/Video/VideoService.cs (:68-160, :7186-7440), the UI-thread wedge watchdog of
// the mandatory video (hunt IB8, HB19; the freeze / lockout rescue of #529, #765-#767).
// A UI-thread heartbeat (Background priority, 1 s) proves the dispatcher still drains. A threadpool timer (3 s)
// notices when it goes stale WHILE A VIDEO HOLDS THE SCREEN and climbs WPF's ladder, measured from the start
// of the stall:
//   rung 1 ( 8 s) - stop the native player OFF the UI thread (its own task, 3 s budget) and post a teardown
//                   for the moment the dispatcher drains again;
//   rung 2 (18 s) - NOT PORTED: WPF retires the shared LibVLC instance and builds a new one. This head has one
//                   LibVLC for every sound and video (docs/avalonia-decisions.md), so retiring it is an owner call;
//   rung 3 (28 s) - the escape hatch: the full-screen topmost video windows are dropped out of the topmost band
//                   and hidden with posted Win32 calls on cached handles (no dispatcher, no LibVLC), so the
//                   desktop and Task Manager are reachable. Windows only: X11 / Wayland have no call here that
//                   reaches another thread's window without the dispatcher.
// Before the first frame and during a teardown only the hatch may run (WPF #750-#753). Once a stall has been
// seen, the strict close veto stands down for the rest of the clip (#765): a lock is only defensible while the
// app responds.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    internal sealed class VideoWedgeWatchdog : IDisposable
    {
        internal const int StallMs = 8000, Rung2Ms = 18000, Rung3Ms = 28000, StopBudgetMs = 3000, PeriodMs = 3000;

        private long _heartbeatTicks;
        private volatile int _rung;
        private volatile bool _stallSeen, _disposed, _preRollLogged;
        private DispatcherTimer? _heartbeat;
        private Timer? _timer;

        /// <summary>A video is on screen or being torn down (WPF _videoPlaying || _isCleaningUp).</summary>
        internal Func<bool> Armed { get; init; } = () => false;
        /// <summary>Playback really started: a player exists to stop (WPF _playbackStarted).</summary>
        internal Func<bool> Live { get; init; } = () => false;
        /// <summary>The teardown owns the players (WPF _isCleaningUp).</summary>
        internal Func<bool> Cleaning { get; init; } = () => false;
        /// <summary>Rung 1, on a worker: stop the native player. True when it returned inside the budget.</summary>
        internal Func<bool> StopPlayers { get; init; } = () => true;
        /// <summary>Rung 1: queue the real teardown for when the dispatcher drains.</summary>
        internal Action PostTeardown { get; init; } = () => { };
        /// <summary>Rung 3: release the topmost windows without the dispatcher.</summary>
        internal Action EscapeHatch { get; init; } = () => { };
        /// <summary>The UI thread's proof of life, once a second (the overlay refreshes its window handles here).</summary>
        internal Action? OnBeat { get; init; }
        /// <summary>UTC ticks; tests step it.</summary>
        internal Func<long> NowTicks { get; init; } = () => DateTime.UtcNow.Ticks;

        /// <summary>A stall past <see cref="StallMs"/> was seen during this clip: the strict close veto stands down.</summary>
        internal bool StallSeen => _stallSeen;
        internal int Rung => _rung;

        /// <summary>Arms both clocks. UI thread.</summary>
        internal void Start()
        {
            Beat();
            _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _heartbeat.Tick += (_, _) => Beat();
            _heartbeat.Start();
            _timer = new Timer(_ => Tick(), null, PeriodMs, PeriodMs);
        }

        /// <summary>The UI thread is alive (the heartbeat timer; tests call it).</summary>
        internal void Beat()
        {
            Interlocked.Exchange(ref _heartbeatTicks, NowTicks());
            try { OnBeat?.Invoke(); } catch (Exception ex) { Log.Debug("VideoWedge: beat hook failed: {E}", ex.Message); }
        }

        /// <summary>WPF WedgeWatchdogTick. Runs on a threadpool thread every 3 s (tests call it).</summary>
        internal void Tick()
        {
            try
            {
                if (_disposed) return;
                bool cleaning = Cleaning();
                if (!Armed() && !cleaning) return;
                if (_rung >= 3) return;   // ladder exhausted
                long stallMs = (NowTicks() - Interlocked.Read(ref _heartbeatTicks)) / TimeSpan.TicksPerMillisecond;
                if (stallMs < StallMs) return;
                _stallSeen = true;

                if (!Live() || cleaning)
                {
                    // Nothing safe to rescue (nothing on screen yet, or the teardown owns the player): the
                    // hatch touches no native VLC state, so it is the only rung allowed here.
                    if (!_preRollLogged)
                    {
                        _preRollLogged = true;
                        Log.Warning("VideoService: UI thread stalled {StallMs}ms during video {Phase} - escape hatch only",
                            stallMs, cleaning ? "TEARDOWN" : "PRE-ROLL");
                    }
                    if (stallMs >= Rung3Ms) RunHatch(stallMs);
                    return;
                }

                if (_rung < 1)
                {
                    _rung = 1;
                    Log.Error("VideoService: UI thread wedged {StallMs}ms during video playback - off-thread rescue (freeze/lockout guard)", stallMs);
                    bool stopped = false;
                    try { stopped = StopPlayers(); } catch (Exception ex) { Log.Debug("VideoWedge: stop failed: {E}", ex.Message); }
                    if (!stopped) Log.Error("VideoService: the player Stop() is still running after {Budget}ms", StopBudgetMs);
                    try { PostTeardown(); } catch (Exception ex) { Log.Debug("VideoWedge: post teardown failed: {E}", ex.Message); }
                    return;
                }
                if (_rung < 2 && stallMs >= Rung2Ms)
                {
                    _rung = 2;   // WPF retires the shared LibVLC here; not ported (see the header)
                    Log.Error("VideoService: still wedged after {StallMs}ms (the shared LibVLC is kept on this head)", stallMs);
                    return;
                }
                if (stallMs >= Rung3Ms) RunHatch(stallMs);
            }
            catch (Exception ex) { Log.Debug("VideoWedge: tick error: {E}", ex.Message); }
        }

        private void RunHatch(long stallMs)
        {
            if (_rung >= 3) return;
            _rung = 3;
            Log.Error("VideoService: UI thread wedged {StallMs}ms - releasing the topmost video window(s) so the desktop is reachable", stallMs);
            // Its own task: a cross-thread window call can itself block on the hung owner thread.
            Task.Run(() => { try { EscapeHatch(); } catch (Exception ex) { Log.Debug("VideoWedge: hatch failed: {E}", ex.Message); } });
        }

        /// <summary>Runs <paramref name="stop"/> on its own task and waits at most the budget (WPF WedgeStopPlayersOffThread):
        /// a Stop() that never returns costs only its own task.</summary>
        internal static bool StopOffThread(Action stop, int budgetMs = StopBudgetMs)
        {
            var task = Task.Run(() => { try { stop(); } catch (Exception ex) { Log.Debug("VideoWedge: player.Stop failed: {E}", ex.Message); } });
            return task.Wait(budgetMs);
        }

        // ---- rung 3 on Windows: posted calls only (the owning thread is by definition not pumping) ----

        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_ASYNCWINDOWPOS = 0x4000;
        private const int SW_HIDE = 0;
        private static readonly IntPtr HWND_NOTOPMOST = new(-2);

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int cmd);

        /// <summary>WPF RunWedgeEscapeHatch's two calls per window. False off Windows (nothing to call).</summary>
        internal static bool ReleaseTopmost(IntPtr[] handles)
        {
            if (!OperatingSystem.IsWindows()) return false;
            foreach (var hwnd in handles)
            {
                if (hwnd == IntPtr.Zero) continue;
                try
                {
                    SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
                    ShowWindowAsync(hwnd, SW_HIDE);
                }
                catch (Exception ex) { Log.Debug("VideoWedge: window release failed: {E}", ex.Message); }
            }
            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            try { _timer?.Dispose(); } catch { }
            _timer = null;
            try { _heartbeat?.Stop(); } catch { }
            _heartbeat = null;
        }
    }
}
