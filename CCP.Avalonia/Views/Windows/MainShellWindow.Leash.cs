// PORTED from WPF 7.1.5 MainWindow/MainWindow.Leash.cs (InitializeLeash, CheckLeashGate and the gate's
// task plumbing) - ledger social#1. THE GATE (LeashGateCard) sits in the remote overlay's cell, one
// Z step under it, and stands up at the next idle moment while a punishment is pending
// (LeashGateRule.ShouldShow, checked every 2 s). Panic and Cut leash are always on it; a panic press
// from the gate runs the real panic and stands the gate back ten minutes. Also wires the app-wide
// reactions (LeashSurfaces): the ask card, the snap, the tug wobble.
// Not ported: hiding an embedded browser under the gate (WPF WebView2 airspace; no in-panel browser
// sits under this cell here) and the "no phrases" start refusal copy (falls back to cannot start).
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using ILeashTaskRunner = ConditioningControlPanel.Controls.Leash.ILeashTaskRunner;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _leashWired;
        private LeashGateCard? _leashGate;
        private DispatcherTimer? _leashGateTimer;
        private DateTime _leashSnoozeUntilUtc = DateTime.MinValue;
        private ILeashTaskRunner? _leashRunner;
        private string? _leashUnplayablePid;

        /// <summary>The gate card (tests).</summary>
        internal LeashGateCard? LeashGate => _leashGate;

        /// <summary>WPF InitializeLeash: the gate, the surfaces and the 2 s gate clock.</summary>
        internal void InitializeLeash()
        {
            if (_leashWired) return;
            _leashWired = true;
            try
            {
                Action<bool> onLeashed = on => Dispatcher.UIThread.Post(() =>
                {
                    // The tray "Cut leash" item re-reads IsLeashed on every open (Tray.cs RefreshCut).
                    if (on) CheckLeashGate();
                    else
                    {
                        LeashHoldRing.Dismiss();
                        // Off the leash by any road (a cut, the holder, a block, sign out): nothing
                        // the leash started may stay running or locked.
                        EndLeashTask("leash off");
                    }
                });
                Platform.LeashHead.LeashedChanged += onLeashed;
                MountLeashGate();

                Func<Window?> leashOwner = () => this;

                Controls.Leash.Explain.LeashExplainer.DefaultOwner = leashOwner;
                LeashSurfaces.Init(() => IsVisible && WindowState != WindowState.Minimized ? this : null);
                LeashSurfaces.TugArrived += WobbleForTug;
                Action onCut = () => Dispatcher.UIThread.Post(() => EndLeashTask("cut"));
                Platform.LeashHead.CutDone += onCut;
                Action<bool, bool> gatePanic = LeashGateOnPanic;
                Platform.LeashTaskHost.GatePanic = gatePanic;

                _leashGateTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(2) };
                _leashGateTimer.Tick += (_, _) => { LeashSurfaces.Rebind(); CheckLeashGate(); };
                _leashGateTimer.Start();
                // Static events: a closed shell must not stay rooted by them (ShellMemoryTests).
                Closed += (_, _) =>
                {
                    _leashGateTimer?.Stop();
                    Platform.LeashHead.LeashedChanged -= onLeashed;
                    Platform.LeashHead.CutDone -= onCut;
                    LeashSurfaces.TugArrived -= WobbleForTug;
                    if (ReferenceEquals(Controls.Leash.Explain.LeashExplainer.DefaultOwner, leashOwner))
                    {
                        Controls.Leash.Explain.LeashExplainer.DefaultOwner = () => null;
                        LeashSurfaces.Init(() => null);
                    }
                    if (ReferenceEquals(Platform.LeashTaskHost.GatePanic, gatePanic)) Platform.LeashTaskHost.GatePanic = null;
                };
                CheckLeashGate();
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash init failed: {E}", ex.Message); }
        }

        /// <summary>Lays the gate in the remote overlay's cell, one Z step under it. Idempotent.</summary>
        internal LeashGateCard? MountLeashGate()
        {
            if (_leashGate != null) return _leashGate;
            if (Named<Border>("RemoteControlOverlay") is not { Parent: Grid host } remote) return null;
            _leashGate = new LeashGateCard();
            Grid.SetRow(_leashGate, Grid.GetRow(remote));
            Grid.SetRowSpan(_leashGate, Grid.GetRowSpan(remote));
            Grid.SetColumn(_leashGate, Grid.GetColumn(remote));
            Grid.SetColumnSpan(_leashGate, Grid.GetColumnSpan(remote));
            _leashGate.ZIndex = remote.ZIndex - 1;
            host.Children.Add(_leashGate);
            _leashGate.StartRequested += StartLeashTask;
            _leashGate.PardonRequested += p => _ = PardonLeashAsync(p);
            _leashGate.PanicRequested += PanicFromLeashGate;
            _leashGate.CutRequested += () => { HideLeashGate(); Platform.LeashHead.Cut(); };
            _leashGate.LaterRequested += () => { _leashUnplayablePid = null; HideLeashGate(); };
            return _leashGate;
        }

        /// <summary>WPF PresentLeashGateFromLauncher's body: a tile press is a deliberate "I want to
        /// play", so it also ends a panic snooze.</summary>
        internal void PresentLeashGateNow()
        {
            _leashSnoozeUntilUtc = DateTime.MinValue;
            CheckLeashGate();
        }

        /// <summary>WPF CheckLeashGate: with a punishment due and nothing in the way (LeashGateRule),
        /// the gate stands over the panel; else it stands down.</summary>
        internal void CheckLeashGate()
        {
            try
            {
                var svc = LeashLocator.Service();
                BindLeashRunner();
                GuardLeashRunner(svc);
                var (due, unplayable) = PickLeashGate(svc);
                if (due != null && LeashGateRule.ShouldShow(ReadLeashWorld(true)))
                    ShowLeashGate(due, svc!.Snapshot.Me?.Pardons ?? 0, unplayable);
                else HideLeashGate();
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash gate check failed: {E}", ex.Message); }
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
        /// punishment was dropped, pardoned or expired): cancel it and let a locked window go.</summary>
        private void GuardLeashRunner(ILeashService? svc)
        {
            var r = _leashRunner;
            if (r == null || !r.IsRunning) return;
            MyLeash? me = null;
            try { me = svc?.Snapshot.Me; } catch { }
            if (!LeashGateRule.Orphaned(r.RunningPid, r.RunningAid, me)) return;
            EndLeashTask("task gone");
        }

        /// <summary>Cancel whatever the leash runner drives and close its window. Idempotent.</summary>
        private void EndLeashTask(string why)
        {
            Platform.LeashTaskHost.EndTask(why);
            _leashUnplayablePid = null;
            HideLeashGate();
        }

        // WPF MainWindow.Leash.cs:173-179. Head differences: no startup modal ladder here (Platform/StartupLadder
        // is the passive route only), so ModalUp reads the tour instead of IsModalUp; the launcher hold is the
        // launcher window itself; a web game owns its own fullscreen, so FullscreenEffect is the video alone.
        internal LeashGateInputs ReadLeashWorld(bool due) => new(
            Due: due,
            SessionRunning: App.Sessions?.IsRunning == true || global::ConditioningControlPanel.CoreEngine.IsRunning,
            GameUp: PanicSurfaces.AnyOwnsTheScreen(),   // WPF ChaosWebViewHost.AnyGameActive: game windows, the intake, a descent, the gaze minigame
            LockCardOpen: LockCardWindow.IsAnyOpen() || BubbleCountWindow.IsAnyOpen(),
            FullscreenEffect: global::ConditioningControlPanel.CoreEngine.Video?.IsPlaying == true,
            ModalUp: global::ConditioningControlPanel.CoreTutorial.IsActive
                     || LockdownActive || Named<Border>("RemoteControlOverlay")?.IsVisible == true,
            PanelAway: !IsVisible || WindowState == WindowState.Minimized || LauncherWindow.Instance?.IsVisible == true,
            Watching: _leashRunner?.IsRunning == true || DateTime.UtcNow < _leashSnoozeUntilUtc);

        private void ShowLeashGate(Punishment p, int pardons, bool unplayable = false) => _leashGate?.Present(p, pardons, unplayable);

        /// <summary>WPF HideLeashGate. Idempotent.</summary>
        internal void HideLeashGate()
        {
            if (_leashGate?.IsUp != true) return;
            _leashGate.Dismiss();
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
            catch (Exception ex) { Serilog.Log.Debug("Leash task start failed: {E}", ex.Message); }
            if (!started)
            {
                Notify(Loc.Get("leash_gate_cannot_start"), NotificationType.Warning);
                return;
            }
            Serilog.Log.Information("Leash: task {Kind} x{Size} started", p.Kind, p.Size);
            _leashGate?.SetProgress(0, Math.Max(1, LeashUiRules.Pips(p)), running: true);
            CheckLeashGate();
        }

        private void OnLeashProgress(string pid, int done, int total) => Dispatcher.UIThread.Post(() =>
        {
            if (_leashGate?.Pid == pid) _leashGate.SetProgress(done, total, running: done < total);
        });

        private void OnLeashCompleted(string pid) => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                Serilog.Log.Information("Leash: task {Pid} done", pid);
                if (_leashRunner?.LastCompletionCapped == true) LeashFx.CapReached();
                else LeashFx.Done();
                if (LeashLocator.Service() is { } s) await s.CompleteAsync(pid);
                Notify(Loc.Get("leash_gate_done"), NotificationType.Success);
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash complete failed: {E}", ex.Message); }
            finally { CheckLeashGate(); }
        });

        /// <summary>The runner went idle by itself: say why, and let the gate come back with the
        /// task still pending. A video that will not play is skipped on the server when it can
        /// be, else left off the gate for a day (and the gate says so once).</summary>
        private void OnLeashStopped(LeashTaskStopped s) => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                Serilog.Log.Information("Leash: task {Id} stopped ({Reason})", s.Id, s.Reason);
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
                Notify(Loc.GetF(LeashUiRules.StopKey(s, kind, skip), holder ?? ""), NotificationType.Info);
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash stop handling failed: {E}", ex.Message); }
            finally { CheckLeashGate(); }
        });

        private async System.Threading.Tasks.Task PardonLeashAsync(Punishment p)
        {
            bool ok = false;
            try { if (LeashLocator.Service() is { } s) ok = await s.PardonAsync(p.Pid); }
            catch (Exception ex) { Serilog.Log.Debug("Leash pardon failed: {E}", ex.Message); }
            if (ok) LeashFx.Done();
            if (ok && _leashUnplayablePid == p.Pid) _leashUnplayablePid = null;
            Notify(Loc.Get(ok ? "leash_gate_pardoned" : "leash_gate_no_pardon"), ok ? NotificationType.Success : NotificationType.Info);
            CheckLeashGate();
        }

        /// <summary>The gate's Panic: the real panic press, then the gate stands back for a while.
        /// Nothing about the leash can stop or delay a panic.</summary>
        private void PanicFromLeashGate()
        {
            _leashSnoozeUntilUtc = DateTime.UtcNow + LeashUiRules.PanicSnooze;
            try { _leashRunner?.Park(); } catch { }
            HideLeashGate();
            PanicSurfaces.ArmSafetyHold();   // also when the key itself is switched off: the way out never costs
            HandlePanicKeyPress(DateTime.Now);
        }

        /// <summary>WPF LeashOnPanicPress's gate half (Platform.LeashTaskHost.OnPanicPress calls it after
        /// the runner parked): the gate stands back ten minutes and nothing reopens by itself.
        /// <paramref name="taskLive"/> = a task ran or its window was open when the press landed.</summary>
        internal void LeashGateOnPanic(bool panicRuns, bool taskLive)
        {
            var gateUp = !panicRuns && _leashGate?.IsUp == true;
            if (!taskLive && !gateUp) return;
            _leashSnoozeUntilUtc = DateTime.UtcNow + LeashUiRules.PanicSnooze;
            _leashGate?.StopRunning();
            HideLeashGate();
        }

        /// <summary>True while a panic press holds the gate back (tests).</summary>
        internal bool LeashGateSnoozed => DateTime.UtcNow < _leashSnoozeUntilUtc;

        /// <summary>A tug from the holder: the window gives a small wobble (the chain jingle has
        /// already played). Motion Off: the toast alone carries it.</summary>
        private void WobbleForTug()
        {
            try
            {
                if (!IsVisible || WindowState == WindowState.Minimized) return;
                if (Named<Grid>("RootGrid") is { } root) LeashFx.Tug(root, 0.8);
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash wobble failed: {E}", ex.Message); }
        }

        private static void Notify(string text, NotificationType kind)
        {
            try { App.Notifications.Show(text, kind); } catch { }
        }
    }
}
