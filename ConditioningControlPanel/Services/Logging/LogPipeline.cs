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
        /// The global floor, held as a switch rather than a constant so the flight recorder can
        /// drop it to Debug later without the file sink following it down.
        /// </summary>
        public static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

        /// <summary>8 MB. The daily roll alone let one bad loop write an unbounded file.</summary>
        public const long FileSizeLimitBytes = 8L * 1024 * 1024;

        private static Logger? _logger;

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

            LevelSwitch.MinimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

            var config = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(LevelSwitch)
                // Order matters: the category is derived from the template, which redaction never
                // touches, but the formatter needs BOTH properties present when it renders.
                .Enrich.With(new CategoryEnricher())
                .Enrich.With(new RedactingEnricher())
                .WriteTo.File(
                    new CcpLineFormatter(),
                    Path.Combine(logsDir, "app-.log"),
                    restrictedToMinimumLevel: verbose ? LogEventLevel.Debug : LogEventLevel.Information,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    // A size cap as well as the daily roll: a render-loop exception writes the same
                    // line thousands of times a minute, and "one file per day" is no cap at all
                    // when the day has a storm in it.
                    fileSizeLimitBytes: FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    // Force a disk flush each second so the LAST lines survive a hard process death
                    // (a native OOM kills the process with no managed unwind - see chaos OOM telemetry).
                    flushToDiskInterval: TimeSpan.FromSeconds(1));

            var built = config.CreateLogger();
            var previous = _logger;
            _logger = built;
            try { previous?.Dispose(); } catch { /* swallow: replacing a logger must not throw */ }
            return built;
        }

        /// <summary>Flush and release the file handle. Idempotent.</summary>
        public static void Shutdown()
        {
            try { Log.CloseAndFlush(); } catch { /* swallow: shutdown is best effort */ }
            try { _logger?.Dispose(); } catch { /* swallow */ }
            _logger = null;
        }
    }
}
