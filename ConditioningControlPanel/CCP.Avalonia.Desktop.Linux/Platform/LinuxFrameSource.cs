using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.FrameSourceBackends;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Linux <see cref="IFrameSource"/> implementation. Holds the backend selected by
/// <see cref="LinuxFrameSourceBackendSelector"/> and delegates every capture to it. Slices
/// A+B select X11 XGetImage (native X11) or the black-frame fallback (Wayland/XWayland/
/// unknown); the MIT-SHM fast path (Slice C / wave 2) and the native Wayland backends
/// (slices D-F) slot in behind the same <see cref="ILinuxFrameSourceBackend"/> seam.
/// </summary>
/// <remarks>
/// <para><b>Privacy hard-line (contract §1.4):</b> this class adds no persistence of its own
/// — frames flow straight from backend to consumer and are memory-only. The only artifact
/// any backend may persist is the portal restore token (slices D-F), via
/// <c>ISecretStore</c>, which holds no image data.</para>
/// <para><b>Lifetime:</b> capture is strictly pull-based. The source performs no work unless a
/// consumer calls <see cref="CaptureAsync"/>; the consuming feature's start/stop governs all
/// activity (no idle/background capture).</para>
/// <para><b>Disposal:</b> implements <see cref="IDisposable"/> so the Microsoft DI container
/// disposes the backend's native display connection on shutdown (the resolved instance type —
/// not the <c>IFrameSource</c> service type — determines disposal).</para>
/// </remarks>
public sealed class LinuxFrameSource : IFrameSource, IDisposable
{
    private readonly ILinuxFrameSourceBackend _backend;
    private readonly ILogger? _logger;
    private bool _disposed;

    public LinuxFrameSource(ILinuxFrameSourceBackend backend, ILogger<LinuxFrameSource>? logger = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _logger = logger;
        _logger?.LogInformation(
            "LinuxFrameSource active with backend {BackendName} (available: {Available})",
            backend.Name, backend.IsAvailable);
    }

    /// <summary>
    /// Strict delegation — the backend enforces the never-throw/black-frame contract
    /// (contract §1.4 / §5). Never logs frame content.
    /// </summary>
    public Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default)
    {
        return _backend.CaptureAsync(screen, cancellationToken);
    }

    /// <summary>Disposes the selected backend (closes any native display connection).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _backend.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LinuxFrameSource: backend disposal faulted");
        }
    }
}
