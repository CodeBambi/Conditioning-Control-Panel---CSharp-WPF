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
    /// <para><b>One truth.</b> <c>AppSettings.DashboardBrowserCollapsed</c> is the whole state.
    /// The two row heights, the body's Visibility, the chevron glyph, the tooltip and the
    /// billboard are all derived from it in <see cref="SettleBrowserFold"/>, which re-reads the
    /// bool every time and is safe to call as often as you like. The height ease does not carry
    /// the state with it: whatever the animation was started for, it lands on
    /// <c>SettleBrowserFold</c>, so an ease that is interrupted, replaced or arrives late cannot
    /// leave the card looking folded while the setting says open. The dashboard becoming visible
    /// settles too, which is the backstop for any path nobody thought of.</para>
    ///
    /// <para><b>What folds.</b> The header strip stays - title, site radios, reload, status, mute,
    /// Pop Out and the chevron - and <c>BrowserFoldBody</c>, everything under it, collapses. The
    /// fold deliberately collapses that ONE element instead of <c>BrowserContainer</c>: the
    /// container's Visibility already has owners (session start/stop, the remote-control takeover,
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
    /// both directions, and only comes back at the settle: a native HWND that is not clipped by
    /// WPF must not be in flight over the companion strip for 180ms.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Interaction motion, so the short end of the 80-400ms band.</summary>
        private const int BrowserFoldMs = 180;

        private bool _browserFoldAnimating;

        /// <summary>
        /// A temporary reveal is in force: the app called the browser, so the card is open even
        /// though the saved preference says folded. Deliberately NOT persisted - it lives for this
        /// run of the app and dies with the process.
        /// </summary>
        private bool _browserRevealed;

        /// <summary>
        /// The one truth, read fresh every time and never cached: the saved preference, minus a
        /// reveal in force. Every surface the fold owns derives from this and nothing else.
        /// </summary>
        private bool BrowserFolded =>
            (App.Settings?.Current?.DashboardBrowserCollapsed ?? true) && !_browserRevealed;

        /// <summary>Restores the saved fold at startup. No animation: this is the shape the
        /// dashboard opens in, not a transition the user asked for.</summary>
        internal void InitDashboardBrowserFold()
        {
            try { ApplyBrowserFold(animate: false); }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitDashboardBrowserFold failed"); }
        }

        /// <summary>
        /// Backstop. Anything that could have left a surface behind - an ease killed by a tab
        /// switch, a theme reload, a path nobody thought of - is corrected the next time the
        /// dashboard comes on screen, because the settle only ever reads the bool.
        /// </summary>
        internal void ResettleBrowserFold()
        {
            try { if (!_browserFoldAnimating) SettleBrowserFold(); }
            catch (Exception ex) { App.Logger?.Debug("ResettleBrowserFold: {E}", ex.Message); }
        }

        /// <summary>
        /// The app is calling the browser - a link from the companion or the avatar, a remote
        /// control command, a site radio, the reload button, a pop-out coming home - so the card
        /// opens whether or not the user left it folded.
        ///
        /// <para>A reveal is NOT a preference. It never writes
        /// <c>AppSettings.DashboardBrowserCollapsed</c>: only the chevron does that. So the card
        /// stays open for the rest of this run - it does not slam shut on a timer while somebody is
        /// watching a page, and the owner's call is that nothing folds it back but a click - and
        /// the next launch opens folded again if that is what the setting says.</para>
        /// </summary>
        /// <param name="reason">Short tag for the log, e.g. "navigate", "site-toggle".</param>
        internal void RevealDashboardBrowser(string reason)
        {
            try
            {
                if (!BrowserFolded) { _browserRevealed = true; return; }
                _browserRevealed = true;
                App.Logger?.Debug("Dashboard browser revealed by {Reason}", reason);
                ApplyBrowserFold(animate: MotionFx.AllowTransitions);
            }
            catch (Exception ex) { App.Logger?.Debug("RevealDashboardBrowser({Reason}): {E}", reason, ex.Message); }
        }

        internal void BtnFoldBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_browserFoldAnimating) return;

                var settings = App.Settings?.Current;
                if (settings == null) return;

                // The chevron is the user stating a preference, so it writes the preference and
                // spends any reveal in force. Toggling against the EFFECTIVE state, not the stored
                // one: on a revealed card the chevron says "open", and clicking it has to shut the
                // card rather than be a no-op that re-affirms a bool nobody can see.
                bool collapsed = BrowserFoldRule.Toggle(BrowserFolded);
                _browserRevealed = false;
                settings.DashboardBrowserCollapsed = collapsed;
                App.Settings?.Save();
                ApplyBrowserFold(animate: MotionFx.AllowTransitions);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BtnFoldBrowser_Click failed"); }
        }

        /// <summary>
        /// Paints the fold from the saved bool. <paramref name="animate"/> is only ever true when
        /// the motion gate allows transitions; either way the last thing that happens is a settle.
        /// </summary>
        private void ApplyBrowserFold(bool animate)
        {
            var frame = SettingsTab?.BrowserCardFrame;
            var body = SettingsTab?.BrowserFoldBody;
            if (frame == null || body == null) return;

            double from = frame.ActualHeight;
            frame.BeginAnimation(FrameworkElement.HeightProperty, null);

            if (!animate || from <= 0 || !IsVisible)
            {
                SettleBrowserFold();
                return;
            }

            // Settle first so the rows and the chevron are already where they belong, then take
            // the body out of the way and measure the landing height. The body - and with it the
            // native window - stays Collapsed for the whole ease in both directions.
            SettleBrowserFold();
            body.Visibility = Visibility.Collapsed;
            try { frame.UpdateLayout(); } catch { }
            double to = frame.ActualHeight;

            if (to <= 0 || Math.Abs(to - from) < 1.0)
            {
                SettleBrowserFold();
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
                try { SettleBrowserFold(); }
                catch (Exception ex) { App.Logger?.Debug("Browser fold settle: {E}", ex.Message); }
            };
            frame.BeginAnimation(FrameworkElement.HeightProperty, ease);
        }

        /// <summary>
        /// Re-reads the bool and puts every surface the fold owns where that bool says it goes.
        /// Idempotent, cheap, and the single place any of these six values is written.
        /// </summary>
        private void SettleBrowserFold()
        {
            var dash = SettingsTab;
            var frame = dash?.BrowserCardFrame;
            var body = dash?.BrowserFoldBody;
            var cardRow = dash?.BrowserCardRow;
            var foldRow = dash?.BrowserFoldRow;
            if (dash == null || frame == null || body == null || cardRow == null || foldRow == null) return;

            bool collapsed = BrowserFolded;
            var star = new GridLength(1, GridUnitType.Star);

            frame.BeginAnimation(FrameworkElement.HeightProperty, null);
            frame.Height = double.NaN;
            cardRow.Height = BrowserFoldRule.CardRowIsStar(collapsed) ? star : GridLength.Auto;
            foldRow.Height = BrowserFoldRule.FoldRowIsStar(collapsed) ? star : new GridLength(0);
            body.Visibility = BrowserFoldRule.BodyShown(collapsed) ? Visibility.Visible : Visibility.Collapsed;

            if (dash.TxtFoldBrowser != null) dash.TxtFoldBrowser.Text = BrowserFoldRule.Chevron(collapsed);
            if (dash.BtnFoldBrowser != null)
                dash.BtnFoldBrowser.ToolTip = Loc.Get(BrowserFoldRule.TooltipKey(collapsed));

            OnBrowserFoldChanged(collapsed);
        }

        /// <summary>
        /// The one hook the rest of the fold hangs off, called on every settle with the state the
        /// bool says. The billboard that fills the freed row listens here.
        /// </summary>
        partial void OnBrowserFoldChanged(bool collapsed);
    }
}
