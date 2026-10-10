// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileBubbleTierFx.cs (WPF 7.1.5).
// The Basic / Prime neon on the profile bubble's rim: a small wobble every 8 seconds about its
// resting -12 degree tilt, and it shows big in a popup to the LEFT of the bubble while the
// account menu is open (the two travel together, owner 2026-10-09). Never over the bubble or the menu. Motion follows the app's level.
using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const double ProfileTierRestAngle = -12;
        private const double ProfileTierWobbleDegrees = 9;
        private const double ProfileTierHoverStartScale = 0.35;
        /// <summary>How far above the badge's own line the big copy sits: clear of the menu's name row.</summary>
        private const double ProfileTierLift = 46;

        private DispatcherTimer? _profileTierWobbleTimer;
        private DispatcherTimer? _profileTierWobbleRun, _profileTierGrowRun;
        private RotateTransform? _profileTierTilt;
        private ScaleTransform? _profileTierBigScale;

        private Image? ProfileTierBadgeSmall => Named<Image>("ProfileBubbleTierBadge");
        internal Popup? ProfileTierBadgePopupHost => Named<Popup>("ProfileTierBadgePopup");
        private Image? ProfileTierBadgeBigImage => Named<Image>("ProfileTierBadgeBig");

        /// <summary>Called once the badge has art (RefreshProfileBubbleTierBadge): the wobble needs no hover.</summary>
        private void EnsureProfileTierFx()
        {
            if (_profileTierWobbleTimer != null) return;
            if (ProfileTierBadgeSmall is not { } small || ProfileTierBadgeBigImage is not { } big) return;
            _profileTierTilt = new RotateTransform(ProfileTierRestAngle);
            small.RenderTransform = _profileTierTilt;
            _profileTierBigScale = new ScaleTransform(1, 1);
            big.RenderTransform = _profileTierBigScale;

            _profileTierWobbleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(8),
            };
            _profileTierWobbleTimer.Tick += (_, _) => ProfileTierWobble();
            _profileTierWobbleTimer.Start();
            Closed += (_, _) => { _profileTierWobbleTimer?.Stop(); _profileTierWobbleRun?.Stop(); _profileTierGrowRun?.Stop(); };
        }

        private void ProfileTierWobble()
        {
            try
            {
                if (_profileTierTilt == null || !IsVisible || WindowState == WindowState.Minimized) return;
                if (ProfileTierBadgeSmall?.IsVisible != true) return;
                if (!AmbientFxCanvas.Env.AllowAmbientLoops || ProfileTierBadgePopupHost?.IsOpen == true) return;

                const double a = ProfileTierWobbleDegrees;
                const double r = ProfileTierRestAngle;
                _profileTierWobbleRun?.Stop();
                _profileTierWobbleRun = TransformTween.Run(_profileTierTilt, TimeSpan.FromMilliseconds(700),
                    new (double, AvaloniaProperty, double)[]
                    {
                        (0, RotateTransform.AngleProperty, r),
                        (0.15, RotateTransform.AngleProperty, r + a),
                        (0.35, RotateTransform.AngleProperty, r - a * 0.75),
                        (0.55, RotateTransform.AngleProperty, r + a * 0.45),
                        (0.75, RotateTransform.AngleProperty, r - a * 0.2),
                        (1, RotateTransform.AngleProperty, r),
                    });
            }
            catch (Exception ex) { Log.Debug("profile tier badge wobble failed: {E}", ex.Message); }
        }

        private void ShowProfileTierBig()
        {
            try
            {
                if (ProfileTierBadgeSmall is not { IsVisible: true, Source: { } art }) return;
                EnsureProfileTierFx();
                if (ProfileTierBadgePopupHost is not { } popup || ProfileTierBadgeBigImage is not { } big) return;
                big.Source = art;
                popup.HorizontalOffset = -4;
                popup.VerticalOffset = -ProfileTierLift;
                popup.IsOpen = true;

                if (_profileTierBigScale == null) return;
                _profileTierGrowRun?.Stop();
                if (!AmbientFxCanvas.Env.AllowTransitions)
                {
                    _profileTierBigScale.ScaleX = _profileTierBigScale.ScaleY = 1;
                    return;
                }
                _profileTierGrowRun = TransformTween.Run(_profileTierBigScale, TimeSpan.FromMilliseconds(260),
                    new (double, AvaloniaProperty, double)[]
                    {
                        (0, ScaleTransform.ScaleXProperty, ProfileTierHoverStartScale),
                        (1, ScaleTransform.ScaleXProperty, 1),
                        (0, ScaleTransform.ScaleYProperty, ProfileTierHoverStartScale),
                        (1, ScaleTransform.ScaleYProperty, 1),
                    }, new BackEaseOut());
            }
            catch (Exception ex) { Log.Debug("profile tier badge hover failed: {E}", ex.Message); }
        }

        private void HideProfileTierBig()
        {
            if (ProfileTierBadgePopupHost is { } popup) popup.IsOpen = false;
        }
    }
}
