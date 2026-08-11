using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Tab animation helpers: per-tab ambient loops and tab transition management.
    public partial class MainWindow
    {
        #region Tab Animation Management

        private void StartSeasonTitleShimmer()
        {
            if (_seasonTitleStoryboard != null) return; // already running
            // Ambient loop: never starts at the Performance tier or under reduced motion — the
            // season banner keeps its static gradient instead.
            if (!Services.MotionFx.AllowAmbientLoops) return;
            try
            {
                // The brush and title live inside the QuestsTabView UserControl, which has its OWN
                // XAML name scope — MainWindow's scope can't resolve them by name, so the old
                // SetTargetName("QuestsTab.SeasonTitleBrush") silently failed (post partial-split)
                // and the shimmer never ran. Target the objects directly instead.
                var brush = QuestsTab?.SeasonTitleBrush;
                var title = QuestsTab?.TxtSeasonTitle;
                if (brush == null || title == null) return;

                _seasonTitleStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
                var startPt = new PointAnimation { From = new Point(-1, 0.5), To = new Point(1, 0.5), Duration = TimeSpan.FromSeconds(3) };
                Storyboard.SetTarget(startPt, brush);
                Storyboard.SetTargetProperty(startPt, new PropertyPath("StartPoint"));
                var endPt = new PointAnimation { From = new Point(0, 0.5), To = new Point(2, 0.5), Duration = TimeSpan.FromSeconds(3) };
                Storyboard.SetTarget(endPt, brush);
                Storyboard.SetTargetProperty(endPt, new PropertyPath("EndPoint"));
                var glow = new DoubleAnimation { From = 0.3, To = 0.9, Duration = TimeSpan.FromSeconds(1.5), AutoReverse = true };
                Storyboard.SetTarget(glow, title);
                Storyboard.SetTargetProperty(glow, new PropertyPath("(TextBlock.Effect).(DropShadowEffect.Opacity)"));
                _seasonTitleStoryboard.Children.Add(startPt);
                _seasonTitleStoryboard.Children.Add(endPt);
                _seasonTitleStoryboard.Children.Add(glow);
                Timeline.SetDesiredFrameRate(_seasonTitleStoryboard, AmbientFrameRate);
                _seasonTitleStoryboard.Begin(this, true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to start season title shimmer: {Error}", ex.Message);
            }
        }

        private void StopSeasonTitleShimmer()
        {
            try
            {
                _seasonTitleStoryboard?.Stop(this);
                _seasonTitleStoryboard = null;
            }
            catch { }
        }

        private void StartLockdownPulse()
        {
            if (_lockdownPulseStoryboard != null) return; // already running
            // Ambient loop: never starts at the Performance tier or under reduced motion — the
            // lockdown card keeps its static pink border instead.
            if (!Services.MotionFx.AllowAmbientLoops) return;
            try
            {
                // Same namescope trap as the season shimmer: the brush and glow live inside the
                // LockdownTabView UserControl, so the old SetTargetName("LockdownTab.…") dotted path
                // could never resolve from MainWindow's scope and threw on every Lockdown tab entry.
                // Target the objects directly.
                var borderBrush = LockdownTab?.LockdownImageBorderBrush;
                var glow = LockdownTab?.LockdownImageGlow;
                if (borderBrush == null || glow == null) return;

                _lockdownPulseStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
                var colorAnim = new ColorAnimation { From = (Color)ColorConverter.ConvertFromString("#FF1493"), To = (Color)ColorConverter.ConvertFromString("#FF69B4"), Duration = TimeSpan.FromSeconds(1.5) };
                Storyboard.SetTarget(colorAnim, borderBrush);
                Storyboard.SetTargetProperty(colorAnim, new PropertyPath(SolidColorBrush.ColorProperty));
                var blurAnim = new DoubleAnimation { From = 12, To = 22, Duration = TimeSpan.FromSeconds(1.5) };
                Storyboard.SetTarget(blurAnim, glow);
                Storyboard.SetTargetProperty(blurAnim, new PropertyPath(DropShadowEffect.BlurRadiusProperty));
                var opacAnim = new DoubleAnimation { From = 0.7, To = 1.0, Duration = TimeSpan.FromSeconds(1.5) };
                Storyboard.SetTarget(opacAnim, glow);
                Storyboard.SetTargetProperty(opacAnim, new PropertyPath(DropShadowEffect.OpacityProperty));
                _lockdownPulseStoryboard.Children.Add(colorAnim);
                _lockdownPulseStoryboard.Children.Add(blurAnim);
                _lockdownPulseStoryboard.Children.Add(opacAnim);
                Timeline.SetDesiredFrameRate(_lockdownPulseStoryboard, AmbientFrameRate);
                _lockdownPulseStoryboard.Begin(this, true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to start lockdown pulse: {Error}", ex.Message);
            }
        }

        private void StopLockdownPulse()
        {
            try
            {
                _lockdownPulseStoryboard?.Stop(this);
                _lockdownPulseStoryboard = null;
            }
            catch { }
        }

        private void StopSkillTreeAnimations()
        {
            if (!_skillTreeAnimationsActive) return;
            _skillTreeAnimationsActive = false;
            try
            {
                // Stop gradient animations on the outer border background
                if (EnhancementsTab.SkillTreeOuterBorder.Background is LinearGradientBrush bgBrush)
                {
                    foreach (var stop in bgBrush.GradientStops)
                    {
                        stop.BeginAnimation(GradientStop.OffsetProperty, null);
                        stop.BeginAnimation(GradientStop.ColorProperty, null);
                    }
                }

                // Stop particle opacity animations
                foreach (var child in EnhancementsTab.SkillTreeCanvas.Children)
                {
                    if (child is System.Windows.Shapes.Ellipse ellipse)
                    {
                        ellipse.BeginAnimation(OpacityProperty, null);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to stop skill tree animations: {Error}", ex.Message);
            }
        }

        private void RestartSkillTreeAnimations()
        {
            if (_skillTreeAnimationsActive) return;
            _skillTreeAnimationsActive = true;
            try
            {
                // Re-apply gradient animations on outer border
                EnhancementsTab.SkillTreeOuterBorder.Background = CreateAnimatedSkillTreeBrush(isHeader: false);

                // Re-animate particles
                foreach (var child in EnhancementsTab.SkillTreeCanvas.Children)
                {
                    if (child is System.Windows.Shapes.Ellipse ellipse)
                    {
                        var opacityAnim = new DoubleAnimation
                        {
                            From = 0,
                            To = 1,
                            Duration = TimeSpan.FromSeconds(2 + Random.Shared.NextDouble() * 3),
                            BeginTime = TimeSpan.FromSeconds(Random.Shared.NextDouble() * 5),
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                        };
                        ellipse.BeginAnimation(OpacityProperty, opacityAnim);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to restart skill tree animations: {Error}", ex.Message);
            }
        }

        #endregion
    }
}
