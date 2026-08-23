using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R0 "slidercreep" - the slider you just set creeps back down. An ember ghost thumb appears exactly
/// on top of the real one and slides back a single notch over a second and a half, then fades out and
/// leaves the real thumb sitting where you left it.
///
/// <para>INVARIANT: Value is never written. Not nudged, not restored, not "put back" - the setting the
/// user chose is the setting they keep, and the only thing that moved was a picture of a thumb in the
/// ghost layer. That is what makes this safe at R0 under Gentle: it is a lie about a number, not a
/// change to one, so the worst case is a double take rather than a session that quietly got louder.</para>
///
/// <para>Prefers the slider the user JUST touched (PossessionPointer.LastClicked inside ten seconds):
/// creeping a slider nobody is looking at is a tree falling in an empty forest, which is exactly the
/// Wave 2 diagnosis.</para>
/// </summary>
public sealed class SliderCreepEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Slider };

    private const double CreepMs = 1500;
    private const double FadeMs = 450;
    private const double MinNotchPx = 12;
    private const double MaxNotchPx = 70;
    private static readonly TimeSpan RecentTouch = TimeSpan.FromSeconds(10);

    private Canvas? _layer;
    private FrameworkElement? _thumbGhost;

    public override string Id => "slidercreep";
    public override PossessionRung MinRung => PossessionRung.Settle;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override bool UsesFlicker => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(3);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var el = target?.Element;
        if (el == null || ctx.Host.GhostLayer == null) return false;

        var slider = SliderOf(el);
        if (slider == null || slider.Maximum <= slider.Minimum) return false;
        if (PossessionVisual.BoundsOf(ctx.Host, slider).IsEmpty) return false;

        // Preference, not a requirement: if the user touched a DIFFERENT slider a moment ago and that
        // one is still on offer, decline this pick and let the deck come back around to the live one.
        var touched = RecentlyTouchedSlider(ctx);
        if (touched != null && !ReferenceEquals(touched, slider)) return false;

        return true;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var el = target?.Element;
        _layer = ctx.Host.GhostLayer;
        if (el == null || _layer == null) return;

        var slider = SliderOf(el);
        if (slider == null) return;

        var sliderBounds = PossessionVisual.BoundsOf(ctx.Host, slider);
        if (sliderBounds.IsEmpty) return;

        bool vertical = slider.Orientation == Orientation.Vertical;

        // The real thumb if the template gave us one, else a plausible thumb worked out from the
        // value: a ghost that starts anywhere else reads as a second control, not as this one moving.
        var thumb = FindDescendant<Thumb>(slider);
        var thumbBounds = thumb != null ? PossessionVisual.BoundsOf(ctx.Host, thumb) : Rect.Empty;
        if (thumbBounds.IsEmpty || thumbBounds.Width <= 1 || thumbBounds.Height <= 1)
            thumbBounds = EstimateThumb(slider, sliderBounds, vertical);
        if (thumbBounds.IsEmpty) return;

        double span = slider.Maximum - slider.Minimum;
        if (span <= 0) return;
        double notch = slider.SmallChange > 0 ? slider.SmallChange : span / 20.0;
        double track = vertical
            ? Math.Max(1, sliderBounds.Height - thumbBounds.Height)
            : Math.Max(1, sliderBounds.Width - thumbBounds.Width);
        double distance = Math.Clamp(notch / span * track, MinNotchPx, MaxNotchPx);

        var move = new TranslateTransform();
        var ghost = new Rectangle
        {
            Width = Math.Max(6, thumbBounds.Width),
            Height = Math.Max(6, thumbBounds.Height),
            RadiusX = Math.Min(6, thumbBounds.Width / 2),
            RadiusY = Math.Min(6, thumbBounds.Height / 2),
            Fill = EmberBrush,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = move,
            Effect = new DropShadowEffect { Color = EmberColor, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 },
        };
        Canvas.SetLeft(ghost, thumbBounds.X);
        Canvas.SetTop(ghost, thumbBounds.Y);
        _layer.Children.Add(ghost);
        _thumbGhost = ghost;

        double slow = Photosafe ? 1.3 : 1.0;
        PossAnim.To(ghost, UIElement.OpacityProperty, 0.6, 220 * slow, PossAnim.EaseOut);

        // Back one notch. "Back" is toward Minimum, which is left on a horizontal slider and DOWN on a
        // vertical one (WPF puts the minimum at the bottom).
        if (vertical) PossAnim.To(move, TranslateTransform.YProperty, distance, CreepMs * slow, PossAnim.EaseInOut);
        else PossAnim.To(move, TranslateTransform.XProperty, -distance, CreepMs * slow, PossAnim.EaseInOut);

        if (!await PossAnim.DelayAsync(CreepMs * slow + 40, ct).ConfigureAwait(true)) return;

        PossAnim.To(ghost, UIElement.OpacityProperty, 0, FadeMs * slow, PossAnim.EaseIn);
        if (!await PossAnim.DelayAsync(FadeMs * slow + 40, ct).ConfigureAwait(true)) return;

        RemoveGhost();
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        RemoveGhost();
        return Task.CompletedTask;
    }

    private void RemoveGhost()
    {
        try
        {
            if (_layer != null && _thumbGhost != null) _layer.Children.Remove(_thumbGhost);
        }
        catch { }
        _thumbGhost = null;
        _layer = null;
    }

    // ---- plumbing ------------------------------------------------------------------------------

    /// <summary>The Slider a target stands for: itself, or the one inside the row that was tagged.</summary>
    private static Slider? SliderOf(FrameworkElement? el)
    {
        if (el is Slider s) return s;
        return FindDescendant<Slider>(el);
    }

    /// <summary>Where the thumb would be if we cannot see it: value fraction along the track.</summary>
    private static Rect EstimateThumb(Slider slider, Rect bounds, bool vertical)
    {
        try
        {
            double span = slider.Maximum - slider.Minimum;
            if (span <= 0) return Rect.Empty;
            double f = Math.Clamp((slider.Value - slider.Minimum) / span, 0, 1);

            if (vertical)
            {
                double h = Math.Min(18, Math.Max(8, bounds.Height * 0.06));
                double y = bounds.Y + (1 - f) * Math.Max(0, bounds.Height - h);
                return new Rect(bounds.X + bounds.Width / 2 - 7, y, 14, h);
            }

            double w = Math.Min(18, Math.Max(8, bounds.Width * 0.06));
            double x = bounds.X + f * Math.Max(0, bounds.Width - w);
            return new Rect(x, bounds.Y + bounds.Height / 2 - 9, w, Math.Min(22, Math.Max(10, bounds.Height)));
        }
        catch { return Rect.Empty; }
    }

    /// <summary>The slider the user pressed in the last ten seconds, when it is still a live target.</summary>
    private static Slider? RecentlyTouchedSlider(PossessionContext ctx)
    {
        try
        {
            if (DateTime.Now - PossessionPointer.LastClickAt > RecentTouch) return null;
            var clicked = PossessionPointer.LastClicked;
            if (clicked == null) return null;

            var slider = SliderOf(clicked) ?? FindAncestor<Slider>(clicked);
            if (slider == null || !slider.IsVisible) return null;

            foreach (var t in ctx.Host.Targets)
            {
                if (t == null || t.Role != PossessionRole.Slider || t.IsLive) continue;
                if (ReferenceEquals(SliderOf(t.Element), slider)) return slider;
            }
            return null;
        }
        catch { return null; }
    }

    internal static T? FindDescendant<T>(DependencyObject? root, int depth = 0) where T : DependencyObject
    {
        if (root == null || depth > 16) return null;
        try
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var found = FindDescendant<T>(child, depth + 1);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }

    internal static T? FindAncestor<T>(DependencyObject? node, int depth = 0) where T : DependencyObject
    {
        try
        {
            while (node != null && depth++ < 24)
            {
                if (node is T hit) return hit;
                node = VisualTreeHelper.GetParent(node);
            }
        }
        catch { }
        return null;
    }
}
