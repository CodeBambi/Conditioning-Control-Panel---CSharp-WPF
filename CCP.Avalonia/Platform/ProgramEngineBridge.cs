using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/Program/ProgramEngineBridge.cs: hands the head's
    /// Core <see cref="SessionRunner"/> to <see cref="ProgramService"/> as three delegates plus the
    /// completed/ended callbacks. Re-attaching detaches the previous runner first.
    /// </summary>
    internal static class ProgramEngineBridge
    {
        private static SessionRunner? _attached;
        private static Action<Models.Session, bool>? _stopped;

        /// <summary>A session started or ended; the Programs tab repaints its session row (WPF
        /// OnSessionStarted/OnSessionStopped -> UpdateProgramSessionRow).</summary>
        internal static event Action? SessionChanged;

        internal static void RaiseSessionChanged() => SessionChanged?.Invoke();

        public static void AttachSessionRunner(this ProgramService service, SessionRunner runner)
        {
            if (_attached != null && _stopped != null) _attached.Stopped -= _stopped;

            // WPF raises SessionStopped then SessionCompleted; OnEngineSessionEnded posts its held
            // rollover, so the completion below always lands first either way.
            _stopped = (session, completed) =>
            {
                if (completed) service.OnEngineSessionCompleted(session.Id);
                service.OnEngineSessionEnded();
                RaiseSessionChanged();
            };
            runner.Stopped += _stopped;
            _attached = runner;

            // SessionRunner has no abandon tracker, so suppressAbandonTracking has nothing to suppress.
            service.AttachEngine(() => runner.IsRunning, () => runner.CurrentSession?.Id,
                _ => runner.Stop(completed: false));
        }
    }
}
