// PORTED from WPF 7.1.5 MainWindow/MainWindow.Leash.cs:41-127 (InitializeLeash, CheckLeashGate) - ledger social#1.
// Deviation, on purpose: WPF lays a LeashGateCard over the panel and starts the punishment through
// LeashTaskRunner + AppLeashTaskHost (lock cards, pink/detention sessions, bubbles, video). The task
// host is not on this head yet, so the gate here is the Social > Leash page: its own card lists what
// waits at the gate with Pardon and Cut leash (both work), and the launcher's "Leash" step lands there.
// ponytail: LeashGateCard overlay + AppLeashTaskHost (Start runs the task) + hold-to-cut on the panic key.
using System;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _leashWired;

        /// <summary>WPF InitializeLeash: the tray and the page follow the leash coming on or off.</summary>
        internal void InitializeLeash()
        {
            if (_leashWired) return;
            _leashWired = true;
            Platform.LeashHead.LeashedChanged += on => Dispatcher.UIThread.Post(() =>
            {
                // The tray "Cut leash" item re-reads IsLeashed on every open (Tray.cs RefreshCut).
                if (on) CheckLeashGate();
            });
        }

        /// <summary>WPF CheckLeashGate: with a punishment due and nothing in the way (LeashGateRule),
        /// bring the player to the gate. Here the gate is the Leash page (see the header).</summary>
        internal void CheckLeashGate()
        {
            try
            {
                var svc = Platform.LeashHead.Service;
                if (svc?.GateDue == null) return;
                // WPF ReadLeashWorld(true), with what this head can see.
                var world = new global::ConditioningControlPanel.Services.Leash.LeashGateInputs(
                    Due: true,
                    SessionRunning: global::ConditioningControlPanel.CoreEngine.IsRunning,
                    GameUp: false,
                    LockCardOpen: LockCardWindow.IsAnyOpen(),
                    FullscreenEffect: global::ConditioningControlPanel.CoreEngine.Video?.IsPlaying == true,
                    ModalUp: LockdownActive,
                    PanelAway: !IsVisible || WindowState == global::Avalonia.Controls.WindowState.Minimized,
                    Watching: false);
                if (!global::ConditioningControlPanel.Services.Leash.LeashGateRule.ShouldShow(world)) return;
                ShowTab("leash");
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash gate check failed: {E}", ex.Message); }
        }
    }
}
