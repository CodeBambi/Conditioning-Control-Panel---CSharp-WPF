// PORTED from WPF 7.1.5 ConditioningControlPanel/MainWindow/MainWindow.PlayTab.cs (331 lines), in parts:
//   ScrollPlayZone       - ScrollPlayZoneFor below (zone pills scroll the wall, play#9).
//   LaunchPlay* / OpenPlayWebApp - the card shims in Views/Tabs/PlayTabView.axaml.cs run the
//                          launcher entry (LauncherWindow.LaunchGame); OpenPlayWebApp is below.
//   RefreshPlayCards     - the lockbands live in PlayTabView.RefreshPlayCards (TierGate verdicts).
//   StartMantraSession   - PORTED below (MantraService is in Core).
// Not here yet: RefreshPlayFreeStamps (no CoreDailyFree seam) and RefreshPlayIntakeCard
// (IntakePassService state on this head).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The Play key ShowTab last landed on ("play", "playsessions", "playeyes"), so a
        /// return to plain Play from another zone scrolls back to Games (WPF TabNavigation.cs:549).</summary>
        private string? _lastPlayKey;

        /// <summary>WPF MainWindow.PlayTab.cs ScrollPlayZone + TabNavigation.cs:547-557: the zone pills
        /// scroll the wall to their header and glow it once; a plain return to Play keeps its scroll
        /// unless the previous pill was another zone.</summary>
        internal void ScrollPlayZoneFor(string tab)
        {
            var before = _lastPlayKey;
            _lastPlayKey = tab;
            string? zone = tab switch
            {
                "playsessions" => "sessions",
                "playeyes" => "eyes",
                _ => before is "playsessions" or "playeyes" ? "games" : null,
            };
            if (zone == null) return;
            var play = Named<Tabs.PlayTabView>("PlayTab");
            if (play == null) { Serilog.Log.Debug("ScrollPlayZone({Zone}): no Play view", zone); return; }
            try { play.ScrollToZone(zone); }
            catch (System.Exception ex) { Serilog.Log.Debug("ScrollPlayZone({Zone}): {E}", zone, ex.Message); }
        }

        /// <summary>WPF OpenPlayWebApp: the Web App card runs the rail door's two lines (open through
        /// the browser launcher, retire the banner beat).</summary>
        internal void OpenPlayWebApp() => DoorWebApp_Click(this, new global::Avalonia.Interactivity.RoutedEventArgs());

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
