using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R3 "roomwarp" - the room itself stops sitting straight. The whole design canvas takes a half-degree
/// lean and a four-pixel sag, a crimson wash deepens over everything as the ladder climbs, and every
/// escape attempt costs the window one pixel of width and height and shifts it two pixels: the room
/// closes in on you, slowly, in units too small to be sure about until you look at the edges.
///
/// <para><b>Why an effect and not a service.</b> It behaves like one - it lives until reassembly, it
/// listens to the lockdown's own events, only one may ever be live - but the director already owns
/// exactly that lifecycle (pick, hold, undo in reverse, sync UndoAll on crash), and a second thing with
/// its own attach/detach story would need its own reason to exist. It takes no target: the target is
/// the room.</para>
///
/// <para><b>Undo is measured, not guessed.</b> The lean rides a TransformLease (which restores the
/// PRIOR transform object, not a fresh identity), and the window's own metrics are recorded once, in
/// full - value plus "was that even a local value" - so the restore is the same window it started as,
/// not a window that happens to be the same size.</para>
/// </summary>
public sealed class RoomWarpEffect : PossessionEffectBase
{
    private const double LeanInMs = 900;
    private const double SagPx = 4;

    /// <summary>Crimson (the theme colour: the room is red). Ember stays the verb - the charge and the
    /// possessed outline are the only ember here.</summary>
    private static readonly Color Crimson = Color.FromRgb(0xDC, 0x14, 0x3C);

    /// <summary>Per-escape toll. Small on purpose: the point is that you cannot be sure, until the
    /// tenth one, that the window is not the size it was.</summary>
    private const double ShrinkPerAttempt = 1;
    private const double NudgePerAttempt = 2;
    private const double MaxShrink = 40;

    /// <summary>One room, one warp. A second lean on the same canvas would just be a bigger angle with
    /// two owners and two ideas of where straight is.</summary>
    private static int _liveCount;

    private FrameworkElement? _canvas;
    private System.Windows.Shapes.Rectangle? _wash;
    private Window? _window;

    private Action<EscapeAttempt>? _onEscape;
    private Action<PossessionRung>? _onRung;

    private double _shrunk;      // running total, so the restore is one subtraction not a replay
    private double _nudgedX;
    private double _nudgedY;

    private bool _metricsCaptured;
    private double _w0, _h0, _left0, _top0;
    private bool _wLocal, _hLocal, _leftLocal, _topLocal;

    public override string Id => "roomwarp";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 2;
    /// <summary>Zero: the room stays crooked until reassembly straightens it.</summary>
    public override TimeSpan HoldFor => TimeSpan.Zero;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        NameOverrideText = "the room";
        if (_liveCount > 0) return false;
        return FindCanvas(ctx) != null && ctx.Host.GhostLayer != null;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _canvas = FindCanvas(ctx);
        _window = ctx.Host.Window;
        if (_canvas == null) return;

        _liveCount++;

        // 1. the lean. RenderTransform, so nothing re-measures and no layout pass can fight it.
        var lease = TakeLease(_canvas);
        if (lease != null)
        {
            lease.SetOrigin(new Point(0.5, 0.5));
            double angle = Amp(Rand(0.5, 1.0)) * Sign();
            PossAnim.To(lease.Rotate, RotateTransform.AngleProperty, angle, LeanInMs, PossAnim.EaseInOut);
            PossAnim.To(lease.Translate, TranslateTransform.YProperty, Amp(SagPx), LeanInMs, PossAnim.EaseInOut);
        }

        // 2. the wash. Its own plate in the ghost layer rather than a tint on the canvas: the canvas
        //    is the thing that leans, and a wash that leans with it shows daylight in two corners.
        BuildWash(ctx);
        ApplyWashOpacity(CurrentRung(ctx), LeanInMs);

        // 3. the toll. Every attempt to leave makes the room a little smaller.
        HookEvents(ctx);

        await PossAnim.DelayAsync(LeanInMs + 30, ct).ConfigureAwait(true);
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        UnhookEvents();

        double ms = UndoMs(duration, 300, 900);

        try
        {
            if (Lease != null && ms > 0)
            {
                PossAnim.To(Lease.Rotate, RotateTransform.AngleProperty, 0, ms, PossAnim.EaseInOut);
                PossAnim.To(Lease.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
            }
            if (_wash != null && ms > 0) PossAnim.To(_wash, UIElement.OpacityProperty, 0, ms, PossAnim.EaseInOut);
            if (ms > 0) await PossAnim.DelayAsync(ms + 30, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession roomwarp undo animation failed: {Error}", ex.Message); }

        try
        {
            if (_wash != null)
            {
                PossAnim.Settle(_wash, UIElement.OpacityProperty, 0);
                Ctx?.Host.GhostLayer?.Children.Remove(_wash);
            }
        }
        catch { }
        _wash = null;

        RestoreWindowMetrics();

        _canvas = null;
        _window = null;
        if (_liveCount > 0) _liveCount--;
    }

    // ---- the room ------------------------------------------------------------------------------

    /// <summary>The design canvas: the fixed-size Grid inside the window's Viewbox. Leaning THAT (and
    /// not RootGrid) keeps the ghost layer, the rubble floor and the wash upright, which is what makes
    /// the lean read as the room tilting under the haunt rather than the screenshot being crooked.</summary>
    private static FrameworkElement? FindCanvas(PossessionContext ctx)
    {
        try
        {
            var window = ctx.Host.Window;
            if (window == null) return null;
            if (window.FindName("DesignCanvas") is FrameworkElement named && named.IsVisible) return named;

            // No name (another room, a future host): the first Viewbox child under the content will do.
            return FirstViewboxChild(window.Content as DependencyObject, 0);
        }
        catch { return null; }
    }

    private static FrameworkElement? FirstViewboxChild(DependencyObject? node, int depth)
    {
        if (node == null || depth > 6) return null;
        try
        {
            if (node is Viewbox vb && vb.Child is FrameworkElement child) return child;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var found = FirstViewboxChild(VisualTreeHelper.GetChild(node, i), depth + 1);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }

    private void BuildWash(PossessionContext ctx)
    {
        try
        {
            var layer = ctx.Host.GhostLayer;
            if (layer == null) return;

            double w = layer.ActualWidth > 0 ? layer.ActualWidth : (ctx.Host.Window?.ActualWidth ?? 0);
            double h = layer.ActualHeight > 0 ? layer.ActualHeight : (ctx.Host.Window?.ActualHeight ?? 0);
            if (w <= 0 || h <= 0) return;

            var brush = new SolidColorBrush(Crimson);
            brush.Freeze();

            _wash = new System.Windows.Shapes.Rectangle
            {
                Width = w,
                Height = h,
                Fill = brush,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(_wash, 0);
            Canvas.SetTop(_wash, 0);
            layer.Children.Add(_wash);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession roomwarp wash failed: {Error}", ex.Message); }
    }

    /// <summary>0.04 at Collapse, 0.08 at It knows. Deep enough that the room reads warmer, shallow
    /// enough that nothing on the page changes contrast class.</summary>
    private void ApplyWashOpacity(PossessionRung rung, double ms)
    {
        try
        {
            if (_wash == null) return;
            double target = rung >= PossessionRung.ItKnows ? 0.08 : 0.04;
            if (Photosafe) target *= 0.5;
            PossAnim.To(_wash, UIElement.OpacityProperty, target, Math.Max(1, ms), PossAnim.EaseInOut);
        }
        catch { }
    }

    private PossessionRung CurrentRung(PossessionContext ctx)
    {
        try { return App.Possession?.CurrentRung ?? ctx.Rung; }
        catch { return ctx.Rung; }
    }

    // ---- the toll ------------------------------------------------------------------------------

    private void HookEvents(PossessionContext ctx)
    {
        try
        {
            _onEscape = _ => ShrinkTheRoom();
            App.Lockdown.EscapeAttempted += _onEscape;
        }
        catch (Exception ex)
        {
            _onEscape = null;
            App.Logger?.Warning("Possession roomwarp escape hook failed: {Error}", ex.Message);
        }

        try
        {
            var director = App.Possession;
            if (director != null)
            {
                _onRung = r => ApplyWashOpacity(r, 1200);
                director.RungChanged += _onRung;
            }
        }
        catch { _onRung = null; }
    }

    private void UnhookEvents()
    {
        try { if (_onEscape != null) App.Lockdown.EscapeAttempted -= _onEscape; } catch { }
        _onEscape = null;
        try { if (_onRung != null && App.Possession != null) App.Possession.RungChanged -= _onRung; } catch { }
        _onRung = null;
    }

    /// <summary>One pixel narrower, one shorter, two across. Skipped whenever the window is not a
    /// plain restored window, or when the toll would push it off the screen it is on: a haunt that
    /// walks the window off the desktop is not a haunt, it is a lost window.</summary>
    private void ShrinkTheRoom()
    {
        var w = _window;
        if (w == null) return;

        try
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
            if (w.WindowState != WindowState.Normal) return;
            if (_shrunk >= MaxShrink) return;

            CaptureWindowMetrics(w);

            double newW = (double.IsNaN(w.Width) ? w.ActualWidth : w.Width) - ShrinkPerAttempt;
            double newH = (double.IsNaN(w.Height) ? w.ActualHeight : w.Height) - ShrinkPerAttempt;
            if (newW < w.MinWidth || newH < w.MinHeight || newW < 400 || newH < 300) return;

            double left = double.IsNaN(w.Left) ? 0 : w.Left;
            double top = double.IsNaN(w.Top) ? 0 : w.Top;
            double newLeft = left + NudgePerAttempt;
            double newTop = top + NudgePerAttempt;

            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)(left + newW / 2), (int)(top + newH / 2)));
            var wa = screen.WorkingArea;
            if (newLeft + newW > wa.Right || newTop + newH > wa.Bottom) return;

            w.Width = newW;
            w.Height = newH;
            w.Left = newLeft;
            w.Top = newTop;

            _shrunk += ShrinkPerAttempt;
            _nudgedX += NudgePerAttempt;
            _nudgedY += NudgePerAttempt;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession roomwarp shrink failed: {Error}", ex.Message); }
    }

    private void CaptureWindowMetrics(Window w)
    {
        if (_metricsCaptured) return;
        try
        {
            _wLocal = w.ReadLocalValue(FrameworkElement.WidthProperty) != DependencyProperty.UnsetValue;
            _hLocal = w.ReadLocalValue(FrameworkElement.HeightProperty) != DependencyProperty.UnsetValue;
            _leftLocal = w.ReadLocalValue(Window.LeftProperty) != DependencyProperty.UnsetValue;
            _topLocal = w.ReadLocalValue(Window.TopProperty) != DependencyProperty.UnsetValue;
            _w0 = double.IsNaN(w.Width) ? w.ActualWidth : w.Width;
            _h0 = double.IsNaN(w.Height) ? w.ActualHeight : w.Height;
            _left0 = w.Left;
            _top0 = w.Top;
            _metricsCaptured = true;
        }
        catch { }
    }

    private void RestoreWindowMetrics()
    {
        var w = _window;
        if (w == null || !_metricsCaptured) return;
        try
        {
            if (w.WindowState == WindowState.Normal)
            {
                if (_wLocal) w.Width = _w0; else if (_shrunk > 0) w.ClearValue(FrameworkElement.WidthProperty);
                if (_hLocal) w.Height = _h0; else if (_shrunk > 0) w.ClearValue(FrameworkElement.HeightProperty);
                if (!double.IsNaN(_left0) && (_leftLocal || _nudgedX > 0)) w.Left = _left0;
                if (!double.IsNaN(_top0) && (_topLocal || _nudgedY > 0)) w.Top = _top0;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession roomwarp metric restore failed: {Error}", ex.Message); }

        _metricsCaptured = false;
        _shrunk = 0;
        _nudgedX = 0;
        _nudgedY = 0;
    }
}
