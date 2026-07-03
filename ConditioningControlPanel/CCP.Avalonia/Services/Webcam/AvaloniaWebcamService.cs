using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Webcam;

namespace ConditioningControlPanel.Avalonia.Services.Webcam;

/// <summary>
/// Avalonia stub for <see cref="IWebcamService"/>.
/// Tracks run state and logs calls so the Lab/Webcam UI can exercise the seam
/// while the real OpenCV/blazeface tracker engine is being ported from WPF.
///
/// Every member that would require a real camera / calibration is implemented
/// honestly as a no-op or empty/false result — there is deliberately no fake
/// success path. Real implementations live in the per-OS desktop heads.
/// </summary>
public sealed class AvaloniaWebcamService : IWebcamService
{
    private readonly ILogger<AvaloniaWebcamService>? _logger;
    private bool _isRunning;

    public AvaloniaWebcamService(ILogger<AvaloniaWebcamService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Always false on the stub — no real calibration is ever produced without
    /// the tracker engine. Callers that gate on this fall through to their
    /// honest "not calibrated" path.
    /// </summary>
    public bool HasCalibration => false;

    public event Action? OnBlink;
    public event Action<Point>? OnLongStare;
    public event Action? OnMouthOpen;
    public event Action? OnTongueOut;
    public event Action<Point>? OnGazeMove;
    public event Action<GazeSide>? OnGazeSide;
    public event Action? OnFaceLost;
    public event Action? OnFaceFound;
    public event Action<WebcamTrackingState>? OnTrackingStateChanged;
    public event Action<double, string>? OnStartupProgress;
    public event Action<double, double>? OnHeadPose;
    public event Action<double, double>? OnRawIris;

    public void StartTracking()
    {
        _isRunning = true;
        _logger?.LogInformation("AvaloniaWebcamService: tracking started (stub)");
    }

    public void StopTracking()
    {
        _isRunning = false;
        _logger?.LogInformation("AvaloniaWebcamService: tracking stopped (stub)");
    }

    public void Calibrate()
    {
        _logger?.LogInformation("AvaloniaWebcamService: calibration requested (stub)");
    }

    public void TestTracker()
    {
        _logger?.LogInformation("AvaloniaWebcamService: tracker test requested (stub)");
    }

    public void RefreshDevices()
    {
        _logger?.LogInformation("AvaloniaWebcamService: device list refreshed (stub)");
    }

    /// <summary>
    /// Empty list — the stub cannot enumerate real capture devices. The UI
    /// surfaces a "(no cameras detected)" entry rather than a fabricated list.
    /// </summary>
    public IReadOnlyList<WebcamDeviceInfo> EnumerateDevices() => Array.Empty<WebcamDeviceInfo>();

    public void RevokeConsent()
    {
        _isRunning = false;
        _logger?.LogInformation("AvaloniaWebcamService: consent revoked (stub)");
    }

    /// <summary>Always null — no calibration exists on the stub.</summary>
    public RuntimeGazeOffset? GetRuntimeOffset() => null;

    /// <summary>No-op without a live calibration. Never fakes a successful nudge.</summary>
    public void SetRuntimeOffset(double dx, double dy, bool persist)
    {
        _logger?.LogInformation("AvaloniaWebcamService: SetRuntimeOffset ignored (no calibration, stub)");
    }

    /// <summary>No-op without a live calibration.</summary>
    public void ClearRuntimeOffset(bool persist)
    {
        _logger?.LogInformation("AvaloniaWebcamService: ClearRuntimeOffset ignored (no calibration, stub)");
    }
}
