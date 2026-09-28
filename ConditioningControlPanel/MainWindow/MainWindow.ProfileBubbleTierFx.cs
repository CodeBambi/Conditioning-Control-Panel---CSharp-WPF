using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Basic / Prime neon on the profile bubble's rim, same juice as the launcher's account chip
    /// (Windows/Launcher/LauncherWindow.TierBadge.cs): a small wobble every 8 seconds about its
    /// resting -12 degree tilt, and a hover shows it big in a popup to the LEFT of the bubble,
    /// because the account menu opens below it. Never over the badge: a layered popup's pixels take
    /// the mouse and would flicker it shut. Motion follows <see cref="MotionFx"/>.
    ///
    /// <para>The hover also opens the account menu a beat later, and a popup opened later sits on top,
    /// so the big badge used to vanish under the menu (owner, 2026-09-26). It now rides
    /// <see cref="ProfileTierLift"/> px higher, so only the menu's empty top edge sits under it, and it
    /// is raised back over the menu whenever the menu opens.</para>
    /// </summary>
    public partial class MainWindow
    {
        private const double ProfileTierRestAngle = -12;
        private const double ProfileTierWobbleDegrees = 9;
        private const double ProfileTierHoverStartScale = 0.35;
        /// <summary>How far above the badge's own line the big copy sits: clear of the menu's name row.</summary>
        private const double ProfileTierLift = 46;

        private static readonly IntPtr HwndTopmostForTierBadge = new(-1);

        private DispatcherTimer? _profileTierWobbleTimer;
        private RotateTransform? _profileTierTilt;
        private ScaleTransform? _profileTierBigScale;

        private void EnsureProfileTierFx()
        {
            if (_profileTierWobbleTimer != null) return;
            _profileTierTilt = new RotateTransform(ProfileTierRestAngle);
            ProfileBubbleTierBadge.RenderTransform = _profileTierTilt;
            _profileTierBigScale = new ScaleTransform(1, 1);
            ProfileTierBadgeBig.RenderTransform = _profileTierBigScale;

            _profileTierWobbleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(8),
            };
            _profileTierWobbleTimer.Tick += (_, _) => ProfileTierWobble();
            // The account menu opens after the badge on the same hover; put the badge back on top.
            ProfileBubblePopup.Opened += (_, _) => RaiseProfileTierPopup();
            _profileTierWobbleTimer.Start();
        }

        private void ProfileTierWobble()
        {
            try
            {
                if (_profileTierTilt == null || !IsVisible || WindowState == WindowState.Minimized) return;
                if (ProfileBubbleTierBadge.Visibility != Visibility.Visible) return;
                if (!MotionFx.AllowAmbientLoops || ProfileTierBadgePopup.IsOpen) return;

                const double a = ProfileTierWobbleDegrees;
                const double r = ProfileTierRestAngle;
                var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(r + a, KeyTime.FromPercent(0.15)));
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(r - a * 0.75, KeyTime.FromPercent(0.35)));
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(r + a * 0.45, KeyTime.FromPercent(0.55)));
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(r - a * 0.2, KeyTime.FromPercent(0.75)));
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(r, KeyTime.FromPercent(1),
                    new SineEase { EasingMode = EasingMode.EaseOut }));
                _profileTierTilt.BeginAnimation(RotateTransform.AngleProperty, shake);
            }
            catch (Exception ex) { App.Logger?.Debug("profile tier badge wobble failed: {E}", ex.Message); }
        }

        private void ProfileTierBadge_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                if (ProfileBubbleTierBadge.Source == null) return;
                EnsureProfileTierFx();
                ProfileTierBadgeBig.Source = ProfileBubbleTierBadge.Source;
                ProfileTierBadgePopup.HorizontalOffset = -4;
                ProfileTierBadgePopup.VerticalOffset = -ProfileTierLift;
                ProfileTierBadgePopup.IsOpen = true;
                RaiseProfileTierPopup();

                if (_profileTierBigScale == null) return;
                if (!MotionFx.AllowTransitions)
                {
                    _profileTierBigScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    _profileTierBigScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    _profileTierBigScale.ScaleX = _profileTierBigScale.ScaleY = 1;
                    return;
                }
                var grow = new DoubleAnimation(ProfileTierHoverStartScale, 1, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 },
                };
                _profileTierBigScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                _profileTierBigScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
            catch (Exception ex) { App.Logger?.Debug("profile tier badge hover failed: {E}", ex.Message); }
        }

        /// <summary>Re-stacks the big badge above every other topmost popup (the account menu).
        /// Deferred to Loaded priority so a popup that is opening this turn has its window.</summary>
        private void RaiseProfileTierPopup()
        {
            if (!ProfileTierBadgePopup.IsOpen) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                try
                {
                    if (!ProfileTierBadgePopup.IsOpen || ProfileTierBadgePopup.Child == null) return;
                    if (PresentationSource.FromVisual(ProfileTierBadgePopup.Child) is not HwndSource src) return;
                    const uint NoSizeNoMoveNoActivate = 0x0001 | 0x0002 | 0x0010;
                    SetWindowPos(src.Handle, HwndTopmostForTierBadge, 0, 0, 0, 0, NoSizeNoMoveNoActivate);
                }
                catch (Exception ex) { App.Logger?.Debug("profile tier badge raise failed: {E}", ex.Message); }
            }));
        }

        private void ProfileTierBadge_MouseLeave(object sender, MouseEventArgs e)
        {
            ProfileTierBadgePopup.IsOpen = false;
        }
    }
}
