using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The logic half of the updater, shared by every head: reading the GitHub release feed,
    /// picking the Setup asset, the skip/attempt markers and the helper-script text. Moved verbatim
    /// from the WPF UpdateService; the head keeps download, elevation, WebView2 cleanup and exit.
    /// Marker methods take the user-data folder so both heads (and tests) point at the same files.
    /// </summary>
    public static class ReleaseFeed
    {
        /// <summary>How long a skip marker suppresses re-offering the same version.</summary>
        public const double SkipMarkerLifetimeHours = 24;

        /// <summary>An attempt marker older than this is cleaned up silently instead of reported.</summary>
        public const double AttemptMarkerReportWindowDays = 7;

        /// <summary>Exit code slot for "the helper never recorded one".</summary>
        public const int UnknownExitCode = -1;

        // ---- release feed -------------------------------------------------------------------

        /// <summary>
        /// The version string from a releases API response's tag_name ("v4.4.4" or "4.4.4"), or
        /// null when there is no tag_name. Not validated: the caller runs it through System.Version.
        /// </summary>
        public static string? ParseTagVersion(string response)
        {
            var tagMatch = Regex.Match(response, "\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"");
            return tagMatch.Success ? tagMatch.Groups[1].Value : null;
        }

        /// <summary>
        /// Release notes and the size of the first asset whose name ends in Setup.exe (null when
        /// there is none). Throws on malformed JSON; the caller treats that as "no notes, size 0".
        /// </summary>
        public static (string Notes, long? SetupSize) ParseNotesAndSetupSize(string response)
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(response);
            var notes = json["body"]?.ToString() ?? "";
            if (json["assets"] is Newtonsoft.Json.Linq.JArray assets)
            {
                foreach (var asset in assets)
                {
                    var name = asset["name"]?.ToString() ?? "";
                    if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
                        return (notes, (long)(asset["size"] ?? 0));
                }
            }
            return (notes, null);
        }

        /// <summary>
        /// The installer's browser_download_url in a releases/tags response, or null. Pattern order
        /// matters: more specific first.
        /// </summary>
        public static string? FindSetupAssetUrl(string response, string version, string tag)
        {
            var patterns = new[] {
                $"-{version}-Setup.exe",     // Inno Setup: ConditioningControlPanel-5.2.4-Setup.exe
                $"-{tag}-Setup.exe",         // Inno Setup with tag format
                "Installer.exe",              // Generic installer name
                "Setup.exe"                   // Any Setup.exe (last resort)
            };
            foreach (var pattern in patterns)
            {
                var assetMatch = Regex.Match(
                    response,
                    $"\"browser_download_url\"\\s*:\\s*\"([^\"]*{Regex.Escape(pattern)}[^\"]*)\"",
                    RegexOptions.IgnoreCase);
                if (assetMatch.Success)
                    return assetMatch.Groups[1].Value;
            }
            return null;
        }

        // ---- skip marker --------------------------------------------------------------------

        private static string SkipFile(string userData) => Path.Combine(userData, "update_skip.txt");

        public static string? GetSkippedUpdateVersion(string userData)
        {
            try
            {
                var skipFile = SkipFile(userData);
                if (File.Exists(skipFile))
                {
                    var lines = File.ReadAllLines(skipFile);
                    return lines.Length > 0 ? lines[0] : null;
                }
            }
            catch { }
            return null;
        }

        public static DateTime GetSkippedUpdateTime(string userData)
        {
            try
            {
                var skipFile = SkipFile(userData);
                if (File.Exists(skipFile))
                    return File.GetLastWriteTime(skipFile);
            }
            catch { }
            return DateTime.MinValue;
        }

        /// <summary>Suppresses re-offering <paramref name="version"/> for 24h.</summary>
        public static void SetSkippedUpdateVersion(string userData, string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return;

            try
            {
                var skipFile = SkipFile(userData);
                Directory.CreateDirectory(Path.GetDirectoryName(skipFile)!);
                File.WriteAllText(skipFile, version);
                Log.Information("Marked update to {Version} as skipped - will not re-offer for {Hours}h",
                    version, SkipMarkerLifetimeHours);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to write update skip file");
            }
        }

        public static void ClearSkippedUpdateVersion(string userData)
        {
            try
            {
                var skipFile = SkipFile(userData);
                if (File.Exists(skipFile))
                {
                    File.Delete(skipFile);
                    Log.Debug("Cleared update skip marker");
                }
            }
            catch { }
        }

        // ---- attempt marker -----------------------------------------------------------------

        /// <summary>Result of a silent update attempt made by the previous run, read back on startup.</summary>
        public sealed class PendingUpdateOutcome
        {
            /// <summary>Version the previous run tried to install.</summary>
            public string Version { get; init; } = "";

            /// <summary>Inno Setup exit code, or <see cref="UnknownExitCode"/> if the helper never recorded one.</summary>
            public int ExitCode { get; init; } = UnknownExitCode;

            /// <summary>True only when the installer reported success AND we are actually running the new build.</summary>
            public bool Succeeded { get; init; }
        }

        public static string AttemptFilePath(string userData) => Path.Combine(userData, "update_attempt.txt");

        public static string AttemptResultFilePath(string userData) => Path.Combine(userData, "update_result.txt");

        /// <summary>Records that we are about to hand <paramref name="version"/> to the installer.</summary>
        public static void SetPendingUpdateAttempt(string userData, string version)
        {
            try
            {
                var attemptFile = AttemptFilePath(userData);
                Directory.CreateDirectory(Path.GetDirectoryName(attemptFile)!);
                try { File.Delete(AttemptResultFilePath(userData)); } catch { }
                File.WriteAllText(attemptFile, version);
                Log.Information("Recorded pending update attempt for {Version}", version);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to write update attempt marker");
            }
        }

        public static void ClearPendingUpdateAttempt(string userData)
        {
            try { File.Delete(AttemptFilePath(userData)); } catch { }
            try { File.Delete(AttemptResultFilePath(userData)); } catch { }
        }

        /// <summary>
        /// Decides whether a finished install attempt actually landed. Inno Setup returns 0 for
        /// success, 2 for user-cancelled and anything else for failure; a rolled-back install can
        /// still leave us on the old build, so the version has to agree with the exit code.
        /// </summary>
        public static bool DidUpdateSucceed(string attemptedVersion, string currentVersion, int exitCode)
        {
            if (exitCode != 0 && exitCode != UnknownExitCode)
                return false;

            if (!Version.TryParse(attemptedVersion, out var attempted) ||
                !Version.TryParse(currentVersion, out var current))
            {
                // Unparseable versions: trust the exit code alone rather than nagging blindly.
                return exitCode == 0;
            }

            return current >= attempted;
        }

        /// <summary>
        /// Reads back the previous run's install attempt and clears the marker. Returns null when no
        /// attempt was pending (or it was too old to be worth reporting). On failure the version is
        /// also written to the skip marker so we stop retrying the same broken install every launch.
        /// </summary>
        public static PendingUpdateOutcome? ConsumePendingUpdateOutcome(string userData, string currentVersion)
        {
            try
            {
                var attemptFile = AttemptFilePath(userData);
                if (!File.Exists(attemptFile))
                    return null;

                var version = File.ReadAllLines(attemptFile).FirstOrDefault()?.Trim() ?? "";
                var age = DateTime.Now - File.GetLastWriteTime(attemptFile);

                var exitCode = UnknownExitCode;
                try
                {
                    var resultFile = AttemptResultFilePath(userData);
                    if (File.Exists(resultFile))
                    {
                        var raw = File.ReadAllLines(resultFile).FirstOrDefault()?.Trim();
                        if (int.TryParse(raw, out var parsed)) exitCode = parsed;
                    }
                }
                catch { }

                ClearPendingUpdateAttempt(userData);

                if (string.IsNullOrEmpty(version))
                    return null;

                var succeeded = DidUpdateSucceed(version, currentVersion, exitCode);

                if (succeeded)
                {
                    Log.Information("Previous update to {Version} completed (installer exit={Code})", version, exitCode);
                    ClearSkippedUpdateVersion(userData);
                    return new PendingUpdateOutcome { Version = version, ExitCode = exitCode, Succeeded = true };
                }

                Log.Error("Update to {Version} did not install (installer exit={Code}, still running {Current})",
                    version, exitCode, currentVersion);

                // Stop the silent retry loop: don't re-offer this version for 24h.
                SetSkippedUpdateVersion(userData, version);

                if (age.TotalDays > AttemptMarkerReportWindowDays)
                {
                    Log.Information("Update attempt marker is {Days:F1} days old, not reporting to the user", age.TotalDays);
                    return null;
                }

                return new PendingUpdateOutcome { Version = version, ExitCode = exitCode, Succeeded = false };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read pending update outcome");
                return null;
            }
        }

        // ---- helper script ------------------------------------------------------------------

        /// <summary>
        /// Text of the external update helper batch script: waits for this process (pid) to exit,
        /// runs the installer silently, records the installer's exit code to resultPath, then
        /// relaunches appExe. Values are baked in as literals so paths with spaces are safe.
        /// Written by the head as UTF-8 without BOM (a BOM can break batch parsing of line one).
        /// </summary>
        public static string BuildUpdateHelperScript(string installerPath, string? installPath, int pid,
            string appExe, string logPath, string resultPath, bool elevated)
        {
            // For an in-place upgrade Inno remembers the prior dir; passing /DIR keeps it explicit
            // and guarantees the new files land where we'll relaunch from.
            var dirArg = string.IsNullOrEmpty(installPath) ? "" : $" /DIR=\"{installPath}\"";

            // When the helper is elevated, a plain `start` would hand the app an admin token for the
            // rest of the session. Going through the shell drops it back to the logged-on user's
            // medium-integrity token, which is what every other launch of the app uses.
            var relaunch = elevated
                ? "start \"\" explorer.exe \"%APPEXE%\""
                : "start \"\" \"%APPEXE%\"";

            var lines = new[]
            {
                "@echo off",
                "setlocal enableextensions",
                $"set \"LOG={logPath}\"",
                $"set \"APPEXE={appExe}\"",
                $"set \"INSTALLER={installerPath}\"",
                $"set \"RESULT={resultPath}\"",
                $"echo [update-helper] start pid={pid} elevated={(elevated ? 1 : 0)} > \"%LOG%\"",
                // Wait for the old app to fully exit (release its exe lock). The PID filter is
                // exact; `find` just detects whether a matching row was returned. Capped at ~30
                // iterations (~1min) so a reused PID can never hang this forever.
                "set /a tries=0",
                ":waitloop",
                $"tasklist /FI \"PID eq {pid}\" /NH 2>nul | find \"{pid}\" >nul",
                "if errorlevel 1 goto gone",
                "set /a tries+=1",
                "if %tries% GEQ 30 (",
                "  echo [update-helper] wait timed out, proceeding anyway >> \"%LOG%\"",
                "  goto gone",
                ")",
                "ping 127.0.0.1 -n 2 >nul",
                "goto waitloop",
                ":gone",
                "echo [update-helper] old app exited (tries=%tries%), running installer >> \"%LOG%\"",
                $"\"%INSTALLER%\" /SILENT /SUPPRESSMSGBOXES /NORESTART{dirArg}",
                "set RC=%errorlevel%",
                // Redirect FIRST: `echo %RC%>file` would parse a single-digit RC as a handle number.
                ">\"%RESULT%\" echo %RC%",
                "if \"%RC%\"==\"0\" (",
                "  echo [update-helper] installer reported success >> \"%LOG%\"",
                ") else (",
                "  echo [update-helper] installer FAILED exit=%RC% >> \"%LOG%\"",
                ")",
                "echo [update-helper] relaunching app >> \"%LOG%\"",
                relaunch,
                "echo [update-helper] done >> \"%LOG%\"",
                "endlocal",
            };

            return string.Join("\r\n", lines) + "\r\n";
        }
    }
}
