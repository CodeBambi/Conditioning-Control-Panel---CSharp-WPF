using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Temporarily overrides the Windows desktop wallpaper with random images
    /// from the user's chosen wallpapers folder (settings.WallpaperSourceFolder, or the
    /// default assets/wallpapers folder). Restores the original on deactivate/dispose.
    /// </summary>
    public class WallpaperService : IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string? lpvParam, int fuWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPI_GETDESKWALLPAPER = 0x0073;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        private string? _originalWallpaperPath;
        private string? _currentImagePath;
        private bool _isActive;
        private List<string> _imagePool = new();
        private readonly Random _random = new();
        private readonly object _lock = new();
        private bool _disposed;

        // ---- remote media (Phase 4, Contract 1 — the one surface that really needs disk) ----
        //
        // SPI_SETDESKWALLPAPER takes a PATH, not a stream and not a URL, so unlike video this
        // cannot dodge the download. Three things follow, and they shape the whole design here:
        //
        //  1. THE FILE MUST OUTLIVE THE CALL. Windows transcodes the image at set time, but it
        //     also writes the path into HKCU Control Panel\Desktop and re-reads it on logon,
        //     theme refresh and explorer restart. Deleting the temp right after the call leaves a
        //     desktop that goes black the next time Windows looks. So a materialized wallpaper is
        //     held for the whole session and only released after the ORIGINAL has been restored.
        //  2. IT CANNOT BLOCK. Activate() and Shuffle() are synchronous, called from the UI and
        //     from Autonomy's pulse. So nothing here downloads on demand: a small pool of
        //     already-materialized files is filled in the background and the picker treats those
        //     paths as ordinary pool entries. The cost is that the FIRST activation on an empty
        //     wallpapers folder still returns false (honestly - nothing changed); it arms a
        //     one-shot retry that fires when the pool lands.
        //  3. WEBP IS NOT A WALLPAPER FORMAT. Windows will not set one, and Scrolller's largest
        //     still rendition is very often webp (Appendix A: r/RealGirls PICTURE = 87 webp / 87
        //     jpg). Remote candidates are therefore filtered to SupportedExtensions, which drops
        //     a real share of every batch. That is why the pool is filled ahead of time rather
        //     than one-for-one with each shuffle.

        /// <summary>Coordinator tenant id. Rotation/dwell are per-consumer; the niche SELECTION
        /// is app-wide and shared with every other remote surface.</summary>
        private const string RemoteConsumerId = "wallpaper";

        /// <summary>How many materialized wallpapers to keep. Each is a real file on disk for the
        /// whole session, so this is deliberately small.</summary>
        private const int RemotePoolTarget = 4;

        /// <summary>Whole-batch budget for one background fill.</summary>
        private const int RemoteFetchTimeoutMs = 60000;

        /// <summary>Materialized remote wallpapers (local temp paths), newest last.</summary>
        private readonly List<string> _remotePool = new();

        /// <summary>0/1 via Interlocked: one background fill at a time.</summary>
        private int _remoteFillInFlight;

        /// <summary>An Activate() found both pools empty while remote was on. When the fill lands,
        /// retry that activation exactly once rather than making the user ask twice.</summary>
        private bool _remoteActivationPending;

        /// <summary>The deferred retry has already been spent for this activation attempt. Without
        /// it a permanently failing CDN would loop: retry -> Activate -> still empty -> re-arm ->
        /// fill -> retry. Reset when an activation actually succeeds, or on Deactivate.</summary>
        private bool _remoteRetryConsumed;

        public bool IsActive
        {
            get { lock (_lock) return _isActive; }
        }

        public string? CurrentFilename
        {
            get { lock (_lock) return _currentImagePath != null ? Path.GetFileName(_currentImagePath) : null; }
        }

        public WallpaperService()
        {
            // A hard kill (render wedge, task-kill, the TerminateProcess tail in App.OnExit) never
            // reaches Deactivate(), so our wallpaper is still on the desktop next launch — and a
            // fresh Activate() would capture THAT as "the original", stranding the user's real
            // wallpaper forever (#692). If the last session left one behind, put it back first.
            try
            {
                RestoreStaleOriginal();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[Wallpaper] Startup restore of the previous session's wallpaper failed");
            }
        }

        /// <summary>
        /// Save the current wallpaper, scan the pool, and set a random image.
        /// Returns false if no images were found or the current wallpaper couldn't be captured.
        /// </summary>
        public bool Activate()
        {
            lock (_lock)
            {
                if (_isActive) return true;

                try
                {
                    // Save current wallpaper path. If Windows can't hand back a usable one we do
                    // NOT touch the desktop at all — a skipped effect is far cheaper than a
                    // wallpaper the user can never get back (#692).
                    var original = ReadCurrentWallpaper();
                    if (original == null)
                    {
                        App.Logger?.Warning("[Wallpaper] Current desktop wallpaper is unreadable (solid colour, slideshow, theme or Spotlight?) — skipping so the desktop stays restorable");
                        return false;
                    }

                    // Scan wallpapers folder — user-chosen folder if set, else the default
                    // assets/wallpapers folder under the effective assets path.
                    var custom = App.Settings?.Current?.WallpaperSourceFolder;
                    var wallpapersDir = !string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom)
                        ? custom
                        : Path.Combine(App.EffectiveAssetsPath, "wallpapers");
                    if (Directory.Exists(wallpapersDir))
                    {
                        _imagePool = Directory.GetFiles(wallpapersDir)
                            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .ToList();
                    }
                    else
                    {
                        App.Logger?.Warning("[Wallpaper] Wallpapers directory not found: {Dir}", wallpapersDir);
                        _imagePool = new List<string>();
                    }

                    // No-op unless remote media is on and consented. Fire-and-forget by contract:
                    // this method is called from the UI thread and from Autonomy's pulse.
                    KickRemoteFill();

                    if (_imagePool.Count == 0 && _remotePool.Count == 0)
                    {
                        App.Logger?.Warning("[Wallpaper] No usable images - local folder {Dir} is empty/absent and the remote pool is cold", wallpapersDir);
                        // Another silent refusal: the effect simply never happens and the user is
                        // never told why. OfferRemoteMediaSource only queues, it never blocks and
                        // never re-enters this service, so calling it under _lock is safe. (It
                        // self-suppresses once the user has already switched off "local".)
                        App.OfferRemoteMediaSource("wallpaper");
                        // If remote IS on, the fill above is in flight - come back to this ONCE
                        // (see _remoteRetryConsumed; a dead CDN must not spin activate/fetch).
                        _remoteActivationPending = RemoteMediaEnabled() && !_remoteRetryConsumed;
                        return false;
                    }

                    // Pick a random wallpaper. Breadcrumb goes down BEFORE the desktop changes:
                    // between SetWallpaper and the persist there is a window where the user's
                    // wallpaper is already gone but nothing on disk remembers it, and a kill in
                    // there strands them permanently (#692). Write first, undo if the set fails.
                    var image = PickImage();
                    if (image == null) return false;
                    PersistOriginal(original);
                    if (!SetWallpaper(image))
                    {
                        PersistOriginal(null);
                        return false;
                    }

                    _originalWallpaperPath = original;
                    _currentImagePath = image;
                    _isActive = true;
                    _remoteActivationPending = false;
                    _remoteRetryConsumed = false;   // the next dry spell gets its own deferred go
                    App.Logger?.Information("[Wallpaper] Activated with {File} (pool: {Count} local + {Remote} remote)",
                        Path.GetFileName(image), _imagePool.Count, _remotePool.Count);
                    return true;
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[Wallpaper] Failed to activate");
                    return false;
                }
            }
        }

        /// <summary>
        /// Restore the original wallpaper.
        /// </summary>
        public void Deactivate()
        {
            lock (_lock)
            {
                if (!_isActive) return;

                try
                {
                    var original = _originalWallpaperPath;
                    if (string.IsNullOrWhiteSpace(original) || !File.Exists(original))
                    {
                        // Never hand SPI_SETDESKWALLPAPER an empty/missing path: Windows clears the
                        // desktop to a flat colour and writes that to the registry (#692).
                        App.Logger?.Warning("[Wallpaper] No usable original to restore ({Path}) — leaving the desktop as-is", original ?? "<null>");
                    }
                    else if (SetWallpaper(original))
                    {
                        App.Logger?.Information("[Wallpaper] Restored original wallpaper");
                        // Restored for real — drop the crash-recovery breadcrumb.
                        PersistOriginal(null);
                    }
                    else
                    {
                        // Keep the persisted path so the next launch gets another go at it.
                        App.Logger?.Warning("[Wallpaper] Windows refused to restore {Path} (win32 {Err})", original, Marshal.GetLastWin32Error());
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[Wallpaper] Failed to restore original wallpaper");
                }
                finally
                {
                    _isActive = false;
                    _currentImagePath = null;
                    // A fresh activation cycle gets its own deferred-retry budget. The materialized
                    // pool is NOT released here — see ReleaseRemotePool for why.
                    _remoteActivationPending = false;
                    _remoteRetryConsumed = false;
                }
            }
        }

        /// <summary>
        /// If active, pick a new random image (different from current if possible).
        /// If not active, activate.
        /// </summary>
        public bool Shuffle()
        {
            lock (_lock)
            {
                if (!_isActive) return Activate();

                try
                {
                    // Keep the remote pool topped up across a long shuffling session, not just at
                    // activation. Still fire-and-forget - a pulse must never wait on a download.
                    KickRemoteFill();

                    var total = _imagePool.Count + _remotePool.Count;
                    if (total == 0) return false;

                    var image = PickImage();
                    if (image == null) return false;
                    if (total > 1)
                    {
                        // Try to pick a different image than current. Bounded: GetFiles can't return
                        // duplicates today, but an unbounded spin here is one duplicate entry away
                        // from wedging the UI thread.
                        for (var attempt = 0; attempt < 8 && image == _currentImagePath; attempt++)
                            image = PickImage() ?? image;
                    }

                    if (!SetWallpaper(image)) return false;

                    _currentImagePath = image;
                    App.Logger?.Debug("[Wallpaper] Shuffled to {File}", Path.GetFileName(image));
                    return true;
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[Wallpaper] Failed to shuffle");
                    return false;
                }
            }
        }

        #region Remote media (Phase 4)

        /// <summary>True when remote media may appear anywhere in the app. Reads
        /// <c>HasRemoteMediaConsent</c>, never the raw consent flag - a user who accepted the For
        /// You feed's card has already agreed to exactly this.</summary>
        private static bool RemoteMediaEnabled()
        {
            var s = App.Settings?.Current;
            return s != null && s.MediaSource != "local" && s.HasRemoteMediaConsent;
        }

        /// <summary>Our channel set. Niche selection is app-wide by design; only rotation and
        /// dwell are per-consumer, which is what asking for our own tenant buys.</summary>
        private static IReadOnlyList<string> RemoteChannels()
        {
            var s = App.Settings?.Current;
            return Fyp.Online.FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
        }

        /// <summary>
        /// One image for the desktop, honouring the app-wide source setting. Caller holds _lock.
        /// "online" draws remote and "mixed" rolls <c>RemoteMediaRatio</c>, but either side falls
        /// back to the other when its own pool is empty - a cold remote pool must never mean a
        /// wallpaper effect that silently stops happening.
        /// </summary>
        private string? PickImage()
        {
            bool remoteAllowed = RemoteMediaEnabled() && _remotePool.Count > 0;
            bool localAvailable = _imagePool.Count > 0;

            if (remoteAllowed && localAvailable)
            {
                var source = App.Settings?.Current?.MediaSource ?? "local";
                var ratio = App.Settings?.Current?.RemoteMediaRatio ?? 30;
                bool useRemote = string.Equals(source, "online", StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(source, "mixed", StringComparison.OrdinalIgnoreCase) && _random.Next(100) < ratio);
                return useRemote
                    ? _remotePool[_random.Next(_remotePool.Count)]
                    : _imagePool[_random.Next(_imagePool.Count)];
            }

            if (remoteAllowed) return _remotePool[_random.Next(_remotePool.Count)];
            if (localAvailable) return _imagePool[_random.Next(_imagePool.Count)];
            return null;
        }

        /// <summary>Fire-and-forget top-up of the materialized remote pool. Single-flight, never
        /// throws, and touches nothing on the UI thread. Caller may hold _lock.</summary>
        private void KickRemoteFill()
        {
            bool claimed = false;
            try
            {
                if (_disposed || !RemoteMediaEnabled()) return;
                if (_remotePool.Count >= RemotePoolTarget) return;
                if (Interlocked.CompareExchange(ref _remoteFillInFlight, 1, 0) != 0) return;
                claimed = true;

                _ = Task.Run(async () =>
                {
                    bool retryActivation = false;
                    try
                    {
                        retryActivation = await FillRemotePoolAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // The coordinator and RemoteMediaCache are both documented never-throw;
                        // an unobserved exception here would surface as an app crash instead.
                        App.Logger?.Warning("[Wallpaper] Remote fill failed: {Error}", ex.Message);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _remoteFillInFlight, 0);
                    }

                    if (retryActivation) RetryPendingActivation();
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Wallpaper] Could not start the remote fill: {Error}", ex.Message);
                // Only release the latch if this call took it — clearing someone else's claim
                // would let two fills run at once and double-download the pool.
                if (claimed) Interlocked.Exchange(ref _remoteFillInFlight, 0);
            }
        }

        /// <summary>
        /// Fetch remote stills and materialize the usable ones to local temp files. Returns true
        /// when an activation was waiting on this pool and can now be retried.
        /// </summary>
        private async Task<bool> FillRemotePoolAsync()
        {
            using var cts = new CancellationTokenSource(RemoteFetchTimeoutMs);
            var coordinator = Fyp.Online.FypOnlineCoordinator.For(
                RemoteConsumerId, RemoteChannels, Fyp.Online.FeedMediaKind.Image);

            var (entries, error) = await coordinator.FetchBatchAsync(cts.Token).ConfigureAwait(false);
            if (error != null)
            {
                App.Logger?.Warning("[Wallpaper] Remote fetch reported {Error} - the desktop stays on the local pool", error);
                return false;
            }

            int wanted;
            lock (_lock) wanted = Math.Max(0, RemotePoolTarget - _remotePool.Count);
            if (wanted == 0) return false;

            var materialized = new List<string>();
            foreach (var entry in entries)
            {
                if (materialized.Count >= wanted) break;
                if (cts.IsCancellationRequested) break;

                if (!Fyp.Online.RemoteMediaFormats.Validate(entry, Fyp.Online.FeedMediaKind.Image, out var reason))
                {
                    App.Logger?.Debug("[Wallpaper] Rejected remote entry {Id}: {Reason}", entry.Id, reason);
                    continue;
                }

                // The wallpaper API's format list is NARROWER than what a remote still can be:
                // Windows will not set a .webp, and webp is frequently the largest rendition a
                // post offers. Checked on the URL first so we never spend a download on a file
                // we already know the desktop will refuse.
                if (!IsWallpaperFormat(entry.Url))
                {
                    App.Logger?.Debug("[Wallpaper] Skipped {Id}: {Ext} is not a desktop-wallpaper format",
                        entry.Id, ExtensionOf(entry.Url));
                    continue;
                }

                var temp = await Fyp.Online.RemoteMediaCache.MaterializeAsync(entry.Url, cts.Token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(temp))
                {
                    App.Logger?.Debug("[Wallpaper] Could not materialize {Id} - skipping it", entry.Id);
                    continue;
                }

                // Belt to the URL check's braces: whatever the cache decided to name the file is
                // what Windows will be handed, so that is what has to be a wallpaper format.
                if (!IsWallpaperFormat(temp) || !File.Exists(temp))
                {
                    App.Logger?.Debug("[Wallpaper] Materialized {Id} as {Ext} which the desktop can't use - releasing it",
                        entry.Id, ExtensionOf(temp));
                    SafeRelease(temp);
                    continue;
                }

                materialized.Add(temp!);
            }

            if (materialized.Count == 0)
            {
                App.Logger?.Debug("[Wallpaper] Remote batch yielded no usable wallpapers ({N} entries offered)", entries.Count);
                return false;
            }

            bool pending;
            int total;
            lock (_lock)
            {
                _remotePool.AddRange(materialized);
                total = _remotePool.Count;
                pending = _remoteActivationPending && !_isActive;
            }

            App.Logger?.Information("[Wallpaper] Materialized {Added} remote wallpapers ({Total} ready)", materialized.Count, total);
            return pending;
        }

        /// <summary>
        /// An activation failed earlier only because the remote pool was cold. Now that it isn't,
        /// have one more go. Marshalled to the UI thread: Activate persists a setting, and a
        /// settings property change from a threadpool thread can reach a live WPF binding.
        /// </summary>
        private void RetryPendingActivation()
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        lock (_lock)
                        {
                            // Re-checked INSIDE the dispatcher callback: the user may have turned
                            // the effect off, or another path may have activated, while this was queued.
                            if (_disposed || _isActive || !_remoteActivationPending) return;
                            _remoteActivationPending = false;
                            _remoteRetryConsumed = true;
                        }
                        App.Logger?.Information("[Wallpaper] Remote pool is warm - retrying the activation that had nothing to show");
                        Activate();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("[Wallpaper] Deferred activation failed: {Error}", ex.Message);
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Wallpaper] Could not schedule the deferred activation: {Error}", ex.Message);
            }
        }

        private static string ExtensionOf(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try
            {
                var cut = path.IndexOfAny(new[] { '?', '#' });
                var clean = cut >= 0 ? path[..cut] : path;
                return Path.GetExtension(clean).ToLowerInvariant();
            }
            catch { return ""; }
        }

        /// <summary>Formats SPI_SETDESKWALLPAPER actually accepts - the same list the local scan
        /// uses, applied to a URL or a temp path.</summary>
        private static bool IsWallpaperFormat(string? path)
            => SupportedExtensions.Contains(ExtensionOf(path));

        private static void SafeRelease(string? temp)
        {
            try { Fyp.Online.RemoteMediaCache.ReleaseTempFile(temp); }
            catch (Exception ex) { App.Logger?.Debug("[Wallpaper] Temp release failed: {Error}", ex.Message); }
        }

        /// <summary>
        /// Drop every materialized wallpaper. ONLY safe once the desktop is no longer pointed at
        /// one of them - Windows re-reads the registry path on logon, theme change and explorer
        /// restart, so releasing a file that is still the current wallpaper is how the desktop
        /// ends up black later. Called from Dispose, AFTER Deactivate has put the original back.
        ///
        /// Deliberately NOT called from Deactivate: Autonomy's wallpaper pulse activates and
        /// deactivates every WallpaperPulseSeconds, and releasing there would re-download the
        /// whole pool on every pulse. Session lifetime matches the content-pack temp model, and
        /// RemoteMediaCache's startup sweep collects anything an unclean exit leaves behind.
        /// </summary>
        private void ReleaseRemotePool()
        {
            List<string> toRelease;
            lock (_lock)
            {
                toRelease = new List<string>(_remotePool);
                _remotePool.Clear();
            }
            foreach (var temp in toRelease) SafeRelease(temp);
            if (toRelease.Count > 0)
                App.Logger?.Debug("[Wallpaper] Released {Count} materialized remote wallpapers", toRelease.Count);
        }

        #endregion

        private static bool SetWallpaper(string path)
        {
            var result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            return result != 0;
        }

        /// <summary>
        /// Read the desktop wallpaper path, or null if Windows didn't hand back a usable one.
        /// The buffer is deliberately well past MAX_PATH — OneDrive-redirected Pictures folders
        /// routinely produce longer paths and the API truncates silently instead of failing.
        /// An empty result is normal for solid-colour, slideshow, theme and Spotlight desktops.
        /// </summary>
        private static string? ReadCurrentWallpaper()
        {
            var sb = new StringBuilder(4096);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0) == 0)
            {
                App.Logger?.Warning("[Wallpaper] SPI_GETDESKWALLPAPER failed (win32 {Err})", Marshal.GetLastWin32Error());
                return null;
            }

            var path = sb.ToString();
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!File.Exists(path))
            {
                App.Logger?.Warning("[Wallpaper] Reported wallpaper does not exist on disk: {Path}", path);
                return null;
            }
            return path;
        }

        /// <summary>
        /// Remember (or forget) the captured original in settings so a session that dies without
        /// reaching Deactivate() can put the user's wallpaper back on the next launch.
        /// Never persists an empty path — that's the value that causes #692 in the first place.
        /// </summary>
        private static void PersistOriginal(string? path)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                var value = string.IsNullOrWhiteSpace(path) ? "" : path!;
                if (s.WallpaperOriginalPath == value) return;

                s.WallpaperOriginalPath = value;
                // SaveImmediate, NOT Save(): the debounced path waits 500ms for quiet, and a kill
                // inside that window leaves no breadcrumb at all — the next launch then captures
                // OUR wallpaper as "the original" and the user's is stranded for good. That is #692
                // again, just through a narrower door. This writes maybe twice a session; the
                // debounce buys nothing here and costs the whole self-heal.
                App.Settings?.SaveImmediate();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Wallpaper] Could not persist the original wallpaper path");
            }
        }

        /// <summary>
        /// Put back a wallpaper a previous session captured but never restored (crash / kill).
        /// Clears the breadcrumb either way once we've had our go at it.
        /// </summary>
        private static void RestoreStaleOriginal()
        {
            var s = App.Settings?.Current;
            var stale = s?.WallpaperOriginalPath;
            if (s == null || string.IsNullOrWhiteSpace(stale)) return;

            if (File.Exists(stale) && SetWallpaper(stale))
                App.Logger?.Information("[Wallpaper] Previous session never restored — put {File} back", Path.GetFileName(stale));
            else
                App.Logger?.Warning("[Wallpaper] Stale original {Path} could not be restored (gone or refused)", stale);

            s.WallpaperOriginalPath = "";
            App.Settings?.Save();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Order matters: Deactivate puts the user's own wallpaper back on the desktop, and
            // only then is it safe to delete the temp file the desktop was pointing at.
            Deactivate();
            ReleaseRemotePool();
        }
    }
}
