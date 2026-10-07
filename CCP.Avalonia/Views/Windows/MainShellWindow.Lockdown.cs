// PORTED from the Lockdown refusals WPF spreads over MainWindow: BtnStart_Click (StartStop.cs:45,
// Stop refused + Stop tripwire), RequestExit (Launcher.cs:184, Exit refused), OnClosing
// (WindowChrome.cs:131, close refused + Close tripwire) and OnGlobalKeyPressed (MainWindow.xaml.cs:888,
// every GLOBAL key ignored). Window and TextBox input are never touched: the secret phrase is typed
// through them (docs/avalonia-decisions.md, Lockdown / Emergency Exit).

using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal static bool LockdownActive => LockdownService.Current?.IsActive == true;

        /// <summary>WPF Lab.cs:635-663/741-745: a control Lockdown holds is greyed at 0.4 with the
        /// "no escape" tooltip, and given back on exit. The control's own refusal stays as well.</summary>
        internal static void HoldUnderLockdown(global::Avalonia.Controls.Control c, bool held)
        {
            c.IsEnabled = !held;
            c.Opacity = held ? 0.4 : 1.0;
            global::Avalonia.Controls.ToolTip.SetTip(c, held ? Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape") : null);
        }

        /// <summary>WPF OnLockdownActivated/Deactivated (Lab.cs:612/707): the CC Labs door is greyed
        /// as well as refused. Bound to the service current at construction (App seeds it first).</summary>
        private void InitializeLockdownGreys()
        {
            void Refresh() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Named<global::Avalonia.Controls.Button>("BtnBackToLauncher") is { } door) door.IsEnabled = !LockdownActive;
            });
            if (LockdownService.Current is { } ld)
            {
                ld.LockdownActivated += Refresh;
                ld.LockdownDeactivated += Refresh;
                Closed += (_, _) => { ld.LockdownActivated -= Refresh; ld.LockdownDeactivated -= Refresh; };
            }
            Refresh();
        }

        /// <summary>WPF StartStop.cs:45: under Lockdown a Stop (button, tray Stop everything) is
        /// refused with the WPF message, after the Stop tripwire. True when refused.</summary>
        internal static bool RefuseStopUnderLockdown()
        {
            if (!LockdownActive) return false;
            try { LockdownService.Current?.NotifyEscapeAttempt(EscapeKinds.Stop); } catch { }
            Serilog.Log.Information("Lockdown: Stop refused");
            if (Current is { } owner)
                _ = MessageDialog.ShowAsync(owner, Loc.Get("title_lockdown"), Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_stop_dur"));
            return true;
        }

        /// <summary>The live shell window, for dialogs raised from static paths (tray).</summary>
        internal static MainShellWindow? Current =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow as MainShellWindow;
    }
}
