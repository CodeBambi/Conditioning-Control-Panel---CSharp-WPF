using System;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.TitleProviderBackends;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Linux implementation of <see cref="IForegroundWindowTitleProvider"/>. Selects a backend
/// once at construction (linux-foreground-title-contract.md §2.1) and delegates each poll
/// to it. This is the Linux equivalent of the Windows head's
/// <c>WindowsForegroundWindowTitleProvider</c> (Win32 <c>GetForegroundWindow</c> +
/// <c>GetWindowText</c>).
/// </summary>
/// <remarks>
/// <para><b>Inert until Start() (privacy, §1.3):</b> the backend may open an X display at
/// construction (setup, not polling), but <see cref="GetForegroundWindowTitle"/> is only
/// ever CALLED by the awareness engine inside its poll tick, which runs only after the
/// engine's <c>AwarenessModeEnabled</c> + <c>AwarenessConsentGiven</c> gate has passed in
/// <c>AwarenessService.Start()</c>. No foreground title is read before the user has
/// consented and enabled the feature.</para>
///
/// <para><b>Privacy contract (§1.3):</b> the returned title is memory-only input for
/// activity classification — never persisted, never logged, never sent over the network.
/// This provider performs no logging of title content; the backend's log lines carry backend
/// names and probe reasons only. The seam returns the TITLE string only (no PID, no
/// process name), matching the Windows title-only contract.</para>
///
/// <para><b>Never-throw:</b> <see cref="GetForegroundWindowTitle"/> swallows backend
/// exceptions and returns <c>null</c> — it is polled every 1.5s from a threadpool thread and
/// must never crash the awareness timer. The provider is <see cref="IDisposable"/> because
/// the selected backend may own a dedicated X display connection (§3.1); the DI container
/// disposes singletons that implement <see cref="IDisposable"/> at shutdown.</para>
/// </remarks>
public sealed class LinuxForegroundWindowTitleProvider : IForegroundWindowTitleProvider, IDisposable
{
    private readonly ILinuxTitleProviderBackend _backend;
    private bool _disposed;

    /// <summary>
    /// DI constructor: selects a backend for the current system using
    /// <see cref="LinuxTitleProviderBackendSelector"/>. The <paramref name="loggerFactory"/>
    /// is optional (null on heads without a logging configuration — logging degrades to a
    /// no-op and functionality is unaffected).
    /// </summary>
    public LinuxForegroundWindowTitleProvider(ILoggerFactory? loggerFactory = null)
        : this(new LinuxTitleProviderBackendSelector(loggerFactory).SelectBackend())
    {
    }

    /// <summary>
    /// Composition constructor with a pre-selected backend (for tests / explicit wiring).
    /// </summary>
    internal LinuxForegroundWindowTitleProvider(ILinuxTitleProviderBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>
    /// Title of the current foreground window, or null/empty when unavailable. Never throws.
    /// </summary>
    public string? GetForegroundWindowTitle()
    {
        if (_disposed) return null;

        try
        {
            return _backend.GetForegroundWindowTitle();
        }
        catch (Exception)
        {
            // Never let a backend exception kill the awareness poll thread.
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _backend.Dispose();
        }
        catch (Exception)
        {
            // Best-effort teardown — never throw from Dispose.
        }
    }
}
