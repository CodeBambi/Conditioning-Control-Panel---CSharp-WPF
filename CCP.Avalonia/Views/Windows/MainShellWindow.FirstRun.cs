// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:539-605 - the first-launch
// branch of InitializeUI, which is the ONLY thing that ever opens the first-run wizard.
//
// WHY THIS PARTIAL EXISTS AT ALL: FirstRunWizard was a finished, rendered, fully ported view that
// nothing in this head constructed. `--render-all` drew it every build and no user could ever
// reach it, because the one line that opens it lives in the shell's constructor and had not been
// written. This file is that line plus the wait it needs.
//
// WHAT IS FAITHFUL:
//   * The gate is claimed in the CONSTRUCTOR (HookFirstRun runs from MainShellWindow's ctor),
//     exactly where WPF claims it, so the Welcomed / FirstRunAssetsPromptShown / LastSeenVersion
//     latch happens at the same instant relative to everything else on the launch path.
//   * The wizard opens LATER, once the window is actually on screen, and hands the first run back
//     if it never gets there. WPF polls IsLoaded for up to 10s for the same reason; Avalonia gives
//     us the Opened event instead, so the poll collapses into one handler.
//   * The post is at DispatcherPriority.Normal, never Loaded. WPF's comment on that line is
//     load-bearing: this app keeps the dispatcher busy enough that Loaded-priority work is starved
//     and the first-launch tour silently never started.
//
// WHAT IS DELIBERATELY NOT PORTED:
//   * The 30s `App.IsUpdateDialogActive` wait. That flag lives in ConditioningControlPanel/
//     App.xaml.cs and this head never shows an update dialog at all (UpdateNotificationDialog has
//     no caller here - see its own file for why), so waiting on it would be waiting on a constant.
//   * `App.EmiDesk.Fire("firstLaunchEver") / ReleaseHold(...)`. There is no EmiDesk seam in Core -
//     CCP.Core/Services/EmiDesk/ carries the book's layout and text, not the desk - so the HOLD
//     has nothing to hold back on this head. Nothing talks over the wizard here either way.
//   * `QueueEmiKnock(knockSeenVersion)`, listed as dropped in MainShellWindow.axaml.cs already.
//   * The whole `else` branch (ShowWhatsNewIfNeeded / TryPresentSeasonRecap / the upgrader's
//     ModPickerDialog). MainShellWindow.Marquee.cs documents why those three are still stubs:
//     they need App.Achievements, App.Seasons and App.xaml.cs's startup-dialog queue. One
//     consequence worth naming: LastSeenVersion is therefore only ever stamped by the first-run
//     gate below, never on an upgrade launch. That is pre-existing and this file does not
//     worsen it - the fix is ShowWhatsNewIfNeeded, not a second stamp here.

using System;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Called once from the constructor. Claims the first-run flags if this is a fresh
        /// install, and arms the one-shot that opens the wizard when the shell is on screen.
        ///
        /// <para>On a render, a --nav-check or any other head that seeded no settings service,
        /// <see cref="FirstRunWizard.ShouldRunAndClaim"/> returns false and nothing is armed -
        /// which is what keeps a modal out of a headless run and keeps CI off a real profile.</para>
        /// </summary>
        private void HookFirstRun()
        {
            try
            {
                if (!FirstRunWizard.ShouldRunAndClaim()) return;
                Opened += OnFirstRunShellOpened;
            }
            catch (Exception ex)
            {
                // A first-run screen must never be the reason a fresh install fails to start.
                Log.Warning(ex, "[FirstRun] Could not arm the first-run wizard");
            }
        }

        private void OnFirstRunShellOpened(object? sender, EventArgs e)
        {
            Opened -= OnFirstRunShellOpened;   // one launch, one wizard

            // Normal, never Loaded - see the header. Posting also gets us off the Opened callback
            // before a modal takes the loop.
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    // ShowDialog throws on an owner that is not VISIBLE, and "loaded" is not the
                    // same question. If the shell never actually made it onto the screen, hand the
                    // flags back rather than spending a first run nobody was shown.
                    if (!IsVisible)
                    {
                        FirstRunWizard.HandBackFirstRun("shell window never became visible");
                        return;
                    }

                    await FirstRunWizard.Run(this);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[FirstRun] The first-run wizard failed to run");
                }
            }, DispatcherPriority.Normal);
        }
    }
}
