using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// EXPERIMENTAL shared-host for chaos bubbles (gated by AppSettings.ChaosBubbleSharedHost).
///
/// Instead of one top-level layered <see cref="Window"/> per bubble — each repositioned via
/// platform window moves every frame, which saturates the UI thread and starves click input under
/// a dense field — every bubble's visual lives as a child of this ONE full-virtual-screen
/// Canvas, positioned with <see cref="Canvas"/>.SetLeft/Top (cheap, batched in one render pass).
///
/// The window is fully CLICK-THROUGH: empty space passes clicks to the desktop, and pops are
/// detected by the global input hook (future BubbleService port) which swallows a hit. No Avalonia
/// hit-testing happens here.
///
/// Keep-alive contract like every chaos overlay: created once at run start, closed only at teardown
/// — layered-window churn deadlocks the render thread. All Add/Remove/Place calls run on the UI
/// thread (spawn/animate/destroy already do).
/// </summary>
public sealed class ChaosBubbleHostOverlay : Window
{
    private static ChaosBubbleHostOverlay? _active;
    private static int _refCount;
    private readonly Canvas _canvas;
    private readonly ILogger<ChaosBubbleHostOverlay>? _logger;

    public static bool IsReady => _active != null;

    /// <summary>The host's render scale (DPI/96 of the monitor Avalonia composed it for). Bubbles on
    /// a screen with a different scale can compensate with a transform of bubbleScale/RenderScale.
    /// Mirrors WPF ChaosBubbleHostOverlay.RenderScale.</summary>
    public static double RenderScale
    {
        get
        {
            double s = _active?.RenderScaling ?? 1.0;
            return s > 0 ? s : 1.0;
        }
    }

    private ChaosBubbleHostOverlay()
    {
        _logger = App.Services?.GetRequiredService<ILogger<ChaosBubbleHostOverlay>>();

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

    /// <summary>Take a reference on the host, creating + showing it if this is the first owner. Each
    /// call must be balanced by exactly one <see cref="CloseActive"/>; the window only dies on the
    /// last release (WPF parity — a chaos run ending must not close a host the ambient game holds).</summary>
    public static void EnsureCreated()
    {
        System.Threading.Interlocked.Increment(ref _refCount);
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
            _active = new ChaosBubbleHostOverlay();
            _active.Show();
            AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
        }
        catch (Exception ex)
        {
            // swallow; diagnostics must never break a run
        }
    }

    /// <summary>Add a bubble visual to the host (UI thread). No-op if the host isn't up.</summary>
    public static void Add(Control el)
    {
        try
        {
            if (_active != null && el != null && !_active._canvas.Children.Contains(el))
                _active._canvas.Children.Add(el);
        }
        catch { }
    }

    /// <summary>Remove a bubble visual from the host (UI thread).</summary>
    public static void Remove(Control el)
    {
        try { if (_active != null && el != null) _active._canvas.Children.Remove(el); }
        catch { }
    }

    /// <summary>Position a bubble visual. Coordinates are PHYSICAL virtual-desktop px (the
    /// cross-screen-safe currency the mouse-hook hit discs live in); the host converts into its own
    /// canvas-local DIPs via its physical-px origin (<see cref="Window.Position"/>) + render scale.
    /// Mirrors WPF ChaosBubbleHostOverlay.Place. UI thread only.</summary>
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

    /// <summary>Release one reference. The window is torn down (run end / shutdown) only when the
    /// LAST owner releases — a chaos run ending must not close a host the ambient game still holds
    /// (WPF parity). UI thread marshalled.</summary>
    public static void CloseActive()
    {
        int n = System.Threading.Interlocked.Decrement(ref _refCount);
        if (n > 0) return;                                                              // another owner still needs it
        if (n < 0) { System.Threading.Interlocked.Exchange(ref _refCount, 0); return; } // unbalanced — clamp
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
