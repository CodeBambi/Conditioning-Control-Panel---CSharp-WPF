using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R1 "dodge" - the button will not be caught. Come within 24 px and it slides out of reach, three
/// times, and then it gives up and lets you click it (INVARIANT: friction, never lockout - it always
/// stays clickable where it lands, and it never runs off the window edge).
/// Never applied to the window's real close / minimize chrome.
/// </summary>
public sealed class DodgeEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Button, PossessionRole.TabHeader };

    private const double ProximityPx = 24;
    private const double DodgeMs = 260;
    private const int MaxDodges = 3;

    private Window? _window;
    private MouseEventHandler? _moveHandler;
    private Rect _homeRect;      // layer space
    private double _offsetX;     // layer space
    private double _scaleX = 1;  // layer units per design unit
    private int _dodges;
    private bool _busy;

    public override string Id => "dodge";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(15);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>The charge fires with the FIRST dodge, not on Apply: the tell must land with the move.</summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var el = target?.Element;
        if (el == null) return false;
        if (PossessionVisual.IsWindowChrome(el)) return false;   // the X may dodge, but never THE X
        return ctx.Host.Window != null;
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var el = target?.Element;
        if (el == null) return Task.CompletedTask;

        _homeRect = PossessionVisual.BoundsOf(ctx.Host, el);
        if (_homeRect.IsEmpty || _homeRect.Width <= 0) return Task.CompletedTask;
        _scaleX = PossessionVisual.ScaleOf(ctx.Host, el).X;

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

            var current = new Rect(_homeRect.X + _offsetX, _homeRect.Y, _homeRect.Width, _homeRect.Height);
            current.Inflate(ProximityPx, ProximityPx);
            if (!current.Contains(p)) return;

            _busy = true;
            _ = DodgeAsync(ctx, el, p);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession dodge move handler failed: {Error}", ex.Message);
            _busy = false;
        }
    }

    private async Task DodgeAsync(PossessionContext ctx, FrameworkElement el, Point cursor)
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
            double distance = Amp(Rand(40, 120));

            // Run AWAY from the cursor, but stay inside the window with a 12 px margin.
            double away = (cursor.X < _homeRect.X + _offsetX + _homeRect.Width / 2) ? 1 : -1;
            double target = _offsetX + away * distance;
            double minOffset = 12 - _homeRect.X;
            double maxOffset = (layerWidth > 0 ? layerWidth - 12 : _homeRect.Right) - _homeRect.Right;
            if (maxOffset < minOffset) maxOffset = minOffset;
            target = Math.Clamp(target, minOffset, maxOffset);

            if (Math.Abs(target - _offsetX) < 6)
            {
                // Cornered: hop the other way instead of vibrating in place.
                target = Math.Clamp(_offsetX - away * distance, minOffset, maxOffset);
            }

            _offsetX = target;
            _dodges++;
            PossAnim.To(lease.Translate, TranslateTransform.XProperty, _offsetX / _scaleX, DodgeMs, PossAnim.EaseOut);

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
        if (lease != null && ms > 0 && Math.Abs(_offsetX) > 0.01)
        {
            PossAnim.To(lease.Translate, TranslateTransform.XProperty, 0, ms, PossAnim.EaseInOut);
            await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
        }

        _offsetX = 0;
        _dodges = 0;
        _busy = false;
    }
}
