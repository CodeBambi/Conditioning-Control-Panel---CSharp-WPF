using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R1 "ghostcursor" - somebody else is using your mouse. A translucent ember arrow fades in off to the
/// side of the window, glides along a bezier to a button or toggle, hovers over it for a beat, presses
/// it (a press ripple in the ghost layer plus a 0.96 squeeze on the real control), then drifts off the
/// edge and fades.
///
/// <para>INVARIANT: nothing is ever CLICKED. The ghost never raises an event, never toggles anything
/// and never touches IsChecked or a Command; the squeeze is a render transform on a lease, so the
/// control is pixel-identical afterwards. The whole point is a press you watch happen to you, not a
/// setting that changed behind your back.</para>
///
/// <para>At R1 it is a two second visit with no press at all - a cursor that is not yours crossing the
/// room. From R2 it presses; from R3 the warden names it ("the ghost cursor"), because by then a
/// second cursor is the loudest thing on screen and deniability is over.</para>
///
/// <para>Photosafe: identical choreography, slower, no flash. There is no flicker in it to begin with
/// (UsesFlicker false) - the ripple is one soft bloom, not a blink.</para>
/// </summary>
public sealed class GhostCursorEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Button, PossessionRole.Toggle };

    /// <summary>A plain arrow cursor, tip at the geometry origin so the translate IS the tip position.</summary>
    private const string ArrowData = "M 0,0 L 0,17.5 L 4.4,13.2 L 7.3,19.2 L 10.1,17.9 L 7.2,12.1 L 12.4,12.1 Z";

    /// <summary>How close to the real pointer is too close: two cursors on top of each other reads as
    /// a rendering artefact, not as company.</summary>
    private const double MinDistanceFromRealCursor = 150;

    private Canvas? _layer;
    private Path? _arrow;
    private Ellipse? _ripple;
    private MatrixTransform? _move;
    private Point _landing;
    private Point _exit;

    public override string Id => "ghostcursor";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;

    /// <summary>Named from R3 up (see the class remarks).</summary>
    public override bool IsBig => (Ctx?.Rung ?? PossessionRung.Settle) >= PossessionRung.Collapse;

    public override bool UsesFlicker => false;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(6);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>The name has to be set before the charge names it, so the sequence is driven by hand.</summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var el = target?.Element;
        if (el == null || ctx.Host.GhostLayer == null) return false;

        var bounds = PossessionVisual.BoundsOf(ctx.Host, el);
        if (bounds.IsEmpty || bounds.Width < 8 || bounds.Height < 8) return false;

        // Prefer a victim the real cursor is nowhere near. PossessionPointer is a hint, and an unset
        // one (nobody has moved the mouse yet) simply skips the preference.
        try
        {
            var p = PossessionPointer.Position;
            if (p.X > 0 || p.Y > 0)
            {
                var c = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                if ((c - p).Length < MinDistanceFromRealCursor) return false;
            }
        }
        catch { }

        return true;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var el = target?.Element;
        _layer = ctx.Host.GhostLayer;
        if (el == null || _layer == null) return;

        var bounds = PossessionVisual.BoundsOf(ctx.Host, el);
        if (bounds.IsEmpty) return;

        bool press = ctx.Rung >= PossessionRung.Melt;
        NameOverrideText = "the ghost cursor";

        // Grammar first: the ember charge over the victim, then the outline, then anything moves.
        await ChargeAndPossessAsync(el, ct).ConfigureAwait(true);
        if (ct.IsCancellationRequested) return;

        double layerW = _layer.ActualWidth > 0 ? _layer.ActualWidth : (ctx.Host.Window?.ActualWidth ?? 0);
        double layerH = _layer.ActualHeight > 0 ? _layer.ActualHeight : (ctx.Host.Window?.ActualHeight ?? 0);
        if (layerW <= 0 || layerH <= 0) return;

        var landing = new Point(bounds.X + bounds.Width * Rand(0.35, 0.65),
                                bounds.Y + bounds.Height * Rand(0.35, 0.65));

        bool fromLeft = landing.X > layerW / 2;
        var start = new Point(fromLeft ? -48 : layerW + 48,
                              Math.Clamp(landing.Y + Rand(-140, 140), 24, Math.Max(24, layerH - 24)));

        // ---- the arrow --------------------------------------------------------------------
        var move = new MatrixTransform(new Matrix(1, 0, 0, 1, start.X, start.Y));
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1.45, 1.45));
        group.Children.Add(move);

        var arrow = new Path
        {
            Data = Geometry.Parse(ArrowData),
            Fill = EmberBrush,
            Stroke = EmberBrush,
            StrokeThickness = 0.8,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = group,
            Effect = new DropShadowEffect { Color = EmberColor, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 },
        };
        Canvas.SetLeft(arrow, 0);
        Canvas.SetTop(arrow, 0);
        _layer.Children.Add(arrow);
        _arrow = arrow;

        double slow = Photosafe ? 1.35 : 1.0;
        double glideMs = (press ? 1500 : 1200) * slow;

        PossAnim.To(arrow, UIElement.OpacityProperty, 0.7, 300 * slow, PossAnim.EaseOut);
        Glide(move, start, landing, glideMs);
        if (!await PossAnim.DelayAsync(glideMs + 40, ct).ConfigureAwait(true)) return;

        // ---- the hover, and the press it never really makes --------------------------------
        if (!await PossAnim.DelayAsync(400 * slow, ct).ConfigureAwait(true)) return;

        if (press)
        {
            Ripple(landing, slow);

            var lease = TakeLease(el);
            if (lease != null)
            {
                lease.SetOrigin(new Point(0.5, 0.5));
                PossAnim.To(lease.Scale, ScaleTransform.ScaleXProperty, 0.96, 90 * slow, PossAnim.EaseOut);
                PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 0.96, 90 * slow, PossAnim.EaseOut);
                if (!await PossAnim.DelayAsync(120 * slow, ct).ConfigureAwait(true)) return;
                PossAnim.To(lease.Scale, ScaleTransform.ScaleXProperty, 1, 190 * slow, PossAnim.EaseOut);
                PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 1, 190 * slow, PossAnim.EaseOut);
            }

            if (!await PossAnim.DelayAsync(420 * slow, ct).ConfigureAwait(true)) return;
        }

        // ---- and it stays, for as long as it is allowed to ---------------------------------
        // The drift off the edge belongs to Undo, not to Apply: HoldFor is what says how long a second
        // cursor is in the room, and an Apply that tidied itself away would leave the possessed outline
        // hanging over a button with nothing standing on it.
        _move = move;
        _exit = new Point(fromLeft ? layerW + 60 : -60,
                          Math.Clamp(landing.Y + Rand(-100, 160), 12, Math.Max(12, layerH - 12)));
        _landing = landing;
    }

    /// <summary>Drive the arrow along a real bezier rather than a straight line: a cursor that travels
    /// in a perfect straight line reads as an animation, and a hand does not move like that.</summary>
    private void Glide(MatrixTransform move, Point from, Point to, double ms)
    {
        try
        {
            var v = to - from;
            var perp = new Vector(-v.Y, v.X);
            if (perp.Length > 0.001) perp.Normalize();
            double bow = Amp(Rand(50, 130)) * Sign();

            var c1 = from + v * 0.32 + perp * bow;
            var c2 = from + v * 0.68 - perp * (bow * 0.55);

            var figure = new PathFigure { StartPoint = from, IsClosed = false };
            figure.Segments.Add(new BezierSegment(c1, c2, to, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            var anim = new MatrixAnimationUsingPath
            {
                PathGeometry = geometry,
                Duration = TimeSpan.FromMilliseconds(Math.Max(1, ms)),
                DoesRotateWithTangent = false,
                FillBehavior = FillBehavior.HoldEnd,
            };
            move.BeginAnimation(MatrixTransform.MatrixProperty, anim);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession ghostcursor glide failed: {Error}", ex.Message); }
    }

    /// <summary>The press you can see: one ember ring blooming out of the point that was pressed.</summary>
    private void Ripple(Point at, double slow)
    {
        try
        {
            var layer = _layer;
            if (layer == null) return;

            const double size = 52;
            var scale = new ScaleTransform(0.2, 0.2);
            var ring = new Ellipse
            {
                Width = size,
                Height = size,
                Stroke = EmberBrush,
                StrokeThickness = 2,
                Fill = null,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
            };
            Canvas.SetLeft(ring, at.X - size / 2);
            Canvas.SetTop(ring, at.Y - size / 2);
            layer.Children.Add(ring);
            _ripple = ring;

            double ms = 520 * slow;
            PossAnim.To(scale, ScaleTransform.ScaleXProperty, 1, ms, PossAnim.EaseOut);
            PossAnim.To(scale, ScaleTransform.ScaleYProperty, 1, ms, PossAnim.EaseOut);
            PossAnim.Pulse(ring, UIElement.OpacityProperty, Photosafe ? 0.5 : 0.8, ms * 0.25, ms * 0.75);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession ghostcursor ripple failed: {Error}", ex.Message); }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        // The squeeze is the only thing that touched the real control; put it back before the lease is
        // handed over, so the restore never lands mid-animation.
        var lease = Lease;
        double ms = UndoMs(duration, 120, 260);
        if (lease != null && ms > 0)
        {
            PossAnim.To(lease.Scale, ScaleTransform.ScaleXProperty, 1, ms, PossAnim.EaseOut);
            PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 1, ms, PossAnim.EaseOut);
            await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
        }

        // And it leaves the way it came. On the synchronous path (UndoAll on crash / dispose) there is
        // no time for any of that, and the arrow simply is not there any more.
        double leaveMs = UndoMs(duration, 400, 900);
        var arrow = _arrow;
        var move = _move;
        if (leaveMs > 0 && arrow != null && move != null)
        {
            Glide(move, _landing, _exit, leaveMs);
            PossAnim.To(arrow, UIElement.OpacityProperty, 0, leaveMs, PossAnim.EaseIn);
            await PossAnim.DelayAsync(leaveMs + 40, CancellationToken.None).ConfigureAwait(true);
        }

        RemoveVisuals();
    }

    private void RemoveVisuals()
    {
        try
        {
            var layer = _layer;
            if (layer != null)
            {
                if (_arrow != null) { try { layer.Children.Remove(_arrow); } catch { } }
                if (_ripple != null) { try { layer.Children.Remove(_ripple); } catch { } }
            }
        }
        catch { }
        _arrow = null;
        _ripple = null;
        _move = null;
        _layer = null;
    }
}
