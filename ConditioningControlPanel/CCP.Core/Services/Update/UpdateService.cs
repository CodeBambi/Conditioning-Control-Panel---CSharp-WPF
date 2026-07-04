using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.Update;

/// <summary>
/// Checks the GitHub Releases API for updates and orchestrates download/installation
/// via the platform-specific <see cref="IUpdateInstaller"/> seam.
/// </summary>
public class UpdateService : IUpdateService, IDisposable
{
    private const string GitHubOwner = "CodeBambi";
    private const string GitHubRepo = "Conditioning-Control-Panel---CSharp-WPF";
    private const string UserAgent = "ConditioningControlPanel";

    private readonly IUpdateInstaller _installer;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<UpdateService>? _logger;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public event EventHandler<UpdateInfo>? UpdateAvailable;
    public event EventHandler<int>? DownloadProgressChanged;
    public event EventHandler<Exception>? UpdateFailed;

    public bool IsUpdateAvailable => LatestUpdate?.IsNewer == true;
    public UpdateInfo? LatestUpdate { get; private set; }
    public bool IsDownloading { get; private set; }

    /// <inheritdoc />
    public string CurrentVersion => "6.2.7";

    /// <summary>
    /// Hard-coded current application version (single source for heads that need a const,
    /// e.g. the What's-New dialog title). Mirrors the WPF UpdateService.AppVersion constant.
    /// </summary>
    public const string AppVersion = "6.2.7";

    /// <summary>
    /// Patch notes for the current version — shown in the update/what's-new dialogs and used as a
    /// fallback when GitHub release notes are unavailable. Update together with <see cref="AppVersion"/>.
    /// </summary>
    public const string CurrentPatchNotes = @"v6.2.7 - Tunnel Vision

🚑 HOTFIX
- Fixed the app freezing on the loading screen on a cold first launch
  (the ""Still loading... the app is fine"" hang). The freeze detector
  mistook a slow first start for a real hang and its crash-dump then
  froze the app for real. No more relaunching a few times to get in.
- Ramping sessions no longer permanently overwrite your saved pink
  filter and spiral opacity when the app closes mid-session. If yours
  got stuck at maximum pink, just set the sliders back once.
- Updating from 6.0.x no longer fails to launch right after the
  installer restarts the app.

✨ NEW
- Prestige: the enhancement tree now survives the seasons. Stat and
  analytics enhancements stay yours forever, mechanical enhancements
  reset each season, and every sparkle you ever spend raises your
  permanent Prestige rank. Your sparkle point balance is never lost at
  season reset anymore.
- Ditzy Data PRO: five new analytics enhancements at the end of the
  tree, for the data nerds. A lifetime dashboard with an activity
  heatmap calendar, season-by-season charts, a personal bests timeline,
  and a per-feature usage report. The capstone certifies you.
- Endless rabbit-hole tunnel: an opt-in 3D tunnel background for Chaos
  that speeds up as your streak climbs. Find the toggle in the Chaos
  settings.
- Eye tracking, rebuilt. Calibration is shorter and far more accurate,
  the bubble test now fine-tunes itself from where you actually aimed,
  and the gaze cursor glides smoothly and locks onto bubbles and
  targets instead of jittering past them. Heads-up before calibrating:
  dim-lit rooms make eye tracking inconsistent, so put some light on
  your face for best results.
- Animated .webp images now actually animate everywhere: flashes, the
  glitch wash, the image cascade, and tease bubbles.
- Solid mode for flashes and subliminals: opt-in renderers that draw on
  one shared overlay instead of popping separate windows. If your
  fullscreen game kicks to desktop when a flash fires, turn these on.

🔧 BUG FIXES
- The splash screen no longer goes ""Not Responding"" when you click it
  while the app loads.
- Premium features check your live subscription instead of a stale
  cached tier, and Discord logins with a linked Patreon keep premium
  access through the change.
- Logging out fully clears your login token.
- ""Hey Bambi"" now catches you even when you speak the moment the mic
  opens.
- Videos triggered by bubbles start from the beginning instead of
  midway through.
- The countdown bar stays glued to its spot instead of detaching.
- Your end-of-session summary no longer hides behind a leftover video
  window.
- Companion awareness lines wait their turn instead of cutting off what
  she is currently saying.
- Overlays keep full screen coverage and bubbles pop correctly on
  setups where monitors use different display scaling.
- Turning voice off while a Voice Lock Card is open switches it to
  typed solving instead of leaving it unsolvable.

⚡ PERFORMANCE
- Chaos stays smooth during heavy floods by shedding render work when
  the bubble field stacks up.

📦 SIZE
- The install is roughly 280 MB lighter: voicelines re-encoded and UI
  art optimized, with no audible or visible quality loss.

Season: Jelly July";

    public UpdateService(IUpdateInstaller installer, ISettingsService settingsService, ILogger<UpdateService>? logger = null)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger;

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
    }

    /// <summary>
    /// Gets the current application version from the running assembly, falling back to the installer.
    /// </summary>
    public static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        if (version != null && (version.Major > 0 || version.Minor > 0 || version.Build > 0))
            return new Version(version.Major, version.Minor, version.Build);

        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            var clean = plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
            if (Version.TryParse(clean, out var parsed))
                return parsed;
        }

        return new Version(1, 0, 0);
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool forceCheck = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_settingsService.Current.OfflineMode)
            {
                _logger?.LogInformation("Offline mode enabled, skipping update check");
                return null;
            }

            var installedVersion = _installer.GetInstalledVersion();
            _logger?.LogInformation("Checking for updates... (current: {Version}, force: {Force}, installed: {Installed})",
                GetCurrentVersion(), forceCheck, installedVersion ?? "n/a");

            // Loop-prevention: if a recent update attempt didn't take, suppress the
            // same version for up to 24h so we don't pester the user every launch.
            var skippedVersion = GetSkippedUpdateVersion();
            if (!string.IsNullOrEmpty(skippedVersion))
            {
                var skipAge = DateTime.Now - GetSkippedUpdateTime();
                if (forceCheck)
                {
                    _logger?.LogInformation("Force check requested, clearing skip marker for {Version}", skippedVersion);
                    ClearSkippedUpdateVersion();
                    skippedVersion = null;
                }
                else if (skipAge.TotalMinutes > 5)
                {
                    _logger?.LogInformation("Skip marker for {Version} is {Minutes:F1} minutes old, clearing it",
                        skippedVersion, skipAge.TotalMinutes);
                    ClearSkippedUpdateVersion();
                    skippedVersion = null;
                }
            }

            var githubUpdate = await CheckGitHubReleasesAsync(cancellationToken).ConfigureAwait(false);
            if (githubUpdate == null)
            {
                _logger?.LogInformation("No updates available from GitHub API");
                LatestUpdate = null;
                ClearSkippedUpdateVersion();
                return null;
            }

            if (githubUpdate.IsNewer && !string.IsNullOrEmpty(skippedVersion) && skippedVersion == githubUpdate.Version)
            {
                var hoursSinceSkip = (DateTime.Now - GetSkippedUpdateTime()).TotalHours;
                if (hoursSinceSkip < 24)
                {
                    _logger?.LogWarning("Skipping update to {Version} — attempted {Hours:F1}h ago but app still on old version. Retry after 24h.",
                        githubUpdate.Version, hoursSinceSkip);
                    githubUpdate.IsNewer = false;
                }
                else
                {
                    ClearSkippedUpdateVersion();
                }
            }

            LatestUpdate = githubUpdate;
            if (LatestUpdate.IsNewer)
            {
                _logger?.LogInformation("Update available: {Version}", LatestUpdate.Version);
                UpdateAvailable?.Invoke(this, LatestUpdate);
            }
            else
            {
                _logger?.LogInformation("Already on latest version: {Version}", GetCurrentVersion());
            }

            return LatestUpdate;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to check for updates");
            UpdateFailed?.Invoke(this, ex);
            return null;
        }
    }

    public async Task<bool> DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (LatestUpdate == null)
            throw new InvalidOperationException("No update available to download");

        try
        {
            IsDownloading = true;
            _logger?.LogInformation("Downloading update, version {Version}...", LatestUpdate.Version);

            var downloadUri = await ResolveInstallerDownloadUriAsync(LatestUpdate.Version, cancellationToken).ConfigureAwait(false);
            if (downloadUri == null)
            {
                _logger?.LogWarning("Could not find installer asset for version {Version}", LatestUpdate.Version);
                return false;
            }

            var progress = new Progress<double>(p => DownloadProgressChanged?.Invoke(this, (int)(p * 100)));
            var success = await _installer.DownloadUpdateAsync(downloadUri, progress, cancellationToken).ConfigureAwait(false);

            if (success)
                SetSkippedUpdateVersion(LatestUpdate.Version);

            return success;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to download update");
            UpdateFailed?.Invoke(this, ex);
            return false;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    public async Task InstallUpdateAsync()
    {
        try
        {
            _settingsService.SaveImmediate(suppressCloudBackup: false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save settings before update");
        }

        await _installer.InstallUpdateAsync().ConfigureAwait(false);
    }

    public async Task<string?> FetchReleaseNotesFromGitHubAsync(string version, CancellationToken cancellationToken = default)
    {
        try
        {
            var tags = new[] { $"v{version}", version };
            foreach (var tag in tags)
            {
                try
                {
                    var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/tags/{tag}";
                    var response = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                    var json = JObject.Parse(response);
                    var body = json["body"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(body) && body != "null")
                    {
                        _logger?.LogDebug("Fetched release notes from GitHub for {Tag}", tag);
                        return body;
                    }
                }
                catch
                {
                    // Tag not found, try next
                }
            }

            _logger?.LogDebug("No release notes found on GitHub for version {Version}", version);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Failed to fetch release notes from GitHub: {Error}", ex.Message);
            return null;
        }
    }

    private async Task<UpdateInfo?> CheckGitHubReleasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            _logger?.LogDebug("Checking GitHub releases API: {Url}", url);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var response = await _httpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);

            var tagMatch = Regex.Match(response, "\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"");
            if (!tagMatch.Success)
            {
                _logger?.LogDebug("Could not parse tag_name from GitHub response");
                return null;
            }

            var latestVersionString = tagMatch.Groups[1].Value;
            _logger?.LogInformation("GitHub API reports latest version: {Version}", latestVersionString);

            if (!Version.TryParse(latestVersionString, out var latestVersion))
            {
                _logger?.LogWarning("Could not parse version from tag: {Tag}", latestVersionString);
                return null;
            }

            var currentVersion = GetCurrentVersion();
            var isNewer = latestVersion > currentVersion;

            _logger?.LogInformation("GitHub version comparison: latest={Latest}, current={Current}, isNewer={IsNewer}",
                latestVersion, currentVersion, isNewer);

            if (!isNewer)
                return null;

            string releaseNotes = "";
            long fileSizeBytes = 0;
            try
            {
                var json = JObject.Parse(response);
                releaseNotes = json["body"]?.ToString() ?? "";
                if (json["assets"] is JArray assets)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString() ?? "";
                        if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            fileSizeBytes = (long)(asset["size"] ?? 0);
                            _logger?.LogDebug("Parsed installer size from GitHub: {Size} bytes", fileSizeBytes);
                            break;
                        }
                    }
                }
            }
            catch (Exception parseEx)
            {
                _logger?.LogDebug("Could not parse assets from GitHub response: {Error}", parseEx.Message);
            }

            return new UpdateInfo
            {
                Version = latestVersionString,
                ReleaseNotes = releaseNotes,
                FileSizeBytes = fileSizeBytes,
                ReleaseDate = DateTime.Now,
                IsNewer = true,
                IsGitHubFallback = true
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "GitHub releases API check failed");
            return null;
        }
    }

    private async Task<Uri?> ResolveInstallerDownloadUriAsync(string version, CancellationToken cancellationToken)
    {
        var tags = new[] { $"v{version}", version };
        foreach (var tag in tags)
        {
            try
            {
                var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/tags/{tag}";
                var response = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

                var patterns = new[]
                {
                    $"-{version}-Setup.exe",
                    $"-{tag}-Setup.exe",
                    "Installer.exe",
                    "Setup.exe"
                };

                foreach (var pattern in patterns)
                {
                    var assetMatch = Regex.Match(
                        response,
                        $"\"browser_download_url\"\\s*:\\s*\"([^\"]*{Regex.Escape(pattern)}[^\"]*)\"",
                        RegexOptions.IgnoreCase);

                    if (assetMatch.Success)
                    {
                        var downloadUrl = assetMatch.Groups[1].Value;
                        _logger?.LogInformation("Found installer asset: {Asset}", Path.GetFileName(new Uri(downloadUrl).LocalPath));
                        return new Uri(downloadUrl);
                    }
                }
            }
            catch
            {
                // Tag not found, try next
            }
        }

        return null;
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
                return File.GetLastWriteTime(skipFile);
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
        }
        catch { }
    }

    private static void ClearSkippedUpdateVersion()
    {
        try
        {
            var skipFile = GetSkipFilePath();
            if (File.Exists(skipFile))
                File.Delete(skipFile);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
