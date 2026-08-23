using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "togglelie" - the toggle says it flipped. The knob slides to the other side, sits there for a
/// beat and a half looking exactly like a setting that just changed, then snaps back with one ember
/// blink.
///
/// <para>INVARIANT: IsChecked is never touched, no command runs, no setting is written. The knob is
/// moved by a render transform on a lease and put back by releasing it, so the control is pixel- and
/// state-identical afterwards. A haunt that actually flipped a user setting would be a bug wearing a
/// costume - and in this app a setting is a consent boundary, so this one is not negotiable.</para>
///
/// <para>Two ways in: the template usually gives us a knob (a Thumb / Ellipse / something named
/// thumb, knob or indicator) which we slide directly; when it does not, we fall back to a snapshot
/// ghost of the whole toggle with an ember knob drawn on the far side, which sells the same lie
/// without guessing at somebody else's template.</para>
///
/// <para>Named by the warden from R3 only: below that a toggle blinking to the other side and back is
/// a micro-tic that the ember charge and outline already own.</para>
/// </summary>
public sealed class ToggleLieEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Toggle };

    private static readonly string[] KnobNames = { "thumb", "knob", "indicator", "dot", "switch", "pill", "handle" };

    private const double SlideMs = 240;

    private FrameworkElement? _knob;
    private double _dx;
    private Ghost? _ghost;
    private Canvas? _layer;
    private Border? _blink;

    public override string Id => "togglelie";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;

    /// <summary>Named only once the room is collapsing (R3+).</summary>
    public override bool IsBig => (Ctx?.Rung ?? PossessionRung.Settle) >= PossessionRung.Collapse;

    public override bool UsesFlicker => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(1500);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var el = target?.Element;
        if (el == null || ctx.Host.GhostLayer == null) return false;
        if (PossessionVisual.BoundsOf(ctx.Host, el).IsEmpty) return false;

        // Either a knob we can slide, or a snapshot we can lie with. Both need real pixels.
        return el.ActualWidth > 10 && el.ActualHeight > 6;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var el = target?.Element;
        _layer = ctx.Host.GhostLayer;
        if (el == null || _layer == null) return;

        var knob = FindKnob(el);
        if (knob != null && TryMirror(el, knob, out _dx) && !TransformLease.IsLeased(knob))
        {
            _knob = knob;
            var lease = TakeLease(knob);
            if (lease != null)
            {
                PossAnim.To(lease.Translate, TranslateTransform.XProperty, _dx, SlideMs * (Photosafe ? 1.4 : 1),
                            PossAnim.EaseOut);
                await PossAnim.DelayAsync(SlideMs + 30, ct).ConfigureAwait(true);
                return;
            }
            _knob = null;
        }

        // ---- fallback: a picture of the toggle, wearing its knob on the wrong side ----------
        try
        {
            var ghost = Ghost.Capture(el, ctx.Host);
            if (ghost == null) return;
            _ghost = ghost;
            ghost.Hide();   // opacity only - the real toggle still takes the click it always did

            double h = Math.Max(8, ghost.SizeDip.Height * 0.72);
            double w = h;
            bool knobOnLeft = KnobLooksLeft(el, knob);
            double x = knobOnLeft
                ? ghost.Origin.X + ghost.SizeDip.Width - w - Math.Max(2, ghost.SizeDip.Height * 0.14)
                : ghost.Origin.X + Math.Max(2, ghost.SizeDip.Height * 0.14);
            double y = ghost.Origin.Y + (ghost.SizeDip.Height - h) / 2;

            var pill = new Ellipse
            {
                Width = w,
                Height = h,
                Fill = EmberBrush,
                Opacity = 0.85,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = EmberColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
            };
            ghost.AddExtra(pill, x, y);
            await PossAnim.DelayAsync(120, ct).ConfigureAwait(true);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession togglelie ghost failed: {Error}", ex.Message); }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        double ms = UndoMs(duration, 150, 320);

        // The snap back is the punchline, so it gets its own ember blink (a slow bloom when photosafe).
        Blink();

        var lease = Lease;
        if (lease != null && ms > 0 && Math.Abs(_dx) > 0.01)
        {
            PossAnim.To(lease.Translate, TranslateTransform.XProperty, 0, ms, PossAnim.EaseOut);
            await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
        }

        try { _ghost?.Dispose(); } catch { }
        _ghost = null;

        if (ms > 0)
        {
            await PossAnim.DelayAsync(Photosafe ? 420 : 240, CancellationToken.None).ConfigureAwait(true);
        }
        RemoveBlink();

        _knob = null;
        _dx = 0;
        _layer = null;
    }

    /// <summary>One ember bloom over the toggle as it snaps back. Photosafe: slower, dimmer, no snap.</summary>
    private void Blink()
    {
        try
        {
            var ctx = Ctx;
            var el = Element;
            var layer = _layer ?? ctx?.Host.GhostLayer;
            if (ctx == null || el == null || layer == null) return;

            var bounds = PossessionVisual.BoundsOf(ctx.Host, el);
            if (bounds.IsEmpty) return;

            var plate = new Border
            {
                Width = bounds.Width,
                Height = bounds.Height,
                CornerRadius = new CornerRadius(Math.Min(12, bounds.Height / 2)),
                Background = EmberBrush,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(plate, bounds.X);
            Canvas.SetTop(plate, bounds.Y);
            layer.Children.Add(plate);
            _blink = plate;
            _layer = layer;

            if (Photosafe) PossAnim.Pulse(plate, UIElement.OpacityProperty, 0.22, 260, 460);
            else PossAnim.Pulse(plate, UIElement.OpacityProperty, 0.45, 90, 220);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession togglelie blink failed: {Error}", ex.Message); }
    }

    private void RemoveBlink()
    {
        try
        {
            if (_layer != null && _blink != null) _layer.Children.Remove(_blink);
        }
        catch { }
        _blink = null;
    }

    // ---- knob plumbing -------------------------------------------------------------------------

    /// <summary>The moving part of the toggle: something named like a knob, else a Thumb, else the
    /// first Ellipse, else a small Border sitting inside a wider one.</summary>
    private static FrameworkElement? FindKnob(FrameworkElement toggle)
    {
        try
        {
            var byName = FindFirst(toggle, fe =>
            {
                var n = fe.Name;
                if (string.IsNullOrEmpty(n)) return false;
                var lower = n.ToLowerInvariant();
                foreach (var k in KnobNames) if (lower.Contains(k)) return true;
                return false;
            });
            if (byName != null) return byName;

            var thumb = SliderCreepEffect.FindDescendant<System.Windows.Controls.Primitives.Thumb>(toggle);
            if (thumb != null) return thumb;

            var ellipse = SliderCreepEffect.FindDescendant<Ellipse>(toggle);
            if (ellipse != null && ellipse.ActualWidth > 3) return ellipse;

            return FindFirst(toggle, fe => fe is Border
                                           && fe.ActualWidth > 4
                                           && fe.ActualWidth < toggle.ActualWidth * 0.6
                                           && fe.ActualHeight > toggle.ActualHeight * 0.35);
        }
        catch { return null; }
    }

    /// <summary>The offset that puts the knob on the OTHER side of the same track (design units, which
    /// is what a TransformLease speaks). False when there is nowhere to slide to.</summary>
    private static bool TryMirror(FrameworkElement toggle, FrameworkElement knob, out double dx)
    {
        dx = 0;
        try
        {
            if (knob.ActualWidth <= 0 || toggle.ActualWidth <= 0) return false;
            var at = knob.TranslatePoint(new Point(0, 0), toggle);
            double left = at.X;
            double slot = toggle.ActualWidth - knob.ActualWidth;
            if (slot <= 8) return false;

            dx = slot - 2 * left;
            return Math.Abs(dx) >= 6;
        }
        catch { return false; }
    }

    /// <summary>Which side the knob is on now, for the fallback ghost (default: the left).</summary>
    private static bool KnobLooksLeft(FrameworkElement toggle, FrameworkElement? knob)
    {
        try
        {
            if (knob == null || toggle.ActualWidth <= 0) return true;
            var at = knob.TranslatePoint(new Point(0, 0), toggle);
            return at.X < (toggle.ActualWidth - knob.ActualWidth) / 2;
        }
        catch { return true; }
    }

    /// <summary>First descendant FrameworkElement matching a predicate (depth-capped, never throws).</summary>
    private static FrameworkElement? FindFirst(DependencyObject? root, Func<FrameworkElement, bool> match, int depth = 0)
    {
        if (root == null || depth > 16) return null;
        try
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe && match(fe)) return fe;
                var found = FindFirst(child, match, depth + 1);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }
}
