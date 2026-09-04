using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// Builds the one logger the app uses. Everything about HOW lines are produced lives here, so
    /// the bootstrap in App.OnStartup stays a two-liner and the pipeline can be reasoned about (and
    /// changed) without touching a 5,000-line file.
    /// </summary>
    public static class LogPipeline
    {
        /// <summary>
        /// The global floor. Debug, so the 2,927 Debug calls in this codebase finally go SOMEWHERE:
        /// the flight recorder's ring, in memory. The file sink below is restricted separately, so
        /// what lands on disk is unchanged unless --verbose asks otherwise.
        /// </summary>
        public static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Debug);

        /// <summary>Identifies this run: names its log file, its diag dumps and its header.</summary>
        public static string SessionId { get; } = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        /// <summary>8 MB. Without a size cap one bad loop writes an unbounded file.</summary>
        public const long FileSizeLimitBytes = 8L * 1024 * 1024;

        /// <summary>This run's log file, or null before the pipeline is built.</summary>
        public static string? SessionFilePath { get; private set; }

        private static Logger? _logger;
        private static ThrottlingSink? _throttle;
        private static CountingSink? _counters;
        private static string? _logsDir;
        private static bool _footerHooked;
        private static bool _footerWritten;
        private static readonly object FooterGate = new();

        /// <summary>
        /// Where the header's <c>lang=</c> and <c>mod=</c> come from. A seam because the logger is
        /// built long before settings are loaded (see <see cref="LogAppReady"/>), and because tests
        /// must not need an App.
        /// </summary>
        internal static Func<(string Language, string ModId)> EnvironmentProvider { get; set; } = DefaultEnvironment;

        private static (string, string) DefaultEnvironment()
        {
            try
            {
                var s = App.Settings?.Current;
                return (s?.Language ?? "?", s?.ActiveModId ?? "?");
            }
            catch
            {
                return ("?", "?"); // swallow: never let a settings read cost us the log
            }
        }

        /// <summary>
        /// True when this run was asked for Debug on disk: <c>--verbose</c> on the command line or
        /// <c>CCP_LOG_VERBOSE=1</c> in the environment. Support asks for one or the other depending
        /// on whether the user can edit a shortcut, so both exist.
        /// </summary>
        public static bool VerboseRequested(string[]? args)
        {
            try
            {
                if (args != null)
                {
                    foreach (var a in args)
                    {
                        if (string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a, "-verbose", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a, "/verbose", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                var env = Environment.GetEnvironmentVariable("CCP_LOG_VERBOSE");
                return env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false; // swallow: an unreadable environment must not stop logging
            }
        }

        /// <summary>
        /// Configure the redactor's roots and build the logger. Safe to call once per process; a
        /// second call disposes the first logger.
        /// </summary>
        public static Serilog.ILogger Build(string logsDir, bool verbose)
        {
            LogRedactor.ConfigureRoots(App.UserDataPath, AppContext.BaseDirectory);
            LogRedactor.AssetsRootProvider = () => App.EffectiveAssetsPath;

            LevelSwitch.MinimumLevel = LogEventLevel.Debug;

            var recorder = new FlightRecorderSink(logsDir, SessionId);
            FlightRecorderSink.Instance = recorder;

            _logsDir = logsDir;
            var (lang, mod) = SafeEnvironment();
            // Retention and the header run BEFORE the sink opens the file: the sweep must not
            // consider the file we are about to write, and the header must be its first line.
            var sessionPath = SessionLog.Prepare(logsDir, SessionId, SafeVersion(), lang, mod);
            SessionFilePath = sessionPath;

            // The file sink is built as its own logger so it can be WRAPPED. Serilog's Logger is an
            // ILogEventSink, which is the only way to put the throttle between the pipeline and the
            // file without reimplementing the rolling file sink. Its own floor is Verbose because
            // the level decision belongs to the outer pipeline; filtering twice would mean two
            // places to get it wrong.
            var fileSink = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(
                    new CcpLineFormatter(),
                    sessionPath,
                    // One file per RUN, not per day. The daily file mixed the session that broke
                    // with the relaunch the user needed in order to report it; a session file is
                    // the same object as "the thing that went wrong", so it can be attached whole.
                    rollingInterval: RollingInterval.Infinite,
                    // The size cap stays: a render-loop exception writes the same line thousands of
                    // times a minute, and one run is no cap at all when the run has a storm in it.
                    fileSizeLimitBytes: FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    // Force a disk flush each second so the LAST lines survive a hard process death
                    // (a native OOM kills the process with no managed unwind - see chaos OOM telemetry).
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();

            var throttled = new ThrottlingSink(fileSink);
            var previousThrottle = _throttle;
            _throttle = throttled;

            var counters = new CountingSink();
            _counters = counters;

            var config = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(LevelSwitch)
                // Order matters: the category is derived from the template, which redaction never
                // touches, but the formatter needs BOTH properties present when it renders.
                .Enrich.With(new CategoryEnricher())
                .Enrich.With(new RedactingEnricher())
                // Debug reaches the ring and nothing else. The file keeps its Information floor
                // unless this run asked for verbose, so enabling Debug costs disk nothing.
                .WriteTo.Sink(recorder)
                .WriteTo.Sink(counters, restrictedToMinimumLevel: LogEventLevel.Warning)
                .WriteTo.Sink(throttled,
                    restrictedToMinimumLevel: verbose ? LogEventLevel.Debug : LogEventLevel.Information);

            try { previousThrottle?.Dispose(); } catch { /* swallow: replacing a sink must not throw */ }

            var built = config.CreateLogger();
            var previous = _logger;
            _logger = built;
            try { previous?.Dispose(); } catch { /* swallow: replacing a logger must not throw */ }

            HookFooter();
            return built;
        }

        /// <summary>
        /// The footer is written from ProcessExit rather than App.OnExit: OnExit does not run when
        /// the app is closed by a taskbar kill or a restart-for-update, and those sessions are
        /// precisely the ones whose end we want recorded.
        /// </summary>
        private static void HookFooter()
        {
            if (_footerHooked) return;
            _footerHooked = true;
            try { AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteSessionFooter(); }
            catch { /* swallow: no footer is survivable, a throw here is not */ }
        }

        /// <summary>
        /// Close the sinks and write the last line. Idempotent - whichever of ProcessExit and
        /// App.OnExit gets there first wins, and the second is a no-op.
        /// </summary>
        public static void WriteSessionFooter()
        {
            lock (FooterGate)
            {
                if (_footerWritten) return;
                _footerWritten = true;
            }

            int warn = _counters?.Warnings ?? 0;
            int err = _counters?.Errors ?? 0;
            int suppressed = _throttle?.TotalSuppressed ?? 0;
            var dir = _logsDir;

            // Sinks first: the file sink holds the handle, and the throttle's flush can still add
            // suppression summaries that belong above the footer.
            Shutdown();

            if (!string.IsNullOrEmpty(dir))
                SessionLog.WriteFooter(dir!, SessionId, Uptime(), warn, err, suppressed);
        }

        /// <summary>
        /// "App ready in N ms", from process start to the first idle dispatcher frame - the moment
        /// the window is actually usable. Startup regressions have shipped unnoticed because
        /// nothing in the log ever said how long startup took.
        /// </summary>
        public static void LogAppReady()
        {
            try
            {
                var (lang, mod) = SafeEnvironment();
                Log.Information("App ready in {Ms} ms | lang={Lang} mod={Mod}",
                    (long)Uptime().TotalMilliseconds, lang, mod);
            }
            catch { /* swallow: a timing line is not worth a startup crash */ }
        }

        private static TimeSpan Uptime()
        {
            try
            {
                using var p = System.Diagnostics.Process.GetCurrentProcess();
                var up = DateTime.Now - p.StartTime;
                return up > TimeSpan.Zero ? up : TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero; // swallow: a missing uptime prints 0:00:00
            }
        }

        private static (string, string) SafeEnvironment()
        {
            try { return EnvironmentProvider(); }
            catch { return ("?", "?"); /* swallow */ }
        }

        private static string SafeVersion()
        {
            try { return UpdateService.AppVersion ?? "?"; }
            catch { return "?"; /* swallow */ }
        }

        /// <summary>Flush and release the file handle. Idempotent.</summary>
        public static void Shutdown()
        {
            try { Log.CloseAndFlush(); } catch { /* swallow: shutdown is best effort */ }
            try { _logger?.Dispose(); } catch { /* swallow */ }
            // Disposing the throttle flushes whatever the last minute suppressed and then closes
            // the file sink underneath it.
            try { _throttle?.Dispose(); } catch { /* swallow */ }
            _logger = null;
            _throttle = null;
        }
    }
}
