// PORTED from ConditioningControlPanel/App.xaml.cs:2165 (Programs = new ProgramService()) and
// MainWindow.Presets.cs:1611 (App.Programs.AttachSessionEngine) - progression#1.
// The Core ProgramService owns the ledger, the day clock and the verifier pass; this head hands it
// the session runner through Platform.ProgramEngineBridge (programs 3a): AttachEngine plus the
// runner's Stopped event, the one place a finished session is credited. The shell re-attaches on
// every program session start (MainShellWindow.StartProgramSessionAsync); the bridge detaches the
// previous runner first, so a day is never credited twice.

using System;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel.Avalonia
{
    public partial class App
    {
        /// <summary>WPF App.Programs (App.xaml.cs:2704): the multi-day Training Programs runtime, the full
        /// writing instance (timers, startup repair + rollover, Dispose flush). A file stamped by a newer
        /// build loads read-only (IsReadOnly) and the Programs tab greys its lifecycle controls.</summary>
        internal static ProgramService? Programs { get; set; }

        /// <summary>WPF App.xaml.cs:2165. After Settings, Progression and Quests (the verifier seam is
        /// QuestService.UpdateQuestProgress -> CoreQuests.TrackProgramVerifierProvider).</summary>
        private static void StartPrograms()
        {
            try
            {
                Programs = new ProgramService();
                CoreQuests.TrackProgramVerifierProvider = (category, amount) => Programs?.TrackVerifier(category, amount);
                if (Sessions is { } runner) Programs.AttachSessionRunner(runner);
            }
            catch (Exception ex)
            {
                // A broken ledger must never stop the app booting: unseeded = browse-only tab.
                Serilog.Log.Error(ex, "ProgramService failed to start");
                Programs = null;
            }
        }

        /// <summary>WPF App.OnExit: Programs?.Dispose() flushes the ledger.</summary>
        private static void StopPrograms()
        {
            try { Programs?.Dispose(); } catch (Exception ex) { Serilog.Log.Warning(ex, "ProgramService dispose failed"); }
        }
    }
}
