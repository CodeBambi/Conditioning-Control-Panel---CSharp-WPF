using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Homework;
using ConditioningControlPanel.Views.Controls;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Homework on the panel. THE CARD sits in the remote overlay's cell, one Z step under it, comes
    /// up at the next idle moment (<see cref="HomeworkGateRule"/>, every 2 s while due) and hides the
    /// embedded browser while up (WebView2 airspace). THE WATCH plays the video the way a remote
    /// <c>play_hypnotube</c> does and samples <see cref="BrowserVideoTimeSource"/> into
    /// <see cref="WatchAccumulator"/> every 500 ms while the page is the homework and visible; 30 s
    /// off the page ends it. Panic and the emergency exit are not touched here.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly TimeSpan HomeworkOffPageGrace = TimeSpan.FromSeconds(30);

        private HomeworkGateCard? _homeworkGate;
        private DispatcherTimer? _homeworkGateTimer;
        private DispatcherTimer? _homeworkWatchTimer;
        private bool _homeworkHidBrowser;
        private bool _homeworkHandingIn;

        private HomeworkCurrent? _homeworkWatch;
        private WatchAccumulator? _homeworkAcc;
        private BrowserVideoTimeSource? _homeworkSource;
        private Microsoft.Web.WebView2.Wpf.WebView2? _homeworkSourceView;
        private DateTime _homeworkOnPageUtc;

        private void InitializeHomework()
        {
            if (App.Homework == null) return;
            if (RemoteControlOverlay.Parent is Grid host)
            {
                _homeworkGate = new HomeworkGateCard();
                Grid.SetRow(_homeworkGate, Grid.GetRow(RemoteControlOverlay));
                Grid.SetRowSpan(_homeworkGate, Grid.GetRowSpan(RemoteControlOverlay));
                Grid.SetColumn(_homeworkGate, Grid.GetColumn(RemoteControlOverlay));
                Grid.SetColumnSpan(_homeworkGate, Grid.GetColumnSpan(RemoteControlOverlay));
                Panel.SetZIndex(_homeworkGate, Panel.GetZIndex(RemoteControlOverlay) - 1);
                host.Children.Add(_homeworkGate);
                _homeworkGate.WatchRequested += StartHomeworkWatch;
                _homeworkGate.LeaveRequested += () => { HideHomeworkGate(); _ = App.Homework?.SetOptInAsync(false); };
            }

            _homeworkGateTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
            _homeworkGateTimer.Tick += (_, _) => CheckHomeworkGate();
            _homeworkWatchTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            _homeworkWatchTimer.Tick += (_, _) => TickHomeworkWatch();

            App.Homework.Changed += CheckHomeworkGate;
            App.Homework.HandedIn += () => App.Notifications?.Show(Loc.Get("homework_toast_done"), NotificationType.Success);
            CheckHomeworkGate();
        }

        /// <summary>True while homework stands between a launcher tile and its game.</summary>
        internal bool HomeworkBlocksGames => App.Homework?.IsDue == true;

        /// <summary>The launcher's way in: bring the card up now if the panel is idle.</summary>
        internal void PresentHomeworkFromLauncher() => CheckHomeworkGate();

        private void CheckHomeworkGate()
        {
            try
            {
                var hw = App.Homework;
                var due = hw?.IsDue == true && hw.Current.Current != null;
                if (due) _homeworkGateTimer?.Start(); else _homeworkGateTimer?.Stop();

                if (due && HomeworkGateRule.ShouldShow(ReadHomeworkWorld(true))) ShowHomeworkGate(hw!.Current.Current!.Title);
                else HideHomeworkGate();
            }
            catch (Exception ex) { App.Logger?.Debug("Homework gate check failed: {E}", ex.Message); }
        }

        private HomeworkGateInputs ReadHomeworkWorld(bool due) => new(
            Due: due,
            SessionRunning: App.IsSessionRunning || App.IsEngineRunning,
            GameUp: ChaosWebViewHost.AnyGameActive,
            LockCardOpen: LockCardWindow.IsAnyOpen() || BubbleCountWindow.IsAnyOpen(),
            FullscreenEffect: _isBrowserFullscreen || App.Video?.IsPlaying == true,
            ModalUp: App.StartupLadder?.IsModalUp == true || App.Tutorial?.IsActive == true
                     || App.Lockdown?.IsActive == true || RemoteControlOverlay.Visibility == Visibility.Visible,
            PanelAway: !IsVisible || WindowState == WindowState.Minimized || App.StartupLadder?.Held == true,
            Watching: _homeworkWatch != null || _homeworkHandingIn);

        private void ShowHomeworkGate(string title)
        {
            if (_homeworkGate == null) return;
            if (!_homeworkGate.IsUp && SettingsTab.BrowserContainer.Visibility == Visibility.Visible)
            {
                SettingsTab.BrowserContainer.Visibility = Visibility.Hidden;
                _homeworkHidBrowser = true;
            }
            _homeworkGate.Present(title);
        }

        private void HideHomeworkGate()
        {
            if (_homeworkGate?.IsUp != true) return;
            _homeworkGate.Dismiss();
            if (!_homeworkHidBrowser) return;
            _homeworkHidBrowser = false;
            // Never hand the browser back over the remote overlay's own blindfold.
            if (RemoteControlOverlay.Visibility != Visibility.Visible || _remoteOverlayBrowserRevealed)
                SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
        }

        private void StartHomeworkWatch()
        {
            var hw = App.Homework?.Current.Current;
            if (hw == null) return;
            _homeworkWatch = hw;
            _homeworkAcc = new WatchAccumulator();
            _homeworkOnPageUtc = DateTime.UtcNow;
            HideHomeworkGate();
            App.Logger?.Information("Homework: watch started for {Day}", hw.Day);
            if (!NavigateToUrlInBrowser(hw.Url, autoPlayFullscreen: true)) { EndHomeworkWatch(); return; }
            _homeworkWatchTimer?.Start();
        }

        private void EndHomeworkWatch()
        {
            _homeworkWatchTimer?.Stop();
            _homeworkSource?.Dispose();
            _homeworkSource = null;
            _homeworkSourceView = null;
            _homeworkWatch = null;
            _homeworkAcc = null;
            CheckHomeworkGate();
        }

        private void TickHomeworkWatch()
        {
            try
            {
                if (_homeworkWatch is not { } hw || _homeworkAcc is not { } acc) { EndHomeworkWatch(); return; }
                var view = _browser?.WebView;
                var core = view?.CoreWebView2;
                if (view == null || core == null || !WatchAccumulator.IsHomeworkPage(core.Source, hw.Url))
                {
                    // Still coming up, or they went somewhere else. Give it time, then let the card back.
                    if (DateTime.UtcNow - _homeworkOnPageUtc > HomeworkOffPageGrace) EndHomeworkWatch();
                    return;
                }
                _homeworkOnPageUtc = DateTime.UtcNow;

                if (!ReferenceEquals(view, _homeworkSourceView))
                {
                    _homeworkSource?.Dispose();
                    _homeworkSource = new BrowserVideoTimeSource(view);
                    _homeworkSource.Attach();
                    _homeworkSourceView = view;
                }

                var window = Window.GetWindow(view);
                var visible = view.IsVisible && window is { IsVisible: true } && window.WindowState != WindowState.Minimized;
                acc.Sample(_homeworkSource!.GetCurrentTimeSeconds(), _homeworkSource.GetDurationSeconds(), visible);
                if (!acc.IsComplete) return;

                var (watched, duration) = (acc.WatchedSeconds, acc.DurationSeconds);
                App.Logger?.Information("Homework: watched {W:0}s of {D:0}s, handing in", watched, duration);
                _homeworkHandingIn = true;   // holds the card back until the server has answered
                EndHomeworkWatch();
                _ = HandInHomeworkAsync(hw, watched, duration);
            }
            catch (Exception ex) { App.Logger?.Debug("Homework watch tick failed: {E}", ex.Message); }
        }

        /// <summary>Accepted: HandedIn toasts. No answer: kept, not due. Refused: the card comes back.</summary>
        private async System.Threading.Tasks.Task HandInHomeworkAsync(HomeworkCurrent hw, double watched, double duration)
        {
            try { if (App.Homework != null) await App.Homework.HandInAsync(hw, watched, duration); }
            catch (Exception ex) { App.Logger?.Debug("Homework hand-in failed: {E}", ex.Message); }
            finally
            {
                _homeworkHandingIn = false;
                CheckHomeworkGate();
            }
        }
    }
}
