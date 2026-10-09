// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.PlayTab.cs.
//
//   RefreshPlayCards / RefreshPlayFreeStamps / RefreshPlayIntakeCard / SetLockband
//                         PORTED into Views/Tabs/PlayTabView.axaml.cs (the view paints itself on
//                         attach, on IsVisible, on IntakePass.PassStateChanged and from
//                         MainShellWindow.Patreon.cs, which is WPF's three call sites).
//   StartMantraSession    PORTED below. Its one caller is the Programs Mantra-task door.
//   LaunchPlayBreakoutDemo / LaunchPlayBreakout / LaunchPlayChess / LaunchPlayGoon
//                         NOT PORTED: BreakoutHostService, PieceByPieceHostService and
//                         GoonHostService are WPF-only WebView hosts. The Play wall's four game
//                         buttons are disabled with the exclusives_not_on_this_build tooltip.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// WPF MainWindow.PlayTab.cs StartMantraSession: focus a running MantraWindow rather than
        /// restart it (a second StartSession would wipe the run), else start the session THEN open the
        /// window, whose Loaded reads CurrentMantra and TargetCount.
        /// </summary>
        internal void StartMantraSession(int targetReps)
        {
            try
            {
                if (global::Avalonia.Application.Current?.ApplicationLifetime is
                        global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    foreach (var w in desktop.Windows)
                        if (w is MantraWindow live) { live.Activate(); live.Focus(); return; }

                App.Mantra.StartSession(targetReps);
                new MantraWindow().Show(this);
            }
            catch (System.Exception ex)
            {
                // ponytail: WPF also shows a MessageBox here; logged only on this head.
                Serilog.Log.Error(ex, "StartMantraSession failed");
            }
        }
    }
}
