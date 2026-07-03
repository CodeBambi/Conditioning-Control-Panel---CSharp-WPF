using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// EXPERIMENTAL shared-host for chaos DVD bouncing-text logos (gated by
/// AppSettings.ChaosDvdSharedHost).
///
/// Instead of one top-level layered <see cref="Window"/> per logo — each repositioned via platform
/// window moves every frame, which saturates the UI thread and freezes the companion avatar under a
/// split — every logo's visual lives as a child of this ONE full-virtual-screen Canvas, positioned
/// with <see cref="Canvas"/>.SetLeft/Top (batched in one render pass).
///
/// Clickable "spanker" logos keep their own per-window path so hit-testing still works; this host
/// is for the non-clickable logo swarm only.
///
/// Keep-alive contract like every chaos overlay: created once at run start, closed only at teardown.
/// All Add/Remove/Place calls run on the UI thread.
/// </summary>
public sealed class ChaosDvdHostOverlay : Window
{
    private static ChaosDvdHostOverlay? _active;
    private readonly Canvas _canvas;
    private readonly ILogger<ChaosDvdHostOverlay>? _logger;

    public static bool IsReady => _active != null;

    private ChaosDvdHostOverlay()
    {
        _logger = App.Services?.GetRequiredService<ILogger<ChaosDvdHostOverlay>>();

        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var (sl, st, sw, sh) = AvaloniaChaosWindowZ.StageBoundsDip();
        Position = new PixelPoint((int)sl, (int)st);
        Width = sw;   // DIP (StageBoundsDip already divided the physical span by scaling)
        Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        Opened += (_, _) => ApplyExStyles();
    }

    /// <summary>Create + show the host at run start (shared-host mode only).</summary>
    public static void EnsureCreated()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                TryCreate();
            else
                Dispatcher.UIThread.Post(TryCreate);
        }
        catch { }
    }

    private static void TryCreate()
    {
        try
        {
            if (_active != null) return;
            _active = new ChaosDvdHostOverlay();
            _active.Show();
            AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
        }
        catch { }
    }

    /// <summary>Add a logo visual to the host (UI thread). No-op if the host isn't up.</summary>
    public static void Add(Control el)
    {
        try
        {
            if (_active != null && el != null && !_active._canvas.Children.Contains(el))
                _active._canvas.Children.Add(el);
        }
        catch { }
    }

    /// <summary>Remove a logo visual from the host (UI thread).</summary>
    public static void Remove(Control el)
    {
        try { if (_active != null && el != null) _active._canvas.Children.Remove(el); }
        catch { }
    }

    /// <summary>Position a logo visual. Coordinates are PHYSICAL virtual-desktop px; the host
    /// converts into its own canvas-local DIPs via its physical-px origin
    /// (<see cref="Window.Position"/>) + render scale. UI thread only.</summary>
    public static void Place(Control el, double xPx, double yPx)
    {
        var w = _active;
        if (w == null || el == null) return;
        double scale = w.RenderScaling <= 0 ? 1.0 : w.RenderScaling;
        Canvas.SetLeft(el, (xPx - w.Position.X) / scale);
        Canvas.SetTop(el, (yPx - w.Position.Y) / scale);
    }

    /// <summary>Re-stack the live host above a mandatory video. UI thread only.</summary>
    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Instant teardown (run end / shutdown) — the only place this window dies.</summary>
    public static void CloseActive()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                TryClose();
            else
                Dispatcher.UIThread.Post(TryClose);
        }
        catch { }
    }

    private static void TryClose()
    {
        try
        {
            var w = _active;
            _active = null;
            if (w != null)
            {
                w._canvas.Children.Clear();
                w.Close();
            }
        }
        catch { }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
