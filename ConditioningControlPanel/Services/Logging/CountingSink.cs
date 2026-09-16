using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// Counts warnings and errors as they go past, so the session footer can state what the run
    /// cost without anyone having to read the file to find out.
    ///
    /// <para>"warn=0 err=0" on the last line is the cheapest triage signal we have: a support
    /// thread can be answered from the footer alone, and a user who says "it was fine" against a
    /// footer reading err=37 is telling us where to look.</para>
    /// </summary>
    public sealed class CountingSink : ILogEventSink
    {
        private int _warnings;
        private int _errors;

        public int Warnings => Volatile.Read(ref _warnings);
        public int Errors => Volatile.Read(ref _errors);

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) return;
            if (logEvent.Level == LogEventLevel.Warning) Interlocked.Increment(ref _warnings);
            else if (logEvent.Level >= LogEventLevel.Error) Interlocked.Increment(ref _errors);
        }
    }
}
