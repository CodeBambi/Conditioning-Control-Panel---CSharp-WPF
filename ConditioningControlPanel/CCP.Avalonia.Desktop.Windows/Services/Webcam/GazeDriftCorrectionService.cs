using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Webcam;

/// <summary>
/// Continuous implicit recalibration: keeps the gaze mapping honest as the user's posture drifts,
/// without them ever opening Quick Recal.
///
/// Principle: when a person clicks something, they are almost always looking at it. Each left-click is
/// therefore a free ground-truth sample. If the projected gaze was fixating near the click point, the
/// gap between them is calibration drift — and we fold a small fraction of it into the runtime offset
/// (the same translational nudge Quick Recal captures). Individual nudges are tiny and gated hard
/// (fixation required, residual must be small enough to plausibly be drift), so a click the user wasn't
/// looking at can't yank the calibration; repeated ordinary clicks converge it back onto truth.
///
/// Portable port of the WPF GazeDriftCorrectionService. Uses the shared <see cref="IMouseHook"/> (the
/// hook is <c>Install()</c>ed on activate but never <c>Uninstall</c>ed — it is a process-lifetime
/// singleton shared with the chaos/bubble features). Click positions are consumed in-memory for the
/// residual math and never logged or persisted — only the resulting offset (numbers) is saved.
/// </summary>
public sealed class GazeDriftCorrectionService : IGazeDriftCorrectionService, IDisposable
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

    private readonly AvaloniaWebcamTrackingService _tracker;
    private readonly IMouseHook _mouseHook;
    private readonly ISettingsService _settings;
    private readonly ILogger<GazeDriftCorrectionService>? _logger;
    private readonly Queue<(DateTime At, Point P)> _recentGaze = new();
    private readonly object _gazeLock = new();

    private bool _hookActive;
    private bool _subscribed;
    private bool _disposed;
    private DateTime _lastNudgeAt = DateTime.MinValue;
    private DateTime _lastPersistAt = DateTime.MinValue;

    // Handler delegates stored as fields so Dispose can unsubscribe them (P-8 leak fix).
    // Previously the OnTrackingStateChanged + settings PropertyChanged lambdas were
    // inline and could never be detached, pinning this service (and the tracker)
    // for the process lifetime.
    private readonly Action<WebcamTrackingState> _onTrackingStateChanged;
    private readonly INotifyPropertyChanged? _subscribedSettings;
    private PropertyChangedEventHandler? _onSettingsChanged;

    public GazeDriftCorrectionService(
        AvaloniaWebcamTrackingService tracker,
        IMouseHook mouseHook,
        ISettingsService settings,
        ILogger<GazeDriftCorrectionService>? logger = null)
    {
        _tracker = tracker;
        _mouseHook = mouseHook;
        _settings = settings;
        _logger = logger;

        _onTrackingStateChanged = _ => EnsureHookState();
        _tracker.OnTrackingStateChanged += _onTrackingStateChanged;
        if (_settings.Current != null)
        {
            _onSettingsChanged = (_, e) =>
            {
                if (e.PropertyName == "WebcamAutoDriftCorrection")
                    EnsureHookState();
            };
            // Capture the exact instance we subscribed to so Dispose detaches from
            // the right object even if SettingsService.Current is later swapped
            // (cloud restore / Reset replace the instance — see ISettingsService).
            _subscribedSettings = _settings.Current;
            _subscribedSettings.PropertyChanged += _onSettingsChanged;
        }
        _mouseHook.LeftButtonDown += OnLeftDown;
        EnsureHookState();
    }

    /// <summary>
    /// Subscribes to gaze + ensures the mouse hook is installed to match the current state
    /// (setting on + tracking running + calibration loaded). Safe to call redundantly; must run on
    /// the UI thread (hook needs a message pump).
    /// </summary>
    private void EnsureHookState()
    {
        if (_disposed) return;
        bool shouldRun = _settings.Current?.WebcamAutoDriftCorrection == true
            && _tracker is { IsRunning: true, Calibration: not null };

        if (shouldRun && !_hookActive)
        {
            try
            {
                // Install is idempotent; never Uninstall here — the hook is a shared singleton.
                _mouseHook.Install();
                _hookActive = true;
                if (!_subscribed)
                {
                    _tracker.OnGazeMove += OnGazeMove;
                    _subscribed = true;
                }
                _logger?.LogInformation("GazeDriftCorrectionService: active (click-driven drift correction)");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "GazeDriftCorrectionService: hook install failed");
            }
        }
        else if (!shouldRun && _hookActive)
        {
            if (_subscribed)
            {
                _tracker.OnGazeMove -= OnGazeMove;
                _subscribed = false;
            }
            _hookActive = false;
            lock (_gazeLock) { _recentGaze.Clear(); }
            _logger?.LogInformation("GazeDriftCorrectionService: inactive");
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

    /// <summary>Mouse-hook callback — bounce the real work to the UI thread, never swallow the click.</summary>
    private void OnLeftDown(object? sender, HookPoint e)
    {
        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
                Dispatcher.UIThread.Post(() => ProcessClick(new Point(e.X, e.Y)));
            else
                ProcessClick(new Point(e.X, e.Y));
        }
        catch { }
    }

    private void ProcessClick(Point physicalPx)
    {
        try
        {
            if (!_hookActive) return;
            var cal = _tracker.Calibration;
            if (cal == null) return;

            // Don't fight the explicit calibration flows — they own the offset while open.
            if (IsExplicitCalibrationOpen()) return;

            var now = DateTime.UtcNow;
            if ((now - _lastNudgeAt).TotalMilliseconds < NudgeCooldownMs) return;

            // Fixation check: the gaze must have been parked (not mid-saccade) when the click landed.
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

            // Click physical px → the calibrated monitor's local DIP space (the space OnGazeMove emits in:
            // local DIPs of the calibration window, which was borderless-maximized on that monitor).
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
            _tracker.SetRuntimeOffset(new RuntimeOffsetData
            {
                Dx = newDx,
                Dy = newDy,
                CapturedAt = now,
            }, persist);
            if (persist) _lastPersistAt = now;
            _lastNudgeAt = now;

            // Magnitude only — no positions in the log (privacy contract).
            _logger?.LogDebug("GazeDriftCorrectionService: drift nudge {Mag:F0} DIPs applied (persist={Persist})",
                residual * NudgeGain, persist);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("GazeDriftCorrectionService.ProcessClick: {Error}", ex.Message);
        }
    }

    /// <summary>True while a calibration / quick-recal window is open (the explicit flows own the offset then).</summary>
    private static bool IsExplicitCalibrationOpen()
    {
        try
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.Windows.Any(w => w is WebcamCalibrationWindow || w is WebcamQuickRecalWindow);
        }
        catch { }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mouseHook.LeftButtonDown -= OnLeftDown;
        _tracker.OnTrackingStateChanged -= _onTrackingStateChanged;
        if (_onSettingsChanged != null && _subscribedSettings != null)
            _subscribedSettings.PropertyChanged -= _onSettingsChanged;
        if (_subscribed)
        {
            _tracker.OnGazeMove -= OnGazeMove;
            _subscribed = false;
        }
        _hookActive = false;
    }
}
