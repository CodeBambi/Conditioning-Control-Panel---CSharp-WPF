using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Phase 4: "the eye is watching" attention-check mechanic. Pops a
    /// calibration-style ring at a random screen position on a randomized
    /// cadence; the user has a grace window to fixate on the ring. Pass →
    /// reward (XP). Fail → configurable penalty (lock card, XP penalty, or
    /// none). Active state is gated by AttentionCheckEnabled and the
    /// AttentionCheckScope (Always vs DuringSessionsOnly).
    ///
    /// Scheduling: each fire schedules the next at a random interval drawn
    /// uniformly from [60/max, 60/min] minutes, so the expected rate is
    /// between AttentionCheckMinPerSession and AttentionCheckMaxPerSession
    /// per hour. (The "Session" name is historical; the timing model is
    /// per-hour over app uptime.)
    ///
    /// Dwell detection is standalone — subscribes to
    /// WebcamTrackingService.OnGazeMove directly rather than going through
    /// GazeFocusService, since the attention-check has its own progress
    /// visual + grace logic that doesn't fit the dwell-pop pipeline.
    /// </summary>
    public class AttentionCheckService
    {
        private const int TickMs = 33; // dwell + grace ticker
        private const int DwellTargetMs = 1000; // time on target to pass
        private const double BoundsSlackDips = 30; // extra hit area around the ring

        // Reward / penalty amounts are fixed by design — these are intentionally
        // not user-tunable. A reachable XP knob would invite gaming; a fixed
        // value preserves the mechanic's role as a check, not a grind lever.
        public const int PassXp = 20;
        public const int FailXpPenalty = 10;

        // First fire after Start() uses a much shorter interval than steady
        // state so the user sees the mechanic work within a few minutes of
        // enabling it. After the first fire, ScheduleNext switches to the
        // settings-driven random-interval model.
        private const double FirstFireMinSeconds = 30;
        private const double FirstFireMaxSeconds = 120;

        private readonly Random _rng = new();
        private DispatcherTimer? _scheduleTimer;
        private DispatcherTimer? _tickTimer;

        private Window? _activeWindow;
        private AttentionCheckControl? _activeControl;
        private Rect _activeBounds; // window bounds in DIPs (with slack baked in for hit-test)
        private DateTime _fireStartedAt;
        private double _dwellMs;
        private System.Windows.Point? _lastGazePoint;
        private bool _gazeSubscribed;

        // Belt-and-braces shutdown cleanup. Setting Owner on the popup window
        // is the primary defense (Owner relationship closes the popup with
        // MainWindow). The MainWindow.Closing handler is the backup, since
        // unowned-window-blocks-OnLastWindowClose has bitten this codebase
        // before (see memory feedback_unowned_window_shutdown_trap.md).
        private Window? _subscribedWindow;

        // Cleared on Start(), set after the first successful Fire(). Drives
        // the short-first-interval discoverability behavior.
        private bool _firstFireScheduled;

        public event Action? OnPass;
        public event Action? OnFail;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Starts the scheduler. Safe to call when already running (no-op).
        /// Hot-reload entry point: settings PropertyChanged handlers can
        /// call Start/Stop to react to AttentionCheckEnabled flips.
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            _firstFireScheduled = false;

            // MainWindow may not exist yet (Start runs in App.OnStartup
            // before MainWindow.Show), so defer subscription until the
            // dispatcher is idle, by which time MainWindow is constructed
            // and Application.Current.MainWindow resolves.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(TrySubscribeMainWindowClosing),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            ScheduleNext();
        }

        /// <summary>
        /// Cancels any pending scheduled fire and runs Fire() immediately.
        /// Used by the "Test now" button in the Configure dialog so the user
        /// can verify the mechanic without waiting for the random interval.
        /// </summary>
        public void FireNow()
        {
            if (!IsRunning) Start();
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            Fire();
        }

        public void Stop()
        {
            IsRunning = false;
            if (_subscribedWindow != null)
            {
                try { _subscribedWindow.Closing -= OnMainWindowClosing; } catch { }
                _subscribedWindow = null;
            }
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            ResolveActive(passed: false, fireEvent: false);
        }

        private void TrySubscribeMainWindowClosing()
        {
            if (_subscribedWindow != null) return;
            var main = System.Windows.Application.Current?.MainWindow;
            if (main == null) return;
            main.Closing += OnMainWindowClosing;
            _subscribedWindow = main;
        }

        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Force-dispose the active popup so it doesn't keep the app
            // alive past OnLastWindowClose. The popup's Owner would handle
            // this on a clean shutdown, but the backup matters when the
            // popup is mid-fade or the Owner reference somehow drifted.
            Stop();
        }

        private void ScheduleNext()
        {
            var settings = App.Settings?.Current;
            if (settings == null || !settings.AttentionCheckEnabled || !IsRunning)
            {
                _scheduleTimer?.Stop();
                _scheduleTimer = null;
                return;
            }

            // First fire uses a short interval (30-120s) so the mechanic is
            // discoverable within minutes of enabling — without this, default
            // settings (min=1, max=5 per hour) put the first check anywhere
            // up to an hour away, and users assume the feature is broken.
            TimeSpan interval;
            if (!_firstFireScheduled)
            {
                var seconds = FirstFireMinSeconds + _rng.NextDouble() * (FirstFireMaxSeconds - FirstFireMinSeconds);
                interval = TimeSpan.FromSeconds(seconds);
                _firstFireScheduled = true;
                App.Logger?.Debug("AttentionCheck: first fire scheduled in {Seconds:F0}s", seconds);
            }
            else
            {
                // Random interval drawn so the expected fires-per-hour lands
                // between min and max.
                var max = Math.Max(1, settings.AttentionCheckMaxPerSession);
                var min = Math.Max(1, Math.Min(max, settings.AttentionCheckMinPerSession));
                var minIntervalMin = 60.0 / max;
                var maxIntervalMin = 60.0 / min;
                var intervalMin = minIntervalMin + _rng.NextDouble() * (maxIntervalMin - minIntervalMin);
                interval = TimeSpan.FromMinutes(intervalMin);
                App.Logger?.Debug("AttentionCheck: next fire scheduled in {Minutes:F1}min", intervalMin);
            }

            _scheduleTimer?.Stop();
            _scheduleTimer = new DispatcherTimer { Interval = interval };
            _scheduleTimer.Tick += (_, _) =>
            {
                _scheduleTimer?.Stop();
                _scheduleTimer = null;
                Fire();
            };
            _scheduleTimer.Start();
        }

        private void Fire()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || !settings.AttentionCheckEnabled)
                {
                    App.Logger?.Debug("AttentionCheck: fire skipped — feature disabled");
                    ScheduleNext();
                    return;
                }

                // Scope gate: skip and reschedule when scope doesn't match
                // current state. Doesn't burn the slot — next fire just lands
                // at a fresh random interval.
                if (settings.AttentionCheckScope == AppSettings.AttentionCheckScopeKind.DuringSessionsOnly
                    && !App.IsSessionRunning)
                {
                    App.Logger?.Debug("AttentionCheck: fire skipped — scope=DuringSessionsOnly but no session running");
                    ScheduleNext();
                    return;
                }

                // Webcam tracking must be on for gaze events to arrive. Without
                // it, the popup auto-fails every time (no gaze → no fixation →
                // grace elapses), silently bleeding XP under XpPenalty mode.
                // The startup no-webcam sticky tells the user how to fix it;
                // here we just skip until tracking is started.
                if (App.Webcam?.IsRunning != true)
                {
                    App.Logger?.Debug("AttentionCheck: fire skipped — webcam tracking not running");
                    ScheduleNext();
                    return;
                }

                if (_activeWindow != null)
                {
                    App.Logger?.Debug("AttentionCheck: fire skipped — popup already active");
                    ScheduleNext();
                    return;
                }

                var screen = App.Webcam?.GetCalibratedScreen()
                    ?? System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) { ScheduleNext(); return; }

                // Position: random within the screen's working area, biased
                // away from the edges so the ring isn't half-clipped.
                var bounds = screen.WorkingArea;
                var margin = 120;
                var size = 84;
                var x = bounds.X + margin + _rng.Next(Math.Max(1, bounds.Width - margin * 2 - size));
                var y = bounds.Y + margin + _rng.Next(Math.Max(1, bounds.Height - margin * 2 - size));

                _activeControl = new AttentionCheckControl();
                _activeWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ResizeMode = ResizeMode.NoResize,
                    Left = x,
                    Top = y,
                    Content = _activeControl,
                    IsHitTestVisible = false,
                };
                // Owner = MainWindow so the popup closes cleanly with the
                // app (avoids the unowned-window-blocks-shutdown trap).
                var owner = System.Windows.Application.Current?.MainWindow;
                if (owner != null && owner.IsLoaded) _activeWindow.Owner = owner;
                _activeWindow.Show();

                // Hit-test bounds with a generous slack so a near-miss
                // counts as fixation — the user's gaze cursor has tracking
                // noise, and we don't want to fail honest attempts.
                _activeBounds = new Rect(
                    x - BoundsSlackDips,
                    y - BoundsSlackDips,
                    size + BoundsSlackDips * 2,
                    size + BoundsSlackDips * 2);

                _activeControl.StartPulse();
                _activeControl.SetProgress(0);

                _fireStartedAt = DateTime.UtcNow;
                _dwellMs = 0;
                _lastGazePoint = null;

                if (App.Webcam != null && !_gazeSubscribed)
                {
                    App.Webcam.OnGazeMove += HandleGazeMove;
                    _gazeSubscribed = true;
                }

                // Audible ping. SystemSounds is the lightest cue available
                // without bundling an audio asset; voice-pass can swap for
                // a themed sound at ship.
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

                _tickTimer?.Stop();
                _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
                _tickTimer.Tick += OnTick;
                _tickTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AttentionCheckService.Fire failed");
                ResolveActive(passed: false, fireEvent: false);
                ScheduleNext();
            }
        }

        private void HandleGazeMove(System.Windows.Point p)
        {
            _lastGazePoint = p;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || _activeWindow == null) { ResolveActive(passed: false); return; }

                var elapsed = (DateTime.UtcNow - _fireStartedAt).TotalMilliseconds;
                var graceMs = settings.AttentionCheckGraceMs;

                if (_lastGazePoint.HasValue && _activeBounds.Contains(_lastGazePoint.Value))
                {
                    _dwellMs += TickMs;
                    _activeControl?.SetProgress(_dwellMs / DwellTargetMs);

                    if (_dwellMs >= DwellTargetMs)
                    {
                        ResolveActive(passed: true);
                        return;
                    }
                }
                else
                {
                    // Gaze left bounds — drain dwell quickly so a brief
                    // glance doesn't bank progress that completes later.
                    _dwellMs = Math.Max(0, _dwellMs - TickMs * 2);
                    _activeControl?.SetProgress(_dwellMs / DwellTargetMs);
                }

                if (elapsed >= graceMs)
                {
                    ResolveActive(passed: false);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AttentionCheckService tick error: {Error}", ex.Message);
                ResolveActive(passed: false);
            }
        }

        private void ResolveActive(bool passed, bool fireEvent = true)
        {
            _tickTimer?.Stop();
            _tickTimer = null;

            if (_gazeSubscribed && App.Webcam != null)
            {
                App.Webcam.OnGazeMove -= HandleGazeMove;
                _gazeSubscribed = false;
            }

            if (_activeControl != null)
            {
                try { _activeControl.StopPulse(); } catch { }
                _activeControl = null;
            }

            if (_activeWindow != null)
            {
                var win = _activeWindow;
                _activeWindow = null;
                try
                {
                    // Brief fade so the dismiss isn't jarring.
                    var anim = new DoubleAnimation
                    {
                        From = win.Opacity, To = 0,
                        Duration = TimeSpan.FromMilliseconds(180),
                    };
                    anim.Completed += (_, _) => { try { win.Close(); } catch { } };
                    win.BeginAnimation(Window.OpacityProperty, anim);
                }
                catch
                {
                    try { win.Close(); } catch { }
                }
            }

            if (fireEvent)
            {
                if (passed) OnPass?.Invoke();
                else OnFail?.Invoke();
            }

            if (IsRunning) ScheduleNext();
        }
    }
}
