// PORTED from WPF 7.1.5 MainWindow/MainWindow.DashboardFold.cs (parity lane E3).
//
// The Home browser card folds shut (owner, 2026-09-12: "lets have the browser be hiddable in an
// expandable top down section"; default folded: "by default browser should be hidden, and
// unhidden whenever we call it"). ONE truth: AppSettings.DashboardBrowserCollapsed, minus a reveal
// in force. The two row heights, the body's visibility, the arrow and the board are all derived
// from it in SettleBrowserFold (Core BrowserFoldRule), which re-reads the bool every time and is
// safe to call as often as you like. The dashboard coming on screen settles too (the backstop).
//
// What folds: the header strip stays (title, site radios, reload, the arrow, status, mute, Pop
// Out); BrowserFoldBody, everything under it, collapses - never BrowserContainer, whose visibility
// has other owners (a running session's hide must survive a fold). The web view is never
// re-parented or disposed, so the page survives a fold.
//
// Deviation: WPF eased the card frame's height over 180 ms with the body collapsed for the whole
// ease. Here the rows settle at once (the body is collapsed either way, so the only thing lost is
// the frame's slide); the arrow, the board and the glow are the same.

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>A temporary reveal: the app called the browser, so the card is open although
        /// the saved preference says folded. Never persisted; dies with the process.</summary>
        private bool _browserRevealed;

        private bool _homeDashboardInitialized;

        /// <summary>The one truth, read fresh every time: the saved preference, minus a reveal.</summary>
        internal bool BrowserFolded => CoreSettings.Current.DashboardBrowserCollapsed && !_browserRevealed;

        private SettingsTabView? HomeTab => Named<SettingsTabView>("SettingsTab");

        /// <summary>
        /// The Home page's first attach (and every later one): restore the fold, wire and paint the
        /// favourites drawer. Idempotent; a later attach only re-settles (the backstop).
        /// </summary>
        internal void InitHomeDashboard()
        {
            try
            {
                SettleBrowserFold();
                if (_homeDashboardInitialized) return;
                _homeDashboardInitialized = true;
                InitFavoritesRail();
            }
            catch (Exception ex) { Log.Warning(ex, "InitHomeDashboard failed"); }
        }

        /// <summary>
        /// The app is calling the browser (a site radio, the reload, a companion link, a remote
        /// command, a pop-out coming home), so the card opens whether or not the user left it
        /// folded. A reveal is NOT a preference: only the arrow writes the setting, and nothing but
        /// a click folds it back for the rest of this run.
        /// </summary>
        internal void RevealDashboardBrowser(string reason)
        {
            try
            {
                bool wasFolded = BrowserFolded;
                _browserRevealed = true;
                if (!wasFolded) return;
                Log.Debug("Dashboard browser revealed by {Reason}", reason);
                SettleBrowserFold();
            }
            catch (Exception ex) { Log.Debug("RevealDashboardBrowser({Reason}): {E}", reason, ex.Message); }
        }

        /// <summary>The arrow is the user stating a preference: it writes the preference and spends
        /// any reveal. It toggles what is ON SCREEN, so on a revealed card it shuts the card.</summary>
        internal void BtnFoldBrowser_Click()
        {
            try
            {
                var s = CoreSettings.Current;
                bool collapsed = BrowserFoldRule.Toggle(BrowserFolded);
                _browserRevealed = false;
                s.DashboardBrowserCollapsed = collapsed;
                CoreSettings.Save();
                SettleBrowserFold();
            }
            catch (Exception ex) { Log.Warning(ex, "BtnFoldBrowser_Click failed"); }
        }

        /// <summary>Re-reads the bool and puts every surface the fold owns where it says. Idempotent,
        /// cheap, and the single place any of these values is written.</summary>
        private void SettleBrowserFold()
        {
            var tab = HomeTab;
            if (tab == null) return;
            bool collapsed = BrowserFolded;
            tab.BrowserCardRow.Height = BrowserFoldRule.CardRowIsStar(collapsed) ? GridLength.Star : GridLength.Auto;
            tab.BrowserFoldRow.Height = BrowserFoldRule.FoldRowIsStar(collapsed) ? GridLength.Star : new GridLength(0);
            tab.BrowserFoldBody.IsVisible = BrowserFoldRule.BodyShown(collapsed);
            tab.PaintFoldArrow(collapsed);
            OnBrowserFoldChanged(collapsed);
        }

        /// <summary>The board lives in the row a shut card gives back (BrowserFoldRule.BillboardShown).</summary>
        private void OnBrowserFoldChanged(bool collapsed)
        {
            try
            {
                if (HomeTab?.FindControl<Border>("DashBillboard") is { } board)
                    board.IsVisible = BrowserFoldRule.BillboardShown(collapsed);
            }
            catch (Exception ex) { Log.Debug("Billboard fold hook failed: {E}", ex.Message); }
        }
    }
}
