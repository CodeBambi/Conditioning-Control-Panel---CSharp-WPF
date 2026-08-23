using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R1 "dodge" - the button will not be caught. Come within 24 px and it slides out of reach; AIM at it
/// and it is already gone before you arrive. Three times, and then it gives up and lets you click it
/// (INVARIANT: friction, never lockout - it always stays clickable where it lands, and it never runs
/// off the window edge).
///
/// <para><b>Predictive (Wave 2).</b> Proximity alone is a dodge you can beat by moving fast: the
/// pointer crosses the 24 px ring and the click has already landed. So the real trigger reads
/// <see cref="PossessionPointer.Velocity"/> and projects the cursor ~300 ms ahead; when THAT point
/// lands inside the button, the button leaves sideways - perpendicular to the approach, which is the
/// one direction a moving hand cannot correct for without stopping. That is the difference between a
/// button that is annoying and a button that is haunted.</para>
///
/// <para><b>The title bar.</b> Wave 2 auto-tags the window chrome, so the X and the minimize button
/// are ordinary Button targets and CAN dodge - the POSSESSION.md rule is that their HIT-TESTING is
/// never touched, not that they hold still. The X still closes the window; you just have to catch it
/// first, and after three dodges it stops running.</para>
/// </summary>
public sealed class DodgeEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles =
        { PossessionRole.Button, PossessionRole.Toggle, PossessionRole.TabHeader };

    private const double ProximityPx = 24;
    private const double DodgeMs = 260;
    private const int MaxDodges = 3;

    /// <summary>How far ahead of the cursor we look. Long enough to beat a fast flick, short enough
    /// that a hand changing its mind mid-sweep does not set the whole room running.</summary>
    private const double PredictSeconds = 0.3;

    /// <summary>Minimum speed before the prediction is trusted at all (px/s). Below this the smoothed
    /// velocity is mostly tremor and the proximity ring is the better trigger.</summary>
    private const double PredictMinSpeed = 220;

    private Window? _window;
    private MouseEventHandler? _moveHandler;
    private Rect _homeRect;      // layer space
    private double _offsetX;     // layer space
    private double _offsetY;     // layer space
    private double _scaleX = 1;  // layer units per design unit
    private double _scaleY = 1;
    private int _dodges;
    private bool _busy;

    public override string Id => "dodge";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(20);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>The charge fires with the FIRST dodge, not on Apply: the tell must land with the move.</summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var el = target?.Element;
        if (el == null) return false;
        return ctx.Host.Window != null;
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var el = target?.Element;
        if (el == null) return Task.CompletedTask;

        _homeRect = PossessionVisual.BoundsOf(ctx.Host, el);
        if (_homeRect.IsEmpty || _homeRect.Width <= 0) return Task.CompletedTask;
        var scale = PossessionVisual.ScaleOf(ctx.Host, el);
        _scaleX = scale.X;
        _scaleY = scale.Y;

        _window = ctx.Host.Window;
        if (_window == null) return Task.CompletedTask;

        _moveHandler = OnPreviewMouseMove;
        _window.PreviewMouseMove += _moveHandler;
        return Task.CompletedTask;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (_busy || _dodges >= MaxDodges) return;
            var ctx = Ctx;
            var el = Element;
            if (ctx == null || el == null) return;

            var relativeTo = (IInputElement?)ctx.Host.GhostLayer ?? _window;
            if (relativeTo == null) return;
            var p = e.GetPosition(relativeTo);

            var current = new Rect(_homeRect.X + _offsetX, _homeRect.Y + _offsetY, _homeRect.Width, _homeRect.Height);

            // 1. Where are they going? The ghost layer is stretched over the whole window, so the
            //    pointer service (window coordinates) and this rectangle share a coordinate space.
            var v = PossessionPointer.Velocity;
            bool predicted = false;
            if (v.Length >= PredictMinSpeed)
            {
                var ahead = new Point(p.X + v.X * PredictSeconds, p.Y + v.Y * PredictSeconds);
                predicted = current.Contains(ahead);
            }

            // 2. Where are they now?
            var ring = current;
            ring.Inflate(ProximityPx, ProximityPx);
            if (!predicted && !ring.Contains(p)) return;

            _busy = true;
            _ = DodgeAsync(ctx, el, p, predicted ? v : default);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession dodge move handler failed: {Error}", ex.Message);
            _busy = false;
        }
    }

    private async Task DodgeAsync(PossessionContext ctx, FrameworkElement el, Point cursor, Vector approach)
    {
        try
        {
            var ct = Cts?.Token ?? CancellationToken.None;

            // Grammar: ember charge (and the warden naming it) before the FIRST dodge only.
            await ChargeAndPossessAsync(el, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;

            var lease = Lease ?? TakeLease(el);
            if (lease == null) return;

            // Everything here is layer space: the cursor came from the ghost layer, and so do the
            // window edges (the layer is stretched over the whole window).
            double layerWidth = ctx.Host.GhostLayer?.ActualWidth ?? ctx.Host.Window?.ActualWidth ?? 0;
            double layerHeight = ctx.Host.GhostLayer?.ActualHeight ?? ctx.Host.Window?.ActualHeight ?? 0;

            double minX = 12 - _homeRect.X;
            double maxX = (layerWidth > 0 ? layerWidth - 12 : _homeRect.Right) - _homeRect.Right;
            if (maxX < minX) maxX = minX;
            double minY = 12 - _homeRect.Y;
            double maxY = (layerHeight > 0 ? layerHeight - 12 : _homeRect.Bottom) - _homeRect.Bottom;
            if (maxY < minY) maxY = minY;

            double targetX;
            double targetY = _offsetY;

            if (approach.Length > 0.001)
            {
                // Predicted: step SIDEWAYS out of the approach line. Both perpendiculars are equally
                // far from the cursor, so the tie is broken by which one the window has room for.
                var dir = approach;
                dir.Normalize();
                var perp = new Vector(-dir.Y, dir.X);
                double distance = Amp(Rand(40, 60));

                double ax = Math.Clamp(_offsetX + perp.X * distance, minX, maxX);
                double ay = Math.Clamp(_offsetY + perp.Y * distance, minY, maxY);
                double bx = Math.Clamp(_offsetX - perp.X * distance, minX, maxX);
                double by = Math.Clamp(_offsetY - perp.Y * distance, minY, maxY);

                double aMoved = Math.Abs(ax - _offsetX) + Math.Abs(ay - _offsetY);
                double bMoved = Math.Abs(bx - _offsetX) + Math.Abs(by - _offsetY);
                if (aMoved >= bMoved) { targetX = ax; targetY = ay; }
                else { targetX = bx; targetY = by; }

                if (aMoved < 6 && bMoved < 6)
                {
                    // Cornered sideways: fall back to running away along the approach instead of
                    // vibrating in place.
                    targetX = Math.Clamp(_offsetX - dir.X * distance, minX, maxX);
                    targetY = Math.Clamp(_offsetY - dir.Y * distance, minY, maxY);
                }
            }
            else
            {
                // Proximity: run AWAY from the cursor, staying inside the window with a 12 px margin.
                double distance = Amp(Rand(40, 120));
                double away = (cursor.X < _homeRect.X + _offsetX + _homeRect.Width / 2) ? 1 : -1;
                targetX = Math.Clamp(_offsetX + away * distance, minX, maxX);
                if (Math.Abs(targetX - _offsetX) < 6)
                    targetX = Math.Clamp(_offsetX - away * distance, minX, maxX);
            }

            if (Math.Abs(targetX - _offsetX) < 2 && Math.Abs(targetY - _offsetY) < 2) return;

            _offsetX = targetX;
            _offsetY = targetY;
            _dodges++;
            PossAnim.To(lease.Translate, TranslateTransform.XProperty, _offsetX / _scaleX, DodgeMs, PossAnim.EaseOut);
            PossAnim.To(lease.Translate, TranslateTransform.YProperty, _offsetY / _scaleY, DodgeMs, PossAnim.EaseOut);

            await PossAnim.DelayAsync(DodgeMs + 120, ct).ConfigureAwait(true);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession dodge failed: {Error}", ex.Message); }
        finally { _busy = false; }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        try
        {
            if (_window != null && _moveHandler != null) _window.PreviewMouseMove -= _moveHandler;
        }
        catch { }
        _moveHandler = null;
        _window = null;

        var lease = Lease;
        double ms = UndoMs(duration, 300, 800);
        if (lease != null && ms > 0 && (Math.Abs(_offsetX) > 0.01 || Math.Abs(_offsetY) > 0.01))
        {
            PossAnim.To(lease.Translate, TranslateTransform.XProperty, 0, ms, PossAnim.EaseInOut);
            PossAnim.To(lease.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
            await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
        }

        _offsetX = 0;
        _offsetY = 0;
        _dodges = 0;
        _busy = false;
    }
}
