using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        public const string AppVersion = "4.2.13";

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

        public UpdateService()
        {
            // Configure GitHub as update source
            // NOTE: prerelease=true is for TESTING ONLY - set to false for production releases
            var source = new GithubSource(
                $"https://github.com/{GitHubOwner}/{GitHubRepo}",
                null, // No access token needed for public repos
                prerelease: true // TODO: Set to false before final release
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
        public async Task<AppUpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            try
            {
                App.Logger?.Information("Checking for updates...");

                // Skip update check if running in development/not installed
                if (!_updateManager.IsInstalled)
                {
                    App.Logger?.Information("App is not installed via Velopack, skipping update check");
                    return null;
                }

                _velopackUpdateInfo = await _updateManager.CheckForUpdatesAsync();

                if (_velopackUpdateInfo == null)
                {
                    App.Logger?.Information("No updates available");
                    _latestUpdate = null;
                    return null;
                }

                var currentVersion = GetCurrentVersion();
                var newVersion = _velopackUpdateInfo.TargetFullRelease.Version;

                // Compare versions - Velopack uses SemanticVersion, convert to compare
                var newVersionParsed = new Version(newVersion.Major, newVersion.Minor, newVersion.Patch);
                var isNewer = newVersionParsed > currentVersion;

                _latestUpdate = new AppUpdateInfo
                {
                    Version = newVersion.ToString(),
                    ReleaseNotes = _velopackUpdateInfo.TargetFullRelease.NotesMarkdown ?? "",
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
                    App.Logger?.Information("Already on latest version: {Version}", currentVersion);
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // UpdateManager doesn't need explicit disposal
            GC.SuppressFinalize(this);
        }
    }
}
