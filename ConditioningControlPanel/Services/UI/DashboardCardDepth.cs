using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls.Depth;

namespace ConditioningControlPanel.Services;

/// <summary>Visual feedback only. The owning card keeps all click and toggle semantics.
/// Polish wave 10 (depth): the mosaic takes the shared lamp. Travel comes from
/// <see cref="DepthRules.TravelFor"/> (pressed PressTravelPx, on ActiveSinkPx, hovered idle
/// lifts HoverLiftPx), an off tile at rest stands HoverLiftPx/2 proud of its socket, the bevel
/// swaps DepthRaisedBevel / DepthPressedBevel by state, and a hovered tile tilts toward the
/// pointer where <see cref="DepthRules.TiltAllowed"/> says so (the launcher tile recipe).</summary>
internal sealed class DashboardCardDepth
{
    /// <summary>An off tile at rest stands this far proud of its socket (up = negative).</summary>
    internal const double RestLiftPx = DepthRules.HoverLiftPx / 2;

    private readonly FrameworkElement _owner;
    private readonly UIElement _bevel;
    private readonly TranslateTransform _travel = new();
    private readonly RotateTransform _tilt = new();
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _active;
    private bool _pressed;
    private bool _hovered;
    private double _target = double.NaN;

    public DashboardCardDepth(FrameworkElement owner, UIElement face, UIElement bevel,
        Func<bool> enabled, Func<bool> active, Func<MouseButtonEventArgs, bool> accepts)
    {
        _owner = owner;
        _bevel = bevel;
        _enabled = enabled;
        _active = active;
        var group = new TransformGroup();
        group.Children.Add(_tilt);
        group.Children.Add(_travel);
        face.RenderTransform = group;
        face.RenderTransformOrigin = new Point(0.5, 0.5);
        owner.PreviewMouseDown += (_, e) =>
        {
            if (!_enabled() || e.ChangedButton is not (MouseButton.Left or MouseButton.Right) || !accepts(e)) return;
            _pressed = true;
            Refresh();
        };
        owner.PreviewMouseUp += (_, _) => Release();
        // Hover is the FACE's, not the owner's: a split tile hands each half its own face, so
        // only the half under the pointer lifts and leans.
        face.MouseEnter += (_, _) => { _hovered = true; Refresh(); };
        face.MouseLeave += (_, _) => { _hovered = false; SettleTilt(); Release(); };
        face.MouseMove += (_, e) => TiltToward(e.GetPosition((IInputElement)face), face.RenderSize);
        owner.MouseLeave += (_, _) => { if (_hovered) { _hovered = false; SettleTilt(); } Release(); };
        owner.LostMouseCapture += (_, _) => Release();
        owner.Unloaded += (_, _) => { _hovered = false; Release(); };
        owner.IsVisibleChanged += (_, _) => { if (!owner.IsVisible) { _hovered = false; Release(); } };
        owner.Loaded += (_, _) => Refresh();
    }

    /// <summary>Where the face sits, px DOWN from the socket line, for a state. Pure: the
    /// shared travel rule plus the off tile's resting lift.</summary>
    internal static double TravelFor(bool enabled, bool pressed, bool active, bool hovered)
    {
        if (!enabled) return 0;
        double t = DepthRules.TravelFor(enabled, pressed, active, hovered);
        return !pressed && !active && !hovered ? -RestLiftPx : t;
    }

    /// <summary>The bevel key for a state: on and pressed sit in the socket, everything else is raised.</summary>
    internal static string BevelKeyFor(bool pressed, bool active) =>
        pressed || active ? "DepthPressedBevel" : "DepthRaisedBevel";

    /// <summary>The tilt for a pointer at (nx, ny) in -1..1 over the tile: the launcher's lean.</summary>
    internal static double TiltFor(double nx, double ny) =>
        Math.Clamp(nx, -1, 1) * -Math.Clamp(ny, -1, 1) * DepthRules.TiltDegrees;

    private static bool TiltAllowed => DepthRules.TiltAllowed(MotionFx.Level, PerformanceProfile.CurrentTier);

    private void TiltToward(Point at, Size size)
    {
        try
        {
            if (!_hovered || _pressed || !_enabled() || !TiltAllowed || size.Width <= 0 || size.Height <= 0) return;
            double nx = at.X / size.Width * 2 - 1;
            double ny = at.Y / size.Height * 2 - 1;
            _tilt.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(TiltFor(nx, ny), TimeSpan.FromMilliseconds(DepthRules.Ms(DepthRules.HoverMs, MotionFx.Level))));
        }
        catch (Exception ex) { App.Logger?.Debug("DashboardCardDepth.TiltToward: {E}", ex.Message); }
    }

    private void SettleTilt()
    {
        try
        {
            int ms = DepthRules.Ms(DepthRules.HoverMs, MotionFx.Level);
            if (ms <= 0 || !_owner.IsLoaded)
            {
                _tilt.BeginAnimation(RotateTransform.AngleProperty, null);
                _tilt.Angle = 0;
                return;
            }
            _tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }
        catch (Exception ex) { App.Logger?.Debug("DashboardCardDepth.SettleTilt: {E}", ex.Message); }
    }

    private void Release()
    {
        _pressed = false;
        Refresh();
    }

    public void Refresh()
    {
        bool enabled = _enabled();
        bool active = enabled && _active();
        double target = TravelFor(enabled, _pressed, active, _hovered);
        _bevel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _bevel.Opacity = _pressed ? 1 : active ? 0.75 : 0.9;
        if (_bevel is FrameworkElement fe && fe.TryFindResource(BevelKeyFor(_pressed, active)) is Brush lamp)
        {
            if (fe is Border b) b.BorderBrush = lamp;
            else if (fe is System.Windows.Shapes.Shape shape) shape.Stroke = lamp;
        }
        if (target == _target && MotionFx.AllowTransitions) return;
        _target = target;
        double from = _travel.Y;
        _travel.BeginAnimation(TranslateTransform.YProperty, null);
        _travel.Y = target;
        int ms = DepthRules.Ms(_pressed ? DepthRules.PressMs : DepthRules.ReleaseMs, MotionFx.Level);
        if (ms <= 0 || !MotionFx.AllowTransitions || !_owner.IsLoaded || !_owner.IsVisible) return;
        if (_pressed || target >= from)
        {
            _travel.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(ms))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
            return;
        }
        // Coming back UP (release, hover): spring past rest by ReleaseOvershootPx, then settle.
        var spring = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        spring.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        spring.KeyFrames.Add(new EasingDoubleKeyFrame(target - DepthRules.ReleaseOvershootPx,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.65)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        spring.KeyFrames.Add(new EasingDoubleKeyFrame(target, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
            new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        _travel.BeginAnimation(TranslateTransform.YProperty, spring);
    }
}
