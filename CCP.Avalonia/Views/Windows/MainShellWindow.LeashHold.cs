// PORTED from WPF 7.1.5 MainWindow/MainWindow.Leash.cs LeashHoldSwallows / OnLeashKeyReleased /
// AskToCutFromHold: while leashed, holding the panic key five seconds asks "Cut the leash?". The
// press itself is untouched (Platform/Win32PanicKey: the first down is THE panic press, repeats are
// swallowed, leashed or not), so the leash never delays or weakens panic. Windows hook only; the
// X11 listener does not report repeats/key-ups yet (ponytail).
// SEAMs for the merge (r10 owns the types; each sits at its exact WPF call site below):
// LeashHoldRing.ShowHeld(held) / LeashHoldRing.Dismiss() (the 5..1 ring), LeashPunishWindow.CloseNow()
// (before the cut). LeashTaskHost.OnPanicPress(false) is marked in Platform/Win32Input.OnPanicPress.
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private static bool _leashHoldWired;
        private static int _leashHoldTicked;
        private static int _leashHoldShown;

        /// <summary>Hooks the panic key's hold clock once (StartPanicKey, Windows path).</summary>
        internal static void WireLeashHold()
        {
            if (_leashHoldWired) return;
            _leashHoldWired = true;
            Platform.Win32PanicKey.Leashed = () => Platform.LeashHead.IsLeashed;
            Platform.Win32PanicKey.LeashHoldDue += () => Dispatcher.UIThread.Post(() =>
            {
                // SEAM: LeashHoldRing.Dismiss()
                AskToCutFromHold();
            });
            Platform.Win32PanicKey.LeashHolding += held =>
            {
                // A new hold (WPF resets on the first down; a lost key-up must not mute the next ticks).
                if (held.TotalSeconds < _leashHoldTicked) { _leashHoldTicked = 0; _leashHoldShown = 0; }
                var left = LeashHoldTick.SecondsLeft(held);
                if (left != _leashHoldShown)
                {
                    _leashHoldShown = left;
                    // SEAM: LeashHoldRing.ShowHeld(held)   (posted: Dispatcher.UIThread.Post)
                }
                if (LeashHoldTick.Due(held, _leashHoldTicked) is int sec)
                {
                    _leashHoldTicked = sec;
                    Dispatcher.UIThread.Post(LeashFx.Tick);
                }
            };
            Platform.Win32PanicKey.PanicReleased += () =>
            {
                _leashHoldTicked = 0;
                _leashHoldShown = 0;
                // SEAM: LeashHoldRing.Dismiss()   (posted: Dispatcher.UIThread.Post)
            };
        }

        /// <summary>WPF AskToCutFromHold: the cut confirm; Yes is the one-click cut (never priced).</summary>
        internal static void AskToCutFromHold()
        {
            if (!Platform.LeashHead.IsLeashed) return;
            string? holder = null;
            try { holder = Platform.LeashHead.Service?.Snapshot.Me?.Holder.Name; } catch { }
            Serilog.Log.Information("Leash: panic key held 5 s, asking to cut");
            LeashCutConfirmWindow.Ask(holder, () =>
            {
                // WPF: HideLeashGate() (no gate overlay on this head yet)
                // SEAM: LeashPunishWindow.CloseNow()
                Platform.LeashHead.Cut();
            });
        }
    }
}
