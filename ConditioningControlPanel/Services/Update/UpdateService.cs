using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

using AppUpdateInfo = ConditioningControlPanel.Models.UpdateInfo;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles automatic updates by checking the GitHub Releases API and running the
    /// Inno Setup installer. Velopack was removed in v5.8.4 (dead since 5.4.10).
    /// </summary>
    public class UpdateService : IDisposable
    {
        /// <summary>
        /// Current application version - UPDATE THIS WHEN BUMPING VERSION
        /// </summary>
        public const string AppVersion = "6.9.1";

        /// <summary>
        /// Patch notes for the current version - UPDATE THIS WHEN BUMPING VERSION
        /// These are shown in the update dialog and can be used when GitHub release notes are unavailable.
        /// </summary>
        public const string CurrentPatchNotes = @"v6.9.1 - The Spiral

this is the big one. on September 1st the Descent ceremony ran and the monthly wipe is gone forever. from here on your level, your XP and your hours are yours for good. this is the version that carries you into that world, and i wanted it to feel special. it does :3

THE ARCADEMY
- the campus takes center stage. classes that are secretly little games, a prize counter, your own locker, report cards, the works. wander around, get graded, get conditioned.
- EMI runs the halls. she deals commentary, hands out hints in Daily Trigger, and cameos in Impulse Control when you least expect her.
- your Sparks wallet follows you around the whole campus, and it now tells you WHY it refused a purchase instead of just refusing.
- faster on phones, prettier in portrait, and the flash guard slider finally says which way is gentler.

EMI'S DESK
- EMI got out of the school. summon her onto your desktop: a little ring on the edge of your screen opens into her CRT glass and she just hangs out. reacts to what the app is doing to you. picks up props. lives her life.
- we wrote her a whole handbook together. Ask EMI anything about the app and she answers, in all 9 languages. it is the new guide and she is very proud of it.
- new users get a proper first contact from her now instead of silence.

EVERYWHERE AT ONCE
- one account, everywhere. desktop, the web at cclabs.app, and now your phone. XP, wallet, presence, profile, it all connects.
- the CCP Mobile public beta is live! grab the APK here: https://github.com/CodeBambi/CCP-Mobile-Releases/releases/latest

THE DESCENT
- the wipe is gone. nothing clears any more, and levels and hours carry forward from here on.
- the season recap card only shows if a reset actually happened to you, the countdown is honest and stands down when it is over, and if a stray reset order ever reaches the app after the Descent, the app refuses it. your progress does not roll back because a server hiccuped.

NEW MOD: INFECTION CONTROL
- a sixth mod joins the shelf, created by Miss Jenny. Nurse Amber runs the ward with over 330 freshly voiced lines: barks, mantra takeovers, her own flash voicelines, and her voice all the way down the Rabbit Hole.
- her own spiral, her own colors, and she comments on nearly everything you do.
- like the other built-ins the audio arrives as a downloadable pack so the installer stays lean.

AND SOME FIXES
- the updater now upgrades the copy you are actually running, the fade slider actually fades, escape always escapes when it should, muted means muted, tooltips are readable again, ghost mode stops freezing, and the menus behave.
- full nerd changelog in the pull requests, numbers 380 through 445.

6.9.1 HOTFIXES (the day after)
- the ceremony's two doors fit on screen at high display scaling, and ""not tonight"" stops re-asking for the session.
- your profile card shows which door you picked, with the +10% on the XP numbers if you cycled.
- the board notice, the Spiral help card and EMI's barks all agree now: nothing was wiped and nothing resets.
- bubbles have their own ""stare to pop"" switch on the bubble page.
- a headset play/pause tap no longer ends a strict lock video.
- a failed update download gets a proper box and a manual download button, and the exe carries its real version number.
- flash audio unducks after a stop, reveal in explorer works with spaces, customize and privacy are hidden on other people's cards, OCR text stays out of the logs, and the single-digit age bypass is closed.
- full nerd changelog in pull requests 451 through 475.";

        private const string GitHubOwner = "CodeBambi";
        private const string GitHubRepo = "Conditioning-Control-Panel---CSharp-WPF";

        /// <summary>
        /// Manual-download fallback shown whenever an automatic install could not complete.
        /// </summary>
        public const string ReleasesPageUrl = "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/latest";

        /// <summary>
        /// How long a skip marker suppresses re-offering the same version. The marker is written
        /// when the user dismisses the update dialog and when an install attempt failed, so this
        /// is the "don't pester me again" window in both cases.
        /// </summary>
        private const double SkipMarkerLifetimeHours = 24;

        /// <summary>
        /// An attempt marker older than this is stale (app wasn't restarted for days) and is
        /// cleaned up silently instead of raising a confusing after-the-fact failure dialog.
        /// </summary>
        private const double AttemptMarkerReportWindowDays = 7;

        /// <summary>Exit code slot for "the helper never recorded one".</summary>
        internal const int UnknownExitCode = -1;

        private AppUpdateInfo? _latestUpdate;
        private bool _disposed;

        /// <summary>
        /// Fired when an update is available
        /// </summary>
        public event EventHandler<AppUpdateInfo>? UpdateAvailable;

        /// <summary>
        /// Fired when download progress changes (0-100)
        /// </summary>
        public event EventHandler<int>? DownloadProgressChanged;

        /// <summary>
        /// Fired when an update check or download fails
        /// </summary>
        public event EventHandler<Exception>? UpdateFailed;

        /// <summary>
        /// Whether an update is available
        /// </summary>
        public bool IsUpdateAvailable => _latestUpdate?.IsNewer == true;

        /// <summary>
        /// Information about the latest available update
        /// </summary>
        public AppUpdateInfo? LatestUpdate => _latestUpdate;

        /// <summary>
        /// Whether a download is in progress
        /// </summary>
        public bool IsDownloading { get; private set; }

        /// <summary>
        /// Gets the install path from registry (set by the installer).
        /// Returns null if not installed via installer or registry key not found.
        /// </summary>
        public static string? GetInstalledPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\CodeBambi\Conditioning Control Panel");
                return key?.GetValue("InstallPath") as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The folder the running exe actually lives in, but only when that folder is a real
        /// Inno install (its uninstaller sits next to the exe). This is the ground truth for an
        /// in-place upgrade: unlike the HKCU InstallPath echo it cannot go stale, and it moves
        /// with the folder when a user relocates the install by hand. Returns null for a
        /// portable/dev run, or when the exe path can't be resolved.
        /// </summary>
        private static string? GetRunningInstallDir()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                if (string.IsNullOrEmpty(exeDir)) return null;

                return File.Exists(Path.Combine(exeDir, "unins000.exe")) ? exeDir : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the installed version from registry (set by the installer).
        /// Returns null if not installed via installer or registry key not found.
        /// </summary>
        public static string? GetInstalledVersion()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\CodeBambi\Conditioning Control Panel");
                return key?.GetValue("Version") as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Whether the app was installed via the installer. The registry entry is the primary
        /// signal, but it lives in HKCU: an install run under a different (elevated) account writes
        /// it to that account's hive, which would silently disable updates forever. Inno's
        /// uninstaller sitting next to our exe is the account-independent fallback.
        /// </summary>
        public static bool IsInstalledViaInstaller
        {
            get
            {
                if (GetInstalledPath() != null) return true;

                try
                {
                    var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                    return !string.IsNullOrEmpty(exeDir) && File.Exists(Path.Combine(exeDir, "unins000.exe"));
                }
                catch
                {
                    return false;
                }
            }
        }

        public UpdateService()
        {
            // No initialization needed — checks query the GitHub Releases API directly.
        }

        /// <summary>
        /// Gets the current application version
        /// </summary>
        public static Version GetCurrentVersion()
        {
            // Use the hardcoded AppVersion constant - most reliable method
            if (Version.TryParse(AppVersion, out var version))
            {
                return version;
            }
            return new Version(1, 0, 0);
        }

        /// <summary>
        /// Check for updates asynchronously
        /// </summary>
        /// <param name="forceCheck">If true, bypasses the 24-hour skip logic for failed updates</param>
        public async Task<AppUpdateInfo?> CheckForUpdatesAsync(bool forceCheck = false, CancellationToken ct = default)
        {
            try
            {
                if (App.Settings?.Current?.OfflineMode == true)
                {
                    App.Logger?.Information("Offline mode enabled, skipping update check");
                    return null;
                }

                App.Logger?.Information("Checking for updates... (current AppVersion: {Version}, force: {Force}, IsInstalledViaInstaller: {IsInstalled})",
                    AppVersion, forceCheck, IsInstalledViaInstaller);

                // Only installed users can self-update; dev/source runs are skipped.
                if (!IsInstalledViaInstaller)
                {
                    App.Logger?.Information("App not installed via installer (running from source/dev), skipping update check");
                    return null;
                }

                // Loop-prevention: if the user dismissed this version, or an install attempt for it
                // didn't take, suppress it for 24h so we don't pester them every launch. The marker
                // must therefore survive the whole 24h — clearing it earlier made the window below
                // unreachable and the skip logic a no-op (#849).
                var skippedVersion = GetSkippedUpdateVersion();
                if (!string.IsNullOrEmpty(skippedVersion))
                {
                    var skipAge = DateTime.Now - GetSkippedUpdateTime();
                    if (forceCheck)
                    {
                        App.Logger?.Information("Force check requested, clearing skip marker for {Version}", skippedVersion);
                        ClearSkippedUpdateVersion();
                        skippedVersion = null;
                    }
                    else if (skipAge.TotalHours >= SkipMarkerLifetimeHours)
                    {
                        App.Logger?.Information("Skip marker for {Version} is {Hours:F1}h old (>= {Limit}h), clearing it",
                            skippedVersion, skipAge.TotalHours, SkipMarkerLifetimeHours);
                        ClearSkippedUpdateVersion();
                        skippedVersion = null;
                    }
                }

                var githubUpdate = await CheckGitHubReleasesAsync();
                if (githubUpdate == null)
                {
                    App.Logger?.Information("No updates available from GitHub API");
                    _latestUpdate = null;
                    ClearSkippedUpdateVersion();
                    return null;
                }

                // The marker was already aged out above, so anything still here is inside the window.
                if (githubUpdate.IsNewer && !string.IsNullOrEmpty(skippedVersion) && skippedVersion == githubUpdate.Version)
                {
                    var hoursSinceSkip = (DateTime.Now - GetSkippedUpdateTime()).TotalHours;
                    App.Logger?.Information("Suppressing update to {Version} — skipped/attempted {Hours:F1}h ago. Re-offering after {Limit}h.",
                        githubUpdate.Version, hoursSinceSkip, SkipMarkerLifetimeHours);
                    githubUpdate.IsNewer = false;
                }

                _latestUpdate = githubUpdate;
                if (_latestUpdate.IsNewer)
                {
                    App.Logger?.Information("Update available: {Version}", _latestUpdate.Version);
                    UpdateAvailable?.Invoke(this, _latestUpdate);
                }
                else
                {
                    App.Logger?.Information("Already on latest version: {Version}", AppVersion);
                }

                return _latestUpdate;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to check for updates");
                UpdateFailed?.Invoke(this, ex);
                return null;
            }
        }

        private static string GetSkipFilePath()
        {
            return Path.Combine(App.UserDataPath, "update_skip.txt");
        }

        private static string? GetSkippedUpdateVersion()
        {
            try
            {
                var skipFile = GetSkipFilePath();
                if (File.Exists(skipFile))
                {
                    var lines = File.ReadAllLines(skipFile);
                    return lines.Length > 0 ? lines[0] : null;
                }
            }
            catch { }
            return null;
        }

        private static DateTime GetSkippedUpdateTime()
        {
            try
            {
                var skipFile = GetSkipFilePath();
                if (File.Exists(skipFile))
                {
                    return File.GetLastWriteTime(skipFile);
                }
            }
            catch { }
            return DateTime.MinValue;
        }

        /// <summary>
        /// Suppresses re-offering <paramref name="version"/> for 24h. Called when the user dismisses
        /// the update dialog and when an install attempt for that version failed.
        /// </summary>
        public static void SetSkippedUpdateVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return;

            try
            {
                var skipFile = GetSkipFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(skipFile)!);
                File.WriteAllText(skipFile, version);
                App.Logger?.Information("Marked update to {Version} as skipped - will not re-offer for {Hours}h",
                    version, SkipMarkerLifetimeHours);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to write update skip file");
            }
        }

        private static void ClearSkippedUpdateVersion()
        {
            try
            {
                var skipFile = GetSkipFilePath();
                if (File.Exists(skipFile))
                {
                    File.Delete(skipFile);
                    App.Logger?.Debug("Cleared update skip marker");
                }
            }
            catch { }
        }

        /// <summary>
        /// Result of a silent update attempt made by the previous run, read back on startup.
        /// </summary>
        public sealed class PendingUpdateOutcome
        {
            /// <summary>Version the previous run tried to install.</summary>
            public string Version { get; init; } = "";

            /// <summary>Inno Setup exit code, or <see cref="UnknownExitCode"/> if the helper never recorded one.</summary>
            public int ExitCode { get; init; } = UnknownExitCode;

            /// <summary>True only when the installer reported success AND we are actually running the new build.</summary>
            public bool Succeeded { get; init; }
        }

        private static string GetAttemptFilePath() => Path.Combine(App.UserDataPath, "update_attempt.txt");

        private static string GetAttemptResultFilePath() => Path.Combine(App.UserDataPath, "update_result.txt");

        /// <summary>
        /// Records that we are about to hand <paramref name="version"/> to the installer. The marker
        /// is read back on the next launch to tell a real upgrade from a silent rollback.
        /// </summary>
        private static void SetPendingUpdateAttempt(string version)
        {
            try
            {
                var attemptFile = GetAttemptFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(attemptFile)!);
                try { File.Delete(GetAttemptResultFilePath()); } catch { }
                File.WriteAllText(attemptFile, version);
                App.Logger?.Information("Recorded pending update attempt for {Version}", version);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to write update attempt marker");
            }
        }

        private static void ClearPendingUpdateAttempt()
        {
            try { File.Delete(GetAttemptFilePath()); } catch { }
            try { File.Delete(GetAttemptResultFilePath()); } catch { }
        }

        /// <summary>
        /// Decides whether a finished install attempt actually landed. Inno Setup returns 0 for
        /// success, 2 for user-cancelled and anything else for failure; a rolled-back install can
        /// still leave us on the old build, so the version has to agree with the exit code.
        /// </summary>
        internal static bool DidUpdateSucceed(string attemptedVersion, string currentVersion, int exitCode)
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
        public static PendingUpdateOutcome? ConsumePendingUpdateOutcome()
        {
            try
            {
                var attemptFile = GetAttemptFilePath();
                if (!File.Exists(attemptFile))
                    return null;

                var version = File.ReadAllLines(attemptFile).FirstOrDefault()?.Trim() ?? "";
                var age = DateTime.Now - File.GetLastWriteTime(attemptFile);

                var exitCode = UnknownExitCode;
                try
                {
                    var resultFile = GetAttemptResultFilePath();
                    if (File.Exists(resultFile))
                    {
                        var raw = File.ReadAllLines(resultFile).FirstOrDefault()?.Trim();
                        if (int.TryParse(raw, out var parsed)) exitCode = parsed;
                    }
                }
                catch { }

                ClearPendingUpdateAttempt();

                if (string.IsNullOrEmpty(version))
                    return null;

                var succeeded = DidUpdateSucceed(version, AppVersion, exitCode);

                if (succeeded)
                {
                    App.Logger?.Information("Previous update to {Version} completed (installer exit={Code})", version, exitCode);
                    ClearSkippedUpdateVersion();
                    return new PendingUpdateOutcome { Version = version, ExitCode = exitCode, Succeeded = true };
                }

                App.Logger?.Error("Update to {Version} did not install (installer exit={Code}, still running {Current})",
                    version, exitCode, AppVersion);

                // Stop the silent retry loop: don't re-offer this version for 24h.
                SetSkippedUpdateVersion(version);

                if (age.TotalDays > AttemptMarkerReportWindowDays)
                {
                    App.Logger?.Information("Update attempt marker is {Days:F1} days old, not reporting to the user", age.TotalDays);
                    return null;
                }

                return new PendingUpdateOutcome { Version = version, ExitCode = exitCode, Succeeded = false };
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to read pending update outcome");
                return null;
            }
        }

        /// <summary>
        /// Downloads the Setup.exe installer from GitHub releases for fresh install updates.
        /// </summary>
        public async Task<string?> DownloadInstallerAsync(Action<int>? progressCallback = null, CancellationToken ct = default)
        {
            if (_latestUpdate == null)
            {
                throw new InvalidOperationException("No update available to download");
            }

            try
            {
                IsDownloading = true;
                App.Logger?.Information("Downloading installer for fresh install, version {Version}...", _latestUpdate.Version);

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "ConditioningControlPanel");
                client.Timeout = TimeSpan.FromMinutes(10);

                // Get release assets from GitHub API
                var version = _latestUpdate.Version;
                var tags = new[] { $"v{version}", version };
                string? downloadUrl = null;
                string? assetName = null;

                foreach (var tag in tags)
                {
                    try
                    {
                        var apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/tags/{tag}";
                        var response = await client.GetStringAsync(apiUrl);

                        // Find the Setup.exe asset. Pattern order matters: more specific first.
                        var patterns = new[] {
                            $"-{version}-Setup.exe",     // Inno Setup: ConditioningControlPanel-5.2.4-Setup.exe
                            $"-{tag}-Setup.exe",         // Inno Setup with tag format
                            "Installer.exe",              // Generic installer name
                            "Setup.exe"                   // Any Setup.exe (last resort)
                        };
                        foreach (var pattern in patterns)
                        {
                            var assetMatch = System.Text.RegularExpressions.Regex.Match(
                                response,
                                $"\"browser_download_url\"\\s*:\\s*\"([^\"]*{System.Text.RegularExpressions.Regex.Escape(pattern)}[^\"]*)\"",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                            if (assetMatch.Success)
                            {
                                downloadUrl = assetMatch.Groups[1].Value;
                                assetName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                                App.Logger?.Information("Found installer asset: {Asset}", assetName);
                                break;
                            }
                        }

                        if (downloadUrl != null) break;
                    }
                    catch
                    {
                        // Tag not found, try next
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new InvalidOperationException($"Could not find Setup.exe installer in GitHub release {version}");
                }

                // Download to temp directory — wipe old installers from previous updates first
                var tempDir = Path.Combine(Path.GetTempPath(), "ConditioningControlPanel_Update");
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
                Directory.CreateDirectory(tempDir);
                var installerPath = Path.Combine(tempDir, assetName ?? "Setup.exe");

                App.Logger?.Information("Downloading installer from {Url} to {Path}", downloadUrl, installerPath);

                // Download with progress and retry logic for transient network errors
                const int maxRetries = 3;
                Exception? lastException = null;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (attempt > 1)
                        {
                            App.Logger?.Information("Retry attempt {Attempt}/{Max} after network error...", attempt, maxRetries);
                            // Exponential backoff: 2s, 4s, 8s
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                        }

                        using var downloadResponse = await client.GetAsync(downloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
                        downloadResponse.EnsureSuccessStatusCode();

                        var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1;
                        var downloadedBytes = 0L;

                        using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        int bytesRead;
                        var lastProgress = -1;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                            downloadedBytes += bytesRead;

                            if (totalBytes > 0)
                            {
                                var progress = (int)(downloadedBytes * 100 / totalBytes);
                                if (progress != lastProgress)
                                {
                                    lastProgress = progress;
                                    progressCallback?.Invoke(progress);
                                    DownloadProgressChanged?.Invoke(this, progress);
                                }
                            }
                        }

                        App.Logger?.Information("Installer downloaded successfully: {Path} ({Size:F1} MB)",
                            installerPath, downloadedBytes / (1024.0 * 1024.0));

                        // Success - exit retry loop
                        lastException = null;
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries && IsTransientNetworkError(ex))
                    {
                        lastException = ex;
                        App.Logger?.Warning(ex, "Download attempt {Attempt} failed with transient error", attempt);
                    }
                }

                // If we exhausted retries, throw the last exception
                if (lastException != null)
                {
                    throw new InvalidOperationException($"Failed to download installer after {maxRetries} attempts: {lastException.Message}", lastException);
                }

                return installerPath;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to download installer");
                UpdateFailed?.Invoke(this, ex);
                throw;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        /// <summary>
        /// Runs the downloaded installer and exits the current application.
        /// The installer will handle the fresh install with folder selection.
        /// </summary>
        public void RunInstallerAndExit(string installerPath)
        {
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException("Installer not found", installerPath);
            }

            App.Logger?.Information("Launching installer for fresh install: {Path}", installerPath);

            // Save settings before exit
            App.Settings?.Save();

            // Clean up browser data and kill WebView2 processes to prevent file locks
            CleanupBeforeFreshInstall();

            // Small delay to ensure processes are terminated
            System.Threading.Thread.Sleep(500);

            // Start the installer
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                // Don't pass any arguments - let the user go through normal install flow
            };

            Process.Start(startInfo);

            // Exit the current application
            App.Logger?.Information("Exiting application for fresh install...");
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Runs the downloaded Inno Setup installer silently to update in place.
        /// Uses the current install path from registry to upgrade without user interaction.
        /// Returns false when the helper could not be launched (e.g. the user declined the UAC
        /// prompt) — the app is then still running and the caller must surface that.
        /// </summary>
        public bool RunInstallerSilentlyAndExit(string installerPath)
        {
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException("Installer not found", installerPath);
            }

            // Where to upgrade. The folder the running exe lives in wins over the registry:
            // HKCU InstallPath is only an echo written at install time, so it goes stale the
            // moment the user moves the folder by hand or an earlier update relocated the app.
            // Feeding a stale value to /DIR is what pins the relocation in place - every future
            // update installs into a folder the user never launches from (ccp-bugs#1090,
            // ccp-bugs#1004), so they keep starting the old exe and keep being told an update is
            // available (ccp-bugs#973, #849, #567, #554). Upgrading whatever copy is running
            // also self-heals an install that already drifted.
            var registryPath = GetInstalledPath();
            var installPath = GetRunningInstallDir();

            if (string.IsNullOrEmpty(installPath))
            {
                // The running copy is not a recognisable Inno install (portable or dev run, or
                // the uninstaller was deleted). Use the registry echo, then the exe's own
                // folder. If neither resolves we pass no /DIR at all and let Inno's
                // UsePreviousAppDir pick the previous location - no /DIR beats a wrong one.
                installPath = !string.IsNullOrEmpty(registryPath)
                    ? registryPath
                    : Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            }
            else if (!string.IsNullOrEmpty(registryPath) &&
                     !string.Equals(Path.TrimEndingDirectorySeparator(installPath),
                                    Path.TrimEndingDirectorySeparator(registryPath),
                                    StringComparison.OrdinalIgnoreCase))
            {
                App.Logger?.Warning(
                    "Install path mismatch - running from {Running} but registry says {Registry}. " +
                    "Upgrading the running copy; the registry value is stale.", installPath, registryPath);
            }

            App.Logger?.Information("Launching installer for silent update: {Path}, InstallDir: {Dir}", installerPath, installPath);

            // Save settings before exit
            App.Settings?.Save();

            // Clean up browser data and kill WebView2 processes to prevent file locks
            CleanupBeforeFreshInstall();

            // Small delay to ensure processes are terminated
            System.Threading.Thread.Sleep(500);

            // Launching the installer directly and immediately calling Shutdown() is a race:
            // this app is a single-file self-contained exe, so it holds an exclusive lock on
            // its own .exe until the process fully exits. The installer's file-copy phase can
            // reach the locked exe before we're gone, and with /SUPPRESSMSGBOXES that failure
            // is silent — the app just vanishes on the old version (#499, BUG-DYRJU5AUDM).
            //
            // Instead, hand off to a tiny external batch helper that: (1) waits for THIS
            // process to fully exit (lock released), (2) runs the installer silently and waits
            // for it, (3) relaunches the app deterministically — never depending on Inno's
            // /RESTARTAPPLICATIONS (which needs RegisterApplicationRestart, which we never call).
            // Every step is logged so a failure is diagnosable instead of invisible.
            var pid = Process.GetCurrentProcess().Id;

            // The freshly-installed exe lands back in the install dir under its known name.
            var appExe = !string.IsNullOrEmpty(installPath)
                ? Path.Combine(installPath, "ConditioningControlPanel.exe")
                : (Process.GetCurrentProcess().MainModule?.FileName ?? "");

            var logPath = Path.Combine(
                App.UserDataPath, "logs", "update-helper.log");

            // installer.iss is PrivilegesRequired=lowest with the override only offered via a
            // *dialog* — which /SILENT can never show. So a Program Files install gets a
            // non-elevated setup that fails, rolls back, and /SUPPRESSMSGBOXES hides it (#849).
            // Elevate the helper (not the installer) so the UAC prompt appears now, while the app
            // is still on screen and we can detect a decline; the installer then inherits the
            // elevated token and the wait-for-exit handoff is unchanged.
            var needsElevation = NeedsElevationToInstall(installPath);

            var helperPath = WriteUpdateHelperScript(
                installerPath, installPath, pid, appExe, logPath, GetAttemptResultFilePath(), needsElevation);

            App.Logger?.Information("Launching update helper: {Helper} (pid={Pid}, appExe={AppExe}, elevate={Elevate}, log={Log})",
                helperPath, pid, appExe, needsElevation, logPath);

            // Run the .cmd via cmd.exe (a batch file is not a PE, so it needs an explicit
            // interpreter). WindowStyle=Hidden keeps the console off screen in both modes; the
            // helper survives our exit because a WPF app doesn't job-object its children.
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{helperPath}\"",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            if (needsElevation)
            {
                // Verb requires ShellExecute; CreateNoWindow is ignored in that mode.
                startInfo.UseShellExecute = true;
                startInfo.Verb = "runas";
            }
            else
            {
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
            }

            // Written before launch so the next startup can tell a real upgrade from a rollback.
            SetPendingUpdateAttempt(_latestUpdate?.Version ?? "");

            try
            {
                Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED — user dismissed the UAC prompt. Stay alive on the old version.
                App.Logger?.Warning("Update cancelled: the elevation prompt was declined");
                ClearPendingUpdateAttempt();
                SetSkippedUpdateVersion(_latestUpdate?.Version ?? "");
                return false;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to launch update helper");
                ClearPendingUpdateAttempt();
                SetSkippedUpdateVersion(_latestUpdate?.Version ?? "");
                return false;
            }

            // Exit the current application so the helper's wait-for-exit can proceed.
            App.Logger?.Information("Exiting application for silent update (helper will install + relaunch)...");
            Application.Current.Shutdown();
            return true;
        }

        /// <summary>
        /// True when the install directory cannot be written by the current token, i.e. the silent
        /// installer needs admin rights. Per-user installs stay prompt-free.
        /// </summary>
        private static bool NeedsElevationToInstall(string? installPath)
        {
            if (string.IsNullOrEmpty(installPath)) return false;

            try
            {
                if (!Directory.Exists(installPath)) return false;

                var probe = Path.Combine(installPath, $".ccp-write-probe-{Guid.NewGuid():N}.tmp");
                using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (IOException)
            {
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Could not probe install dir writability, assuming elevation is needed");
                return true;
            }
        }

        /// <summary>
        /// Writes the external update helper batch script and returns its path. The script waits
        /// for this process (pid) to exit, runs the installer silently, records the installer's exit
        /// code to resultPath, then relaunches appExe.
        /// Values are baked in as literals (no positional-arg parsing) so paths with spaces are safe.
        /// </summary>
        private static string WriteUpdateHelperScript(string installerPath, string? installPath, int pid,
            string appExe, string logPath, string resultPath, bool elevated)
        {
            var helperDir = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath();
            var helperPath = Path.Combine(helperDir, "update-helper.cmd");

            // Ensure the log directory exists — the first `echo > "%LOG%"` fails otherwise.
            try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); } catch { }

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

            var script = string.Join("\r\n", lines) + "\r\n";
            // UTF-8 without BOM — a BOM can break batch parsing of the first line.
            File.WriteAllText(helperPath, script, new System.Text.UTF8Encoding(false));
            return helperPath;
        }

        /// <summary>
        /// Cleans up browser data and kills WebView2 processes before fresh install.
        /// This prevents "Failed to remove existing application directory" errors.
        /// </summary>
        private static void CleanupBeforeFreshInstall()
        {
            try
            {
                App.Logger?.Information("Cleaning up before fresh install...");

                // Get the current installation directory
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                var installDir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(installDir)) return;

                // Kill any WebView2 processes that might be using our browser_data
                KillWebView2Processes(installDir);

                // Delete browser_data folder in install directory (old location)
                var browserDataPath = Path.Combine(installDir, "browser_data");
                if (Directory.Exists(browserDataPath))
                {
                    App.Logger?.Information("Deleting browser_data folder: {Path}", browserDataPath);
                    try
                    {
                        Directory.Delete(browserDataPath, true);
                        App.Logger?.Information("Browser data deleted successfully");
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("Could not delete browser_data: {Error}", ex.Message);
                        // Try to at least delete the lock file
                        TryDeleteLockFile(browserDataPath);
                    }
                }

                // Also clean up Velopack install location if different
                var velopackPath = Path.Combine(
                    App.UserDataPath,
                    "current",
                    "browser_data");

                if (Directory.Exists(velopackPath) && !velopackPath.Equals(browserDataPath, StringComparison.OrdinalIgnoreCase))
                {
                    App.Logger?.Information("Deleting Velopack browser_data: {Path}", velopackPath);
                    try
                    {
                        Directory.Delete(velopackPath, true);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("Could not delete Velopack browser_data: {Error}", ex.Message);
                        TryDeleteLockFile(velopackPath);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error during pre-install cleanup");
                // Continue anyway - installer might still succeed
            }
        }

        /// <summary>
        /// Kills WebView2 processes that are using the app's browser data folder.
        /// Uses wmic command line to identify processes by their command line arguments.
        /// </summary>
        private static void KillWebView2Processes(string installDir)
        {
            try
            {
                App.Logger?.Information("Looking for WebView2 processes to kill...");

                // Use wmic to get WebView2 processes with their command lines
                var startInfo = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = "process where \"name='msedgewebview2.exe'\" get processid,commandline /format:csv",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var wmicProcess = Process.Start(startInfo);
                if (wmicProcess == null) return;

                var output = wmicProcess.StandardOutput.ReadToEnd();
                wmicProcess.WaitForExit(5000);

                var killedCount = 0;
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    // Skip header line
                    if (line.Contains("CommandLine") || string.IsNullOrWhiteSpace(line))
                        continue;

                    // Check if this process is using our install directory
                    if (line.Contains(installDir, StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("ConditioningControlPanel", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract PID from CSV (format: Node,CommandLine,ProcessId)
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var pidStr = parts[^1].Trim(); // Last part is ProcessId
                            if (int.TryParse(pidStr, out var pid))
                            {
                                try
                                {
                                    var process = Process.GetProcessById(pid);
                                    App.Logger?.Information("Killing WebView2 process {Id}", pid);
                                    process.Kill();
                                    process.WaitForExit(2000);
                                    process.Dispose();
                                    killedCount++;
                                }
                                catch (Exception ex)
                                {
                                    App.Logger?.Debug("Could not kill process {Id}: {Error}", pid, ex.Message);
                                }
                            }
                        }
                    }
                }

                if (killedCount > 0)
                {
                    App.Logger?.Information("Killed {Count} WebView2 processes", killedCount);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error killing WebView2 processes: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Tries to delete the WebView2 lock file specifically.
        /// </summary>
        private static void TryDeleteLockFile(string browserDataPath)
        {
            try
            {
                var lockFile = Path.Combine(browserDataPath, "EBWebView", "Default", "LOCK");
                if (File.Exists(lockFile))
                {
                    File.Delete(lockFile);
                    App.Logger?.Information("Deleted WebView2 lock file");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not delete lock file: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Clean up stale temp folders. Called on app startup.
        /// Velopack-specific cleanup (packages/, staging/, app-X.X.X/) was removed in v5.8.4
        /// since Velopack hasn't been the install path since v5.4.10.
        /// </summary>
        public static void CleanupOldPackages()
        {
            try
            {
                var deletedCount = 0;
                long freedBytes = 0;

                // Legacy: clean up the old %TEMP%\Velopack dir if a pre-5.4.10 user
                // upgraded into a current build and left it behind.
                var velopackTemp = Path.Combine(Path.GetTempPath(), "Velopack");
                if (Directory.Exists(velopackTemp))
                {
                    try
                    {
                        freedBytes += GetDirectorySize(new DirectoryInfo(velopackTemp));
                        Directory.Delete(velopackTemp, true);
                        deletedCount++;
                        App.Logger?.Debug("Cleanup: Deleted legacy Velopack temp at {Path}", velopackTemp);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Cleanup: Could not delete legacy Velopack temp: {Error}", ex.Message);
                    }
                }

                // Each build of a self-contained single-file app extracts native libs to a
                // new %TEMP%\.net\ConditioningControlPanel\<hash>=\ folder (~200MB each).
                // Old ones are never cleaned up automatically.
                CleanupDotNetTempCache(ref deletedCount, ref freedBytes);

                if (deletedCount > 0)
                {
                    App.Logger?.Information("Cleaned up {Count} stale cache item(s), freed {Size:F1} MB",
                        deletedCount, freedBytes / (1024.0 * 1024.0));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to cleanup stale caches");
            }
        }

        /// <summary>
        /// Marks a folder we are in the middle of deleting. Renaming before deleting makes the
        /// removal all-or-nothing from the point of view of anyone still using the folder: if a
        /// file inside is locked the rename fails outright and the folder is left completely
        /// intact, instead of being stripped of everything that happened not to be mapped.
        /// </summary>
        private const string ParkedFolderSuffix = ".stale-";

        private static bool IsParkedFolder(string dir) =>
            Path.GetFileName(dir).Contains(ParkedFolderSuffix, StringComparison.Ordinal);

        /// <summary>
        /// Returns the <c>&lt;base&gt;\&lt;hash&gt;</c> folder THIS process is running out of, or
        /// null if it cannot be established (a normal non-bundled build, or an unreadable module
        /// list). Null means "do not delete anything" — never "probably that one".
        /// </summary>
        private static string? ResolveLiveExtractionFolder(string dotnetTempBase)
        {
            try
            {
                var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dotnetTempBase))
                             + Path.DirectorySeparatorChar;

                foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                {
                    string file;
                    // A module can vanish between enumeration and read; skip it rather than
                    // abandoning the whole scan.
                    try { file = module.FileName ?? string.Empty; } catch { continue; }

                    if (file.Length <= prefix.Length ||
                        !file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // <base>\<hash>\libSkiaSharp.dll and <base>\<hash>\libvlc\win-x64\libvlc.dll
                    // both answer the same thing: the first segment after the base.
                    var rest = file.Substring(prefix.Length);
                    var sep = rest.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                    var hash = sep < 0 ? rest : rest.Substring(0, sep);
                    if (hash.Length == 0) continue;

                    return Path.Combine(Path.TrimEndingDirectorySeparator(prefix), hash);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Cleanup: could not read the module list: {Error}", ex.Message);
            }

            return null;
        }

        /// <summary>
        /// True when another process of this same executable is alive. Fails CLOSED — if we
        /// cannot tell, we report true and the caller prunes nothing.
        /// </summary>
        private static bool OtherInstancesRunning()
        {
            try
            {
                using var me = Process.GetCurrentProcess();
                foreach (var other in Process.GetProcessesByName(me.ProcessName))
                {
                    using (other)
                    {
                        if (other.Id != me.Id) return true;
                    }
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Park-then-delete one stale folder. Returns true only when the folder is actually gone.
        /// </summary>
        private static bool TryRemoveStaleFolder(string dir, out long size)
        {
            size = 0;
            try
            {
                size = GetDirectorySize(new DirectoryInfo(dir));

                var parked = Path.Combine(
                    Path.GetDirectoryName(dir)!,
                    Path.GetFileName(dir) + ParkedFolderSuffix + Environment.ProcessId);

                // The rename is the safety interlock: it throws (leaving the folder untouched)
                // if anything in there is still mapped by a live process.
                Directory.Move(dir, parked);
                Directory.Delete(parked, true);
                return true;
            }
            catch (Exception ex)
            {
                // Loud, not silent. The empty catch that used to be here is why a half-deleted
                // live folder went unnoticed until users started reporting a dead app.
                App.Logger?.Warning("Cleanup: could not remove stale .NET cache folder {Path}: {Error}",
                    dir, ex.Message);
                size = 0;
                return false;
            }
        }

        /// <summary>
        /// Finishes off folders a previous run parked but could not delete (a crash between the
        /// rename and the delete, or a file that was still locked at the time).
        /// </summary>
        private static void SweepParkedFolders(string dotnetTempBase, ref int deletedCount, ref long freedBytes)
        {
            foreach (var dir in Directory.GetDirectories(dotnetTempBase))
            {
                if (!IsParkedFolder(dir)) continue;
                try
                {
                    var size = GetDirectorySize(new DirectoryInfo(dir));
                    Directory.Delete(dir, true);
                    freedBytes += size;
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Cleanup: parked folder {Path} still not removable: {Error}", dir, ex.Message);
                }
            }
        }
        /// <summary>
        /// Deletes extraction-cache folders left behind by PREVIOUS builds.
        ///
        /// <para>A self-contained single-file build unpacks its natives into
        /// <c>%TEMP%\.net\ConditioningControlPanel\{hash}\</c>, one folder per build, several
        /// hundred MB each, and the .NET host never removes the old ones. So we do - but only
        /// ones we can PROVE are not in use.</para>
        ///
        /// <para><b>Why the paranoia.</b> This method used to pick the live folder by a
        /// heuristic: the first folder whose LastWriteTime sat within five minutes of process
        /// start. On 2026-08-20 a 6.8.2 -> 6.8.3 upgrade put two folders inside that window (the
        /// outgoing build had been running four minutes earlier), the loop matched the WRONG one
        /// first and broke, and every other folder - including the one this very process was
        /// running out of - went through <c>Directory.Delete(dir, true)</c>. That delete removes
        /// what it can and throws when it reaches a mapped image section, so the live folder was
        /// stripped down to the only three files a running WPF app has locked
        /// (D3DCompiler_47_cor3, PresentationNative_cor3, wpfgfx_cor3) and the throw landed in an
        /// empty catch. libSkiaSharp was gone; three seconds later AmbientFxCanvas asked for an
        /// SKPaint and the app died in InitializeComponent with a XamlParseException that named a
        /// decorative FX control and nothing else.</para>
        ///
        /// <para>The .NET host cannot save us here: it verifies and re-extracts missing files at
        /// process START, and this runs on a background thread well after that. Every subsequent
        /// launch repeated the same delete, so the install stayed broken until the folder was
        /// removed by hand. Hence the two hard rules below - identify the live folder exactly, and
        /// when anything is uncertain delete NOTHING. Reclaiming disk is a nicety; bricking the
        /// app is not a tradeoff worth making for it.</para>
        /// </summary>
        private static void CleanupDotNetTempCache(ref int deletedCount, ref long freedBytes)
        {
            try
            {
                var dotnetTempBase = Path.Combine(Path.GetTempPath(), ".net", "ConditioningControlPanel");
                if (!Directory.Exists(dotnetTempBase)) return;

                // Finish any parked folder a previous run could not fully remove (see below).
                SweepParkedFolders(dotnetTempBase, ref deletedCount, ref freedBytes);

                // RULE 1: ask the loader which folder is ours, never the clock. Every native we
                // have mapped out of the bundle lives under the active hash folder, so our own
                // module list names it exactly. NativeBundleGuard has already loaded Skia by the
                // time this background task runs, so there is always at least one such module to
                // find; if there somehow is not, we bail rather than guess.
                var liveFolder = ResolveLiveExtractionFolder(dotnetTempBase);
                if (liveFolder == null)
                {
                    // Not a single-file build, or we simply could not tell. Guessing is what
                    // caused the incident above.
                    App.Logger?.Debug("Cleanup: could not identify the live extraction folder — skipping .NET cache prune");
                    return;
                }

                // RULE 2: another instance of this exe may be running out of a DIFFERENT hash
                // folder (mid-update, or a second copy). We cannot see its folder from here, and
                // half-deleting it would brick that process exactly the way we bricked ourselves.
                // The prune is pure disk hygiene — skipping it costs nothing but a later retry.
                if (OtherInstancesRunning())
                {
                    App.Logger?.Debug("Cleanup: another instance is running — skipping .NET cache prune");
                    return;
                }

                var staleCount = 0;
                foreach (var dir in Directory.GetDirectories(dotnetTempBase))
                {
                    if (string.Equals(dir, liveFolder, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (IsParkedFolder(dir))
                        continue; // already handled by the sweep above

                    if (TryRemoveStaleFolder(dir, out var size))
                    {
                        freedBytes += size;
                        deletedCount++;
                        staleCount++;
                    }
                }

                if (staleCount > 0)
                {
                    App.Logger?.Information("Cleanup: Deleted {Count} stale .NET cache folder(s) from {Path} (kept live folder {Live})",
                        staleCount, dotnetTempBase, Path.GetFileName(liveFolder));
                }
                // Also clean up CCPUpdateHelper cache - this is a temp copy of our exe used during updates.
                // It extracts to its own .NET cache folder that's never cleaned up.
                // By the time the main app runs, the update helper has exited, so all folders are safe to delete.
                var helperTempBase = Path.Combine(Path.GetTempPath(), ".net", "CCPUpdateHelper");
                if (Directory.Exists(helperTempBase))
                {
                    var helperCount = 0;
                    foreach (var dir in Directory.GetDirectories(helperTempBase))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            var dirSize = GetDirectorySize(dirInfo);
                            Directory.Delete(dir, true);
                            freedBytes += dirSize;
                            deletedCount++;
                            helperCount++;
                        }
                        catch
                        {
                            // May be locked if update helper is still running — skip
                        }
                    }

                    if (helperCount > 0)
                    {
                        App.Logger?.Information("Cleanup: Deleted {Count} stale CCPUpdateHelper cache folder(s)",
                            helperCount);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Cleanup: Error cleaning .NET temp cache: {Error}", ex.Message);
            }
        }

        private static long GetDirectorySize(DirectoryInfo dir)
        {
            try
            {
                return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Checks the GitHub Releases API for the latest release.
        /// </summary>
        private async Task<AppUpdateInfo?> CheckGitHubReleasesAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "ConditioningControlPanel");
                client.Timeout = TimeSpan.FromSeconds(15);

                // Get latest release from GitHub API
                var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                App.Logger?.Debug("Checking GitHub releases API: {Url}", url);

                var response = await client.GetStringAsync(url);

                // Parse tag_name to get version (format: "v4.4.4" or "4.4.4")
                var tagMatch = System.Text.RegularExpressions.Regex.Match(response, "\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"");
                if (!tagMatch.Success)
                {
                    App.Logger?.Debug("Could not parse tag_name from GitHub response");
                    return null;
                }

                var latestVersionString = tagMatch.Groups[1].Value;
                App.Logger?.Information("GitHub API reports latest version: {Version}", latestVersionString);

                if (!Version.TryParse(latestVersionString, out var latestVersion))
                {
                    App.Logger?.Warning("Could not parse version from tag: {Tag}", latestVersionString);
                    return null;
                }

                var currentVersion = GetCurrentVersion();
                var isNewer = latestVersion > currentVersion;

                App.Logger?.Information("GitHub version comparison: latest={Latest}, current={Current}, isNewer={IsNewer}",
                    latestVersion, currentVersion, isNewer);

                if (!isNewer)
                {
                    return null; // Already on latest
                }

                // Parse using proper JSON parsing
                var releaseNotes = "";
                long fileSizeBytes = 0;
                try
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(response);

                    // Parse release notes (body field)
                    releaseNotes = json["body"]?.ToString() ?? "";
                    var assets = json["assets"] as Newtonsoft.Json.Linq.JArray;
                    if (assets != null)
                    {
                        foreach (var asset in assets)
                        {
                            var name = asset["name"]?.ToString() ?? "";
                            if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                fileSizeBytes = (long)(asset["size"] ?? 0);
                                App.Logger?.Debug("Parsed installer size from GitHub: {Size} bytes ({SizeMB:F1} MB)",
                                    fileSizeBytes, fileSizeBytes / (1024.0 * 1024.0));
                                break;
                            }
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    App.Logger?.Debug("Could not parse assets from GitHub response: {Error}", parseEx.Message);
                }

                return new AppUpdateInfo
                {
                    Version = latestVersionString,
                    ReleaseNotes = releaseNotes,
                    FileSizeBytes = fileSizeBytes,
                    ReleaseDate = DateTime.Now,
                    IsNewer = true,
                    IsGitHubFallback = true // Flag to indicate this came from GitHub API
                };
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GitHub releases API check failed");
                return null;
            }
        }

        /// <summary>
        /// Fetches release notes from GitHub API for a specific version.
        /// </summary>
        public static async Task<string?> FetchReleaseNotesFromGitHubAsync(string version)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "ConditioningControlPanel");

                // Try to find the release by tag (v4.3.11 or 4.3.11)
                var tags = new[] { $"v{version}", version };

                foreach (var tag in tags)
                {
                    try
                    {
                        var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/tags/{tag}";
                        var response = await client.GetStringAsync(url);

                        // Parse JSON to get body field (release notes)
                        var json = Newtonsoft.Json.Linq.JObject.Parse(response);
                        var body = json["body"]?.ToString();

                        if (!string.IsNullOrWhiteSpace(body) && body != "null")
                        {
                            App.Logger?.Debug("Fetched release notes from GitHub for {Tag}", tag);
                            return body;
                        }
                    }
                    catch
                    {
                        // Tag not found, try next
                    }
                }

                App.Logger?.Debug("No release notes found on GitHub for version {Version}", version);
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to fetch release notes from GitHub: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Determines if an exception is a transient network error that should be retried.
        /// </summary>
        private static bool IsTransientNetworkError(Exception ex)
        {
            // Check for common transient network errors
            if (ex is System.Net.Http.HttpRequestException ||
                ex is System.IO.IOException ||
                ex is System.Net.Sockets.SocketException ||
                ex is TaskCanceledException)
            {
                return true;
            }

            // Check inner exception
            if (ex.InnerException != null)
            {
                return IsTransientNetworkError(ex.InnerException);
            }

            // Check message for common transient error patterns
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("forcibly closed") ||
                   message.Contains("connection was closed") ||
                   message.Contains("network") ||
                   message.Contains("timeout") ||
                   message.Contains("transport");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
