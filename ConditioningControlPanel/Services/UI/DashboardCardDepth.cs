using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel.Services;

/// <summary>Visual feedback only. The owning card keeps all click and toggle semantics.</summary>
internal sealed class DashboardCardDepth
{
    private readonly FrameworkElement _owner;
    private readonly UIElement _bevel;
    private readonly TranslateTransform _travel = new();
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _active;
    private bool _pressed;
    private double _target = double.NaN;

    public DashboardCardDepth(FrameworkElement owner, UIElement face, UIElement bevel,
        Func<bool> enabled, Func<bool> active, Func<MouseButtonEventArgs, bool> accepts)
    {
        _owner = owner;
        _bevel = bevel;
        _enabled = enabled;
        _active = active;
        face.RenderTransform = _travel;
        owner.PreviewMouseDown += (_, e) =>
        {
            if (!_enabled() || e.ChangedButton is not (MouseButton.Left or MouseButton.Right) || !accepts(e)) return;
            _pressed = true;
            Refresh();
        };
        owner.PreviewMouseUp += (_, _) => Release();
        owner.MouseLeave += (_, _) => Release();
        owner.LostMouseCapture += (_, _) => Release();
        owner.Unloaded += (_, _) => Release();
        owner.IsVisibleChanged += (_, _) => { if (!owner.IsVisible) Release(); };
        owner.Loaded += (_, _) => Refresh();
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
        double target = !enabled ? 0 : _pressed ? 4 : active ? 2.5 : 0;
        _bevel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _bevel.Opacity = _pressed ? 1 : active ? 0.55 : 0.8;
        if (target == _target && MotionFx.AllowTransitions) return;
        _target = target;
        double from = _travel.Y;
        _travel.BeginAnimation(TranslateTransform.YProperty, null);
        _travel.Y = target;
        if (MotionFx.AllowTransitions && _owner.IsLoaded && _owner.IsVisible)
            _travel.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(_pressed ? 70 : 140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
    }
}
