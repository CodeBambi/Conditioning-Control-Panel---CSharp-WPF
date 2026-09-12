using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Home dashboard's browser card folds shut (owner ask, 2026-09-12: "we can recover some
    /// space, lets have the browser be hiddable in an expandable top down section"). The window
    /// keeps its size and the column keeps its widths; the card simply hands its rows back.
    ///
    /// <para>What folds. The header strip stays - title, site radios, reload, status, mute, Pop Out
    /// and the chevron - and <c>BrowserFoldBody</c>, everything under it, collapses. The fold
    /// deliberately collapses that ONE element instead of <c>BrowserContainer</c>: the container's
    /// Visibility already has owners (session start/stop, the remote-control takeover,
    /// MainWindow.Settings) and a folded card must never un-hide a browser a running session hid.
    /// A Collapsed ancestor is enough anyway, and is the stronger guarantee: it drives
    /// <c>IsVisible=false</c> on the WebView2's HwndHost, so the NATIVE window is hidden rather
    /// than merely sized to nothing - which is what airspace requires, because WPF's layout clip
    /// does not reliably clip a native child. The control is never re-parented and never disposed,
    /// so the page, its session, its audio and the pop-out flow all survive a fold, and expanding
    /// shows the same page with no reload.</para>
    ///
    /// <para>The animation is a height ease on the card frame, through the
    /// <see cref="MotionFx.AllowTransitions"/> gate. The body is Collapsed for the WHOLE of it, in
    /// both directions, and only comes back at the end of an expand: a native HWND that is not
    /// clipped by WPF must not be in flight over the companion strip for 180ms.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Interaction motion, so the short end of the 80-400ms band.</summary>
        private const int BrowserFoldMs = 180;

        private bool _browserFoldAnimating;

        /// <summary>Restores the saved fold at startup. No animation: this is the shape the
        /// dashboard opens in, not a transition the user asked for.</summary>
        internal void InitDashboardBrowserFold()
        {
            try { ApplyBrowserFold(App.Settings?.Current?.DashboardBrowserCollapsed ?? false, animate: false); }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitDashboardBrowserFold failed"); }
        }

        internal void BtnFoldBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_browserFoldAnimating) return;

                bool collapsed = !(App.Settings?.Current?.DashboardBrowserCollapsed ?? false);
                var settings = App.Settings?.Current;
                if (settings != null)
                {
                    settings.DashboardBrowserCollapsed = collapsed;
                    App.Settings?.Save();
                }
                ApplyBrowserFold(collapsed, animate: MotionFx.AllowTransitions);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BtnFoldBrowser_Click failed"); }
        }

        /// <summary>
        /// Paints the fold. <paramref name="collapsed"/> is the state to land on;
        /// <paramref name="animate"/> is only ever true when the motion gate allows transitions.
        /// </summary>
        private void ApplyBrowserFold(bool collapsed, bool animate)
        {
            var dash = SettingsTab;
            var frame = dash?.BrowserCardFrame;
            var body = dash?.BrowserFoldBody;
            var cardRow = dash?.BrowserCardRow;
            var foldRow = dash?.BrowserFoldRow;
            if (dash == null || frame == null || body == null || cardRow == null || foldRow == null) return;

            // Down = shut and this opens it; up = open and this shuts it.
            if (dash.TxtFoldBrowser != null) dash.TxtFoldBrowser.Text = collapsed ? "▾" : "▴";
            if (dash.BtnFoldBrowser != null)
                dash.BtnFoldBrowser.ToolTip = Loc.Get(collapsed ? "tooltip_browser_unfold" : "tooltip_browser_fold");

            double from = frame.ActualHeight;
            frame.BeginAnimation(FrameworkElement.HeightProperty, null);

            // The two rows trade: the card is "*" open and Auto shut, and the row under it takes
            // whatever the card gave up. Everything below in the column follows for free.
            cardRow.Height = collapsed ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            foldRow.Height = collapsed ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            if (!animate || from <= 0 || !IsVisible)
            {
                frame.Height = double.NaN;
                body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                OnBrowserFoldChanged(collapsed);
                return;
            }

            // Measure the landing height with the body already out of the way, then pin the frame
            // at where it was and ease between the two. The body (and with it the native window)
            // stays Collapsed until an expand has finished.
            body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            frame.Height = double.NaN;
            try { frame.UpdateLayout(); } catch { }
            double to = frame.ActualHeight;
            if (!collapsed) body.Visibility = Visibility.Collapsed;

            if (to <= 0 || Math.Abs(to - from) < 1.0)
            {
                frame.Height = double.NaN;
                body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                OnBrowserFoldChanged(collapsed);
                return;
            }

            frame.Height = from;
            _browserFoldAnimating = true;
            var ease = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(BrowserFoldMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            };
            ease.Completed += (_, _) =>
            {
                _browserFoldAnimating = false;
                try
                {
                    frame.BeginAnimation(FrameworkElement.HeightProperty, null);
                    frame.Height = double.NaN;
                    body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                    OnBrowserFoldChanged(collapsed);
                }
                catch (Exception ex) { App.Logger?.Debug("Browser fold settle: {E}", ex.Message); }
            };
            frame.BeginAnimation(FrameworkElement.HeightProperty, ease);
        }

        /// <summary>
        /// The one hook the rest of the fold hangs off, called once the card has settled in its new
        /// shape. The billboard that fills the freed row listens here.
        /// </summary>
        partial void OnBrowserFoldChanged(bool collapsed);
    }
}
