using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Hands the WPF head's <see cref="SessionEngine"/> to the Core-side
    /// <see cref="ProgramService"/>, which now knows the engine only as three delegates and two
    /// callbacks - it has to, because the ledger is in CCP.Core and the engine is not.
    ///
    /// An extension method rather than an edit to the two call sites: <c>App.Programs?.
    /// AttachSessionEngine(_sessionEngine)</c> in MainWindow.Presets.cs and
    /// MainWindow.ProgramsTab.cs keeps reading exactly as it did.
    /// </summary>
    internal static class ProgramEngineBridge
    {
        private static SessionEngine? _attached;
        private static EventHandler<SessionCompletedEventArgs>? _completed;
        private static EventHandler? _stopped;

        /// <summary>
        /// Wire the MainWindow-owned session engine. Mirrors BarkService.AttachSessionEngine - the
        /// engine is created lazily on first session, so the service cannot subscribe at its own
        /// construction. Re-attaching detaches the previous engine first.
        /// </summary>
        public static void AttachSessionEngine(this ProgramService service, SessionEngine engine)
        {
            if (service == null || engine == null) return;

            try
            {
                if (_attached != null)
                {
                    if (_completed != null) _attached.SessionCompleted -= _completed;
                    if (_stopped != null) _attached.SessionStopped -= _stopped;
                }

                _completed = (_, e) => service.OnEngineSessionCompleted(e?.Session?.Id);
                engine.SessionCompleted += _completed;

                // A held rollover has to be released on ANY end, not just a completion: a session
                // the user stops at 04:05 would otherwise keep the clock shut until the next minute
                // tick, and a stop during shutdown would never release it at all.
                _stopped = (_, _) => service.OnEngineSessionEnded();
                engine.SessionStopped += _stopped;

                _attached = engine;

                service.AttachEngine(
                    () => engine.IsRunning,
                    () => engine.CurrentSession?.Id,
                    suppressAbandonTracking => engine.StopSession(
                        suppressAbandonTracking: suppressAbandonTracking));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ProgramService: AttachSessionEngine failed");
            }
        }
    }
}
