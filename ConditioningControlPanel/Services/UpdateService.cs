using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Velopack;
using Velopack.Sources;

// Alias to avoid ambiguity with Velopack.UpdateInfo
using AppUpdateInfo = ConditioningControlPanel.Models.UpdateInfo;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles automatic updates using Velopack with GitHub Releases
    /// </summary>
    public class UpdateService : IDisposable
    {
        /// <summary>
        /// Current application version - UPDATE THIS WHEN BUMPING VERSION
        /// </summary>
        public const string AppVersion = "5.1.1";

        /// <summary>
        /// Patch notes for the current version - UPDATE THIS WHEN BUMPING VERSION
        /// These are shown in the update dialog and can be used when GitHub release notes are unavailable.
        /// </summary>
        public const string CurrentPatchNotes = @"v5.1.1 - BUG FIXES

🛠️ BUG FIXES
• Fixed session crash caused by BoolToVisibility converter
• Fixed content packs installing to wrong directory
• Packs now install to your chosen assets folder

🔒 WHITELIST CHANGES
• Whitelist now managed server-side for easier updates
• Whitelisted users get full Patreon features";

        private const string GitHubOwner = "CodeBambi";
        private const string GitHubRepo = "Conditioning-Control-Panel---CSharp-WPF";

        private readonly UpdateManager _updateManager;
        private AppUpdateInfo? _latestUpdate;
        private Velopack.UpdateInfo? _velopackUpdateInfo;
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
        /// Fired when an update is downloaded and ready to install
        /// </summary>
        public event EventHandler? UpdateReady;

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
        /// Whether the app was installed via Velopack (vs running from source/dev)
        /// </summary>
        public bool IsInstalled => _updateManager.IsInstalled;

        /// <summary>
        /// Version threshold that requires a fresh install (major update)
        /// </summary>
        private static readonly Version FreshInstallVersion = new Version(5, 1, 0);

        /// <summary>
        /// Checks if the update requires a fresh install (upgrading from pre-5.1 to 5.1+)
        /// </summary>
        public bool RequiresFreshInstall
        {
            get
            {
                if (_latestUpdate == null || !_latestUpdate.IsNewer)
                    return false;

                var currentVersion = GetCurrentVersion();
                if (!Version.TryParse(_latestUpdate.Version, out var newVersion))
                    return false;

                // Fresh install required when: current < 5.1 AND new >= 5.1
                return currentVersion < FreshInstallVersion && newVersion >= FreshInstallVersion;
            }
        }

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
        /// Whether the app was installed via the installer (has registry entry)
        /// </summary>
        public static bool IsInstalledViaInstaller => GetInstalledPath() != null;

        public UpdateService()
        {
            // Configure GitHub as update source
            var source = new GithubSource(
                $"https://github.com/{GitHubOwner}/{GitHubRepo}",
                null, // No access token needed for public repos
                prerelease: false
            );

            _updateManager = new UpdateManager(source);
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
                // Skip update check if offline mode is enabled
                if (App.Settings?.Current?.OfflineMode == true)
                {
                    App.Logger?.Information("Offline mode enabled, skipping update check");
                    return null;
                }

                App.Logger?.Information("Checking for updates... (current AppVersion: {Version}, force: {Force})", AppVersion, forceCheck);

                // Skip update check if running in development/not installed
                if (!_updateManager.IsInstalled)
                {
                    App.Logger?.Information("App is not installed via Velopack, skipping update check");
                    return null;
                }

                // Check if we recently attempted an update to prevent loops
                // But allow bypass if user manually requested the check OR if skip is old
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
                    else if (skipAge.TotalMinutes > 5)
                    {
                        // If skip file is older than 5 minutes, user restarted normally - clear it
                        App.Logger?.Information("Skip marker for {Version} is {Minutes:F1} minutes old, clearing it",
                            skippedVersion, skipAge.TotalMinutes);
                        ClearSkippedUpdateVersion();
                        skippedVersion = null;
                    }
                    else
                    {
                        App.Logger?.Information("Recently attempted update to {Version} ({Minutes:F1} min ago), checking if loop prevention needed",
                            skippedVersion, skipAge.TotalMinutes);
                    }
                }

                _velopackUpdateInfo = await _updateManager.CheckForUpdatesAsync();

                if (_velopackUpdateInfo == null)
                {
                    App.Logger?.Information("Velopack returned null, trying GitHub API fallback...");

                    // Fallback: Check GitHub releases API directly
                    var githubUpdate = await CheckGitHubReleasesAsync();
                    if (githubUpdate != null)
                    {
                        App.Logger?.Information("GitHub API found update: {Version}", githubUpdate.Version);
                        _latestUpdate = githubUpdate;
                        if (githubUpdate.IsNewer)
                        {
                            UpdateAvailable?.Invoke(this, githubUpdate);
                        }
                        return githubUpdate;
                    }

                    App.Logger?.Information("No updates available from Velopack or GitHub API");
                    _latestUpdate = null;
                    ClearSkippedUpdateVersion();
                    return null;
                }

                var currentVersion = GetCurrentVersion();
                var newVersion = _velopackUpdateInfo.TargetFullRelease.Version;
                var newVersionString = newVersion.ToString();

                App.Logger?.Information("Velopack reports update: {NewVersion}, Current: {CurrentVersion}",
                    newVersionString, currentVersion);

                // Compare versions - Velopack uses SemanticVersion, convert to compare
                var newVersionParsed = new Version(newVersion.Major, newVersion.Minor, newVersion.Patch);
                var isNewer = newVersionParsed > currentVersion;

                // Safety check 1: prevent update loop by verifying versions don't match
                if (newVersionParsed.Major == currentVersion.Major &&
                    newVersionParsed.Minor == currentVersion.Minor &&
                    newVersionParsed.Build == currentVersion.Build)
                {
                    App.Logger?.Information("Version match - no update needed (current: {Current}, target: {Target})",
                        currentVersion, newVersionParsed);
                    isNewer = false;
                    ClearSkippedUpdateVersion();
                }

                // Safety check 2: if we just tried to update to this version and failed, skip it
                if (isNewer && !string.IsNullOrEmpty(skippedVersion) && skippedVersion == newVersionString)
                {
                    var skipTime = GetSkippedUpdateTime();
                    var hoursSinceSkip = (DateTime.Now - skipTime).TotalHours;

                    if (hoursSinceSkip < 24) // Don't prompt for same version within 24 hours of failed update
                    {
                        App.Logger?.Warning("Skipping update to {Version} - attempted {Hours:F1} hours ago but app still running old version. " +
                            "This indicates the update didn't apply correctly. Will retry after 24 hours.",
                            newVersionString, hoursSinceSkip);
                        isNewer = false;
                    }
                    else
                    {
                        App.Logger?.Information("24 hours passed since failed update attempt, allowing retry");
                        ClearSkippedUpdateVersion();
                    }
                }

                // Get release notes - try Velopack first, then fetch from GitHub API
                var releaseNotes = _velopackUpdateInfo.TargetFullRelease.NotesMarkdown ?? "";
                if (string.IsNullOrWhiteSpace(releaseNotes))
                {
                    App.Logger?.Debug("Velopack didn't provide release notes, fetching from GitHub API...");
                    releaseNotes = await FetchReleaseNotesFromGitHubAsync(newVersionString) ?? "";
                }

                _latestUpdate = new AppUpdateInfo
                {
                    Version = newVersionString,
                    ReleaseNotes = releaseNotes,
                    FileSizeBytes = _velopackUpdateInfo.TargetFullRelease.Size,
                    ReleaseDate = DateTime.Now, // Velopack doesn't expose release date directly
                    IsNewer = isNewer
                };

                if (_latestUpdate.IsNewer)
                {
                    App.Logger?.Information("Update available: {NewVersion} (current: {CurrentVersion})",
                        newVersion, currentVersion);
                    UpdateAvailable?.Invoke(this, _latestUpdate);
                }
                else
                {
                    App.Logger?.Information("Already on latest version or update skipped: {Version}", currentVersion);
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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "ConditioningControlPanel", "update_skip.txt");
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

        private static void SetSkippedUpdateVersion(string version)
        {
            try
            {
                var skipFile = GetSkipFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(skipFile)!);
                File.WriteAllText(skipFile, version);
                App.Logger?.Information("Marked update to {Version} as pending - will track if it succeeds", version);
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
        /// Download the available update with progress reporting
        /// </summary>
        public async Task DownloadUpdateAsync(CancellationToken ct = default)
        {
            if (_velopackUpdateInfo == null || _latestUpdate == null || !_latestUpdate.IsNewer)
            {
                throw new InvalidOperationException("No update available to download");
            }

            try
            {
                IsDownloading = true;

                App.Logger?.Information("Downloading update {Version}...", _latestUpdate.Version);

                // Download with progress reporting
                await _updateManager.DownloadUpdatesAsync(
                    _velopackUpdateInfo,
                    progress =>
                    {
                        DownloadProgressChanged?.Invoke(this, progress);
                        App.Logger?.Debug("Download progress: {Progress}%", progress);
                    }
                );

                App.Logger?.Information("Update downloaded successfully");
                UpdateReady?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to download update");
                UpdateFailed?.Invoke(this, ex);
                throw;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        /// <summary>
        /// Apply the downloaded update and restart the application
        /// </summary>
        public void ApplyUpdateAndRestart()
        {
            App.Logger?.Information("Applying update and restarting...");

            // Mark this version as pending - if we restart and still see this version
            // as an update, we know the update failed and should skip it
            if (_latestUpdate != null)
            {
                SetSkippedUpdateVersion(_latestUpdate.Version);
            }

            // Save settings before restart
            App.Settings?.Save();

            // Apply update and restart - Velopack handles all the complexity
            _updateManager.ApplyUpdatesAndRestart(_velopackUpdateInfo);
        }

        /// <summary>
        /// Apply the downloaded update without restarting (will apply on next launch)
        /// </summary>
        public void ApplyUpdateOnExit()
        {
            if (_velopackUpdateInfo != null)
            {
                App.Logger?.Information("Update will be applied on next restart");
                _updateManager.ApplyUpdatesAndExit(_velopackUpdateInfo);
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

                        // Find the Setup.exe asset - look for common installer patterns
                        var patterns = new[] { "Setup.exe", "-win-Setup.exe", "Installer.exe", "-Setup.exe" };
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

                // Download to temp directory
                var tempDir = Path.Combine(Path.GetTempPath(), "ConditioningControlPanel_Update");
                Directory.CreateDirectory(tempDir);
                var installerPath = Path.Combine(tempDir, assetName ?? "Setup.exe");

                // Delete old installer if exists
                if (File.Exists(installerPath))
                {
                    File.Delete(installerPath);
                }

                App.Logger?.Information("Downloading installer from {Url} to {Path}", downloadUrl, installerPath);

                // Download with progress
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
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ConditioningControlPanel",
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
        /// Clean up old update packages to free disk space.
        /// Should be called on app startup after a successful update.
        /// </summary>
        public static void CleanupOldPackages()
        {
            try
            {
                var deletedCount = 0;
                long freedBytes = 0;

                // Get the app's installation directory
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                var currentDir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(currentDir)) return;

                // Velopack installs to: %LocalAppData%\{AppId}\current\
                var appRootDir = Path.GetDirectoryName(currentDir);

                App.Logger?.Debug("Cleanup: exe={ExePath}, currentDir={CurrentDir}, appRoot={AppRoot}",
                    exePath, currentDir, appRootDir);

                if (!string.IsNullOrEmpty(appRootDir))
                {
                    // Clean packages directory
                    CleanupDirectory(Path.Combine(appRootDir, "packages"), ref deletedCount, ref freedBytes);

                    // Clean staging directory (used during updates)
                    CleanupDirectory(Path.Combine(appRootDir, "staging"), ref deletedCount, ref freedBytes);

                    // Clean any temp directories
                    CleanupDirectory(Path.Combine(appRootDir, "temp"), ref deletedCount, ref freedBytes);
                    CleanupDirectory(Path.Combine(appRootDir, "tmp"), ref deletedCount, ref freedBytes);

                    // Clean old app-X.X.X version folders (Velopack keeps these for rollback)
                    CleanupOldVersionFolders(appRootDir, ref deletedCount, ref freedBytes);
                }

                // Also check LocalAppData directly for ConditioningControlPanel folders
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appDataDir = Path.Combine(localAppData, "ConditioningControlPanel");

                if (Directory.Exists(appDataDir) && appDataDir != appRootDir)
                {
                    App.Logger?.Debug("Cleanup: Also checking appDataDir={AppDataDir}", appDataDir);
                    CleanupDirectory(Path.Combine(appDataDir, "packages"), ref deletedCount, ref freedBytes);
                    CleanupDirectory(Path.Combine(appDataDir, "staging"), ref deletedCount, ref freedBytes);
                }

                // Clean Velopack temp in system temp
                var tempDir = Path.GetTempPath();
                var velopackTemp = Path.Combine(tempDir, "Velopack");
                if (Directory.Exists(velopackTemp))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(velopackTemp);
                        freedBytes += GetDirectorySize(dirInfo);
                        Directory.Delete(velopackTemp, true);
                        deletedCount++;
                        App.Logger?.Debug("Cleanup: Deleted Velopack temp at {Path}", velopackTemp);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Cleanup: Could not delete Velopack temp: {Error}", ex.Message);
                    }
                }

                if (deletedCount > 0)
                {
                    App.Logger?.Information("Cleaned up {Count} old update item(s), freed {Size:F1} MB",
                        deletedCount, freedBytes / (1024.0 * 1024.0));
                }
                else
                {
                    App.Logger?.Debug("Cleanup: No old packages found to clean up");
                }

                // Update Windows registry to reflect actual size in "Add or Remove Programs"
                UpdateRegistryEstimatedSize(appRootDir);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to cleanup old update packages");
            }
        }

        /// <summary>
        /// Update the Windows registry EstimatedSize value to reflect actual app size.
        /// This fixes the "Add or Remove Programs" display showing incorrect size.
        /// </summary>
        private static void UpdateRegistryEstimatedSize(string? appRootDir)
        {
            try
            {
                if (string.IsNullOrEmpty(appRootDir)) return;

                // Calculate actual size of the installation
                var dirInfo = new DirectoryInfo(appRootDir);
                if (!dirInfo.Exists) return;

                var actualSizeBytes = GetDirectorySize(dirInfo);
                var actualSizeKb = (int)(actualSizeBytes / 1024); // Registry expects KB as DWORD

                App.Logger?.Debug("Registry: Actual app size is {Size:F1} MB ({SizeKb} KB)",
                    actualSizeBytes / (1024.0 * 1024.0), actualSizeKb);

                // Try to find and update the uninstall registry key
                // Velopack uses the app name as the registry key
                var uninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

                using var uninstallKey = Registry.CurrentUser.OpenSubKey(uninstallKeyPath, false);
                if (uninstallKey == null)
                {
                    App.Logger?.Debug("Registry: Could not open uninstall key in HKCU");
                    return;
                }

                // Look for our app's uninstall key (could be "ConditioningControlPanel" or similar)
                var possibleKeyNames = new[] { "ConditioningControlPanel", "Conditioning Control Panel" };

                foreach (var keyName in possibleKeyNames)
                {
                    try
                    {
                        using var appKey = Registry.CurrentUser.OpenSubKey($@"{uninstallKeyPath}\{keyName}", true);
                        if (appKey != null)
                        {
                            var currentSize = appKey.GetValue("EstimatedSize") as int?;
                            App.Logger?.Debug("Registry: Found key '{KeyName}', current EstimatedSize={CurrentSize} KB",
                                keyName, currentSize);

                            appKey.SetValue("EstimatedSize", actualSizeKb, RegistryValueKind.DWord);
                            App.Logger?.Information("Registry: Updated EstimatedSize from {Old} KB to {New} KB ({SizeMB:F1} MB)",
                                currentSize, actualSizeKb, actualSizeBytes / (1024.0 * 1024.0));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Registry: Could not update key '{KeyName}': {Error}", keyName, ex.Message);
                    }
                }

                // Also try HKLM (requires admin, will likely fail but try anyway)
                try
                {
                    foreach (var keyName in possibleKeyNames)
                    {
                        using var appKey = Registry.LocalMachine.OpenSubKey($@"{uninstallKeyPath}\{keyName}", true);
                        if (appKey != null)
                        {
                            appKey.SetValue("EstimatedSize", actualSizeKb, RegistryValueKind.DWord);
                            App.Logger?.Information("Registry: Updated HKLM EstimatedSize to {Size} KB", actualSizeKb);
                            return;
                        }
                    }
                }
                catch
                {
                    // Expected to fail without admin rights
                }

                App.Logger?.Debug("Registry: Could not find uninstall key for app");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to update registry EstimatedSize");
            }
        }

        private static void CleanupDirectory(string dirPath, ref int deletedCount, ref long freedBytes)
        {
            if (!Directory.Exists(dirPath)) return;

            App.Logger?.Debug("Cleanup: Checking directory {Path}", dirPath);

            // Delete .nupkg files
            foreach (var file in Directory.GetFiles(dirPath, "*.nupkg"))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    freedBytes += fileInfo.Length;
                    File.Delete(file);
                    deletedCount++;
                    App.Logger?.Debug("Cleanup: Deleted {File}", file);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to delete: {File}", file);
                }
            }

            // Delete .tmp files
            foreach (var file in Directory.GetFiles(dirPath, "*.tmp"))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    freedBytes += fileInfo.Length;
                    File.Delete(file);
                    deletedCount++;
                }
                catch { /* Ignore */ }
            }

            // Delete all files in staging directories
            if (dirPath.Contains("staging", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dirPath);
                    freedBytes += GetDirectorySize(dirInfo);
                    Directory.Delete(dirPath, true);
                    deletedCount++;
                    App.Logger?.Debug("Cleanup: Deleted staging directory {Path}", dirPath);
                }
                catch { /* Ignore */ }
            }
        }

        private static void CleanupOldVersionFolders(string appRootDir, ref int deletedCount, ref long freedBytes)
        {
            try
            {
                // Velopack creates folders like app-1.0.0, app-1.0.1 for each version
                // The current version runs from "current" which is a junction/symlink
                // We can safely delete old app-X.X.X folders

                var currentVersion = GetCurrentVersion();
                var currentVersionFolder = $"app-{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";

                App.Logger?.Debug("Cleanup: Looking for old version folders in {Path} (current: {Current})",
                    appRootDir, currentVersionFolder);

                foreach (var dir in Directory.GetDirectories(appRootDir, "app-*"))
                {
                    var dirName = Path.GetFileName(dir);

                    // Skip current version folder
                    if (dirName.Equals(currentVersionFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        App.Logger?.Debug("Cleanup: Skipping current version folder {Dir}", dirName);
                        continue;
                    }

                    // Skip "current" folder (it's the actual running app location)
                    if (dirName.Equals("current", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // CRITICAL: Rescue user assets from old version folder BEFORE deleting
                    // This prevents data loss when users had assets in the old app folder
                    RescueAssetsFromFolder(dir);

                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var dirSize = GetDirectorySize(dirInfo);
                        Directory.Delete(dir, true);
                        freedBytes += dirSize;
                        deletedCount++;
                        App.Logger?.Information("Cleanup: Deleted old version folder {Dir} ({Size:F1} MB)",
                            dirName, dirSize / (1024.0 * 1024.0));
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Cleanup: Could not delete {Dir}: {Error}", dirName, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Cleanup: Error scanning for old version folders: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Rescues user assets (images, videos, spirals) from an old version folder before it gets deleted.
        /// This prevents data loss when updating from versions that stored assets in the app folder.
        /// </summary>
        private static void RescueAssetsFromFolder(string oldVersionFolder)
        {
            try
            {
                var rescuedCount = 0;

                // Check for assets folder in old version
                var oldAssetsPath = Path.Combine(oldVersionFolder, "assets");
                if (Directory.Exists(oldAssetsPath))
                {
                    // Migrate images
                    var oldImages = Path.Combine(oldAssetsPath, "images");
                    var newImages = Path.Combine(App.UserAssetsPath, "images");
                    if (Directory.Exists(oldImages))
                    {
                        Directory.CreateDirectory(newImages);
                        rescuedCount += CopyNewFiles(oldImages, newImages);
                    }

                    // Migrate videos (check both "videos" and legacy "startle_videos")
                    var newVideos = Path.Combine(App.UserAssetsPath, "videos");
                    Directory.CreateDirectory(newVideos);

                    var oldVideos = Path.Combine(oldAssetsPath, "videos");
                    if (Directory.Exists(oldVideos))
                    {
                        rescuedCount += CopyNewFiles(oldVideos, newVideos);
                    }

                    var oldStartleVideos = Path.Combine(oldAssetsPath, "startle_videos");
                    if (Directory.Exists(oldStartleVideos))
                    {
                        rescuedCount += CopyNewFiles(oldStartleVideos, newVideos);
                    }
                }

                // Check for Spirals folder in old version
                var oldSpirals = Path.Combine(oldVersionFolder, "Spirals");
                var newSpirals = Path.Combine(App.UserDataPath, "Spirals");
                if (Directory.Exists(oldSpirals))
                {
                    Directory.CreateDirectory(newSpirals);
                    rescuedCount += CopyNewFiles(oldSpirals, newSpirals);
                }

                if (rescuedCount > 0)
                {
                    App.Logger?.Information("Rescued {Count} asset files from old version folder {Folder}",
                        rescuedCount, Path.GetFileName(oldVersionFolder));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to rescue assets from {Folder}", oldVersionFolder);
            }
        }

        /// <summary>
        /// Copies files from source to destination, skipping files that already exist.
        /// Returns the number of files copied.
        /// </summary>
        private static int CopyNewFiles(string sourceDir, string destDir)
        {
            var copiedCount = 0;
            try
            {
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    var fileName = Path.GetFileName(file);
                    var destFile = Path.Combine(destDir, fileName);

                    // Don't overwrite existing files
                    if (File.Exists(destFile)) continue;

                    try
                    {
                        File.Copy(file, destFile);
                        copiedCount++;
                        App.Logger?.Debug("Rescued asset: {File}", fileName);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("Failed to rescue {File}: {Error}", fileName, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to enumerate files in {Dir}: {Error}", sourceDir, ex.Message);
            }
            return copiedCount;
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
        /// Checks GitHub releases API directly for the latest release.
        /// Used as fallback when Velopack doesn't detect updates.
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

                // Parse release notes (body field)
                var releaseNotes = "";
                var bodyMatch = System.Text.RegularExpressions.Regex.Match(response, "\"body\"\\s*:\\s*\"([^\"]*(?:\\\\.[^\"]*)*)\"");
                if (bodyMatch.Success)
                {
                    releaseNotes = bodyMatch.Groups[1].Value
                        .Replace("\\r\\n", "\n")
                        .Replace("\\n", "\n")
                        .Replace("\\\"", "\"");
                }

                return new AppUpdateInfo
                {
                    Version = latestVersionString,
                    ReleaseNotes = releaseNotes,
                    FileSizeBytes = 0, // Unknown from API
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
        /// Used as fallback when Velopack doesn't provide notes.
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
                        // Simple parsing without full JSON library
                        var bodyStart = response.IndexOf("\"body\":");
                        if (bodyStart > 0)
                        {
                            bodyStart = response.IndexOf("\"", bodyStart + 7) + 1;
                            var bodyEnd = response.IndexOf("\"", bodyStart);

                            // Handle escaped quotes and newlines
                            var body = response.Substring(bodyStart, bodyEnd - bodyStart);
                            body = body.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\\"", "\"");

                            if (!string.IsNullOrWhiteSpace(body) && body != "null")
                            {
                                App.Logger?.Debug("Fetched release notes from GitHub for {Tag}", tag);
                                return body;
                            }
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // UpdateManager doesn't need explicit disposal
            GC.SuppressFinalize(this);
        }
    }
}
