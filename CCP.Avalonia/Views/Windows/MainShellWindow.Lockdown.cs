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
        private static MainShellWindow? Current =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow as MainShellWindow;
    }
}
