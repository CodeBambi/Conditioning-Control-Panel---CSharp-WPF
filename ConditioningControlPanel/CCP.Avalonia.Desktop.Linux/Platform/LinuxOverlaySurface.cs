using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Linux IOverlaySurface implementation that delegates to the best available backend.
/// </summary>
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

    public bool IsVisible => _backend.IsVisible;

    public void Show() => _backend.Show();

    public void Hide() => _backend.Hide();

    public void Close() => _backend.Close();

    public void SetClickThrough(bool enabled)
    {
        _backend.SetClickThrough(enabled);
    }

    public void SetBounds(PixelRect rect)
    {
        _backend.SetBounds(rect);
    }

    /// <summary>
    /// Updates the per-region input capture mask. Clicks inside the capture regions are absorbed;
    /// clicks outside pass through. Pass an empty list for full click-through.
    /// </summary>
    public void SetInputCaptureRegions(IReadOnlyList<PixelRect> captureRegions)
    {
        _backend.SetInputCaptureRegions(captureRegions);
    }
}
