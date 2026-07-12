using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.TitleProviderBackends;

/// <summary>
/// Last-resort foreground-title backend: returns <c>null</c> so the awareness engine runs
/// and classifies activity as <c>Unknown</c> (no reactions fire). Pure managed code — ZERO
/// P/Invokes, always constructs (linux-foreground-title-contract.md §5.1). The terminal
/// arm of every backend-selection path (no Wayland backends in this wave, no X display,
/// unrecognized session, selector fault).
/// </summary>
/// <remarks>
/// <para><b>Privacy contract (§1.3):</b> the <paramref name="reason"/> string names the
/// backend/probe outcome ONLY — it MUST NEVER contain window-title content. It is logged
/// exactly once so a user scanning logs can see why awareness reports Unknown on their
/// desktop, without any title data reaching the log.</para>
/// </remarks>
internal sealed class FallbackTitleBackend : ILinuxTitleProviderBackend
{
    private readonly string _reason;
    private readonly ILogger? _logger;
    private int _unavailabilityLogged;

    public FallbackTitleBackend(string reason, ILogger? logger = null)
    {
        _reason = reason;
        _logger = logger;
    }

    public string Name => "FallbackTitleBackend";

    public string? GetForegroundWindowTitle()
    {
        if (Interlocked.Exchange(ref _unavailabilityLogged, 1) == 0)
        {
            // §1.3 + §5.1: one informational line carrying the REASON only (never title
            // content). Awareness then classifies activity as Unknown on this desktop.
            _logger?.LogInformation(
                "Foreground title detection unavailable: {Reason}. " +
                "Awareness will classify activity as Unknown on this desktop.",
                _reason);
        }

        return null;
    }

    public void Dispose()
    {
        // Pure managed — nothing to release. Implementing IDisposable satisfies the
        // ILinuxTitleProviderBackend contract so the provider can dispose backends uniformly.
    }
}
