using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Linux IOverlaySurface implementation that delegates to the selected backend.
/// </summary>
/// <remarks>
/// Never-throw seam (linux-overlay-contract.md §2.3): even if the backend faults (e.g. no
/// display server at all in headless CI), overlay operations degrade to logged no-ops —
/// <c>Show()</c> must not throw. <c>Close()</c> disposes the backend (contract §6.0 item 5:
/// backends own a dedicated X display connection).
/// </remarks>
public sealed class LinuxOverlaySurface : IOverlaySurface
{
    private readonly ILinuxOverlayBackend _backend;
    private readonly ILogger<LinuxOverlaySurface>? _logger;

    public LinuxOverlaySurface(ILinuxOverlayBackend backend, ILogger<LinuxOverlaySurface>? logger = null)
    {
        _backend = backend;
        _logger = logger;
        _logger?.LogInformation(
            "LinuxOverlaySurface initialized with backend: {Backend} (Topmost: {Topmost}, PerRegion: {PerRegion})",
            _backend.Name, _backend.SupportsTopmost, _backend.SupportsPerRegionInputShape);
    }

    public bool IsVisible
    {
        get
        {
            try
            {
                return _backend.IsVisible;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "LinuxOverlaySurface: IsVisible faulted");
                return false;
            }
        }
    }

    public void Show() => Guard(() => _backend.Show(), nameof(Show));

    public void Hide() => Guard(() => _backend.Hide(), nameof(Hide));

    public void Close() => Guard(() => _backend.Dispose(), nameof(Close)); // Dispose closes + releases the X display

    public void SetClickThrough(bool enabled) =>
        Guard(() => _backend.SetClickThrough(enabled), nameof(SetClickThrough));

    public void SetBounds(PixelRect rect) => Guard(() => _backend.SetBounds(rect), nameof(SetBounds));

    /// <summary>
    /// Updates the per-region input capture mask. Clicks inside the capture regions are absorbed;
    /// clicks outside pass through. Pass an empty list for full click-through.
    /// </summary>
    public void SetInputCaptureRegions(IReadOnlyList<PixelRect> captureRegions) =>
        Guard(() => _backend.SetInputCaptureRegions(captureRegions), nameof(SetInputCaptureRegions));

    private void Guard(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "LinuxOverlaySurface: {Operation} faulted on backend {Backend}; degrading to no-op",
                operation, _backend.Name);
        }
    }
}
