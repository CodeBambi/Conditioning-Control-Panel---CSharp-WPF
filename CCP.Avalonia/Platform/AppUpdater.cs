using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The head half of the updater over Core <see cref="ReleaseFeed"/>: WPF UpdateService (check,
    /// download, silent install through the Inno helper) plus the App.xaml.cs flows that drive it
    /// (startup check :4858, manual check :5252, install :5063). Windows installs in place exactly
    /// as WPF does; every other OS only notifies and opens the releases page - it never downloads
    /// or runs anything.
    /// </summary>
    internal static class AppUpdater
    {
        // WPF UpdateService.cs:151-152.
        internal const string GitHubApi = "https://api.github.com/repos/CodeBambi/Conditioning-Control-Panel---CSharp-WPF";

        /// <summary>Test transport; null = the default one.</summary>
        internal static HttpMessageHandler? Handler;

        /// <summary>Opens a page in the browser, else copies it (WPF BrowserLauncher). Tests capture it.</summary>
        internal static Func<TopLevel?, string, Task> OpenUrl = async (top, url) =>
        {
            if (await ExternalOpener.OpenAsync(top, url)) return;
            try { if (top?.Clipboard is { } c) await c.SetTextAsync(url); } catch { }
        };

        internal static UpdateInfo? Latest { get; private set; }
        private static bool _busy;

        /// <summary>WPF App.IsUpdateDialogActive: an update offer or manual check owns the screen.</summary>
        internal static bool IsUpdateDialogActive => _busy;
        private static string UserData => CorePaths.UserData;

        /// <summary>
        /// Where the releases API lives, or null for "no network": a CCP_USERDATA_DIR sandbox
        /// (tests, Keincheck) never calls GitHub unless CCP_UPDATE_API_URL names a loopback host -
        /// the same rule App.SkipStartupFetch applies to content packs.
        /// </summary>
        internal static string? ApiBase()
        {
            if (LoopbackUrl.IsHonoured(Environment.GetEnvironmentVariable("CCP_UPDATE_API_URL"), out var u))
                return u.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return Sandboxed ? null : GitHubApi;
        }

        private static bool Sandboxed => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"));

        /// <summary>The startup dialog while it is open (tests close it).</summary>
        internal static UpdateNotificationDialog? OpenDialog { get; private set; }

        private static HttpClient Client(TimeSpan timeout)
        {
            var c = Handler is null ? new HttpClient() : new HttpClient(Handler, disposeHandler: false);
            c.DefaultRequestHeaders.Add("User-Agent", "ConditioningControlPanel");
            c.Timeout = timeout;
            return c;
        }

        private static MainShellWindow? Shell =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainShellWindow;

        // ---- check (WPF UpdateService.CheckForUpdatesAsync :303 / CheckGitHubReleasesAsync :1261) ----

        internal static async Task<UpdateInfo?> CheckAsync(bool force)
        {
            if (CoreSettings.Current.OfflineMode)
            {
                Log.Information("Offline mode enabled, skipping update check");
                return null;
            }
            // Only installed users can self-update on Windows (UpdateService.cs:316); Linux notifies.
            if (OperatingSystem.IsWindows() && RunningInstallDir() is null && RegistryInstallPath() is null)
            {
                Log.Information("App not installed via installer (running from source/dev), skipping update check");
                return null;
            }
            if (ApiBase() is not { } api)
            {
                Log.Information("Updater: sandboxed profile without a loopback CCP_UPDATE_API_URL - no update check");
                return null;
            }

            // Loop-prevention: a skipped/failed version stays quiet for 24h unless forced (#849).
            var skipped = ReleaseFeed.GetSkippedUpdateVersion(UserData);
            if (!string.IsNullOrEmpty(skipped) && (force
                || (DateTime.Now - ReleaseFeed.GetSkippedUpdateTime(UserData)).TotalHours >= ReleaseFeed.SkipMarkerLifetimeHours))
            {
                ReleaseFeed.ClearSkippedUpdateVersion(UserData);
                skipped = null;
            }

            var info = await FetchLatestAsync(api);
            if (info is null)
            {
                Latest = null;
                ReleaseFeed.ClearSkippedUpdateVersion(UserData);
                return null;
            }
            if (skipped == info.Version)
            {
                Log.Information("Suppressing update to {Version} - skipped/attempted within {Limit}h", info.Version, ReleaseFeed.SkipMarkerLifetimeHours);
                info.IsNewer = false;
            }
            Latest = info;
            return info;
        }

        private static async Task<UpdateInfo?> FetchLatestAsync(string api)
        {
            try
            {
                using var client = Client(TimeSpan.FromSeconds(15));
                var response = await client.GetStringAsync($"{api}/releases/latest");
                var tag = ReleaseFeed.ParseTagVersion(response);
                if (tag is null || !Version.TryParse(tag, out var latest)) return null;
                if (!Version.TryParse(CoreReleaseContent.AppVersion, out var current)) current = new Version(1, 0, 0);
                Log.Information("GitHub version comparison: latest={Latest}, current={Current}", latest, current);
                if (latest <= current) return null;

                var info = new UpdateInfo { Version = tag, ReleaseDate = DateTime.Now, IsNewer = true, IsGitHubFallback = true };
                try
                {
                    var (notes, size) = ReleaseFeed.ParseNotesAndSetupSize(response);
                    info.ReleaseNotes = notes;
                    info.FileSizeBytes = size ?? 0;
                }
                catch (Exception ex) { Log.Debug("Could not parse assets from GitHub response: {Error}", ex.Message); }
                return info;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GitHub releases API check failed");
                return null;
            }
        }

        // ---- flows (WPF App.xaml.cs) --------------------------------------------------------------

        /// <summary>
        /// WPF CheckForUpdatesInBackgroundAsync (:4858): report a previous install that did not take
        /// (reads WPF's own update_attempt/update_result markers), light the pill, then offer the
        /// notification dialog once (App.xaml.cs:4903). WPF queues it last on its StartupLadder;
        /// this head has no ladder, so it waits until the age gate is accepted and the first-run
        /// wizard / age-gate modals are closed. A skipped version never gets here (CheckAsync).
        /// </summary>
        internal static async Task StartupAsync(MainShellWindow shell)
        {
            try
            {
                await Task.Delay(500);
                var outcome = ReleaseFeed.ConsumePendingUpdateOutcome(UserData, CoreReleaseContent.AppVersion);
                if (outcome is { Succeeded: false })
                    UpdateFailedDialog.ShowFor(shell, Loc.Get("title_update_failed"),
                        Loc.GetF("msg_update_install_failed", outcome.Version, CoreReleaseContent.AppVersion));

                var info = await CheckAsync(force: false);
                if (info?.IsNewer != true) return;
                shell.ShowUpdatePill(info.Version);

                var closed = false;
                shell.Closed += (_, _) => closed = true;
                while (!closed && (!CoreSettings.Current.HasAcceptedAgeVerification || !shell.IsVisible || StartupModalOpen()))
                    await Task.Delay(500);
                if (!closed && !_busy) await OfferAsync(shell, info);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background update check failed");
            }
        }

        private static bool StartupModalOpen() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
                .Any(w => w is FirstRunWizard or MessageDialog && w.IsVisible) == true;

        /// <summary>The notification dialog: Install (Windows) / Download (elsewhere), or Later = quiet for 24h.</summary>
        private static async Task OfferAsync(Window owner, UpdateInfo info)
        {
            _busy = true;
            try
            {
                var dialog = OpenDialog = new UpdateNotificationDialog(info)
                {
                    Topmost = true,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                await dialog.ShowDialog<bool?>(owner);
                OpenDialog = null;
                if (dialog.InstallRequested) await InstallAsync(owner);
                else ReleaseFeed.SetSkippedUpdateVersion(UserData, info.Version);
            }
            finally
            {
                OpenDialog = null;
                _busy = false;
            }
        }

        /// <summary>WPF MainWindow.AccountShell.cs:482. A known update on Linux opens the page
        /// directly (the pill's tooltip says so); otherwise the manual check runs.</summary>
        internal static Task PillClickedAsync(Window owner) =>
            !OperatingSystem.IsWindows() && Latest?.IsNewer == true
                ? InstallAsync(owner)
                : ManualCheckAsync(owner);

        /// <summary>WPF CheckForUpdatesManuallyAsync (App.xaml.cs:5252), minus the server-banner
        /// fallback (no marquee banner service on this head).</summary>
        internal static async Task ManualCheckAsync(Window owner)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var info = await CheckAsync(force: true);
                if (info?.IsNewer == true)
                {
                    Shell?.ShowUpdatePill(info.Version);
                    _busy = false;
                    await OfferAsync(owner, info);
                }
                else
                {
                    Shell?.ShowUpdateAvailableButton(false);
                    await MessageDialog.ShowAsync(owner, Loc.Get("title_no_updates"),
                        Loc.GetF("msg_already_on_latest", CoreReleaseContent.AppVersion));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Manual update check failed");
                await MessageDialog.ShowAsync(owner, Loc.Get("title_update_check_failed"), Loc.GetF("msg_update_check_failed", ex.Message));
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// "Install" for <see cref="Latest"/>. Off Windows this is notify-only by design (the Linux
        /// packages update through their own channels): open the releases page, nothing else.
        /// </summary>
        internal static async Task InstallAsync(Window owner)
        {
            if (!OperatingSystem.IsWindows())
            {
                await OpenUrl(owner, ReleaseLinks.ReleasesPageUrl);
                return;
            }
            if (Latest is { } info) await DownloadAndRunAsync(owner, info);
        }

        // ---- Windows install (WPF App.DownloadAndRunInstallerAsync :5063, UpdateService :418-790) ----

        private static async Task DownloadAndRunAsync(Window owner, UpdateInfo info)
        {
            var progress = new UpdateProgressDialog { Topmost = true };
            // WPF :5071-5085 hides the main window and the avatar tube while it downloads; the
            // progress dialog stays up and owns the confirm (ShowDialog needs a visible owner).
            var hidden = ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
                ?? Array.Empty<Window>()).Where(w => w.IsVisible).ToList();
            void Restore() { foreach (var w in hidden) try { w.Show(); } catch { } }
            try
            {
                var api = ApiBase() ?? throw new InvalidOperationException("No release feed in a sandboxed profile");
                progress.Show();
                foreach (var w in hidden) w.Hide();
                var fill = progress.FindControl<Border>("ProgressFill");
                var installer = await DownloadInstallerAsync(api, info.Version, p => Dispatcher.UIThread.Post(() =>
                    progress.SetProgress(p / 100.0, (fill?.Parent as Control)?.Bounds.Width ?? 0)));

                var go = await MessageDialog.ConfirmAsync(progress, Loc.Get("title_ready_to_update"), Loc.Get("msg_ready_to_update"));
                progress.Close();
                if (!go) { Restore(); return; }
                if (!RunInstallerSilentlyAndExit(installer, info.Version))
                {
                    Restore();
                    UpdateFailedDialog.ShowFor(owner, Loc.Get("title_update_not_installed"), Loc.Get("msg_update_permission_declined"));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to download installer");
                try { progress.Close(); } catch { }
                Restore();
                UpdateFailedDialog.ShowFor(owner, Loc.Get("title_update_failed"), Loc.Get("msg_update_download_failed"), ex.Message);
            }
        }

        private static async Task<string> DownloadInstallerAsync(string api, string version, Action<int> progress)
        {
            using var client = Client(TimeSpan.FromMinutes(10));
            string? url = null;
            foreach (var tag in new[] { $"v{version}", version })
            {
                try { url = ReleaseFeed.FindSetupAssetUrl(await client.GetStringAsync($"{api}/releases/tags/{tag}"), version, tag); }
                catch { /* tag not found, try next */ }
                if (url != null) break;
            }
            if (url is null) throw new InvalidOperationException($"Could not find Setup.exe installer in GitHub release {version}");
            if (Sandboxed && !LoopbackUrl.IsHonoured(url, out _))
                throw new InvalidOperationException($"Sandboxed profile: refusing non-loopback installer URL {url}");

            var dir = Path.Combine(Path.GetTempPath(), "ConditioningControlPanel_Update");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Path.GetFileName(new Uri(url).LocalPath));

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (attempt > 1) await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? -1;
                    await using var src = await response.Content.ReadAsStreamAsync();
                    await using var dst = File.Create(path);
                    var buffer = new byte[81920];
                    long done = 0;
                    int read, last = -1;
                    while ((read = await src.ReadAsync(buffer)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, read));
                        done += read;
                        if (total > 0 && (int)(done * 100 / total) is var p && p != last) progress(last = p);
                    }
                    return path;
                }
                catch (Exception ex) when (IsTransientNetworkError(ex))
                {
                    if (attempt >= 3)
                        throw new InvalidOperationException($"Failed to download installer after 3 attempts: {ex.Message}", ex);
                    Log.Warning(ex, "Download attempt {Attempt} failed with transient error", attempt);
                }
            }
        }

        /// <summary>
        /// WPF RunInstallerSilentlyAndExit (UpdateService.cs:603): the Core helper script waits for
        /// this pid, runs Setup /SILENT into the running install dir, records the exit code and
        /// relaunches ConditioningControlPanel.exe. False = the helper never started (UAC declined).
        /// No WebView2 cleanup: this head keeps no browser_data folder next to the exe.
        /// </summary>
        private static bool RunInstallerSilentlyAndExit(string installerPath, string version)
        {
            var registry = RegistryInstallPath();
            var installPath = RunningInstallDir()
                ?? (string.IsNullOrEmpty(registry) ? Path.GetDirectoryName(Environment.ProcessPath) : registry);
            var appExe = !string.IsNullOrEmpty(installPath)
                ? Path.Combine(installPath, "ConditioningControlPanel.exe")
                : Environment.ProcessPath ?? "";
            var logPath = Path.Combine(UserData, "logs", "update-helper.log");
            var elevate = NeedsElevation(installPath);
            try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); } catch { }

            var helper = Path.Combine(Path.GetDirectoryName(installerPath)!, "update-helper.cmd");
            File.WriteAllText(helper, ReleaseFeed.BuildUpdateHelperScript(installerPath, installPath, Environment.ProcessId,
                appExe, logPath, ReleaseFeed.AttemptResultFilePath(UserData), elevate), new UTF8Encoding(false));

            var start = new ProcessStartInfo("cmd.exe", $"/c \"{helper}\"") { WindowStyle = ProcessWindowStyle.Hidden };
            if (elevate) { start.UseShellExecute = true; start.Verb = "runas"; }
            else { start.UseShellExecute = false; start.CreateNoWindow = true; }

            ReleaseFeed.SetPendingUpdateAttempt(UserData, version);
            try { Process.Start(start); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Update helper did not launch (elevation declined?)");
                ReleaseFeed.ClearPendingUpdateAttempt(UserData);
                ReleaseFeed.SetSkippedUpdateVersion(UserData, version);
                return false;
            }
            Log.Information("Exiting for silent update (helper will install + relaunch)");
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            return true;
        }

        /// <summary>WPF UpdateService.IsTransientNetworkError (:1387).</summary>
        internal static bool IsTransientNetworkError(Exception ex)
        {
            if (ex is HttpRequestException or IOException or System.Net.Sockets.SocketException or TaskCanceledException)
                return true;
            if (ex.InnerException != null) return IsTransientNetworkError(ex.InnerException);
            var m = ex.Message.ToLowerInvariant();
            return m.Contains("forcibly closed") || m.Contains("connection was closed") || m.Contains("network")
                || m.Contains("timeout") || m.Contains("transport");
        }

        private static bool NeedsElevation(string? installPath)
        {
            if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return false;
            try
            {
                using (File.Create(Path.Combine(installPath, $".ccp-write-probe-{Guid.NewGuid():N}.tmp"), 1, FileOptions.DeleteOnClose)) { }
                return false;
            }
            catch { return true; }
        }

        private static string? RunningInstallDir()
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "unins000.exe")) ? dir : null;
        }

        private static string? RegistryInstallPath()
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\CodeBambi\Conditioning Control Panel");
                return key?.GetValue("InstallPath") as string;
            }
            catch { return null; }
        }
    }
}
