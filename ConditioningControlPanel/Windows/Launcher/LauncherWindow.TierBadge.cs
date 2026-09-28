using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The Basic / Prime neon on the account chip. At 20 px nobody read it, so it asks for a look:
/// a small wobble every 8 seconds, and a hover shows it big in a popup hung just under the chip.
/// The popup sits BELOW the badge, never over it: a layered popup's painted pixels take the
/// mouse, so a copy covering the badge would fire MouseLeave and flicker open and shut.
/// Motion follows <see cref="MotionFx"/>: no wobble without ambient loops, no pop-in easing
/// with transitions off (the big copy still shows, it just appears).
/// </summary>
public partial class LauncherWindow
{
    private const double TierWobbleEverySeconds = 8;
    private const double TierWobbleDegrees = 9;
    private const double TierHoverStartScale = 0.35;

    private DispatcherTimer? _tierWobbleTimer;
    private RotateTransform? _tierBadgeTilt;
    private ScaleTransform? _tierBigScale;

    private void EnsureTierBadgeFx()
    {
        if (_tierWobbleTimer != null) return;
        _tierBadgeTilt = new RotateTransform(0);
        TierBadge.RenderTransform = _tierBadgeTilt;
        _tierBigScale = new ScaleTransform(1, 1);
        TierBadgeBig.RenderTransform = _tierBigScale;

        _tierWobbleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(TierWobbleEverySeconds),
        };
        _tierWobbleTimer.Tick += (_, _) => TierBadgeWobble();
        _tierWobbleTimer.Start();
    }

    private void TierBadgeWobble()
    {
        try
        {
            if (_tierBadgeTilt == null || !IsVisible || TierBadge.Visibility != Visibility.Visible) return;
            if (!MotionFx.AllowAmbientLoops || TierBadgePopup.IsOpen) return;

            // A damped shake: out, back past centre, a smaller echo, rest. About 0.7 s.
            var a = TierWobbleDegrees;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(-a, KeyTime.FromPercent(0.15)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(a * 0.8, KeyTime.FromPercent(0.35)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(-a * 0.45, KeyTime.FromPercent(0.55)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(a * 0.2, KeyTime.FromPercent(0.75)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1),
                new SineEase { EasingMode = EasingMode.EaseOut }));
            _tierBadgeTilt.BeginAnimation(RotateTransform.AngleProperty, shake);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tier badge wobble failed"); }
    }

    private void TierBadge_MouseEnter(object sender, MouseEventArgs e)
    {
        try
        {
            if (TierBadge.Source == null) return;
            EnsureTierBadgeFx();
            TierBadgeBig.Source = TierBadge.Source;

            // Centre the big copy under the small one.
            var src = TierBadge.Source;
            double bigWidth = src.Height > 0 ? TierBadgeBig.Height * src.Width / src.Height : TierBadgeBig.Height;
            TierBadgePopup.HorizontalOffset = (TierBadge.ActualWidth - bigWidth) / 2 - TierBadgeBig.Margin.Left;
            TierBadgePopup.VerticalOffset = 2;
            TierBadgePopup.IsOpen = true;

            if (_tierBigScale == null) return;
            if (!MotionFx.AllowTransitions)
            {
                _tierBigScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _tierBigScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _tierBigScale.ScaleX = _tierBigScale.ScaleY = 1;
                return;
            }
            var grow = new DoubleAnimation(TierHoverStartScale, 1, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 },
            };
            _tierBigScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _tierBigScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tier badge hover failed"); }
    }

    private void TierBadge_MouseLeave(object sender, MouseEventArgs e)
    {
        TierBadgePopup.IsOpen = false;
    }

    private void TierBadgeFxStop()
    {
        _tierWobbleTimer?.Stop();
        TierBadgePopup.IsOpen = false;
    }
}
