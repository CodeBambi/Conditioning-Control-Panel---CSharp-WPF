using System;
using System.Linq;
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
    ///
    /// <para>Safety pass (2026-09-28): a panic press stops ANY running gate task (the runner parks
    /// with its progress, the lock card / video goes, the gate stands back ten minutes, nothing
    /// reopens by itself); a task whose activity stopped sends the gate back with the task still
    /// pending; a task that no longer exists (the leash ended from the other side, the punishment
    /// was dropped) is cancelled and its locked window closed; a video that will not play is
    /// skipped on the server or left off the gate for a day.</para>
    /// </summary>
    public partial class MainWindow
    {
        private LeashGateCard? _leashGate;
        private DispatcherTimer? _leashGateTimer;
        private bool _leashHidBrowser;
        private DateTime _leashSnoozeUntilUtc = DateTime.MinValue;
        private ILeashTaskRunner? _leashRunner;
        private readonly LeashHoldToCut _leashHold = new();
        private int _leashHoldTicked;
        private int _leashHoldShown;
        private string? _leashUnplayablePid;

        private void InitializeLeash()
        {
            try
            {
                App.LeashedChanged += on => Dispatcher.BeginInvoke(() =>
                {
                    _trayIcon?.SyncLeashIcon();
                    // Hold-to-cut must work even with the panic key switched off.
                    if (on) _keyboardHook?.Start();
                    else
                    {
                        _leashHold.Up();
                        LeashHoldRing.Dismiss();
                        // Off the leash by any road (a cut, the holder, a block, sign out): nothing
                        // the leash started may stay running or locked.
                        EndLeashTask("leash off");
                        // The hook stayed on for the leash; it goes quiet again if nothing else needs it.
                        var s = App.Settings?.Current;
                        if (s != null && !s.PanicKeyEnabled && !s.KeywordTriggersEnabled && App.Lockdown?.IsActive != true)
                            _keyboardHook?.Stop();
                    }
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
                    _leashGate.LaterRequested += () => { _leashUnplayablePid = null; HideLeashGate(); };
                }

                LeashSurfaces.Init(() => IsVisible && WindowState != WindowState.Minimized
                    ? this
                    : Services.Launcher.LauncherHost.IsShown ? Services.Launcher.LauncherHost.WindowRef : null);
                LeashSurfaces.TugArrived += WobbleForTug;
                LeashSurfaces.CutDone += () => EndLeashTask("cut");

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
                GuardLeashRunner(svc);
                var (due, unplayable) = PickLeashGate(svc);
                if (due != null && Services.Leash.LeashGateRule.ShouldShow(ReadLeashWorld(true)))
                    ShowLeashGate(due, svc!.Snapshot.Me?.Pardons ?? 0, unplayable);
                else HideLeashGate();
            }
            catch (Exception ex) { App.Logger?.Debug("Leash gate check failed: {E}", ex.Message); }
        }

        /// <summary>What the gate shows: a video that just would not play (once, until "Not now"),
        /// else the oldest pending punishment the gate still owes.</summary>
        private (Punishment? Due, bool Unplayable) PickLeashGate(ILeashService? svc)
        {
            if (svc == null) { _leashUnplayablePid = null; return (null, false); }
            // Read first: GateDue is where a sign-out is noticed, and it clears the snapshot the
            // unplayable card below reads.
            var due = svc.GateDue;
            if (_leashUnplayablePid is { } pid)
            {
                var p = svc.Snapshot.Me?.Pending.FirstOrDefault(x => x.Pid == pid);
                if (p != null && svc.IsUnplayable(pid)) return (p, true);
                _leashUnplayablePid = null;
            }
            return (due, false);
        }

        /// <summary>The task behind the runner went away (the leash ended from the other side, the
        /// punishment was dropped by an intensity change, pardoned or expired, the assignment was
        /// replaced): cancel it and let a locked video window go.</summary>
        private void GuardLeashRunner(ILeashService? svc)
        {
            var r = _leashRunner;
            if (r == null || !r.IsRunning) return;
            MyLeash? me = null;
            try { me = svc?.Snapshot.Me; } catch { }
            if (!Services.Leash.LeashGateRule.Orphaned(r.RunningPid, r.RunningAid, me)) return;
            EndLeashTask("task gone");
        }

        /// <summary>Cancel whatever the leash runner drives and close its window. Idempotent.</summary>
        private void EndLeashTask(string why)
        {
            try
            {
                if (_leashRunner?.IsRunning == true || LeashPunishWindow.Current != null)
                    App.Logger?.Information("Leash: task ended ({Why})", why);
                _leashRunner?.Cancel();
            }
            catch (Exception ex) { App.Logger?.Debug("Leash cancel failed: {E}", ex.Message); }
            LeashPunishWindow.CloseNow();
            _leashUnplayablePid = null;
            HideLeashGate();
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

        private void ShowLeashGate(Punishment p, int pardons, bool unplayable = false)
        {
            if (_leashGate == null) return;
            if (!_leashGate.IsUp && SettingsTab.BrowserContainer.Visibility == Visibility.Visible)
            {
                SettingsTab.BrowserContainer.Visibility = Visibility.Hidden;
                _leashHidBrowser = true;
            }
            _leashGate.Present(p, pardons, unplayable);
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
                _leashRunner.Stopped -= OnLeashStopped;
            }
            _leashRunner = next;
            if (_leashRunner != null)
            {
                _leashRunner.Progress += OnLeashProgress;
                _leashRunner.Completed += OnLeashCompleted;
                _leashRunner.Stopped += OnLeashStopped;
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
                var noPhrases = p.Kind == PunishKind.Lines && !AppLeashTaskHost.HasEnabledPhrase(App.Settings?.Current?.LockCardPhrases);
                App.Notifications?.Show(Loc.Get(noPhrases ? "leash_stop_no_phrases" : "leash_gate_cannot_start"), NotificationType.Warning);
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
                if (_leashRunner?.LastCompletionCapped == true) LeashFx.CapReached();
                else LeashFx.Done();
                if (LeashLocator.Service() is { } s) await s.CompleteAsync(pid);
                App.Notifications?.Show(Loc.Get("leash_gate_done"), NotificationType.Success);
            }
            catch (Exception ex) { App.Logger?.Debug("Leash complete failed: {E}", ex.Message); }
            finally { CheckLeashGate(); }
        }

        /// <summary>The runner went idle by itself: say why, and let the gate come back with the
        /// task still pending. A video that will not play is skipped on the server when it can
        /// be, else left off the gate for a day (and the gate says so once).</summary>
        private async void OnLeashStopped(LeashTaskStopped s)
        {
            try
            {
                App.Logger?.Information("Leash: task {Id} stopped ({Reason})", s.Id, s.Reason);
                _leashGate?.StopRunning();
                var svc = LeashLocator.Service();
                string? holder = null;
                try { holder = svc?.Snapshot.Me?.Holder.Name; } catch { }
                PunishKind? kind = null;
                try { kind = svc?.Snapshot.Me?.Pending.FirstOrDefault(p => p.Pid == s.Id)?.Kind; } catch { }

                LeashSkipResult? skip = null;
                if (!s.Assignment && s.Reason == LeashTaskStop.Unplayable)
                {
                    LeashFx.Denied();
                    skip = svc == null ? LeashSkipResult.Marked : await svc.SkipUnplayableAsync(s.Id);
                    if (skip == LeashSkipResult.Marked) _leashUnplayablePid = s.Id;
                }
                var key = LeashUiRules.StopKey(s, kind, skip);
                App.Notifications?.Show(Loc.GetF(key, holder ?? ""), NotificationType.Info);
            }
            catch (Exception ex) { App.Logger?.Debug("Leash stop handling failed: {E}", ex.Message); }
            finally { CheckLeashGate(); }
        }

        private async System.Threading.Tasks.Task PardonLeashAsync(Punishment p)
        {
            bool ok = false;
            try { if (LeashLocator.Service() is { } s) ok = await s.PardonAsync(p.Pid); }
            catch (Exception ex) { App.Logger?.Debug("Leash pardon failed: {E}", ex.Message); }
            if (ok) LeashFx.Done();
            if (ok && _leashUnplayablePid == p.Pid) _leashUnplayablePid = null;
            App.Notifications?.Show(Loc.Get(ok ? "leash_gate_pardoned" : "leash_gate_no_pardon"), ok ? NotificationType.Success : NotificationType.Info);
            CheckLeashGate();
        }

        /// <summary>The gate's Panic: the real panic ladder, then the gate stands back for a while.
        /// Nothing about the leash can stop or delay a panic.</summary>
        private void PanicFromLeashGate()
        {
            _leashSnoozeUntilUtc = DateTime.UtcNow + LeashUiRules.PanicSnooze;
            try { _leashRunner?.Park(); } catch { }
            HideLeashGate();
            HandlePanicKeyPress();
        }

        /// <summary>A key-down of the panic key: true = a repeat of a held key, swallow it, leashed or
        /// not (DESK-5). While leashed, five seconds of holding asks "Cut the leash?"; the hold shows
        /// a 5..1 ring.</summary>
        private bool LeashHoldSwallows(System.Windows.Input.Key key)
        {
            var s = App.Settings?.Current;
            if (s == null || key.ToString() != s.PanicKey) return false;
            var now = DateTime.UtcNow;
            var leashed = LeashSurfaces.IsLeashed;
            var (repeat, due) = _leashHold.Down(now, leashed);
            if (!repeat) { _leashHoldTicked = 0; _leashHoldShown = 0; }
            if (due)
            {
                Dispatcher.BeginInvoke(LeashHoldRing.Dismiss);
                Dispatcher.BeginInvoke(AskToCutFromHold);
            }
            // The tick and the ring only ever ride a swallowed repeat while leashed (never the first
            // down, which is the real panic press) and are posted, so the hook returns at once.
            else if (leashed && repeat && _leashHold.HeldFor(now) is { } held)
            {
                var left = LeashHoldTick.SecondsLeft(held);
                if (left != _leashHoldShown)
                {
                    _leashHoldShown = left;
                    Dispatcher.BeginInvoke(() => LeashHoldRing.ShowHeld(held));
                }
                if (LeashHoldTick.Due(held, _leashHoldTicked) is int sec)
                {
                    _leashHoldTicked = sec;
                    Dispatcher.BeginInvoke(LeashFx.Tick);
                }
            }
            return repeat;
        }

        private void OnLeashKeyReleased(System.Windows.Input.Key key)
        {
            if (key.ToString() != App.Settings?.Current?.PanicKey) return;
            _leashHold.Up();
            _leashHoldShown = 0;
            Dispatcher.BeginInvoke(LeashHoldRing.Dismiss);
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

        /// <summary>First thing a panic press (and the blink stop) does while a leash task runs:
        /// the runner parks with its progress, so nothing it drives comes back by itself (lines
        /// used to reopen a lock card every 3 s), the video window closes, bubbles the task
        /// started stop, and the gate stands back ten minutes. The rest of the panic ladder then
        /// takes down the lock card and the session. The punishment itself stays pending.</summary>
        private void LeashOnPanicPress() => LeashOnPanicPress(panicRuns: true);

        /// <param name="panicRuns">False when the panic itself will not run (switched off, or
        /// Lockdown holding the keys): nothing else takes the task's lock card down then, so a lines
        /// task closes its own card here. A card the leash did not open is left alone.</param>
        private void LeashOnPanicPress(bool panicRuns)
        {
            var running = _leashRunner?.IsRunning == true;
            var gateUp = !panicRuns && _leashGate?.Visibility == Visibility.Visible;
            if (!running && LeashPunishWindow.Current == null && !gateUp) return;
            // An Escape aimed at closing the Ctrl+K palette is not a panic (the ladder's rung 2).
            if (SettingsPaletteWindow.IsOpen && !LockCardWindow.IsAnyOpen()
                && string.Equals(App.Settings?.Current?.PanicKey, "Escape", StringComparison.OrdinalIgnoreCase)) return;
            App.Logger?.Information("Leash: panic press stops the running task (kept pending, gate back in {M} min, panic runs={Runs})",
                LeashUiRules.PanicSnooze.TotalMinutes, panicRuns);
            _leashSnoozeUntilUtc = DateTime.UtcNow + LeashUiRules.PanicSnooze;
            var linesCard = !panicRuns && (_leashRunner as LeashTaskRunner)?.RunningKind == PunishKind.Lines;
            try { _leashRunner?.Park(); } catch { }
            if (linesCard) { try { LockCardWindow.ForceCloseAll(); } catch { } }
            LeashPunishWindow.CloseNow();
            _leashGate?.StopRunning();
            HideLeashGate();
        }

        /// <summary>
        /// The panic key while leashed when the panic cannot run (the player switched it off, or
        /// Lockdown holds every key). Panic always works on a leash, so the leash part runs anyway:
        /// the task parks, its window closes, the gate stands back, and holding the key still asks
        /// to cut. Nothing else a panic does (the session, effects, the exit ladder) is touched.
        /// Runs inside the hook callback: it only decides and posts. True = the press was taken.
        /// </summary>
        private bool LeashPanicKeyWhilePanicOff(System.Windows.Input.Key key)
        {
            var s = App.Settings?.Current;
            if (s == null || key.ToString() != s.PanicKey || !LeashSurfaces.IsLeashed) return false;
            VideoDiag.Log("PANIC", "panic key while leashed with the panic off - leash safety only");
            Dispatcher.BeginInvoke(() =>
            {
                try { LeashOnPanicPress(panicRuns: false); }
                catch (Exception ex) { App.Logger?.Warning("Leash: panic-off stop failed: {E}", ex.Message); }
            });
            return true;
        }

        /// <summary>The global key hook goes quiet only when nothing needs it. While leashed it stays
        /// on whatever the panic setting says: the panic key is the leash's way out (hold to cut).</summary>
        private void StopKeyboardHookUnlessLeashed()
        {
            if (LeashSurfaces.IsLeashed) return;
            _keyboardHook?.Stop();
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
