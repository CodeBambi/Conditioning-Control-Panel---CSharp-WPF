using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R3 "fall" - a card gives up. It tilts off its bottom-left corner, then slides out of the bottom of
/// the window and leaves a dashed ember chalk outline in the hole where it used to be. It cannot be
/// clicked while it is gone (hit-testing is restored exactly on the way back up), and after the hold it
/// climbs back into its seat.
/// </summary>
public sealed class FallEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Card };

    private const double TiltMs = 400;
    private const double FallMs = 900;
    private const double ClimbMs = 900;

    private Rectangle? _chalk;
    private bool _hitTestChanged;
    private bool _priorHitTestLocal;
    private bool _priorHitTest = true;

    public override string Id => "fall";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(45);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>
    /// A card is dropped off the bottom of the window AND has its hit-testing turned off for 45 s, so
    /// everything inside it goes with it. <c>Possession.Exclude</c> inherits DOWN only: the Lockdown
    /// card is enrollable while BtnEmergencyExit and TxtLockdownExit sit inside it, and taking it
    /// would take the exits. <see cref="PossessionOffLimits"/> is the shared rule (StealCardEffect
    /// has carried the same one privately since wave 2).
    /// </summary>
    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var el = target?.Element;
        if (el == null) return false;
        if (PossessionVisual.IsWindowChrome(el)) return false;
        if (PossessionOffLimits.IsOffLimits(el)) return false;
        return true;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var el = target?.Element;
        if (el == null) return;

        var lease = TakeLease(el);
        if (lease == null) return;
        lease.SetOrigin(new Point(0, 1));   // it leans off its bottom-left corner, like a shelf giving way

        var bounds = PossessionVisual.BoundsOf(ctx.Host, el);
        DrawChalk(ctx, bounds);

        // 1. the lean.
        PossAnim.To(lease.Rotate, RotateTransform.AngleProperty, Amp(8), TiltMs, PossAnim.EaseInOut);
        if (!await PossAnim.DelayAsync(TiltMs + 20, ct).ConfigureAwait(true)) return;

        // 2. the fall. Distance is NOT halved when photosafe (a half-fallen card reads as a bug);
        //    the rotation is.
        double layerH = ctx.Host.GhostLayer?.ActualHeight ?? ctx.Host.Window?.ActualHeight ?? (bounds.Bottom + 400);
        double sy = PossessionVisual.ScaleOf(ctx.Host, el).Y;
        // bounds are layer pixels, the lease moves the card in design units.
        double drop = Math.Max(120, (layerH - bounds.Bottom + 40) / sy);

        try
        {
            _priorHitTestLocal = el.ReadLocalValue(UIElement.IsHitTestVisibleProperty) != DependencyProperty.UnsetValue;
            _priorHitTest = el.IsHitTestVisible;
            el.IsHitTestVisible = false;
            _hitTestChanged = true;
        }
        catch { }

        PossAnim.To(lease.Translate, TranslateTransform.YProperty, drop, FallMs, PossAnim.Gravity);
        PossAnim.To(lease.Rotate, RotateTransform.AngleProperty, Amp(14), FallMs, PossAnim.EaseIn);
        await PossAnim.DelayAsync(FallMs + 20, ct).ConfigureAwait(true);
    }

    /// <summary>The chalk mark: a dashed ember rectangle in the hole the card left. Attribution that
    /// stays put even though the possessed element itself is off-screen.</summary>
    private void DrawChalk(PossessionContext ctx, Rect bounds)
    {
        try
        {
            var layer = ctx.Host.GhostLayer;
            if (layer == null || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

            _chalk = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Stroke = EmberBrush,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                RadiusX = 6,
                RadiusY = 6,
                Fill = null,
                Opacity = 0,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_chalk, bounds.X);
            Canvas.SetTop(_chalk, bounds.Y);
            layer.Children.Add(_chalk);
            PossAnim.To(_chalk, UIElement.OpacityProperty, 0.55, TiltMs, PossAnim.EaseOut);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession fall chalk failed: {Error}", ex.Message); }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        double ms = UndoMs(duration, 400, ClimbMs);
        var lease = ms > 0 ? Lease : null;

        if (lease != null)
        {
            PossAnim.To(lease.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseOut);
            PossAnim.To(lease.Rotate, RotateTransform.AngleProperty, 0, ms, PossAnim.EaseOut);
        }
        if (_chalk != null && ms > 0) PossAnim.To(_chalk, UIElement.OpacityProperty, 0, ms * 0.6, PossAnim.EaseInOut);
        if (ms > 0 && (lease != null || _chalk != null))
            await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);

        try
        {
            if (_chalk != null) Ctx?.Host.GhostLayer?.Children.Remove(_chalk);
        }
        catch { }
        _chalk = null;

        try
        {
            var el = Element;
            if (el != null && _hitTestChanged)
            {
                if (_priorHitTestLocal) el.IsHitTestVisible = _priorHitTest;
                else el.ClearValue(UIElement.IsHitTestVisibleProperty);
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession fall hit-test restore failed: {Error}", ex.Message); }
        _hitTestChanged = false;
    }
}
