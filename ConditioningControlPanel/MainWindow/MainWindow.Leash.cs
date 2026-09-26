using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The leash on the panel. THE GATE sits in the remote overlay's cell, one Z step under it,
    /// and stands up at the next idle moment while a punishment is pending (the Homework gate's
    /// busy rule, <see cref="LeashUiRules.ShouldShow"/>, checked every 2 s). It hides the embedded
    /// browser while up (WebView2 airspace). Panic and Cut leash are always on it; a panic press
    /// from the gate runs the real panic ladder and stands the gate back for ten minutes. Also
    /// wires the app-wide reactions (<see cref="LeashSurfaces"/>): the ask card, the snap, the tug
    /// wobble. The launcher's game tiles open the gate first (LauncherHost).
    /// </summary>
    public partial class MainWindow
    {
        private LeashGateCard? _leashGate;
        private DispatcherTimer? _leashGateTimer;
        private bool _leashHidBrowser;
        private DateTime _leashSnoozeUntilUtc = DateTime.MinValue;
        private ILeashTaskRunner? _leashRunner;
        private readonly LeashHoldToCut _leashHold = new();

        private void InitializeLeash()
        {
            try
            {
                App.LeashedChanged += on => Dispatcher.BeginInvoke(() =>
                {
                    _trayIcon?.SyncLeashIcon();
                    // Hold-to-cut must work even with the panic key switched off.
                    if (on) _keyboardHook?.Start();
                    else _leashHold.Up();
                });
                if (RemoteControlOverlay.Parent is Grid host)
                {
                    _leashGate = new LeashGateCard();
                    Grid.SetRow(_leashGate, Grid.GetRow(RemoteControlOverlay));
                    Grid.SetRowSpan(_leashGate, Grid.GetRowSpan(RemoteControlOverlay));
                    Grid.SetColumn(_leashGate, Grid.GetColumn(RemoteControlOverlay));
                    Grid.SetColumnSpan(_leashGate, Grid.GetColumnSpan(RemoteControlOverlay));
                    // Under the remote overlay, over the Homework gate (the leash gate goes first).
                    Panel.SetZIndex(_leashGate, Panel.GetZIndex(RemoteControlOverlay) - 1);
                    host.Children.Add(_leashGate);
                    _leashGate.StartRequested += StartLeashTask;
                    _leashGate.PardonRequested += p => _ = PardonLeashAsync(p);
                    _leashGate.PanicRequested += PanicFromLeashGate;
                    _leashGate.CutRequested += () => { HideLeashGate(); LeashSurfaces.Cut(); };
                }

                LeashSurfaces.Init(() => IsVisible && WindowState != WindowState.Minimized
                    ? this
                    : Services.Launcher.LauncherHost.IsShown ? Services.Launcher.LauncherHost.WindowRef : null);
                LeashSurfaces.TugArrived += WobbleForTug;
                LeashSurfaces.CutDone += () => { _leashRunner?.Cancel(); HideLeashGate(); };

                _leashGateTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
                _leashGateTimer.Tick += (_, _) => { LeashSurfaces.Rebind(); CheckLeashGate(); };
                _leashGateTimer.Start();
                CheckLeashGate();
            }
            catch (Exception ex) { App.Logger?.Debug("Leash init failed: {E}", ex.Message); }
        }

        /// <summary>True while a punishment stands between a launcher tile and its game.</summary>
        internal bool LeashBlocksGames
        {
            get
            {
                try { return LeashLocator.Service()?.GateDue != null; } catch { return false; }
            }
        }

        /// <summary>The launcher's way in: bring the gate up now if the panel is idle. A tile press
        /// is a deliberate "I want to play", so it also ends a panic snooze.</summary>
        internal void PresentLeashGateFromLauncher()
        {
            _leashSnoozeUntilUtc = DateTime.MinValue;
            CheckLeashGate();
        }

        private void CheckLeashGate()
        {
            try
            {
                var svc = LeashLocator.Service();
                BindLeashRunner();
                var due = svc?.GateDue;
                if (due != null && Services.Leash.LeashGateRule.ShouldShow(ReadLeashWorld(true)))
                    ShowLeashGate(due, svc!.Snapshot.Me?.Pardons ?? 0);
                else HideLeashGate();
            }
            catch (Exception ex) { App.Logger?.Debug("Leash gate check failed: {E}", ex.Message); }
        }

        private Services.Leash.LeashGateInputs ReadLeashWorld(bool due) => new(
            Due: due,
            SessionRunning: App.IsSessionRunning || App.IsEngineRunning,
            GameUp: ChaosWebViewHost.AnyGameActive,
            LockCardOpen: LockCardWindow.IsAnyOpen() || BubbleCountWindow.IsAnyOpen(),
            FullscreenEffect: _isBrowserFullscreen || App.Video?.IsPlaying == true,
            ModalUp: App.StartupLadder?.IsModalUp == true || App.Tutorial?.IsActive == true
                     || App.Lockdown?.IsActive == true || RemoteControlOverlay.Visibility == Visibility.Visible,
            PanelAway: !IsVisible || WindowState == WindowState.Minimized || App.StartupLadder?.Held == true,
            Watching: _leashRunner?.IsRunning == true || DateTime.UtcNow < _leashSnoozeUntilUtc);

        private void ShowLeashGate(Punishment p, int pardons)
        {
            if (_leashGate == null) return;
            if (!_leashGate.IsUp && SettingsTab.BrowserContainer.Visibility == Visibility.Visible)
            {
                SettingsTab.BrowserContainer.Visibility = Visibility.Hidden;
                _leashHidBrowser = true;
            }
            _leashGate.Present(p, pardons);
        }

        private void HideLeashGate()
        {
            if (_leashGate?.IsUp != true) return;
            _leashGate.Dismiss();
            if (!_leashHidBrowser) return;
            _leashHidBrowser = false;
            // Never hand the browser back over the remote overlay's own blindfold.
            if (RemoteControlOverlay.Visibility != Visibility.Visible || _remoteOverlayBrowserRevealed)
                SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
        }

        private void BindLeashRunner()
        {
            ILeashTaskRunner? next = null;
            try { next = LeashLocator.Runner(); } catch { }
            if (ReferenceEquals(next, _leashRunner)) return;
            if (_leashRunner != null)
            {
                _leashRunner.Progress -= OnLeashProgress;
                _leashRunner.Completed -= OnLeashCompleted;
            }
            _leashRunner = next;
            if (_leashRunner != null)
            {
                _leashRunner.Progress += OnLeashProgress;
                _leashRunner.Completed += OnLeashCompleted;
            }
        }

        private void StartLeashTask(Punishment p)
        {
            BindLeashRunner();
            bool started = false;
            try { started = _leashRunner?.Start(p) == true; }
            catch (Exception ex) { App.Logger?.Debug("Leash task start failed: {E}", ex.Message); }
            if (!started)
            {
                App.Notifications?.Show(Loc.Get("leash_gate_cannot_start"), NotificationType.Warning);
                return;
            }
            App.Logger?.Information("Leash: task {Kind} x{Size} started", p.Kind, p.Size);
            _leashGate?.SetProgress(0, Math.Max(1, LeashUiRules.Pips(p)), running: true);
            CheckLeashGate();
        }

        private void OnLeashProgress(string pid, int done, int total)
        {
            if (_leashGate?.Pid == pid) _leashGate.SetProgress(done, total, running: done < total);
        }

        private async void OnLeashCompleted(string pid)
        {
            try
            {
                App.Logger?.Information("Leash: task {Pid} done", pid);
                if (LeashLocator.Service() is { } s) await s.CompleteAsync(pid);
                App.Notifications?.Show(Loc.Get("leash_gate_done"), NotificationType.Success);
            }
            catch (Exception ex) { App.Logger?.Debug("Leash complete failed: {E}", ex.Message); }
            finally { CheckLeashGate(); }
        }

        private async System.Threading.Tasks.Task PardonLeashAsync(Punishment p)
        {
            bool ok = false;
            try { if (LeashLocator.Service() is { } s) ok = await s.PardonAsync(p.Pid); }
            catch (Exception ex) { App.Logger?.Debug("Leash pardon failed: {E}", ex.Message); }
            App.Notifications?.Show(Loc.Get(ok ? "leash_gate_pardoned" : "leash_gate_no_pardon"), ok ? NotificationType.Success : NotificationType.Info);
            CheckLeashGate();
        }

        /// <summary>The gate's Panic: the real panic ladder, then the gate stands back for a while.
        /// Nothing about the leash can stop or delay a panic.</summary>
        private void PanicFromLeashGate()
        {
            _leashSnoozeUntilUtc = DateTime.UtcNow + LeashUiRules.PanicSnooze;
            try { _leashRunner?.Cancel(); } catch { }
            HideLeashGate();
            HandlePanicKeyPress();
        }

        /// <summary>A key-down of the panic key while leashed: true = a repeat of a held key,
        /// swallow it. Five seconds of holding asks "Cut the leash?".</summary>
        private bool LeashHoldSwallows(System.Windows.Input.Key key)
        {
            var s = App.Settings?.Current;
            if (s == null || key.ToString() != s.PanicKey) return false;
            var (repeat, due) = _leashHold.Down(DateTime.UtcNow, LeashSurfaces.IsLeashed);
            if (due) Dispatcher.BeginInvoke(AskToCutFromHold);
            return repeat;
        }

        private void OnLeashKeyReleased(System.Windows.Input.Key key)
        {
            if (key.ToString() == App.Settings?.Current?.PanicKey) _leashHold.Up();
        }

        private void AskToCutFromHold()
        {
            if (!LeashSurfaces.IsLeashed) return;
            string? holder = null;
            try { holder = LeashLocator.Service()?.Snapshot.Me?.Holder.Name; } catch { }
            App.Logger?.Information("Leash: panic key held 5 s, asking to cut");
            LeashCutConfirmWindow.Ask(holder, () =>
            {
                HideLeashGate();
                LeashPunishWindow.CloseNow();
                LeashSurfaces.Cut();
            });
        }

        /// <summary>First thing a panic press does: a leash video window closes, the task stops,
        /// the gate stands back. The punishment itself stays pending.</summary>
        private void LeashOnPanicPress()
        {
            if (LeashPunishWindow.Current == null) return;
            _leashSnoozeUntilUtc = DateTime.UtcNow + LeashUiRules.PanicSnooze;
            try { _leashRunner?.Cancel(); } catch { }
            LeashPunishWindow.CloseNow();
        }

        /// <summary>A tug from the holder: the window gives a small wobble (the chain jingle has
        /// already played). Motion Off: the toast alone carries it.</summary>
        private void WobbleForTug()
        {
            try
            {
                FrameworkElement? target = IsVisible && WindowState != WindowState.Minimized ? RootGrid
                    : Services.Launcher.LauncherHost.WindowRef?.Content as FrameworkElement;
                if (target != null) LeashFx.Tug(target, 0.8);
            }
            catch (Exception ex) { App.Logger?.Debug("Leash wobble failed: {E}", ex.Message); }
        }
    }
}
