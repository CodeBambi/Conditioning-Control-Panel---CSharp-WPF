using System;
using System.Collections.Generic;
using System.Windows;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Continuous implicit recalibration: keeps the gaze mapping honest as the
/// user's posture drifts, without them ever opening Quick Recal.
///
/// Principle: when a person clicks something, they are almost always looking
/// at it. Each left-click is therefore a free ground-truth sample. If the
/// projected gaze was fixating near the click point, the gap between them is
/// calibration drift — and we fold a small fraction of it into the runtime
/// offset (the same translational nudge Quick Recal captures). Individual
/// nudges are tiny and gated hard (fixation required, residual must be small
/// enough to plausibly be drift), so a click the user wasn't looking at can't
/// yank the calibration; repeated ordinary clicks converge it back onto truth
/// within a handful of interactions.
///
/// Scope and privacy: the low-level mouse hook is installed ONLY while webcam
/// tracking is running with a calibration loaded and the
/// WebcamAutoDriftCorrection setting is on. Click positions are consumed
/// in-memory for the residual math and never logged or persisted — only the
/// resulting offset (numbers, same as Quick Recal) is saved.
/// </summary>
public class GazeDriftCorrectionService : IDisposable
{
    private const double MaxResidualDips = 220;    // farther than this from gaze → user wasn't looking at the click
    private const double MinResidualDips = 15;     // smaller than this → nothing worth fixing
    private const double NudgeGain = 0.15;         // fraction of the residual folded in per click
    private const double MaxTotalOffsetDips = 300; // cumulative runtime-offset clamp per axis
    private const int GazeFreshMs = 300;           // newest gaze sample must be this recent
    private const int FixationWindowMs = 350;      // gaze must have been stable over this window…
    private const double FixationMaxSpreadDips = 90; // …within this spread, else user was mid-saccade
    private const int MinFixationSamples = 5;
    private const int NudgeCooldownMs = 500;
    private const int PersistThrottleMs = 30000;   // batch disk writes; in-memory offset applies instantly

    private readonly GlobalMouseHook _hook = new();
    private readonly Queue<(DateTime At, Point P)> _recentGaze = new();
    private readonly object _gazeLock = new();

    private bool _hookActive;
    private bool _subscribed;
    private bool _disposed;
    private DateTime _lastNudgeAt = DateTime.MinValue;
    private DateTime _lastPersistAt = DateTime.MinValue;

    public GazeDriftCorrectionService()
    {
        if (App.Webcam != null)
        {
            App.Webcam.OnTrackingStateChanged += _ => EnsureHookState();
        }
        var settings = App.Settings?.Current;
        if (settings != null)
        {
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Models.AppSettings.WebcamAutoDriftCorrection))
                    EnsureHookState();
            };
        }
        _hook.LeftDown = OnLeftDown;
        EnsureHookState();

        if (Application.Current != null)
            Application.Current.Exit += (_, _) => Dispose();
    }

    /// <summary>
    /// Installs/removes the mouse hook and gaze subscription to match the
    /// current state: enabled setting + tracking running + calibration loaded.
    /// Safe to call redundantly; must run on the UI thread (hook needs a
    /// message pump).
    /// </summary>
    private void EnsureHookState()
    {
        if (_disposed) return;
        bool shouldRun = App.Settings?.Current?.WebcamAutoDriftCorrection == true
            && App.Webcam is { IsRunning: true, Calibration: not null };

        if (shouldRun && !_hookActive)
        {
            try
            {
                _hook.Start();
                _hookActive = true;
                if (!_subscribed && App.Webcam != null)
                {
                    App.Webcam.OnGazeMove += OnGazeMove;
                    _subscribed = true;
                }
                App.Logger?.Information("GazeDriftCorrectionService: active (click-driven drift correction)");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazeDriftCorrectionService: hook start failed");
            }
        }
        else if (!shouldRun && _hookActive)
        {
            try { _hook.Stop(); } catch { }
            _hookActive = false;
            if (_subscribed && App.Webcam != null)
            {
                App.Webcam.OnGazeMove -= OnGazeMove;
                _subscribed = false;
            }
            lock (_gazeLock) { _recentGaze.Clear(); }
            App.Logger?.Information("GazeDriftCorrectionService: inactive");
        }
    }

    private void OnGazeMove(Point p)
    {
        var now = DateTime.UtcNow;
        lock (_gazeLock)
        {
            _recentGaze.Enqueue((now, p));
            while (_recentGaze.Count > 0 && (now - _recentGaze.Peek().At).TotalMilliseconds > FixationWindowMs * 2)
                _recentGaze.Dequeue();
        }
    }

    /// <summary>
    /// Hook-thread callback — must stay cheap. Snapshot the click point,
    /// bounce the real work to the dispatcher, never swallow the click.
    /// </summary>
    private bool OnLeftDown(Point physicalPx)
    {
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
                dispatcher.BeginInvoke(() => ProcessClick(physicalPx));
        }
        catch { }
        return false;
    }

    private void ProcessClick(Point physicalPx)
    {
        try
        {
            if (!_hookActive) return;
            var webcam = App.Webcam;
            var cal = webcam?.Calibration;
            if (webcam == null || cal == null) return;

            // Don't fight the explicit calibration flows — they own the
            // offset while open.
            if (WebcamCalibrationWindow.IsShowing) return;
            foreach (Window w in Application.Current.Windows)
            {
                if (w is WebcamQuickRecalWindow) return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastNudgeAt).TotalMilliseconds < NudgeCooldownMs) return;

            // Fixation check: the gaze must have been parked (not mid-saccade)
            // when the click landed, else "gaze at click time" is meaningless.
            List<(DateTime At, Point P)> snapshot;
            lock (_gazeLock) { snapshot = new List<(DateTime, Point)>(_recentGaze); }
            if (snapshot.Count < MinFixationSamples) return;
            if ((now - snapshot[^1].At).TotalMilliseconds > GazeFreshMs) return;

            var window = snapshot.FindAll(s => (now - s.At).TotalMilliseconds <= FixationWindowMs);
            if (window.Count < MinFixationSamples) return;

            double sx = 0, sy = 0;
            foreach (var s in window) { sx += s.P.X; sy += s.P.Y; }
            double gx = sx / window.Count, gy = sy / window.Count;
            foreach (var s in window)
            {
                double dx0 = s.P.X - gx, dy0 = s.P.Y - gy;
                if (Math.Sqrt(dx0 * dx0 + dy0 * dy0) > FixationMaxSpreadDips) return;
            }

            // Click physical px → the calibrated monitor's local DIP space
            // (the space OnGazeMove emits in: window-local DIPs of the
            // calibration window, which was borderless-maximized on that
            // monitor).
            var bounds = cal.MonitorBounds;
            double dpi = bounds?.DpiScale is > 0.25 and < 8.0 ? bounds.DpiScale : 1.0;
            double originX = bounds?.DeviceName != null ? bounds.X : 0;
            double originY = bounds?.DeviceName != null ? bounds.Y : 0;
            double clickX = (physicalPx.X - originX) / dpi;
            double clickY = (physicalPx.Y - originY) / dpi;

            // Ignore clicks off the calibrated monitor entirely.
            if (bounds != null
                && (clickX < 0 || clickY < 0 || clickX > bounds.Width || clickY > bounds.Height)) return;

            double rx = clickX - gx;
            double ry = clickY - gy;
            double residual = Math.Sqrt(rx * rx + ry * ry);
            if (residual < MinResidualDips || residual > MaxResidualDips) return;

            var prev = cal.RuntimeOffset;
            double newDx = Math.Clamp((prev?.Dx ?? 0) + rx * NudgeGain, -MaxTotalOffsetDips, MaxTotalOffsetDips);
            double newDy = Math.Clamp((prev?.Dy ?? 0) + ry * NudgeGain, -MaxTotalOffsetDips, MaxTotalOffsetDips);

            bool persist = (now - _lastPersistAt).TotalMilliseconds >= PersistThrottleMs;
            webcam.SetRuntimeOffset(new RuntimeOffsetData
            {
                Dx = newDx,
                Dy = newDy,
                CapturedAt = now,
            }, persist);
            if (persist) _lastPersistAt = now;
            _lastNudgeAt = now;

            // Magnitude only — no positions in the log (privacy contract).
            App.Logger?.Debug("GazeDriftCorrectionService: drift nudge {Mag:F0} DIPs applied (persist={Persist})",
                residual * NudgeGain, persist);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeDriftCorrectionService.ProcessClick: {Error}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _hook.Stop(); } catch { }
        _hook.Dispose();
        if (_subscribed && App.Webcam != null)
        {
            App.Webcam.OnGazeMove -= OnGazeMove;
            _subscribed = false;
        }
        _hookActive = false;
    }
}
