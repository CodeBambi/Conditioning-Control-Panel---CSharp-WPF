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
    public string CurrentVersion => "6.3.2";   // WPF Services/Update/UpdateService.cs (v6.3.2 bump)

    /// <summary>
    /// Hard-coded current application version (single source for heads that need a const,
    /// e.g. the What's-New dialog title). Mirrors the WPF UpdateService.AppVersion constant.
    /// </summary>
    public const string AppVersion = "6.3.2";   // WPF Services/Update/UpdateService.cs:23

    /// <summary>
    /// Patch notes for the current version — shown in the update/what's-new dialogs and used as a
    /// fallback when GitHub release notes are unavailable. Update together with <see cref="AppVersion"/>.
    /// </summary>
    public const string CurrentPatchNotes = @"v6.3.2 - Deeper Down

6.3.0 rebuilt Down the Rabbit Hole into a guided descent through four chambers and
sixteen biomes, with a Warren leveling hub, boon drafts, and full voiceover. 6.3.1
kept refining that rework. 6.3.2 is a hotfix on top - breaking a freeze that could
lock your PC and smoothing the last rough edges.

🩹 HOTFIX
- Fixed a mandatory-video freeze that could hard-lock your PC; an off-thread watchdog now breaks the wedge and releases you.
- Fixed a Rabbit Hole hang when flash-pool images decrypted on the main thread.
- Down the Rabbit Hole: Esc now steps out cleanly (pause, then exit fullscreen, then close), with C#-owned fullscreen.
- Rabbit Hole play-test batch: opt out of Cheshire, flash from your own media, plus video and pause fixes.
- Webcam gaze-test window now has a mouse-clickable close button.
- Animated tease-bubble cap now scales with your performance tier.

✨ FEATURES
- Down the Rabbit Hole: brand-new Cheshire visual-novel tutorial that walks you in, replacing the old lesson cards.
- 3 local save slots you pick before the hole opens, so multiple runs stay separate.
- Full biome-drift voiceover now rendered across the descent.

🎨 UI/UX
- Emote stack icon and HUD/VN polish pass.
- Spiral overlay now pulls from a pool of 8 animated effects at random.
- New boon card art.
- 2-second reveal delay plus a spotlight halo on whatever a lesson or Cheshire is explaining.
- De-stormed narration cooldown so voice lines stop overlapping.

🔧 BUG FIXES
- Fixed a boot hang where the game loader spun forever (stray backtick in a CSS template).
- Fixed a video black-screen crash during runs.
- Boon draft now auto-picks a random card on timeout instead of leaving you with nothing.

Season: Jelly July";   // WPF Services/Update/UpdateService.cs:29-61 (verbatim)

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
