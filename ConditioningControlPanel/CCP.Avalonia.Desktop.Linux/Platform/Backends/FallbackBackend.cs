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
/// Last-resort overlay backend (Tier 5). Creates a basic always-on-top window with
/// no click-through support. Guarantees the overlay is visible but all clicks are captured.
/// </summary>
/// <remarks>
/// This backend is used when:
/// - Session type is unknown (neither X11 nor Wayland detected)
/// - X11 session but XFixes extension unavailable
/// - Wayland session but no layer-shell or input region support (GNOME with security restrictions)
/// 
/// Documented degrade: SetClickThrough is a no-op. All input is captured by the overlay.
/// User must close the overlay to interact with the desktop.
/// </remarks>
public sealed class FallbackBackend : ILinuxOverlayBackend
{
    private readonly string _fallbackReason;
    private readonly ILogger? _logger;
    private Window? _window;

    public FallbackBackend(string fallbackReason, ILogger<FallbackBackend>? logger = null)
    {
        _fallbackReason = fallbackReason;
        _logger = logger;
    }

    public string Name => "FallbackBackend";
    public bool IsAvailable => true; // Always available
    public bool SupportsPerRegionInputShape => false;
    public bool SupportsTopmost => true; // Best-effort via Avalonia Topmost property

    public bool IsVisible => _window?.IsVisible ?? false;

    public void Show()
    {
        EnsureWindow();
        _window!.Show();
        _logger?.LogDebug("FallbackBackend: Show (reason: {Reason})", _fallbackReason);
    }

    public void Hide()
    {
        _window?.Hide();
        _logger?.LogDebug("FallbackBackend: Hide");
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
        _logger?.LogDebug("FallbackBackend: Close");
    }

    public void SetClickThrough(bool enabled)
    {
        // No-op: click-through not supported in fallback mode
        // This is a documented degrade - all clicks are captured by the overlay
        _logger?.LogTrace(
            "FallbackBackend: SetClickThrough({Enabled}) - no-op (click-through unavailable in fallback mode)",
            enabled);
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
        // No-op: per-region input shaping not supported
        // Entire window captures all input
        _logger?.LogTrace(
            "FallbackBackend: SetInputCaptureRegions({Count} regions) - no-op (per-region unavailable)",
            captureRegions.Count);
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
            "Click-through is NOT available - all input will be captured by the overlay.",
            _fallbackReason);
    }
}
