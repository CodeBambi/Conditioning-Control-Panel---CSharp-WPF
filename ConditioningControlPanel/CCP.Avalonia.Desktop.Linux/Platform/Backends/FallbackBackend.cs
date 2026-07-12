using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using CorePixelRect = ConditioningControlPanel.Core.Platform.PixelRect;
using ILinuxOverlayBackend = ConditioningControlPanel.Core.Platform.ILinuxOverlayBackend;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Backends;

/// <summary>
/// Last-resort overlay backend. Pure Avalonia (ZERO P/Invokes — always constructs), used
/// when no X display is reachable or the X11 probe failed.
/// </summary>
/// <remarks>
/// <para><b>Never-trap rule (linux-overlay-contract.md §1.4, normative):</b> this backend
/// cannot honor <c>SetClickThrough(true)</c>, so it MUST NOT display the surface while
/// ambient mode is requested — an invisible, transparent, topmost window that captures all
/// input locks the user out of their desktop. When ambient click-through is requested the
/// surface is hidden/refused with one logged reason. Full-capture mode
/// (<c>SetClickThrough(false)</c> — mandatory video, lock card) MAY show: capturing input
/// is then the intended behavior. The draft's "user must close the overlay" degrade is
/// rejected by the contract.</para>
///
/// <para>Capability flags are honest (contract §1.1): no per-region input shape, and
/// <see cref="SupportsTopmost"/> is false — Avalonia's <c>Topmost</c> is set best-effort,
/// but with no display-server-level mechanism behind it this backend must not claim a
/// guaranteed capability it cannot deliver.</para>
/// </remarks>
public sealed class FallbackBackend : ILinuxOverlayBackend
{
    private readonly string _fallbackReason;
    private readonly ILogger? _logger;
    private Window? _window;
    private bool _clickThroughRequested;
    private bool _showRequested;
    private bool _refusalLogged;
    private bool _disposed;

    public FallbackBackend(string fallbackReason, ILogger<FallbackBackend>? logger = null)
    {
        _fallbackReason = fallbackReason;
        _logger = logger;
    }

    public string Name => "FallbackBackend";
    public bool IsAvailable => true; // Always available — zero P/Invokes, always constructs
    public bool SupportsPerRegionInputShape => false;
    public bool SupportsTopmost => false; // Best-effort Avalonia Topmost only — do not overstate

    public bool IsVisible => _window?.IsVisible ?? false;

    public void Show()
    {
        _showRequested = true;

        if (_clickThroughRequested)
        {
            // §1.4: ambient mode requested but click-through is impossible here.
            // Refuse to show instead of trapping input behind an invisible wall.
            LogRefusalOnce();
            _window?.Hide();
            return;
        }

        EnsureWindow();
        _window!.Show();
        _logger?.LogDebug("FallbackBackend: Show (full-capture mode; reason: {Reason})", _fallbackReason);
    }

    public void Hide()
    {
        _showRequested = false;
        _window?.Hide();
        _logger?.LogDebug("FallbackBackend: Hide");
    }

    public void Close()
    {
        _showRequested = false;
        _window?.Close();
        _window = null;
        _logger?.LogDebug("FallbackBackend: Close");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThroughRequested = enabled;

        if (enabled)
        {
            // §1.4: cannot honor ambient click-through → hide the surface now if visible.
            if (_window is { IsVisible: true })
            {
                _window.Hide();
            }
            LogRefusalOnce();
        }
        else
        {
            // Full-capture mode is honest here (capturing is intended). Re-show if the
            // caller still wants the surface visible.
            _refusalLogged = false;
            if (_showRequested)
            {
                EnsureWindow();
                _window!.Show();
            }
        }
    }

    public void SetBounds(CorePixelRect rect)
    {
        EnsureWindow();
        _window!.Position = new PixelPoint((int)rect.X, (int)rect.Y);
        _window!.Width = rect.Width;
        _window!.Height = rect.Height;
    }

    public void SetInputCaptureRegions(IReadOnlyList<CorePixelRect> captureRegions)
    {
        // Per-region input shaping is not supported (honest capability flag). Regions only
        // matter in ambient mode, which this backend refuses to display anyway (§1.4).
        _logger?.LogTrace(
            "FallbackBackend: SetInputCaptureRegions({Count} regions) - no-op (per-region unavailable)",
            captureRegions.Count);
    }

    private void LogRefusalOnce()
    {
        if (_refusalLogged) return;
        _refusalLogged = true;
        _logger?.LogWarning(
            "FallbackBackend: ambient click-through requested but unavailable — overlay surface hidden " +
            "(never-trap rule, linux-overlay-contract.md §1.4). Reason: {Reason}",
            _fallbackReason);
    }

    private void EnsureWindow()
    {
        if (_window != null) return;

        _window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = false,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false, // Note: this only affects Avalonia hit-testing, not OS-level
            Title = "CCP Overlay (Fallback)"
        };

        _logger?.LogInformation(
            "FallbackBackend: Created fallback window (reason: {Reason}). " +
            "Full-capture overlays only; ambient click-through mode hides the surface (§1.4).",
            _fallbackReason);
    }
}
