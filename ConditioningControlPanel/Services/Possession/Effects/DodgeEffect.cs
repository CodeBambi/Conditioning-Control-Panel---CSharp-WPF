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
///
/// <para><b>Where it is allowed to land (the invariant, in one place).</b> "Inside the window" is not
/// the same as "still visible and still clickable", and the difference is two real ways to strand a
/// control:
/// <list type="bullet">
/// <item>a clipped ancestor. The nav rail is a 56 px strip with <c>ClipToBounds</c> (it grows to a
/// 236 px flyout on hover and snaps back on leave). A door that steps sideways leaves the strip, and
/// clipped in WPF means invisible AND un-hittable - for the whole 20 s hold. Anything inside a strip
/// is therefore left alone entirely: it cannot leave its lane, and moving ALONG the lane only parks
/// it under a neighbouring door.</item>
/// <item>the title bar. The chrome sits in a 36 px row above opaque page content that paints over it,
/// so a downward dodge puts the X somewhere the click never reaches. Chrome dodges stay in the title
/// bar band, horizontally.</item>
/// </list>
/// So every dodge is clamped into an ALLOWED REGION (the layer, minus a margin, intersected with
/// every clipping ancestor and with the chrome's band), and a step that cannot land whole inside it
/// falls back to the proximity behaviour or does not happen at all. <see cref="DodgeRegion"/> holds
/// the arithmetic, away from WPF, so it can be pinned by a test.</para>
/// </summary>
public sealed class DodgeEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles =
        { PossessionRole.Button, PossessionRole.Toggle, PossessionRole.TabHeader };

    private const double ProximityPx = 24;
    private const double DodgeMs = 260;
    private const int MaxDodges = 3;

    /// <summary>Below this a "dodge" is a twitch: not worth a charge, and not worth reading as a
    /// move. It is also the floor a victim's allowed region has to clear to be a candidate.</summary>
    private const double MinStepPx = 6;

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

    /// <summary>
    /// A control with nowhere legal to go is not a dodge candidate at all. Answering that HERE and
    /// not at the first mouse move is what keeps the grammar honest: the deck picks someone else
    /// instead of spending an ember charge and a warden line on a button that then holds still.
    /// </summary>
    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var el = target?.Element;
        if (el == null) return false;
        if (ctx.Host.Window == null) return false;

        var home = PossessionVisual.BoundsOf(ctx.Host, el);
        if (home.IsEmpty || home.Width <= 0) return false;

        var region = AllowedRegion(ctx, el, home);
        if (!DodgeRegion.TryOffsetRange(home, region,
                                        out double minX, out double maxX,
                                        out double minY, out double maxY))
            return false;

        // Room for a step worth seeing, on either axis.
        return (maxX - minX) >= MinStepPx || (maxY - minY) >= MinStepPx;
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
            // window edges and every clip rectangle (the layer is stretched over the whole window).
            // Measured fresh on every dodge, because the rail opens, the window resizes and a tab
            // switch changes what is clipping the victim between one step and the next.
            var region = AllowedRegion(ctx, el, _homeRect);
            if (region.IsEmpty) return;
            if (!DodgeRegion.TryOffsetRange(_homeRect, region,
                                            out double minX, out double maxX,
                                            out double minY, out double maxY))
                return;

            double targetX = _offsetX;
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

                if (aMoved < MinStepPx && bMoved < MinStepPx)
                {
                    // Cornered sideways: fall back to running away along the approach instead of
                    // vibrating in place.
                    targetX = Math.Clamp(_offsetX - dir.X * distance, minX, maxX);
                    targetY = Math.Clamp(_offsetY - dir.Y * distance, minY, maxY);
                }
            }

            if (Math.Abs(targetX - _offsetX) < MinStepPx && Math.Abs(targetY - _offsetY) < MinStepPx)
            {
                // Either this was a proximity trigger, or the predicted step had nowhere legal to go
                // (a chrome button whose perpendicular is vertical, a region with no sideways slack).
                // The safe branch: run AWAY from the cursor along X, inside the same region.
                targetY = Math.Clamp(_offsetY, minY, maxY);
                double distance = Amp(Rand(40, 120));
                double away = (cursor.X < _homeRect.X + _offsetX + _homeRect.Width / 2) ? 1 : -1;
                targetX = Math.Clamp(_offsetX + away * distance, minX, maxX);
                if (Math.Abs(targetX - _offsetX) < MinStepPx)
                    targetX = Math.Clamp(_offsetX - away * distance, minX, maxX);
            }

            if (Math.Abs(targetX - _offsetX) < 2 && Math.Abs(targetY - _offsetY) < 2) return;

            // The acceptance test, asserted rather than assumed: the control is fully inside the
            // region it is allowed to occupy, which is where it stays visible and clickable.
            if (!DodgeRegion.Contains(region, new Rect(_homeRect.X + targetX, _homeRect.Y + targetY,
                                                       _homeRect.Width, _homeRect.Height)))
                return;

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

    // ---- where it may land ---------------------------------------------------------------------

    /// <summary>
    /// The rectangle the victim must stay whole inside, in LAYER space. Rect.Empty means "this
    /// control may not dodge at all", which is a perfectly good answer: friction is the point, and a
    /// control the user cannot see or click is not friction.
    /// </summary>
    private static Rect AllowedRegion(PossessionContext ctx, FrameworkElement el, Rect home)
    {
        try
        {
            var host = ctx.Host;
            var layer = host.GhostLayer;
            double layerWidth = layer?.ActualWidth ?? host.Window?.ActualWidth ?? 0;
            double layerHeight = layer?.ActualHeight ?? host.Window?.ActualHeight ?? 0;
            if (layerWidth <= 0 || layerHeight <= 0) return Rect.Empty;

            var region = new Rect(DodgeRegion.EdgeMarginPx, DodgeRegion.EdgeMarginPx,
                                  Math.Max(0, layerWidth - DodgeRegion.EdgeMarginPx * 2),
                                  Math.Max(0, layerHeight - DodgeRegion.EdgeMarginPx * 2));

            for (DependencyObject? node = VisualTreeHelper.GetParent(el);
                 node != null;
                 node = VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement fe) continue;
                if (layer != null && ReferenceEquals(fe, layer)) break;

                var clip = ClipRectOf(host, fe);
                if (clip.IsEmpty) continue;
                if (DodgeRegion.IsStrip(clip, layerWidth, layerHeight)) return Rect.Empty;

                region = Rect.Intersect(region, clip);
                if (region.IsEmpty) return Rect.Empty;
            }

            if (PossessionVisual.IsWindowChrome(el))
            {
                // Horizontal only, and inside the band the chrome lives in. The victim's own row IS
                // the band's height here: an X that steps down lands under row 1's opaque content,
                // where the click never reaches it (POSSESSION.md: it must stay clickable where it
                // lands). The window margin is deliberately not applied vertically - the title bar
                // starts at the top of the window, and insetting it would leave no legal row at all.
                double x0 = region.X, x1 = region.Right;
                var band = TitleBarBand(host, el);
                if (!band.IsEmpty)
                {
                    x0 = Math.Max(x0, band.X);
                    x1 = Math.Min(x1, band.Right);
                }
                if (x1 - x0 < home.Width) return Rect.Empty;
                return new Rect(x0, home.Y, x1 - x0, home.Height);
            }

            return region;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession dodge region failed: {Error}", ex.Message);
            return Rect.Empty;
        }
    }

    /// <summary>What this ancestor clips away, in layer space. Rect.Empty when it clips nothing.</summary>
    private static Rect ClipRectOf(IPossessionHost host, FrameworkElement fe)
    {
        var result = Rect.Empty;
        try
        {
            var layer = host?.GhostLayer;
            if (layer == null) return Rect.Empty;

            if (fe.ClipToBounds) result = Ghost.LayerBoundsOf(host!, fe);

            var clip = fe.Clip;
            if (clip != null)
            {
                var bounds = clip.Bounds;
                if (!bounds.IsEmpty)
                {
                    var explicitClip = fe.TransformToVisual(layer).TransformBounds(bounds);
                    result = result.IsEmpty ? explicitClip : Rect.Intersect(result, explicitClip);
                }
            }
        }
        catch { return Rect.Empty; }
        return result;
    }

    /// <summary>The band the window chrome sits in, in layer space (the nearest ancestor whose name
    /// says title bar). Empty when there is none - the caller then keeps the victim in its own row.</summary>
    private static Rect TitleBarBand(IPossessionHost host, FrameworkElement el)
    {
        try
        {
            for (DependencyObject? node = VisualTreeHelper.GetParent(el);
                 node != null;
                 node = VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement fe) continue;
                var name = fe.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (name!.IndexOf("titlebar", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Ghost.LayerBoundsOf(host, fe);
            }
        }
        catch { }
        return Rect.Empty;
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

/// <summary>
/// The dodge's geometry, with no WPF in it: given where the control sits and where it is allowed to
/// be, how far may it move? Split out because "the button stayed visible and clickable" is the whole
/// acceptance test for <see cref="DodgeEffect"/> and a rectangle test can be written down.
/// </summary>
internal static class DodgeRegion
{
    /// <summary>Breathing room kept between the victim and the window edge.</summary>
    internal const double EdgeMarginPx = 12;

    /// <summary>A clip narrower (or shorter) than this fraction of the window is a LANE, not a room:
    /// the nav rail's 56 px strip, a thumbnail host, a 22 px ticker. Nothing dodges inside one.</summary>
    internal const double StripFraction = 0.5;

    /// <summary>Sub-pixel slack, so a rectangle that is exactly flush still counts as inside.</summary>
    internal const double Epsilon = 0.5;

    /// <summary>True when this clip is a lane on either axis.</summary>
    internal static bool IsStrip(Rect clip, double layerWidth, double layerHeight)
    {
        if (clip.IsEmpty) return false;
        if (layerWidth > 0 && clip.Width < layerWidth * StripFraction) return true;
        if (layerHeight > 0 && clip.Height < layerHeight * StripFraction) return true;
        return false;
    }

    /// <summary>True when <paramref name="rect"/> sits whole inside <paramref name="region"/>.</summary>
    internal static bool Contains(Rect region, Rect rect)
    {
        if (region.IsEmpty || rect.IsEmpty) return false;
        return rect.X >= region.X - Epsilon
            && rect.Y >= region.Y - Epsilon
            && rect.Right <= region.Right + Epsilon
            && rect.Bottom <= region.Bottom + Epsilon;
    }

    /// <summary>
    /// The offsets (from <paramref name="home"/>) that keep the victim whole inside the region.
    /// False when the region cannot hold it at all, which is the caller's cue not to dodge.
    /// </summary>
    internal static bool TryOffsetRange(Rect home, Rect region,
                                        out double minX, out double maxX,
                                        out double minY, out double maxY)
    {
        minX = maxX = minY = maxY = 0;
        if (home.IsEmpty || region.IsEmpty) return false;
        if (home.Width <= 0 || home.Height <= 0) return false;
        if (region.Width + Epsilon < home.Width || region.Height + Epsilon < home.Height) return false;

        minX = region.X - home.X;
        maxX = region.Right - home.Right;
        minY = region.Y - home.Y;
        maxY = region.Bottom - home.Bottom;
        if (maxX < minX) maxX = minX;
        if (maxY < minY) maxY = minY;
        return true;
    }
}
