// PORTED from ConditioningControlPanel/App.xaml.cs:2165 (Programs = new ProgramService()) and
// MainWindow.Presets.cs:1611 (App.Programs.AttachSessionEngine) - progression#1.
// The Core ProgramService owns the ledger, the day clock and the verifier pass; this head hands it
// the session runner (AttachEngine) and the end-of-session signal (the runner's session log, which
// carries the session id and whether it completed - the same two facts WPF's SessionCompleted /
// SessionStopped pair gives it).

using System;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel.Avalonia
{
    public partial class App
    {
        /// <summary>WPF App.Programs: the multi-day Training Programs runtime.</summary>
        internal static ProgramService? Programs { get; set; }

        /// <summary>WPF App.xaml.cs:2165. After Settings, Progression and Quests (the verifier seam is
        /// QuestService.UpdateQuestProgress -> CoreQuests.TrackProgramVerifierProvider).</summary>
        private static void StartPrograms()
        {
            try
            {
                Programs = new ProgramService();
                CoreQuests.TrackProgramVerifierProvider = (category, amount) => Programs?.TrackVerifier(category, amount);
                if (Sessions is { } runner)
                {
                    Programs.AttachEngine(
                        () => runner.IsRunning,
                        () => runner.CurrentSession?.Id,
                        suppressAbandon => runner.Stop(completed: false));
                    runner.SessionLog.LogReady += (_, e) =>
                    {
                        try
                        {
                            if (e.Log.Completed) Programs?.OnEngineSessionCompleted(e.Log.SessionId);
                            Programs?.OnEngineSessionEnded();
                        }
                        catch (Exception ex) { Serilog.Log.Warning(ex, "[Programs] session end hook failed"); }
                    };
                }
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
