// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Avatar.cs:37-125 (Takeover countdown
// bar): InitTakeoverCountdownBar, OnAutonomyEnabledChangedForBar, StartCountdownTicker,
// TakeoverCountdown_Tick. A thin pink bar under the avatar that drains toward the next random Takeover
// action. The 100 ms ticker runs only while Takeover is enabled (no always-on render loop when idle).
// The WPF x:Named TakeoverCountdownScale is a ScaleTransform set on the fill here.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _takeoverCountdownTimer;
        private AutonomyScheduler? _barAutonomy;
        private ScaleTransform? _takeoverCountdownScale;

        /// <summary>Where the bar reads Takeover from (the shell's scheduler); a seam for tests.</summary>
        internal Func<AutonomyScheduler?> TakeoverSource { get; set; } =
            () => Windows.MainShellWindow.Current?.Autonomy;

        /// <summary>WPF InitTakeoverCountdownBar: idempotent, hooks EnabledChanged, starts the
        /// ticker when Takeover is already on.</summary>
        internal void InitTakeoverCountdownBar()
        {
            try
            {
                var autonomy = TakeoverSource();
                if (autonomy == null) return;
                if (_barAutonomy != null) _barAutonomy.EnabledChanged -= OnAutonomyEnabledChangedForBar;
                _barAutonomy = autonomy;
                autonomy.EnabledChanged += OnAutonomyEnabledChangedForBar;
                Closed -= OnClosedStopCountdownBar;
                Closed += OnClosedStopCountdownBar;
                if (autonomy.IsEnabled) StartCountdownTicker();
            }
            catch { }
        }

        private void OnClosedStopCountdownBar(object? sender, EventArgs e)
        {
            try
            {
                if (_barAutonomy != null) _barAutonomy.EnabledChanged -= OnAutonomyEnabledChangedForBar;
                _takeoverCountdownTimer?.Stop();
            }
            catch { }
        }

        private void OnAutonomyEnabledChangedForBar(object? sender, bool enabled)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnAutonomyEnabledChangedForBar(sender, enabled));
                return;
            }
            try
            {
                if (enabled) StartCountdownTicker();
                else
                {
                    _takeoverCountdownTimer?.Stop();
                    if (this.FindControl<Border>("TakeoverCountdownBar") is { } bar) bar.IsVisible = false;
                }
            }
            catch { }
        }

        private void StartCountdownTicker()
        {
            if (_takeoverCountdownTimer == null)
            {
                _takeoverCountdownTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
                _takeoverCountdownTimer.Tick += (_, _) => TakeoverCountdownTick();
            }
            _takeoverCountdownTimer.Start();
        }

        /// <summary>WPF TakeoverCountdown_Tick: shown only with ShowTakeoverCountdownBar on and Takeover
        /// running with a random fire scheduled; the fill scales 1 (full) to 0 (about to fire).</summary>
        internal void TakeoverCountdownTick()
        {
            try
            {
                if (this.FindControl<Border>("TakeoverCountdownBar") is not { } bar) return;
                var autonomy = _barAutonomy ?? TakeoverSource();
                var show = CoreSettings.Current?.ShowTakeoverCountdownBar == true && autonomy?.IsEnabled == true;
                double? frac = show ? autonomy!.NextRandomFireFraction : null;
                if (frac == null)
                {
                    if (bar.IsVisible) bar.IsVisible = false;
                    return;
                }
                if (!bar.IsVisible) bar.IsVisible = true;
                if (_takeoverCountdownScale == null && this.FindControl<Border>("TakeoverCountdownFill") is { } fill)
                {
                    _takeoverCountdownScale = new ScaleTransform(1, 1);
                    fill.RenderTransform = _takeoverCountdownScale;
                }
                if (_takeoverCountdownScale != null) _takeoverCountdownScale.ScaleX = frac.Value;
            }
            catch { }
        }
    }
}
