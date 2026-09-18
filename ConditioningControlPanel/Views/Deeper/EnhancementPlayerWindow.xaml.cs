using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Microsoft.Win32;

namespace ConditioningControlPanel.Views.Deeper
{
    /// <summary>
    /// End-user runtime UI for Deeper enhancements.
    ///
    /// Pick an audio file → optionally pick (or auto-discover) a matching
    /// .ccpenh.json → press play. The host service binds the engine to the
    /// audio player's time source while playing, unbinds on stop. Any failure
    /// path falls back to plain audio playback (the engine just doesn't run);
    /// the user always gets to hear their file.
    /// </summary>
    public partial class EnhancementPlayerWindow : Window
    {
        private readonly EnhancementAudioPlayer _player;
        private readonly EnhancementHostService _host;
        private EnhancementAudioPlayerTimeSource? _timeSource;
        private BrowserVideoTimeSource? _videoSource;
        private DispatcherTimer? _uiTimer;
        private float[]? _peaks;
        private bool _suppressVolumeSync;
        private bool _videoBrowserReady;
        // Exactly one file:// URL we just asked the WebView2 to navigate to from
        // a user-picked local video. NavigationStarting allows this URL once,
        // then clears it. Any other file:// nav (e.g. hostile redirect from a
        // shared .ccpenh.json) is rejected by the host allowlist.
        private string? _initialAllowedFileUrl;
        // Sticky audio path so the user can press Play again after Stop
        // (the underlying player nulls its CurrentPath on Stop, by design).
        private string? _lastAudioPath;
        private bool _loadInProgress;
        // Set at the top of Window_Closing; async continuations that would start
        // playback or bind the engine must bail when it's true.
        private bool _isClosing;

        // Tracks WHICH branch of TryAutoLoadEnhancement supplied the current
        // enhancement (or whether the user picked it manually). Drives the
        // small "From library / Embedded in media / ..." badge under TxtEnhPath.
        private enum DiscoverySource { Manual, Library, Sidecar, Embedded, Url, PromotedFromEmbedded }
        private DiscoverySource _lastDiscoverySource = DiscoverySource.Manual;
        // Path of the most recently loaded media in the player. Lets the
        // "Create new enhancement..." button hand the editor a pre-linked
        // media so the user starts authoring against the right file.
        private string? _lastMediaPathForCreateNew;
        // The media the player is actually showing right now (audio path,
        // local video path or remote URL). _lastMediaPathForCreateNew is only
        // set by the auto-load ladder, so a library-launched video used to
        // cache its duration under the WRONG key (or none).
        private string? _currentMediaPath;

        // Mission 3: structured event log replaces the flat string collection.
        // Implementation lives in EnhancementPlayerWindow.Mission3.cs.

        // Fullscreen state for the embedded video. Mirrors MainWindow's browser-fullscreen
        // path: when HT (or any HTML5 video) requests fullscreen via the WebView2 API, we
        // pop the WebView out into a borderless topmost window covering the whole monitor.
        private Window? _videoFullscreenWindow;
        private bool _isVideoFullscreen;
        private bool _fsTransitionInFlight;
        // An exit request that landed while a fullscreen transition was in flight.
        // Enter/Exit pump the dispatcher (Invoke at Render priority), so a teardown
        // can arrive mid-swap; without this it was dropped and the borderless host
        // stayed up forever.
        private bool _fsExitPending;
        private bool _isPlayerDualMonitorActive;

        public EnhancementPlayerWindow(EnhancementAudioPlayer player, EnhancementHostService host)
        {
            InitializeComponent();
            WindowChromeHelper.ApplyDarkTitleBar(this);
            WindowChromeHelper.RestoreOwnerOnClose(this);
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _host = host ?? throw new ArgumentNullException(nameof(host));

            _player.Loaded += OnPlayerLoaded;
            _player.Ended += OnPlayerEnded;
            _host.Loaded += OnHostLoaded;
            _host.LoadFailed += OnHostLoadFailed;
            _host.ActionLogged += OnHostActionLogged;
            // Mission 3: Diagnostic events now route to a separate handler
            // so the event log can categorize them as Engine (vs Action).
            _host.Diagnostic += OnHostDiagnostic;

            InitializeMission3();

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            var settings = App.Settings?.Current;
            _player.Volume = settings?.DeeperPlayerVolume ?? EnhancementAudioPlayer.DefaultVolume;
            UpdateVolumeFromPlayer();
            RestoreWindowBounds(settings);
            SubscribeWebcamStateForButton();
            _instance = this;
        }

        // -- Single instance -----------------------------------------------------
        // Every launch site shares App.DeeperPlayer / App.DeeperHost, and closing
        // ANY player window stops the player and unbinds the engine (Window_Closing),
        // so two open players killed each other's playback. One window, reused.

        private static EnhancementPlayerWindow? _instance;

        /// <summary>The open player window, or null when none is up.</summary>
        public static EnhancementPlayerWindow? Current
            => _instance != null && !_instance._isClosing ? _instance : null;

        /// <summary>
        /// Show the player (creating it if needed), bring it to the front, then run
        /// <paramref name="load"/> against it. The owner is only applied to a NEW
        /// window; an already-open player keeps whoever owned it first.
        /// </summary>
        public static EnhancementPlayerWindow ShowOrActivate(Window? owner, Action<EnhancementPlayerWindow>? load = null)
        {
            var win = Current;
            if (win == null)
            {
                win = new EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost);
                if (owner != null) win.Owner = owner;
                win.Show();
            }
            else
            {
                try
                {
                    if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                    win.Activate();
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
            load?.Invoke(win);
            return win;
        }

        /// <summary>
        /// Load an in-memory enhancement (editor Preview, catalogue, tutorial). The
        /// host fires Loaded which routes through UpdateHostUi and the right media
        /// loader. Defers until the window's controls exist.
        /// </summary>
        public void LoadEnhancementFromMemory(Enhancement enhancement, string sourceTag)
        {
            if (enhancement == null) return;
            void Load()
            {
                _lastDiscoverySource = DiscoverySource.Manual;
                _host.LoadFromMemory(enhancement, sourceTag);
            }
            if (IsLoaded) Load();
            else QueueDeferredLoad(Load);
        }

        /// <summary>
        /// Convenience constructor: opens the Player with an in-memory enhancement
        /// pre-loaded (used by the editor's Preview button). For video MediaType
        /// the WebView2 auto-navigates to the enhancement's MediaSource on load.
        /// </summary>
        public EnhancementPlayerWindow(
            EnhancementAudioPlayer player,
            EnhancementHostService host,
            Enhancement enhancement,
            string sourceTag)
            : this(player, host)
        {
            if (enhancement == null) return;
            // Defer to Loaded so OnHostLoaded's UI-pane swap runs after the
            // window's controls are instantiated. LoadFromMemory fires Loaded
            // synchronously which then dispatches to the UI thread anyway.
            Loaded += (_, _) => _host.LoadFromMemory(enhancement, sourceTag);
        }

        /// <summary>
        /// Public entry for launching the player on an enhancement .ccpenh.json
        /// file (e.g. the hub library row's ▶ button). The host fires Loaded
        /// → OnHostLoaded → UpdateHostUi which then routes through the right
        /// media loader (LoadVideoUrlAsync for remote URLs, LoadLocalVideoAsync
        /// for local video files, BindEngineIfReady for audio) based on the
        /// enhancement's MediaType + MediaSource. Defers via Loaded if the
        /// window hasn't fully initialized.
        /// </summary>
        public void LoadEnhancementFile(string ccpenhJsonPath)
        {
            if (string.IsNullOrWhiteSpace(ccpenhJsonPath)) return;
            void Load()
            {
                _lastDiscoverySource = DiscoverySource.Library;
                _host.LoadFromFile(ccpenhJsonPath);
            }
            if (IsLoaded) Load();
            else QueueDeferredLoad(Load);
        }

        /// <summary>
        /// Public entry for external launchers (Windows file association,
        /// MainWindow drag-drop dispatch). Mirrors BtnPickAudio_Click's
        /// dispatch logic but takes a path directly. Defers via Loaded if the
        /// window hasn't finished initializing yet — LoadLocalVideoAsync
        /// touches WebView2 and TxtVideoStatus, both of which require the
        /// XAML to be live.
        /// </summary>
        public void OpenLocalMediaFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            void Load()
            {
                if (IsLocalVideoFile(path))
                    _ = LoadLocalVideoAsync(path);
                else
                    LoadAudio(path);
                TryAutoLoadEnhancement(path);
            }

            if (IsLoaded) Load();
            else QueueDeferredLoad(Load);
        }

        // Caller may invoke LoadEnhancementFile / OpenLocalMediaFile more than
        // once before the window's Loaded event fires (e.g. multiple files
        // dispatched in quick succession from the Windows file association).
        // Holding a single pending action means the last call wins and we only
        // subscribe to Loaded once, avoiding both duplicate loads and lambda
        // accumulation on the Loaded event.
        private Action? _pendingDeferredLoad;
        private bool _deferredLoadSubscribed;

        private void QueueDeferredLoad(Action action)
        {
            _pendingDeferredLoad = action;
            if (_deferredLoadSubscribed) return;
            _deferredLoadSubscribed = true;
            Loaded += OnDeferredLoadReady;
        }

        private void OnDeferredLoadReady(object sender, RoutedEventArgs e)
        {
            Loaded -= OnDeferredLoadReady;
            _deferredLoadSubscribed = false;
            var action = _pendingDeferredLoad;
            _pendingDeferredLoad = null;
            try { action?.Invoke(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "EnhancementPlayer: deferred load failed"); }
        }

        // -- Drag & drop -------------------------------------------------------
        // Accepts media (audio/video) and *.ccpenh.json files dropped onto the
        // window. Media routes through OpenLocalMediaFile (which also runs
        // TryAutoLoadEnhancement to pick up sidecar / library / embedded
        // companions). Enhancement files route through LoadEnhancementFile.
        // WebView2's HwndHost area eats drops over the video preview, but the
        // chrome/title-bar/event-log areas still accept them.

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                e.Effects = DragDropEffects.None;
                if (e.Data.GetDataPresent(DataFormats.FileDrop)
                    && e.Data.GetData(DataFormats.FileDrop) is string[] files
                    && files.Any(IsDroppablePlayerPath))
                {
                    e.Effects = DragDropEffects.Copy;
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
                e.Handled = true;

                // Enhancement (.ccpenh.json) wins over raw media so a drop
                // containing both a media file and its sidecar opens the
                // project; OnHostLoaded will then load the media via the
                // enhancement's MediaSource.
                var enhPath = files.FirstOrDefault(IsEnhancementJsonPath);
                if (!string.IsNullOrEmpty(enhPath))
                {
                    _lastDiscoverySource = DiscoverySource.Manual;
                    LoadEnhancementFile(enhPath);
                    return;
                }
                var mediaPath = files.FirstOrDefault(IsLocalMediaFile);
                if (!string.IsNullOrEmpty(mediaPath))
                {
                    OpenLocalMediaFile(mediaPath);
                    return;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: drop handler failed");
            }
        }

        private static bool IsDroppablePlayerPath(string path)
            => IsLocalMediaFile(path) || IsEnhancementJsonPath(path);

        private static bool IsLocalMediaFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg"
                       or ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi" or ".m4v";
        }

        private static bool IsEnhancementJsonPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase);
        }

        // -- File pickers ------------------------------------------------------

        private void BtnPickAudio_Click(object sender, RoutedEventArgs e)
        {
            // Method name is historical - the picker now accepts both audio
            // and video. Dispatch on file extension below.
            var dlg = new OpenFileDialog
            {
                Title = Loc.Get("deeper_player_pick_media"),
                Filter =
                    "Media (audio + video)|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.mp4;*.webm;*.mkv;*.mov;*.avi;*.m4v"
                    + "|Audio (*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg)|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg"
                    + "|Video (*.mp4;*.webm;*.mkv;*.mov;*.avi;*.m4v)|*.mp4;*.webm;*.mkv;*.mov;*.avi;*.m4v"
                    + "|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            var path = dlg.FileName;
            if (IsLocalVideoFile(path))
            {
                _ = LoadLocalVideoAsync(path);
            }
            else
            {
                LoadAudio(path);
            }
            TryAutoLoadEnhancement(path);
        }

        // Canonical definition lives in EnhancementResolver so the player and
        // the video bridge agree on what counts as a local video file.
        private static bool IsLocalVideoFile(string path) => EnhancementResolver.IsLocalVideoFile(path);

        // Local video playback path: navigate the WebView2 directly to the
        // file:// URL of the chosen file. Edge's built-in media viewer renders
        // a <video> element, which BrowserVideoTimeSource's existing JS bridge
        // already knows how to drive (querySelector('video') + currentTime).
        // Mirrors LoadVideoUrlAsync but for local paths instead of remote URLs.
        private async Task LoadLocalVideoAsync(string path)
        {
            try
            {
                UnbindEngineIfRunning();
                _player.Stop();
                _currentMediaPath = path;
                EnsureUiTimerRunning();

                ShowMediaPaneFor(MediaTypes.Video);
                TxtVideoStatus.Text = Loc.Get("deeper_player_video_loading");
                TxtVideoStatus.Visibility = Visibility.Visible;

                if (!await EnsureVideoBrowserReadyAsync())
                {
                    TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                    return;
                }
                if (_isClosing) return; // closed during WebView2 init — don't navigate a disposed browser

                // file:/// URL navigation - WebView2 wraps a local media file in
                // its default media-viewer page with a real <video> element.
                // Authorize this exact file:// URL through NavigationStarting once.
                var fileUri = new Uri(path).AbsoluteUri;
                _initialAllowedFileUrl = fileUri;
                BindVideoSource();
                VideoBrowser.CoreWebView2.Navigate(fileUri);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: local video load failed");
                TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
            }
        }

        private void BtnPickEnhancement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = Loc.Get("deeper_player_pick_enh"),
                Filter = "Deeper Enhancement (*.ccpenh.json)|*.ccpenh.json|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            _lastDiscoverySource = DiscoverySource.Manual;
            _host.LoadFromFile(dlg.FileName);
        }

        private void BtnUnloadEnhancement_Click(object sender, RoutedEventArgs e) => _host.Unload();

        private async void BtnLoadUrl_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new UrlPromptDialog { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.Result)) return;

            TxtStatus.Text = Loc.Get("deeper_player_status_fetching_url");
            try
            {
                var enh = await App.DeeperFetcher.FetchAsync(dlg.Result).ConfigureAwait(true);
                if (enh == null)
                {
                    TxtStatus.Text = Loc.Get("deeper_player_status_url_failed");
                    return;
                }
                _lastDiscoverySource = DiscoverySource.Url;
                _host.LoadFromMemory(enh, dlg.Result);
                TxtStatus.Text = Loc.Get("deeper_player_status_url_loaded");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Player URL load error: {Error}", ex.Message);
                TxtStatus.Text = Loc.Get("deeper_player_status_url_failed");
            }
        }

        private void TryAutoLoadEnhancement(string mediaPath)
        {
            // 0) Embedded metadata bundled into the media file itself
            //    (the export-as-media flow from the editor). Checked FIRST so
            //    a self-contained file beats any stale sidecar that happens
            //    to share a basename in the same folder.
            // 1) Side-by-side: foo.mp3 → foo.ccpenh.json next to it.
            // 2) Library lookup by media_source pattern (Phase 10).
            // Track the discovery branch so OnHostLoaded can show the right
            // "from library / embedded / ..." badge under TxtEnhPath.
            _lastMediaPathForCreateNew = mediaPath;
            BtnCreateNewEnhancement.Visibility = Visibility.Collapsed;
            ClearHint();
            try
            {
                // Detection ladder (embedded -> sidecar -> library) now lives in
                // the shared EnhancementResolver so the mandatory/asset video
                // bridge resolves identically. The player keeps the tier-specific
                // side effects (library promotion, badge, banner, "create new"
                // fallback). ResolveForLocalMedia may throw on IO errors; the
                // existing outer catch handles that exactly as before (button
                // stays collapsed, no status set).
                var resolved = EnhancementResolver.ResolveForLocalMedia(mediaPath);
                switch (resolved.Source)
                {
                    case EnhancementDiscoverySource.Embedded:
                    {
                        var embedded = resolved.Enhancement!;
                        var mediaType = IsLocalVideoFile(mediaPath)
                            ? Models.Deeper.MediaTypes.Video
                            : Models.Deeper.MediaTypes.Audio;
                        // Auto-promote: if the library doesn't already have a
                        // matching entry for this media, save the embedded JSON
                        // into the user's library so the project survives next time
                        // the file is opened and shows up in the Deeper tab.
                        var existing = App.EnhancementLibrary?.FindMatch(mediaPath, mediaType);
                        if (existing == null)
                        {
                            var saved = App.EnhancementLibrary?.PromoteToLibrary(embedded, mediaPath);
                            if (!string.IsNullOrEmpty(saved))
                            {
                                _lastDiscoverySource = DiscoverySource.PromotedFromEmbedded;
                                _host.LoadFromFile(saved!);
                                ShowPromotedBanner(Path.GetFileName(saved!));
                                return;
                            }
                            // Promotion failed (e.g. read-only library) — fall back
                            // to the original in-memory load so the user still gets
                            // their enhancement this session.
                        }
                        _lastDiscoverySource = DiscoverySource.Embedded;
                        _host.LoadFromMemory(embedded, "embedded:" + Path.GetFileName(mediaPath));
                        return;
                    }

                    case EnhancementDiscoverySource.Sidecar:
                        _lastDiscoverySource = DiscoverySource.Sidecar;
                        _host.LoadFromFile(resolved.FilePath!);
                        return;

                    case EnhancementDiscoverySource.Library:
                        _lastDiscoverySource = DiscoverySource.Library;
                        _host.LoadFromFile(resolved.FilePath!);
                        return;

                    default:
                        // Nothing found. Surface the "Create new enhancement..."
                        // button so the user can author against this media.
                        BtnCreateNewEnhancement.Visibility = Visibility.Visible;
                        // Sticky: LoadAudio is async void and writes "Playing." into
                        // TxtStatus when its waveform await resumes, which used to
                        // wipe this line within a second. The hint has its own element.
                        ShowHint(Loc.Get("deeper_player_no_enh_for_media"), autoClearSeconds: null);
                        return;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: auto-load enhancement failed: {Error}", ex.Message);
            }
        }

        // Non-modal toast in the status bar, auto-clears after ~6s.
        private void ShowPromotedBanner(string filename)
        {
            ShowHint(string.Format(Loc.Get("deeper_player_promoted_to_library_fmt"), filename), autoClearSeconds: 6);
        }

        // -- Sticky hint (own element next to TxtStatus) ------------------------
        private DispatcherTimer? _promotedClearTimer;

        private void ShowHint(string text, double? autoClearSeconds)
        {
            try
            {
                _promotedClearTimer?.Stop();
                _promotedClearTimer = null;
                if (TxtHint == null) return;
                TxtHint.Text = text;
                TxtHint.Visibility = Visibility.Visible;
                if (autoClearSeconds is double secs && secs > 0)
                {
                    _promotedClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
                    _promotedClearTimer.Tick += (_, _) => ClearHint();
                    _promotedClearTimer.Start();
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void ClearHint()
        {
            try
            {
                _promotedClearTimer?.Stop();
                _promotedClearTimer = null;
                if (TxtHint == null) return;
                TxtHint.Text = "";
                TxtHint.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void BtnCreateNewEnhancement_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastMediaPathForCreateNew)) return;
            try
            {
                var mediaType = IsLocalVideoFile(_lastMediaPathForCreateNew)
                    ? Models.Deeper.MediaTypes.Video
                    : Models.Deeper.MediaTypes.Audio;
                var blank = App.EnhancementLibrary?.CreateBlank(mediaType, _lastMediaPathForCreateNew)
                            ?? new Enhancement { MediaType = mediaType, MediaSource = _lastMediaPathForCreateNew };
                var editor = new DeeperEditorWindow(blank, null) { Owner = this };
                editor.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: open editor for new enhancement failed");
            }
        }

        // -- Audio loading -----------------------------------------------------

        private async void LoadAudio(string path)
        {
            // Re-entrancy guard: BtnPlayPause's "replay-after-stop" branch
            // calls LoadAudio synchronously from the click handler, and a
            // second click while LoadWaveformAsync is awaiting would race a
            // second _player.Stop() / Play() pair against the first, which
            // can leave NAudio's WaveOutEvent in an undefined state.
            if (_loadInProgress) return;
            _loadInProgress = true;
            try
            {
                // Stop any in-flight playback so the new file replaces it cleanly.
                UnbindEngineIfRunning();
                _player.Stop();
                _lastAudioPath = path;
                _currentMediaPath = path;
                EnsureUiTimerRunning();

                TxtAudioPath.Text = path;
                TxtStatus.Text = Loc.Get("deeper_player_status_loading_audio");
                await LoadWaveformAsync(path);

                // The window may have closed during the (potentially seconds-long)
                // waveform decode. Its Closing teardown already ran — Play + Bind
                // here would start an engine with no owner, no Ended handler, and
                // no way to ever stop (audio and effects run to app exit).
                if (_isClosing) return;

                if (!_player.Play(path))
                {
                    TxtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
                    return;
                }

                TxtTotal.Text = FormatTime(_player.DurationMs / 1000.0);
                BtnPlayPause.Content = "⏸";
                BindEngineIfReady();
                TxtStatus.Text = Loc.Get("deeper_player_status_playing");
            }
            finally
            {
                _loadInProgress = false;
            }
        }

        private async Task LoadWaveformAsync(string path)
        {
            _peaks = null;
            try
            {
                var data = await AudioWaveformCache.LoadAsync(path);
                _peaks = data.Peaks;
                RenderWaveform();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: waveform decode failed: {Error}", ex.Message);
                _peaks = null;
                WaveformPath.Data = null;
            }
        }

        // -- Transport ---------------------------------------------------------

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

        private void TogglePlayPause()
        {
            EnsureUiTimerRunning();
            // Video mode: drive the WebView2's <video> via JS bridge.
            if (_videoSource != null)
            {
                if (_videoSource.IsPlaying)
                {
                    _videoSource.Pause();
                    BtnPlayPause.Content = "▶";
                }
                else
                {
                    MaybePromptForWebcamBeforePlay();
                    // Stop unbinds the engine (so dispatched one-shots die with
                    // it); re-bind before the video rolls again.
                    EnsureVideoEngineBound();
                    _videoSource.Play();
                    BtnPlayPause.Content = "⏸";
                }
                return;
            }

            if (_player.IsPlaying)
            {
                _player.Pause();
                BtnPlayPause.Content = "▶";
            }
            else if (_player.IsPaused)
            {
                MaybePromptForWebcamBeforePlay();
                _player.Resume();
                BtnPlayPause.Content = "⏸";
            }
            else if (!string.IsNullOrEmpty(_player.CurrentPath))
            {
                MaybePromptForWebcamBeforePlay();
                _player.Play(_player.CurrentPath);
                BtnPlayPause.Content = "⏸";
                BindEngineIfReady();
            }
            else if (!string.IsNullOrEmpty(_lastAudioPath))
            {
                // Resume after Stop: the underlying player cleared its handle,
                // but we kept the path so the user can hit Play to start over.
                MaybePromptForWebcamBeforePlay();
                LoadAudio(_lastAudioPath);
            }
            else
            {
                // Nothing loaded — point users at the dedicated pickers in the
                // header rather than ambushing them with a file dialog. The
                // earlier fallback popped OpenFileDialog on every empty-state
                // Play click, which was indistinguishable from a misclick on
                // the wrong button.
                TxtStatus.Text = Loc.Get("deeper_player_status_pick_first");
            }
        }

        // Suppression flag — true once we've asked about webcam for the
        // currently-loaded enhancement, so we don't badger the user every
        // play/pause/resume. Reset in UpdateHostUi when a new enhancement
        // becomes the loaded one.
        private bool _webcamPromptShownForCurrentEnh;

        // Offer to start webcam tracking if the loaded enhancement has
        // webcam-driven rules and the webcam isn't already running. Inline,
        // non-blocking banner (was a modal YesNo MessageBox): playback proceeds
        // regardless, the banner just decides whether the webcam-gated rules
        // will actually fire.
        private void MaybePromptForWebcamBeforePlay()
        {
            if (_webcamPromptShownForCurrentEnh) return;
            var enh = _host?.LoadedEnhancement;
            if (enh == null) return;
            if (!EnhancementNeedsWebcam(enh)) return;
            var svc = App.Webcam;
            if (svc == null || svc.IsRunning)
            {
                _webcamPromptShownForCurrentEnh = true;
                return;
            }
            _webcamPromptShownForCurrentEnh = true;
            try
            {
                TxtWebcamBanner.Text = Loc.Get("deeper_player_webcam_banner_text");
                BtnWebcamBannerEnable.Visibility = Visibility.Visible;
                WebcamBanner.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void BtnWebcamBannerDismiss_Click(object sender, RoutedEventArgs e)
            => WebcamBanner.Visibility = Visibility.Collapsed;

        private async void BtnWebcamBannerEnable_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null) { WebcamBanner.Visibility = Visibility.Collapsed; return; }
            try
            {
                if (!WebcamTrackingService.IsConsentCurrent())
                {
                    // Consent + calibration live in the Lab tab, not here. Say so
                    // in the banner itself instead of popping another modal.
                    TxtWebcamBanner.Text = Loc.Get("deeper_player_eye_tracking_first_time");
                    BtnWebcamBannerEnable.Visibility = Visibility.Collapsed;
                    return;
                }
                BtnWebcamBannerEnable.IsEnabled = false;
                // Off the UI thread: Start() opens the camera + loads ONNX models
                // and can block for seconds.
                if (await svc.StartAsync())
                {
                    // Remember that THIS player session started the webcam so
                    // Window_Closing can put it back the way it found it.
                    _playerStartedWebcam = true;
                    WebcamBanner.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtWebcamBanner.Text = string.Format(Loc.Get("deeper_player_eye_tracking_start_failed_fmt"), svc.State);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: webcam start from banner failed");
            }
            finally
            {
                BtnWebcamBannerEnable.IsEnabled = true;
            }
        }

        // True when MaybePromptForWebcamBeforePlay actually started the camera
        // in this player session. Used by Window_Closing to stop it on exit.
        private bool _playerStartedWebcam;

        // Webcam dependency check. Delegates to the shared
        // EnhancementCapabilities.NeedsWebcam so the player detects webcam rules
        // identically to the browser hub and the mandatory-video nudge. The old
        // copy here scanned only enh.Rules; new-editor files store webcam
        // triggers only in TimelineItems (the save back-projection to Rules was
        // removed), so the Rules-only scan silently missed them and skipped the
        // pre-play webcam prompt.
        private static bool EnhancementNeedsWebcam(Models.Deeper.Enhancement enh)
        {
            return EnhancementCapabilities.NeedsWebcam(enh);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_videoSource != null)
            {
                // Mirror the audio path: unbind so dispatched one-shots (haptics,
                // flash, subliminal) stop with the video instead of running out.
                // Detach stops the source's poll timer; TogglePlayPause re-binds.
                UnbindEngineIfRunning();
                try { _videoSource.Pause(); _videoSource.Seek(0); } catch (Exception ex) { Diag.Swallowed(ex); }
                BtnPlayPause.Content = "▶";
                TxtCurrent.Text = "0:00";
                TxtStatus.Text = Loc.Get("deeper_player_status_stopped");
                return;
            }

            UnbindEngineIfRunning();
            _player.Stop();
            BtnPlayPause.Content = "▶";
            TxtCurrent.Text = "0:00";
            UpdatePlayhead(0);
            TxtStatus.Text = Loc.Get("deeper_player_status_stopped");
        }

        private Action<WebcamTrackingState>? _onWebcamStateChanged;

        private async void BtnEyeTracking_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                MessageBox.Show(this,
                    Loc.Get("deeper_player_eye_tracking_unavailable"),
                    Loc.Get("deeper_player_btn_eye_tracking_start"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (svc.IsRunning)
            {
                // Off the UI thread: the teardown joins the capture thread for up
                // to 5s and then disposes the capture graph and the ONNX sessions
                // (BUG-BRR252E2RM - the panel looked hung after turning the camera
                // off, and had to be killed from Task Manager).
                try { await svc.StopAsync(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "EnhancementPlayer: webcam StopAsync threw"); }
                return;
            }

            // First time using webcam tracking: send the user to the Lab tab to
            // read the privacy explanation and run first-time setup before we
            // turn the camera on from inside the player. The consent flag is
            // flipped server-side by WebcamConsentDialog (in the Lab), so once
            // that's done future clicks bypass this branch.
            if (!WebcamTrackingService.IsConsentCurrent())
            {
                MessageBox.Show(this,
                    Loc.Get("deeper_player_eye_tracking_first_time"),
                    Loc.Get("deeper_player_btn_eye_tracking_start"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Consent already on file: a single confirmation, then start.
            var confirm = MessageBox.Show(this,
                Loc.Get("deeper_player_eye_tracking_confirm_start"),
                Loc.Get("deeper_player_btn_eye_tracking_start"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                if (!await svc.StartAsync())
                {
                    MessageBox.Show(this,
                        string.Format(Loc.Get("deeper_player_eye_tracking_start_failed_fmt"), svc.State),
                        Loc.Get("deeper_player_btn_eye_tracking_start"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: eye tracking start failed");
                MessageBox.Show(this,
                    string.Format(Loc.Get("deeper_player_eye_tracking_start_failed_fmt"), ex.Message),
                    Loc.Get("deeper_player_btn_eye_tracking_start"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ±10% zoom on the embedded browser. Uses WebView2.ZoomFactor, which
        // proxies to CoreWebView2's zoom level — same effect as Ctrl+/Ctrl- in
        // Edge. Clamped to keep the user from zooming themselves into a stuck
        // state (extreme out makes the page unreadable, extreme in eats RAM).
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => AdjustVideoZoom(+0.10);
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => AdjustVideoZoom(-0.10);

        private void AdjustVideoZoom(double delta)
        {
            try
            {
                if (VideoBrowser?.CoreWebView2 == null) return;
                var next = Math.Clamp(VideoBrowser.ZoomFactor + delta, 0.25, 5.0);
                VideoBrowser.ZoomFactor = next;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: zoom adjust failed: {Error}", ex.Message);
            }
        }

        // Picture-in-Picture toggle. Chromium maintains a single PiP window
        // app-wide, so calling requestPictureInPicture when one is already
        // open just moves focus; exitPictureInPicture closes it. We pick the
        // largest <video> on the page so ad/preroll iframes don't grab PiP
        // ahead of the real player.
        private async void BtnPictureInPicture_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (VideoBrowser?.CoreWebView2 == null) return;
                await VideoBrowser.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            if (document.pictureInPictureElement) {
                                document.exitPictureInPicture();
                                return;
                            }
                            var vids = document.querySelectorAll('video');
                            var best = null, bestArea = 0;
                            for (var i = 0; i < vids.length; i++) {
                                var r = vids[i].getBoundingClientRect();
                                var a = r.width * r.height;
                                if (a > bestArea) { best = vids[i]; bestArea = a; }
                            }
                            if (best && best.requestPictureInPicture) {
                                best.requestPictureInPicture();
                            }
                        } catch (_) {}
                    })();
                ");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: PiP toggle failed: {Error}", ex.Message);
            }
        }

        private void SubscribeWebcamStateForButton()
        {
            var svc = App.Webcam;
            if (svc == null || _onWebcamStateChanged != null) return;
            _onWebcamStateChanged = state => UpdateEyeTrackingButton(state);
            svc.OnTrackingStateChanged += _onWebcamStateChanged;
            UpdateEyeTrackingButton(svc.State);
        }

        private void UnsubscribeWebcamStateForButton()
        {
            var svc = App.Webcam;
            if (svc == null || _onWebcamStateChanged == null) return;
            try { svc.OnTrackingStateChanged -= _onWebcamStateChanged; } catch (Exception ex) { Diag.Swallowed(ex); }
            _onWebcamStateChanged = null;
        }

        private void UpdateEyeTrackingButton(WebcamTrackingState state)
        {
            // OnTrackingStateChanged fires on the dispatcher thread already, but
            // dispatch defensively in case a future change moves it.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => UpdateEyeTrackingButton(state));
                return;
            }
            if (BtnEyeTracking == null) return;
            bool running = state == WebcamTrackingState.Tracking || state == WebcamTrackingState.FaceLost
                        || state == WebcamTrackingState.Starting;
            BtnEyeTracking.Content = Loc.Get(running
                ? "deeper_player_btn_eye_tracking_stop"
                : "deeper_player_btn_eye_tracking_start");
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressVolumeSync) return;
            // Fires during InitializeComponent when XAML applies Value="80",
            // which is before the constructor reaches the _player assignment.
            if (_player == null) return;
            var v = (int)Math.Round(e.NewValue);
            _player.Volume = v;
            _videoSource?.SetVolume(v / 100.0);
            // A hand-dragged slider ends a mute; a muted 0 is never persisted.
            if (!_muteChanging) _volumeBeforeMute = null;
            if (_volumeBeforeMute == null) PersistVolume(v);
        }

        private static void PersistVolume(int volume)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null || s.DeeperPlayerVolume == volume) return;
                s.DeeperPlayerVolume = volume;
                App.Settings?.Save(); // debounced 500 ms inside SettingsService
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void UpdateVolumeFromPlayer()
        {
            try
            {
                _suppressVolumeSync = true;
                SliderVolume.Value = Math.Clamp(_player.Volume, 0, 100);
            }
            finally { _suppressVolumeSync = false; }
        }

        // -- UI tick (transport label, playhead) -------------------------------

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            // Suspend playhead writes while the user is dragging the mini-timeline scrubber
            // (Mission3.cs), otherwise the tick fights the drag. _miniScrubbing is the live
            // flag; the old _isScrubbing was never assigned.
            if (_miniScrubbing) return;
            // Reparent in progress — touching VideoBrowser now would race with
            // the WebView2 swap and can throw against an unattached browser.
            if (_fsTransitionInFlight) return;
            if (VideoBrowser != null
                && VideoBrowser.Parent == null
                && VisualTreeHelper.GetParent(VideoBrowser) == null)
            {
                return;
            }

            if (_videoSource != null)
            {
                var t = _videoSource.GetCurrentTimeSeconds();
                var d = _videoSource.GetDurationSeconds();
                TxtCurrent.Text = FormatTime(t);
                if (d > 0)
                {
                    TxtTotal.Text = FormatTime(d);
                    RememberDurationOnce(_currentMediaPath, d);
                }
                BtnPlayPause.Content = _videoSource.IsPlaying ? "⏸" : "▶";
            }
            else
            {
                var ms = _player.CurrentTimeMs;
                TxtCurrent.Text = FormatTime(ms / 1000.0);
                UpdatePlayhead(_player.DurationMs > 0 ? (double)ms / _player.DurationMs : 0);
                if (_player.DurationMs > 0) RememberDurationOnce(_lastAudioPath, _player.DurationMs / 1000.0);
            }

            // Mission 3 mini-timeline + overlay + status pill updates.
            UpdateMiniPlayheadX();
            UpdateMiniTimelineReadout();
            RefreshNowRegionOverlay();
            UpdateStatusPill();

            // Nothing loaded, nothing bound: stop ticking. Every load path and
            // the transport call EnsureUiTimerRunning to wake it back up.
            if (IsPlayerIdle()) _uiTimer?.Stop();
        }

        private bool IsPlayerIdle()
            => _videoSource == null
               && _host.LoadedEnhancement == null
               && !_player.IsPlaying && !_player.IsPaused
               && string.IsNullOrEmpty(_player.CurrentPath);

        private void EnsureUiTimerRunning()
        {
            try { if (_uiTimer != null && !_uiTimer.IsEnabled && !_isClosing) _uiTimer.Start(); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void UpdateMiniTimelineReadout()
        {
            try
            {
                if (TxtMiniTimelineReadout == null) return;
                double curSec = _videoSource != null
                    ? _videoSource.GetCurrentTimeSeconds()
                    : _player.CurrentTimeMs / 1000.0;
                double totalSec = _videoSource != null
                    ? _videoSource.GetDurationSeconds()
                    : _player.DurationMs / 1000.0;
                if (totalSec <= 0) totalSec = _miniTotalSeconds;
                TxtMiniTimelineReadout.Text = $"{FormatTime(curSec)} / {FormatTime(totalSec)}";
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        // -- Waveform render + scrub ------------------------------------------

        private void RenderWaveform()
        {
            if (_peaks == null || _peaks.Length == 0)
            {
                WaveformPath.Data = null;
                return;
            }

            var w = WaveformCanvas.ActualWidth;
            var h = WaveformCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var midY = h / 2.0;
            var amp = (h - 4) / 2.0;
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                int samples = Math.Min(_peaks.Length, Math.Max(64, (int)w));
                for (int i = 0; i < samples; i++)
                {
                    var x = (double)i / (samples - 1) * w;
                    var idx = (int)Math.Round((double)i / (samples - 1) * (_peaks.Length - 1));
                    var v = Math.Clamp(_peaks[idx], 0f, 1f);
                    ctx.BeginFigure(new Point(x, midY - v * amp), false, false);
                    ctx.LineTo(new Point(x, midY + v * amp), true, false);
                }
            }
            geom.Freeze();
            WaveformPath.Data = geom;
        }

        private void UpdatePlayhead(double frac)
        {
            var w = WaveformCanvas.ActualWidth;
            var h = WaveformCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var x = Math.Clamp(frac, 0, 1) * w;
            PlayheadLine.X1 = PlayheadLine.X2 = x;
            PlayheadLine.Y1 = 0;
            PlayheadLine.Y2 = h;
        }

        private void WaveformCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var w = WaveformCanvas.ActualWidth;
            if (w <= 0 || _player.DurationMs <= 0) return;
            var frac = Math.Clamp(e.GetPosition(WaveformCanvas).X / w, 0, 1);
            _player.Seek(frac * _player.DurationMs / 1000.0);
            UpdatePlayhead(frac);
        }

        // -- Engine binding ---------------------------------------------------

        private void BindEngineIfReady()
        {
            if (_host.LoadedEnhancement == null) return;
            // Don't double-bind: if we're already running on the video source,
            // the audio path shouldn't take over.
            if (_videoSource != null) return;
            // Audio host: build a fresh time source and let the host own its
            // attach/detach lifetime.
            _timeSource = new EnhancementAudioPlayerTimeSource(_player);
            _host.Bind(_timeSource,
                attach: () => _timeSource?.Attach(),
                detach: () => { _timeSource?.Detach(); _timeSource = null; });
        }

        private void UnbindEngineIfRunning()
        {
            if (!_host.IsRunning) return;
            _host.UnbindEngine();
        }

        // -- Player + host event handlers -------------------------------------

        private void OnPlayerLoaded(string path) { /* status updated in LoadAudio */ }

        private void OnPlayerEnded()
        {
            try
            {
                if (Dispatcher.CheckAccess()) HandlePlayerEnded();
                else Dispatcher.BeginInvoke(HandlePlayerEnded);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: OnPlayerEnded marshal failed: {Error}", ex.Message);
            }
        }

        private void HandlePlayerEnded()
        {
            UnbindEngineIfRunning();
            BtnPlayPause.Content = "▶";
            TxtStatus.Text = Loc.Get("deeper_player_status_ended");
        }

        private void OnHostLoaded(Models.Deeper.Enhancement? enh, string? path)
        {
            try
            {
                if (Dispatcher.CheckAccess()) UpdateHostUi(enh, path);
                else Dispatcher.BeginInvoke(() => UpdateHostUi(enh, path));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: OnHostLoaded marshal failed: {Error}", ex.Message);
            }
        }

        private void UpdateHostUi(Models.Deeper.Enhancement? enh, string? path)
        {
            // New enhancement loaded → re-arm the webcam pre-play prompt.
            _webcamPromptShownForCurrentEnh = false;
            WebcamBanner.Visibility = Visibility.Collapsed;
            EnsureUiTimerRunning();

            if (enh == null)
            {
                TxtEnhPath.Text = Loc.Get("deeper_player_no_enh");
                TxtEnhMetadata.Text = "";
                BtnUnloadEnhancement.Visibility = Visibility.Collapsed;
                TxtEnhSource.Visibility = Visibility.Collapsed;
                BtnEyeTracking.Visibility = Visibility.Collapsed;
                Title = Loc.Get("deeper_player_title");
                UnbindEngineIfRunning();
                ShowMediaPaneFor(MediaTypes.Audio); // default back to audio UI
                return;
            }
            ClearHint();
            TxtEnhPath.Text = path ?? "";
            var creator = string.IsNullOrEmpty(enh.Metadata?.Creator) ? "" : $" — {enh.Metadata.Creator}";
            var name = string.IsNullOrEmpty(enh.Metadata?.Name) ? "(untitled)" : enh.Metadata!.Name;
            Title = string.Format(Loc.Get("deeper_player_window_title_fmt"), name);
            // The eye-tracking control only means something when the
            // enhancement has webcam-driven rules.
            BtnEyeTracking.Visibility = EnhancementNeedsWebcam(enh) ? Visibility.Visible : Visibility.Collapsed;
            var counts = $"{enh.Regions.Count} regions, {enh.HapticTracks.Sum(t => t?.Events?.Count ?? 0)} haptic events, {enh.Rules.Count} rules";
            TxtEnhMetadata.Text = $"{name}{creator}  ·  {counts}";
            BtnUnloadEnhancement.Visibility = Visibility.Visible;
            BtnCreateNewEnhancement.Visibility = Visibility.Collapsed;

            // Discovery-source badge
            var key = _lastDiscoverySource switch
            {
                DiscoverySource.Library => "deeper_player_enh_source_library",
                DiscoverySource.Sidecar => "deeper_player_enh_source_sidecar",
                DiscoverySource.Embedded => "deeper_player_enh_source_embedded",
                DiscoverySource.PromotedFromEmbedded => "deeper_player_enh_source_library",
                DiscoverySource.Url => "deeper_player_enh_source_url",
                _ => "deeper_player_enh_source_manual",
            };
            TxtEnhSource.Text = Loc.Get(key);
            TxtEnhSource.Visibility = Visibility.Visible;

            ShowMediaPaneFor(enh.MediaType);
            if (enh.MediaType == MediaTypes.Video && IsRemoteVideoUrl(enh.MediaSource))
            {
                _ = LoadVideoUrlAsync(enh.MediaSource);
            }
            else if (enh.MediaType == MediaTypes.Video
                     && !string.IsNullOrEmpty(enh.MediaSource)
                     && File.Exists(enh.MediaSource))
            {
                // Local-video enhancement: route through the file:// loader so
                // the WebView2 picks up the chosen file. Mirrors the auto-load
                // path the picker uses when the user picks a .mp4 directly.
                _ = LoadLocalVideoAsync(enh.MediaSource);
            }
            else if (_player.IsPlaying)
            {
                // Audio mode: if audio is already playing, attach the engine now.
                BindEngineIfReady();
            }

            // Mission 3: re-skin the file context strip + mini-timeline + status pill.
            RefreshFileContextStrip(enh, path);
            OnEnhancementLoadedForMini(enh);
            UpdateStatusPill();
        }

        // -- Pane swap + video loading ----------------------------------------

        private void ShowMediaPaneFor(string? mediaType)
        {
            var isVideo = string.Equals(mediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase);
            AudioFileRow.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
            AudioPane.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
            VideoPane.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            // Volume stays up in video mode too: the slider drives the page's
            // <video>.volume through BrowserVideoTimeSource.SetVolume.
            VolumePanel.Visibility = Visibility.Visible;
            BtnPictureInPicture.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool IsRemoteVideoUrl(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadVideoUrlAsync(string url)
        {
            try
            {
                // Stop any audio path that might be active (mode swap).
                UnbindEngineIfRunning();
                _player.Stop();
                _currentMediaPath = url;
                EnsureUiTimerRunning();

                TxtVideoStatus.Text = Loc.Get("deeper_player_video_loading");
                TxtVideoStatus.Visibility = Visibility.Visible;

                // Pre-validate the URL against the same allowlist NavigationStarting
                // enforces, so a hostile MediaSource in a shared .ccpenh.json never
                // reaches the WebView2 in the first place.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var initialUri)
                    || initialUri.Scheme != Uri.UriSchemeHttps
                    || !IsAllowedPlayerHost(initialUri))
                {
                    App.Logger?.Warning(
                        "EnhancementPlayer: rejected video MediaSource {Host}", initialUri?.Host);
                    TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                    TxtStatus.Text = Loc.Get("deeper_player_status_host_not_allowed");
                    return;
                }

                if (!await EnsureVideoBrowserReadyAsync())
                {
                    TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                    return;
                }
                if (_isClosing) return; // closed during WebView2 init — don't navigate a disposed browser

                BindVideoSource();
                VideoBrowser.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: video load failed");
                TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
            }
        }

        // Bind the engine to a fresh BrowserVideoTimeSource against the
        // current WebView. Called BEFORE Navigate so the source is live by the
        // time the user sees an interactive page — previously we waited for
        // NavigationCompleted and races between "page interactive" and
        // "NavigationCompleted fires" left the Play button silently dead.
        // The source itself doesn't cache document state; it polls
        // querySelector('video') fresh each tick, so it doesn't care which
        // navigation it was constructed against.
        private void BindVideoSource()
        {
            _videoSource?.Dispose();
            _videoSource = new BrowserVideoTimeSource(VideoBrowser);
            _videoSource.SetVolume(SliderVolume.Value / 100.0);
            EnsureVideoEngineBound();
        }

        // Bind (or re-bind after Stop) the engine to the live video source. With
        // no enhancement loaded there is nothing to bind, but the source still
        // has to poll so the transport readout works.
        private void EnsureVideoEngineBound()
        {
            var src = _videoSource;
            if (src == null || _host.IsRunning) return;
            if (_host.LoadedEnhancement == null)
            {
                try { src.Attach(); } catch (Exception ex) { Diag.Swallowed(ex); }
                return;
            }
            _host.Bind(src,
                attach: () => src.Attach(),
                detach: () => { try { src.Detach(); } catch (Exception ex) { Diag.Swallowed(ex); } });
        }

        // Single hardened WebView2 init for the Player. Mirrors
        // DeeperEditorWindow.InitializeBrowserAsync: separate user-data folder
        // (no cookie sharing with the main browser tab), Settings hardening,
        // and a NavigationStarting allowlist that blocks anything off-list.
        private async Task<bool> EnsureVideoBrowserReadyAsync()
        {
            if (_videoBrowserReady) return VideoBrowser.CoreWebView2 != null;

            // Separate user-data folder from the main browser tab. The Player
            // navigates URLs sourced from .ccpenh.json files that may be shared
            // between users; sharing cookies/storage with the main tab would
            // let a hostile page read the user's signed-in HT cookies via
            // document.cookie / authenticated fetch.
            var userDataFolder = System.IO.Path.Combine(
                App.UserDataPath,
                "browser_data_deeper_player");
            System.IO.Directory.CreateDirectory(userDataFolder);
            // --autoplay-policy=no-user-gesture-required: Chromium otherwise
            // rejects programmatic v.play() calls (the WPF Play button is not
            // a JS user gesture), which manifested as "I click Play and
            // nothing happens" on Editor → Preview → browser-video flows.
            //
            // --disable-direct-composition-video-overlays: in HTML5 fullscreen
            // Chromium promotes the <video> to a DirectComposition hardware
            // overlay (MPO) plane that the GPU scans out ABOVE DWM-composited
            // topmost windows. That hides our Spiral / Pink-filter overlay
            // windows the moment the HT video goes fullscreen (mandatory videos
            // use a WPF MediaElement so DWM composites them normally and the
            // overlays stay on top — only the WebView2 path was affected).
            // Disabling the video overlay plane forces the video back through
            // normal compositing so the overlays render on top again.
            var options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--autoplay-policy=no-user-gesture-required --disable-direct-composition-video-overlays"
            };
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder, options: options)
                .ConfigureAwait(true);
            await VideoBrowser.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            if (VideoBrowser.CoreWebView2 == null) return false;

            // Settings hardening — JS stays on (BrowserVideoTimeSource needs it
            // to drive the <video> element) but every other surface is off.
            var settings = VideoBrowser.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;

            // Fullscreen toggle on dblclick. Belt-and-suspenders for BOTH
            // directions:
            //
            // EXIT (already in fullscreen):
            //   JS-only `document.exitFullscreen()` is unreliable in WebView2
            //   — sometimes the page state doesn't actually clear, and
            //   `ContainsFullScreenElementChanged` never fires, so the
            //   borderless host window stays up forever. So:
            //     1) dblHandler calls exitLoop (retries exitFullscreen up
            //        to 5x in case the first call no-ops).
            //     2) dblHandler posts 'ccp_exit_fullscreen' WebMessage too.
            //     3) C#'s OnVideoWebMessageReceived force-closes the
            //        fullscreen Window directly — independent of page state.
            //   Either the page exits cleanly (event handler closes window)
            //   or it doesn't (WebMessage handler closes window). Both
            //   routes converge on the user being out of fullscreen.
            //
            // ENTER (not yet in fullscreen):
            //   tryEnterFs picks the largest <video> on the page (same
            //   approach BtnPictureInPicture_Click uses, so ad/preroll
            //   iframes don't grab fullscreen ahead of the real player)
            //   and calls requestFullscreen() on it. The HTML5 fullscreen
            //   API requires a user gesture, which dblclick satisfies. The
            //   resulting fullscreenchange → Chromium's
            //   ContainsFullScreenElementChanged → C#'s
            //   OnVideoFullscreenChanged → existing EnterVideoFullscreen
            //   reparent path. No new C# needed.
            //
            // stopImmediatePropagation runs only when we actually act
            // (either exited or found a video to enter on). On non-video
            // pages dblclick flows through untouched.
            await VideoBrowser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                (function() {
                    function inAnyFs() {
                        return !!(document.fullscreenElement || window._ccpForcedFs);
                    }
                    function postExit() {
                        try { window.chrome.webview.postMessage('ccp_exit_fullscreen'); } catch (_) {}
                    }
                    function exitLoop(remaining) {
                        if (remaining <= 0 || !document.fullscreenElement) return;
                        try {
                            var p = document.exitFullscreen ? document.exitFullscreen()
                                  : (document.webkitExitFullscreen ? document.webkitExitFullscreen() : null);
                            if (p && p.then) {
                                p.then(function(){ exitLoop(remaining - 1); },
                                       function(){ setTimeout(function(){ exitLoop(remaining - 1); }, 30); });
                            } else {
                                setTimeout(function(){ exitLoop(remaining - 1); }, 30);
                            }
                        } catch (_) {
                            setTimeout(function(){ exitLoop(remaining - 1); }, 30);
                        }
                    }
                    function tryEnterFs() {
                        try {
                            var vids = document.querySelectorAll('video');
                            if (!vids || vids.length === 0) return false;
                            var best = null, bestArea = 0;
                            for (var i = 0; i < vids.length; i++) {
                                var r = vids[i].getBoundingClientRect();
                                var a = r.width * r.height;
                                if (a > bestArea) { best = vids[i]; bestArea = a; }
                            }
                            var req = best && (best.requestFullscreen
                                            || best.webkitRequestFullscreen);
                            if (!req) return false;
                            var p = req.call(best);
                            if (p && p.catch) p.catch(function(){});
                            return true;
                        } catch (_) { return false; }
                    }
                    function dblHandler(e) {
                        if (inAnyFs()) {
                            if (e) {
                                try { e.stopImmediatePropagation(); } catch (_) {}
                                try { e.preventDefault(); } catch (_) {}
                            }
                            exitLoop(5);
                            postExit();
                        } else {
                            // Enter only if there's actually a video. Without
                            // this guard, dblclick on non-video pages would
                            // swallow the page's own dblclick handler.
                            if (!tryEnterFs()) return;
                            if (e) {
                                try { e.stopImmediatePropagation(); } catch (_) {}
                                try { e.preventDefault(); } catch (_) {}
                            }
                        }
                    }
                    // Only the native dblclick event — not a click-pair
                    // detector, which would also fire on legitimate two-click
                    // sequences (timeline scrub + play, ad dismiss, etc.).
                    document.addEventListener('dblclick', dblHandler, true);
                    document.addEventListener('dblclick', dblHandler, false);
                    window.addEventListener('dblclick', dblHandler, true);
                    function bindOnVideo() {
                        try {
                            var vids = document.querySelectorAll('video');
                            for (var i = 0; i < vids.length; i++) {
                                var v = vids[i];
                                if (v._ccpBound) continue;
                                v._ccpBound = true;
                                v.addEventListener('dblclick', dblHandler, true);
                                v.addEventListener('dblclick', dblHandler, false);
                            }
                        } catch (_) {}
                    }
                    bindOnVideo();
                    setInterval(function() {
                        if (inAnyFs()) bindOnVideo();
                    }, 1000);
                    document.addEventListener('fullscreenchange', function() {
                        if (!document.fullscreenElement && window._ccpForcedFs) {
                            postExit();
                        }
                    });

                    // ESC from inside the page. The borderless host window's
                    // WPF KeyDown handler almost never sees a key while the
                    // WebView2 owns focus (the Chromium HWND eats it), so ESC
                    // was effectively dead once the page had focus. Post the
                    // exit message from the page instead - C# force-closes the
                    // host regardless of page fullscreen state.
                    function escHandler(e) {
                        if (!e) return;
                        var isEsc = e.key === 'Escape' || e.key === 'Esc' || e.keyCode === 27;
                        if (!isEsc) return;
                        if (!inAnyFs()) return;
                        exitLoop(5);
                        postExit();
                    }
                    document.addEventListener('keydown', escHandler, true);
                    window.addEventListener('keydown', escHandler, true);

                    // Keyboard relay. The Chromium HWND owns focus while the page is
                    // up, so the WPF window's PreviewKeyDown never sees a key; post
                    // the ones the player binds (see HandlePlayerKeyDown) back to C#.
                    function isEditable(t) {
                        return !!(t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable));
                    }
                    function keyName(e) {
                        var k = e.key;
                        if (k === ' ' || e.code === 'Space') return 'Space';
                        if (k === 'Escape' || k === 'Esc') return 'Escape';
                        if (k === 'ArrowLeft' || k === 'ArrowRight' || k === 'ArrowUp' || k === 'ArrowDown') return k;
                        if (k === 'm' || k === 'M') return 'M';
                        if (k === 'f' || k === 'F') return 'F';
                        return null;
                    }
                    function relayDown(e) {
                        if (!e || isEditable(e.target)) return;
                        var n = keyName(e);
                        if (!n) return;
                        // F inside the page still has the user gesture, so the HTML5
                        // fullscreen request can succeed here; C# only gets the key
                        // when there was no video to go fullscreen on.
                        if (n === 'F' && !e.repeat && !inAnyFs() && tryEnterFs()) {
                            try { e.preventDefault(); } catch (_) {}
                            return;
                        }
                        try {
                            window.chrome.webview.postMessage('ccp_key:' + n + ':' + (e.shiftKey ? 1 : 0) + ':' + (e.repeat ? 1 : 0));
                        } catch (_) {}
                        if (n !== 'Escape' && document.querySelector('video')) {
                            try { e.preventDefault(); } catch (_) {}
                        }
                    }
                    function relayUp(e) {
                        if (!e || keyName(e) !== 'Escape') return;
                        try { window.chrome.webview.postMessage('ccp_keyup:Escape'); } catch (_) {}
                    }
                    window.addEventListener('keydown', relayDown, true);
                    window.addEventListener('keyup', relayUp, true);

                    // Hide the mouse cursor after ~2 s idle while the forced
                    // fullscreen host is up. Chromium owns the cursor over its
                    // HWND, so this has to happen in the page, not in WPF.
                    var cursorTimer = null;
                    function hideCursor() {
                        if (!window._ccpForcedFs) return;
                        try { document.documentElement.style.cursor = 'none'; } catch (_) {}
                    }
                    function armCursor() {
                        try { document.documentElement.style.cursor = ''; } catch (_) {}
                        if (cursorTimer) clearTimeout(cursorTimer);
                        cursorTimer = setTimeout(hideCursor, 2000);
                    }
                    window._ccpArmCursor = armCursor;
                    window.addEventListener('mousemove', function() { if (window._ccpForcedFs) armCursor(); }, true);

                    // Ctrl+MouseWheel = page zoom. IsZoomControlEnabled is
                    // false in WebView2 settings so the built-in shortcut is
                    // off and we own the gesture. preventDefault stops the
                    // page from scrolling; the WebMessage round-trips to C#
                    // which calls CoreWebView2.ZoomFactor (10% per notch).
                    window.addEventListener('wheel', function(e) {
                        if (!e.ctrlKey) return;
                        try { e.preventDefault(); } catch (_) {}
                        try {
                            window.chrome.webview.postMessage(
                                e.deltaY > 0 ? 'ccp_zoom_out' : 'ccp_zoom_in');
                        } catch (_) {}
                    }, { passive: false, capture: true });
                })();
            ");

            _videoBrowserReady = true;
            VideoBrowser.CoreWebView2.NavigationStarting += OnVideoNavStarting;
            VideoBrowser.CoreWebView2.NavigationCompleted += OnVideoNavCompleted;
            VideoBrowser.CoreWebView2.ContainsFullScreenElementChanged += OnVideoFullscreenChanged;
            VideoBrowser.CoreWebView2.WebMessageReceived += OnVideoWebMessageReceived;
            return true;
        }

        private void OnVideoWebMessageReceived(object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = e.TryGetWebMessageAsString();
                if (msg == "ccp_exit_fullscreen")
                {
                    App.Logger?.Information("EnhancementPlayer: ccp_exit_fullscreen received (forced FS active = {Active}, hasWindow = {HasWin})",
                        _isVideoFullscreen, _videoFullscreenWindow != null);
                    // Honor the "force-close independent of page state" contract
                    // documented above the JS handler — if either the flag or
                    // the temp window says we're not clean, retry cleanup. This
                    // covers the case where ExitVideoFullscreen ran, cleared
                    // the flag, but Close() left the window alive.
                    if (_isVideoFullscreen || _videoFullscreenWindow != null)
                    {
                        Dispatcher.BeginInvoke(() => ForceExitVideoFullscreen("page requested exit"));
                    }
                }
                else if (msg == "ccp_zoom_in")
                {
                    Dispatcher.BeginInvoke(() => { try { AdjustVideoZoom(+0.10); } catch (Exception ex) { Diag.Swallowed(ex); } });
                }
                else if (msg == "ccp_zoom_out")
                {
                    Dispatcher.BeginInvoke(() => { try { AdjustVideoZoom(-0.10); } catch (Exception ex) { Diag.Swallowed(ex); } });
                }
                else if (msg != null && msg.StartsWith("ccp_key:", StringComparison.Ordinal))
                {
                    // ccp_key:<name>:<shift 0|1>:<repeat 0|1>
                    var parts = msg.Split(':');
                    if (parts.Length >= 4 && TryMapRelayKey(parts[1], out var key))
                    {
                        bool shift = parts[2] == "1", repeat = parts[3] == "1";
                        Dispatcher.BeginInvoke(() => { try { HandlePlayerKeyDown(key, shift, repeat); } catch (Exception ex) { Diag.Swallowed(ex); } });
                    }
                }
                else if (msg == "ccp_keyup:Escape")
                {
                    Dispatcher.BeginInvoke(() => { try { HandleEscapeUp(); } catch (Exception ex) { Diag.Swallowed(ex); } });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: web message handler error: {Error}", ex.Message);
            }
        }

        private static bool IsAllowedPlayerHost(Uri uri)
        {
            return UrlSafety.HostMatches(uri, DeeperConfig.PreviewHostAllowlist);
        }

        private void OnVideoNavStarting(object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            try
            {
                // One-shot file:// permit for a user-picked local video.
                if (!string.IsNullOrEmpty(_initialAllowedFileUrl)
                    && string.Equals(e.Uri, _initialAllowedFileUrl, StringComparison.Ordinal))
                {
                    _initialAllowedFileUrl = null;
                    return;
                }

                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps
                    || !IsAllowedPlayerHost(uri))
                {
                    e.Cancel = true;
                    App.Logger?.Debug("EnhancementPlayer: blocked nav to {Host}", Services.Logging.UrlLog.Host(e.Uri));
                }
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "blocking nav on error");
                e.Cancel = true;
            }
        }

        private void OnVideoNavCompleted(object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            // Post-navigation UI work only. _videoSource is bound up front in
            // LoadVideoUrlAsync / LoadLocalVideoAsync (see BindVideoSource), so
            // by the time this fires the engine is already wired and the poll
            // loop will reconcile BtnPlayPause within one tick.
            try
            {
                if (!e.IsSuccess)
                {
                    // A blocked (allowlist) or failed navigation used to show
                    // "Playing." and the pause glyph over an empty pane.
                    var msg = string.Format(Loc.Get("deeper_player_video_nav_failed_fmt"), e.WebErrorStatus);
                    TxtVideoStatus.Text = msg;
                    TxtVideoStatus.Visibility = Visibility.Visible;
                    TxtStatus.Text = msg;
                    BtnPlayPause.Content = "▶";
                    IngestErrorLine(msg);
                    App.Logger?.Debug("EnhancementPlayer: video navigation failed ({Status})", e.WebErrorStatus);
                    return;
                }
                TxtVideoStatus.Visibility = Visibility.Collapsed;
                BtnPlayPause.Content = "⏸";
                TxtStatus.Text = Loc.Get("deeper_player_status_playing");
                _videoSource?.SetVolume(SliderVolume.Value / 100.0);

                // Re-arm the forced-fullscreen flag on the NEW document. It lives
                // on `window`, so every navigation wipes it — and a video that
                // ends and rolls into the next page navigates. Without this the
                // page-side escape hatches (dblclick / ESC / fullscreenchange)
                // all decide we are not in fullscreen, ESC no-ops, and the
                // borderless host is left up with nothing in it and no way out
                // (Discord report BUG-J9PPPJT274).
                if (_isVideoFullscreen || _videoFullscreenWindow != null)
                {
                    try { FireScript("window._ccpForcedFs = true;"); }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                }

                ScrollVideoIntoView();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: post-nav UI update failed");
            }
        }

        // Scroll the embedded player into the viewport center on nav and
        // whenever a new <video> appears. We deliberately do NOT mutate the
        // page DOM/CSS — earlier attempts to hide site chrome (stretch-to-
        // fill, sibling-hide overlays) broke JW Player's internal controls
        // and left the user staring at a black rectangle. The natural page
        // renders normally; the Pop out (PiP) button is the answer when the
        // user wants a chrome-free view.
        private void ScrollVideoIntoView()
        {
            try
            {
                if (VideoBrowser?.CoreWebView2 == null) return;
                FireScript(@"
                    (function() {
                        if (window._ccpMaxInstalled) {
                            try { window._ccpMaxApply && window._ccpMaxApply(); } catch (_) {}
                            return;
                        }
                        window._ccpMaxInstalled = true;

                        function maximize(v) {
                            if (!v || v._ccpMaximized) return;
                            // Walk up to find the player container — first
                            // ancestor with width >= 200 and height >= 150.
                            // Falls back to <video> itself if no good
                            // container is found.
                            var node = v.parentElement;
                            var container = v;
                            while (node && node !== document.body) {
                                var r = node.getBoundingClientRect();
                                if (r.width >= 200 && r.height >= 150) { container = node; break; }
                                node = node.parentElement;
                            }
                            try {
                                container.scrollIntoView({
                                    behavior: 'smooth',
                                    block: 'center',
                                    inline: 'center'
                                });
                            } catch (_) { /* ignore */ }
                            v._ccpMaximized = true;
                        }

                        // Iframes known to throw on .contentDocument access
                        // (cross-origin). Native throws cost ~10-50us each in
                        // Chromium, and a doc-wide observer firing at 60Hz
                        // multiplied that across every ad iframe on every tick.
                        // Once we've thrown on an iframe once, skip it.
                        var crossOriginIframes = new WeakSet();

                        function apply() {
                            try {
                                var vids = document.querySelectorAll('video');
                                for (var i = 0; i < vids.length; i++) {
                                    var v = vids[i];
                                    var r = v.getBoundingClientRect();
                                    if (r.width > 0 && r.height > 0) {
                                        maximize(v);
                                    }
                                }
                                // Same-origin iframes (rare on HT, common on
                                // generic embed pages). Cross-origin iframes
                                // throw on contentDocument access — swallow
                                // the first throw and cache so we skip them
                                // on subsequent calls.
                                var ifs = document.querySelectorAll('iframe');
                                for (var j = 0; j < ifs.length; j++) {
                                    var f = ifs[j];
                                    if (crossOriginIframes.has(f)) continue;
                                    try {
                                        var d = f.contentDocument;
                                        if (!d) continue;
                                        var iv = d.querySelectorAll('video');
                                        for (var k = 0; k < iv.length; k++) maximize(iv[k]);
                                    } catch (_) {
                                        crossOriginIframes.add(f);
                                    }
                                }
                            } catch (_) { /* ignore */ }
                        }
                        window._ccpMaxApply = apply;

                        // rAF coalescer: the MutationObserver below fires on
                        // every style/class/childList mutation, which on a
                        // playing HT or TikTok page is dozens of times per
                        // animation frame (progress bar updates, control fades,
                        // engagement counters). Without this, apply() ran for
                        // each one even though it's idempotent once
                        // _ccpMaximized is set. Coalesce to at most one
                        // apply() per frame regardless of mutation volume.
                        function scheduledApply() {
                            if (window._ccpMaxScheduled) return;
                            window._ccpMaxScheduled = true;
                            requestAnimationFrame(function() {
                                window._ccpMaxScheduled = false;
                                apply();
                            });
                        }

                        // Initial run + retry burst for the first ~6s so
                        // late-attached players catch up even before the
                        // observer fires.
                        apply();
                        var tries = 0;
                        var iv = setInterval(function() {
                            apply();
                            if (++tries > 60) clearInterval(iv);
                        }, 100);

                        // Persistent observer: any new <video> node (or any
                        // restyle that resizes a maximized container) re-runs
                        // apply() so the user can't end up looking at chrome.
                        // Routed through scheduledApply so high-frequency
                        // mutations collapse to one apply() per frame.
                        try {
                            var mo = new MutationObserver(scheduledApply);
                            mo.observe(document.documentElement, {
                                childList: true, subtree: true,
                                attributes: true, attributeFilter: ['style', 'class']
                            });
                        } catch (_) { /* ignore */ }
                    })();
                ");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: video maximize injection failed: {Error}", ex.Message);
            }
        }

        // -- Real fullscreen for the embedded video --------------------------
        // The WebView2 raises ContainsFullScreenElementChanged when the user clicks
        // the fullscreen button on the embedded HTML5 video. By default the WebView
        // would just expand the <video> inside our window's bounds, which leaves the
        // Player chrome (transport, event log, etc.) visible. To match the main
        // browser's behavior, we reparent the WebView into a borderless topmost
        // window covering the Player's monitor for the duration of fullscreen.

        private void OnVideoFullscreenChanged(object? sender, object? e)
        {
            // We unsubscribe for the duration of Enter/Exit, but the in-flight
            // guard is a second line of defence if a stray FS-changed manages
            // to fire mid-swap.
            if (_fsTransitionInFlight) return;
            try
            {
                var contains = VideoBrowser?.CoreWebView2?.ContainsFullScreenElement ?? false;

                if (contains)
                {
                    if (_isVideoFullscreen) return;

                    // Always reparent — even on single monitor, the user
                    // expects HT fullscreen to cover the screen, not just the
                    // player's video pane. The dblclick exit works via the
                    // JS click-pair + ccp_exit_fullscreen WebMessage path,
                    // which closes the borderless window through the flag
                    // even when the page lost HTML5 fullscreen at reparent.
                    var screens = App.GetAllScreensCached();
                    var dualMonitor = App.Settings?.Current?.DualMonitorEnabled == true && screens.Length > 1;
                    if (dualMonitor)
                    {
                        _isPlayerDualMonitorActive = App.ScreenMirror?.EnableMirror() ?? false;
                    }
                    EnterVideoFullscreen();
                }
                else
                {
                    if (!_isVideoFullscreen) return;
                    if (_isPlayerDualMonitorActive)
                    {
                        try { App.ScreenMirror?.DisableMirror(); } catch (Exception ex) { Diag.Swallowed(ex); }
                        _isPlayerDualMonitorActive = false;
                    }
                    ForceExitVideoFullscreen("page left html5 fullscreen");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: fullscreen toggle failed");
                // A throw on the way INTO fullscreen unwinds inside
                // EnterVideoFullscreen; a throw on the way out must not leave the
                // borderless host on screen with no way to reach it.
                try { ForceExitVideoFullscreen("fullscreen toggle error"); }
                catch (Exception exIgnored) { Diag.Swallowed(exIgnored); }
            }
        }

        // Parent-agnostic detach. The original code only handled Panel parents
        // and silently no-op'd everything else; that left VideoBrowser still
        // attached and the next `Window.Content = VideoBrowser` threw
        // "Visual already has a parent" mid-Enter, leaving state inconsistent.
        // Returns true iff `child` is fully detached on return.
        private static bool TryDetachFromUiParent(FrameworkElement child)
        {
            if (child == null) return false;
            var logical = LogicalTreeHelper.GetParent(child);
            switch (logical)
            {
                case Panel p:
                    p.Children.Remove(child);
                    break;
                case Decorator dec when ReferenceEquals(dec.Child, child):
                    dec.Child = null;
                    break;
                case ContentControl cc when ReferenceEquals(cc.Content, child):
                    cc.Content = null;
                    break;
                case ContentPresenter cp when ReferenceEquals(cp.Content, child):
                    cp.Content = null;
                    break;
                case null:
                    break;
                default:
                    return false;
            }
            return child.Parent == null && VisualTreeHelper.GetParent(child) == null;
        }

        private void EnterVideoFullscreen()
        {
            if (_fsTransitionInFlight || _isVideoFullscreen || VideoBrowser == null) return;
            _fsTransitionInFlight = true;
            var hadFsSubscription = false;
            Window? built = null;
            try
            {
                try
                {
                    if (VideoBrowser.CoreWebView2 != null)
                    {
                        VideoBrowser.CoreWebView2.ContainsFullScreenElementChanged -= OnVideoFullscreenChanged;
                        hadFsSubscription = true;
                    }
                }
                catch (Exception ex) { Diag.Swallowed(ex); }

                // Find which monitor this Player is currently on so the fullscreen
                // window lands on the same screen the user was looking at.
                var screen = System.Windows.Forms.Screen.FromHandle(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle);

                if (!TryDetachFromUiParent(VideoBrowser))
                {
                    App.Logger?.Warning(
                        "EnhancementPlayer: aborting fullscreen — VideoBrowser parent is an unsupported type ({Type})",
                        LogicalTreeHelper.GetParent(VideoBrowser)?.GetType().FullName ?? "<null>");
                    return;
                }
                if (VideoBrowser.Parent != null || VisualTreeHelper.GetParent(VideoBrowser) != null)
                {
                    App.Logger?.Warning("EnhancementPlayer: detach reported success but VideoBrowser still has a parent; aborting fullscreen");
                    SafeRestoreVideoBrowserToPane();
                    return;
                }

                // Build the fullscreen window with borderless properties up front
                // (matches MainWindow.EnterBrowserFullscreen at MainWindow.xaml.cs:17675).
                built = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = Brushes.Black,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = screen.Bounds.X + 100,
                    Top = screen.Bounds.Y + 100,
                    Width = 400,
                    Height = 300,
                    Content = VideoBrowser
                };

                // Capture delegates in locals so the Closed handler can
                // unsubscribe them before the window is unreferenced — keeps
                // these lambdas (which capture `this`) from pinning the
                // EnhancementPlayerWindow if WPF holds internal refs to the
                // closed fullscreen window.
                // Same bindings as the player window (Esc/F11 exit, Space, arrows,
                // M, F) plus the hold-Esc escape hatch, which needs KeyUp.
                KeyEventHandler keyHandler = (_, args) =>
                {
                    if (args.Key == Key.F11) { ExitFullscreenViaScript(); args.Handled = true; return; }
                    if (HandlePlayerKeyDown(args.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), args.IsRepeat))
                        args.Handled = true;
                };
                KeyEventHandler keyUpHandler = (_, args) =>
                {
                    if (args.Key == Key.Escape) { HandleEscapeUp(); args.Handled = true; }
                };

                // Clear THIS window's content, not whatever _videoFullscreenWindow
                // happens to point at — a fast exit/re-enter would otherwise blank
                // the new host while the old one closes.
                System.ComponentModel.CancelEventHandler closingHandler = (s, _) =>
                {
                    if (s is Window w) w.Content = null;
                    else if (built != null) built.Content = null;
                };

                // TOPMOST IS RENTED, NOT OWNED (#905). A fullscreen video has to sit over the
                // taskbar and over the app, so the window is born Topmost - but it kept that
                // claim even after another window took focus, which is how the post-session
                // recap ended up alive and modal BEHIND it: invisible, holding the input queue,
                // with no way to reach the button that dismisses it. Dropping the claim on
                // deactivation lets any dialog surface, and taking it back on activation
                // restores the fullscreen look the moment the user comes back to the video.
                EventHandler deactivatedHandler = (_, _) =>
                {
                    try { if (_videoFullscreenWindow != null) _videoFullscreenWindow.Topmost = false; }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                };
                EventHandler activatedHandler = (_, _) =>
                {
                    try { if (_videoFullscreenWindow != null) _videoFullscreenWindow.Topmost = true; }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                    // Re-taking the claim re-raises this window to the FRONT of the topmost band,
                    // which is exactly what buried the pink tint / spiral / flashes for the rest of
                    // the run (#1041/#1051/#1052). Put them straight back on top instead of waiting
                    // out the reconcile tick.
                    try { App.Overlay?.RequestForcedZOrderReassert(); }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                };

                EventHandler? closedHandler = null;
                closedHandler = (_, _) =>
                {
                    // Defensive: if Closing didn't clear Content, detach now so
                    // the reparent below can't throw "already has parent".
                    try
                    {
                        TryDetachFromUiParent(VideoBrowser);
                        SafeRestoreVideoBrowserToPane();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("EnhancementPlayer: WebView re-parent on close failed: {Error}", ex.Message);
                    }

                    try { built!.KeyDown -= keyHandler; } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { built!.KeyUp -= keyUpHandler; } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { built!.Closing -= closingHandler; } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { built!.Deactivated -= deactivatedHandler; } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { built!.Activated -= activatedHandler; } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { built!.Closed -= closedHandler!; } catch (Exception ex) { Diag.Swallowed(ex); }

                    _videoFullscreenWindow = null;
                    // Release the z-order registration on the window's OWN teardown too, so a close
                    // that did not come through ExitVideoFullscreen can't strand the forced-tick
                    // flag on (SetFullscreenBrowserActive is idempotent per owner).
                    try { App.Overlay?.SetFullscreenBrowserActive(built, false); }
                    catch (Exception ex) { Diag.Swallowed(ex); }

                    // After reparent the underlying Chromium HWND is in a state
                    // where the page no longer receives input events (mouse
                    // wheel scroll, clicks) until the user clicks into the
                    // WebView. Focusing the WebView programmatically wakes the
                    // inner HWND back up so the HT page is scrollable on
                    // return. Mouse.Capture(null) clears any stuck capture
                    // from the fullscreen click sequence.
                    try { Mouse.Capture(null); } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { VideoBrowser?.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
                };

                built.KeyDown += keyHandler;
                built.KeyUp += keyUpHandler;
                built.Closing += closingHandler;
                built.Deactivated += deactivatedHandler;
                built.Activated += activatedHandler;
                built.Closed += closedHandler;

                // Show small first, pump render queue, then maximize — matches the
                // pattern in MainWindow.EnterBrowserFullscreen which avoids a sizing
                // glitch on per-monitor DPI displays.
                _videoFullscreenWindow = built;
                built.Show();
                built.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                built.WindowState = WindowState.Maximized;

                // Flag the page so the JS click-pair / dblclick handlers fire
                // even when the page itself lost HTML5 fullscreen during the
                // reparent. `window._ccpForcedFs` is read alongside
                // `document.fullscreenElement` in the dblclick handler so the
                // user can always exit our WPF "forced fullscreen" by
                // double-clicking the video, regardless of page state.
                try { FireScript("window._ccpForcedFs = true; try { window._ccpArmCursor && window._ccpArmCursor(); } catch (_) {}"); }
                catch (Exception ex) { Diag.Swallowed(ex); }

                // Commit state only after reparent is fully in place.
                _isVideoFullscreen = true;
                // A new Topmost window at the front of the topmost band buries every effect window,
                // and none of them re-raise themselves: register it so OverlayService forces a
                // z-order pass on every tick while the fullscreen video is up (#1041/#1051/#1052).
                try { App.Overlay?.SetFullscreenBrowserActive(built, true); }
                catch (Exception ex) { App.Logger?.Debug("EnhancementPlayer: fullscreen z-order notify failed: {Error}", ex.Message); }
                App.Logger?.Information("EnhancementPlayer: entered fullscreen on {Screen}", screen.DeviceName);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "EnhancementPlayer: failed to enter fullscreen");
                try
                {
                    if (built != null)
                    {
                        try { built.Content = null; } catch (Exception exIgnored) { Diag.Swallowed(exIgnored); }
                        try { built.Close(); } catch (Exception exIgnored) { Diag.Swallowed(exIgnored); }
                    }
                }
                catch (Exception exIgnored) { Diag.Swallowed(exIgnored); }
                _videoFullscreenWindow = null;
                SafeRestoreVideoBrowserToPane();
                try
                {
                    if (VideoBrowser?.CoreWebView2 != null)
                    {
                        FireScript(
                            "window._ccpForcedFs = false; try { if (document.exitFullscreen && document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
                    }
                }
                catch (Exception exIgnored) { Diag.Swallowed(exIgnored); }
                // _isVideoFullscreen never flipped, so no further unwind.
            }
            finally
            {
                if (hadFsSubscription)
                {
                    try
                    {
                        if (VideoBrowser?.CoreWebView2 != null)
                            VideoBrowser.CoreWebView2.ContainsFullScreenElementChanged += OnVideoFullscreenChanged;
                    }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                }
                _fsTransitionInFlight = false;
                DrainPendingFullscreenExit();
            }
        }

        // Put VideoBrowser back into the Player's VideoPane Grid iff it's
        // currently parent-free. Caller is responsible for prior detach.
        private void SafeRestoreVideoBrowserToPane()
        {
            try
            {
                if (VideoBrowser == null) return;
                if (VideoBrowser.Parent != null) return;
                if (VisualTreeHelper.GetParent(VideoBrowser) != null) return;
                if (VideoPane?.Child is Grid grid && !grid.Children.Contains(VideoBrowser))
                    grid.Children.Insert(0, VideoBrowser);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: SafeRestoreVideoBrowserToPane failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Fire-and-forget a page script without leaking an unobserved task exception.
        /// If the WebView2 is torn down while the script is in flight, the returned task
        /// faults — and an unobserved fault reaches the finalizer and crashes the app
        /// (the "CoreWebView2 ... after the WebView2 control is disposed" crash, #381).
        /// The OnlyOnFaulted continuation observes the exception so it stays contained.
        /// </summary>
        private void FireScript(string js)
        {
            try
            {
                var cw = VideoBrowser?.CoreWebView2;
                if (cw == null) return;
                cw.ExecuteScriptAsync(js).ContinueWith(
                    t => { _ = t.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void ExitVideoFullscreen()
        {
            if (_fsTransitionInFlight) return;
            // Allow cleanup whenever EITHER the flag OR the temp window is
            // still live — a partial prior exit (flag cleared but window
            // never closed) needs to be retryable.
            if (!_isVideoFullscreen && _videoFullscreenWindow == null) return;
            _fsTransitionInFlight = true;
            var hadFsSubscription = false;
            try
            {
                try
                {
                    if (VideoBrowser?.CoreWebView2 != null)
                    {
                        VideoBrowser.CoreWebView2.ContainsFullScreenElementChanged -= OnVideoFullscreenChanged;
                        hadFsSubscription = true;
                    }
                }
                catch (Exception ex) { Diag.Swallowed(ex); }

                _isVideoFullscreen = false;
                // Clear the JS flag and best-effort exit any HTML5 fullscreen
                // that may still be active on the page. Both calls are no-ops
                // if the WebView is gone or the page already exited.
                try
                {
                    if (VideoBrowser?.CoreWebView2 != null)
                    {
                        FireScript(
                            "window._ccpForcedFs = false; try { document.documentElement.style.cursor = ''; } catch (_) {} try { if (document.exitFullscreen && document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
                    }
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
                if (_videoFullscreenWindow != null)
                {
                    try { App.Overlay?.SetFullscreenBrowserActive(_videoFullscreenWindow, false); }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                    try { _videoFullscreenWindow.Close(); }
                    catch (Exception ex) { App.Logger?.Debug("EnhancementPlayer: fullscreen window close failed: {Error}", ex.Message); }
                }
                // Back to windowed: the effects were pinned to the front of the topmost band while
                // the fullscreen window was up, so re-seat everything one last time now that the
                // forced-tick flag is off.
                try { App.Overlay?.RequestForcedZOrderReassert(); }
                catch (Exception ex) { Diag.Swallowed(ex); }
                App.Logger?.Information("EnhancementPlayer: exited fullscreen");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "EnhancementPlayer: failed to exit fullscreen");
            }
            finally
            {
                if (hadFsSubscription)
                {
                    try
                    {
                        if (VideoBrowser?.CoreWebView2 != null)
                            VideoBrowser.CoreWebView2.ContainsFullScreenElementChanged += OnVideoFullscreenChanged;
                    }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                }
                _fsTransitionInFlight = false;
                DrainPendingFullscreenExit();
            }
        }

        /// <summary>
        /// THE teardown for the borderless fullscreen host. Every end path funnels
        /// here — ESC/F11, the page's dblclick + ccp_exit_fullscreen message, an
        /// error mid-playback, and the Player window closing. Idempotent, always
        /// runs on the UI thread, never throws, and always ends with the host
        /// window closed and VideoBrowser back in the Player's pane.
        ///
        /// Before this existed, exit depended on the PAGE still being in HTML5
        /// fullscreen. It usually isn't: the reparent drops it, and a video that
        /// ends and navigates wipes window._ccpForcedFs too. exitFullscreen() then
        /// no-ops, ContainsFullScreenElementChanged never fires, and the borderless
        /// host is stranded — empty, un-draggable (WindowStyle.None), un-resizable
        /// (ResizeMode.NoResize), swallowing input as a topmost window.
        /// Discord report BUG-J9PPPJT274.
        /// </summary>
        private void ForceExitVideoFullscreen(string reason)
        {
            if (!Dispatcher.CheckAccess())
            {
                try
                {
                    if (Application.Current?.Dispatcher?.HasShutdownStarted != true)
                        Dispatcher.BeginInvoke(() => ForceExitVideoFullscreen(reason));
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
                return;
            }

            if (!_isVideoFullscreen && _videoFullscreenWindow == null) return;

            // Mid-swap: Enter/Exit pump the dispatcher, so we can land on top of
            // one. Queue instead of racing it — the transition's finally drains
            // this via DrainPendingFullscreenExit.
            if (_fsTransitionInFlight)
            {
                _fsExitPending = true;
                App.Logger?.Information(
                    "EnhancementPlayer: fullscreen teardown deferred ({Reason}) — transition in flight", reason);
                return;
            }

            App.Logger?.Information("EnhancementPlayer: fullscreen teardown ({Reason})", reason);

            // Ordered path first: unsubscribes, clears the page flag, closes.
            try { ExitVideoFullscreen(); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex,
                    "EnhancementPlayer: ordered fullscreen exit failed ({Reason}); forcing the host closed", reason);
            }

            // Guaranteed path: whatever the ordered attempt did or didn't manage,
            // the host window does not survive this method.
            var host = _videoFullscreenWindow;
            if (host == null && !_isVideoFullscreen) return;

            _fsTransitionInFlight = false;
            _isVideoFullscreen = false;
            _videoFullscreenWindow = null;

            if (host != null)
            {
                try { App.Overlay?.SetFullscreenBrowserActive(host, false); }
                catch (Exception ex) { Diag.Swallowed(ex); }
                try { host.Content = null; }
                catch (Exception ex) { Diag.Swallowed(ex); }
                try { host.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex,
                        "EnhancementPlayer: forced close of the fullscreen host failed ({Reason}); hiding it instead", reason);
                    // Last resort: a window we cannot close must at least stop
                    // covering the app and eating input.
                    try { host.Topmost = false; } catch (Exception exIgnored) { Diag.Swallowed(exIgnored); }
                    try { host.Hide(); } catch (Exception exIgnored) { Diag.Swallowed(exIgnored); }
                }
            }

            // The host's Closed handler normally re-parents the WebView; do it
            // here too in case that handler was already detached or threw.
            try { TryDetachFromUiParent(VideoBrowser); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { SafeRestoreVideoBrowserToPane(); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { App.Overlay?.RequestForcedZOrderReassert(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>
        /// Session / panic teardown hook. The Deeper player's forced fullscreen is
        /// the same shape as the built-in browser's (#952) — topmost, chrome-less,
        /// taskbar-less — so a panic that leaves it standing hands the user a window
        /// they cannot move, resize or close. Safe to call unconditionally: it
        /// early-outs on every player that isn't in fullscreen.
        /// </summary>
        internal static void ForceExitFullscreenAll()
        {
            try
            {
                if (Application.Current == null) return;
                if (Application.Current.Dispatcher?.HasShutdownStarted == true) return;
                foreach (var w in Application.Current.Windows
                                     .OfType<EnhancementPlayerWindow>().ToList())
                {
                    try { w.ForceExitVideoFullscreen("session/panic teardown"); }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "EnhancementPlayer: teardown fullscreen exit failed");
                    }
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>
        /// Runs a teardown that arrived while a fullscreen transition held the
        /// lock. Called from the finally of both Enter and Exit.
        /// </summary>
        private void DrainPendingFullscreenExit()
        {
            if (!_fsExitPending) return;
            _fsExitPending = false;
            if (!_isVideoFullscreen && _videoFullscreenWindow == null) return;
            try
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                Dispatcher.BeginInvoke(() => ForceExitVideoFullscreen("deferred exit"));
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void ExitFullscreenViaScript()
        {
            // Ask the page to drop HTML5 fullscreen first so page and window stay
            // in sync — but never DEPEND on it. When the page isn't in HTML5
            // fullscreen (the common case after our reparent, and always after the
            // video ends and the page navigates) this script is a no-op and no
            // event comes back, which is exactly how the host window got stranded.
            try
            {
                if (VideoBrowser?.CoreWebView2 != null)
                {
                    FireScript(
                        "window._ccpForcedFs = false; (function(){if(document.fullscreenElement)document.exitFullscreen();})();");
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }

            ForceExitVideoFullscreen("esc/f11");
        }

        private void OnHostActionLogged(string line)
        {
            try
            {
                if (Dispatcher.CheckAccess()) IngestActionLine(line);
                else Dispatcher.BeginInvoke(() => IngestActionLine(line));
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void OnHostDiagnostic(string line)
        {
            try
            {
                if (Dispatcher.CheckAccess()) IngestDiagnosticLine(line);
                else Dispatcher.BeginInvoke(() => IngestDiagnosticLine(line));
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void OnHostLoadFailed(string reason)
        {
            try
            {
                void Apply()
                {
                    TxtStatus.Text = string.Format(Loc.Get("deeper_player_status_enh_failed_fmt"), reason);
                    // Also log to the structured event log so failures don't
                    // disappear once the user navigates to another file.
                    IngestErrorLine(reason);
                }
                if (Dispatcher.CheckAccess()) Apply();
                else Dispatcher.BeginInvoke(Apply);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        // -- Cleanup -----------------------------------------------------------

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Flag first: async continuations (LoadAudio's waveform await) check this
            // so a close that lands mid-await can't start playback + bind an engine
            // that no longer has an owner to unbind it.
            _isClosing = true;
            if (ReferenceEquals(_instance, this)) _instance = null;
            SaveWindowBounds();
            CancelEscHold();

            // Per-step try/catch: a single catch-all around the whole teardown
            // means an early throw (e.g. ScreenMirror NRE) skips _uiTimer.Stop
            // and leaves dead delegates pinned on the App.* singletons. Stop
            // the tick timer first so no UI work is queued onto a dying window.
            try { _uiTimer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { if (_uiTimer != null) _uiTimer.Tick -= UiTimer_Tick; } catch (Exception ex) { Diag.Swallowed(ex); }
            _uiTimer = null;

            // DispatcherTimer is rooted by the Dispatcher while running; if the
            // banner is mid-display when the window closes, the timer's lambda
            // captures `this` and briefly pins the window until the 6s tick.
            try { _promotedClearTimer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _promotedClearTimer = null;

            // Exit fullscreen synchronously so the reparent-on-Closed lambda
            // releases VideoBrowser back to VideoPane BEFORE we dispose it.
            // Check the window reference too — a partial prior exit can leave
            // the borderless host alive with the flag already cleared, and
            // skipping cleanup here would orphan it past the player's death.
            //
            // ForceExitVideoFullscreen defers when a transition holds the lock,
            // and there is no "later" once the window is closing — EnterVideoFullscreen
            // pumps the dispatcher at render priority mid-transition, so a close can
            // land right inside one and leave the borderless host alive as a
            // see-through, un-interactable ghost with VideoBrowser disposed out from
            // under it (#381). Drop the lock first so the teardown runs here and now.
            _fsTransitionInFlight = false;
            _fsExitPending = false;
            try { ForceExitVideoFullscreen("player window closing"); }
            catch (Exception ex) { Diag.Swallowed(ex); }

            try
            {
                if (_isPlayerDualMonitorActive)
                {
                    try { App.ScreenMirror?.DisableMirror(); } catch (Exception ex) { Diag.Swallowed(ex); }
                    _isPlayerDualMonitorActive = false;
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }

            // Unsubscribe singleton-service events. Each in its own try so a
            // throw on one (e.g. _player already disposed) doesn't strand the
            // others as dead delegates on the app-lifetime singletons.
            try { _player.Loaded -= OnPlayerLoaded; } catch (Exception ex) { Diag.Swallowed(ex); }
            try { _player.Ended -= OnPlayerEnded; } catch (Exception ex) { Diag.Swallowed(ex); }
            try { _host.Loaded -= OnHostLoaded; } catch (Exception ex) { Diag.Swallowed(ex); }
            try { _host.LoadFailed -= OnHostLoadFailed; } catch (Exception ex) { Diag.Swallowed(ex); }
            try { _host.ActionLogged -= OnHostActionLogged; } catch (Exception ex) { Diag.Swallowed(ex); }
            try { _host.Diagnostic -= OnHostDiagnostic; } catch (Exception ex) { Diag.Swallowed(ex); }
            try { UnsubscribeWebcamStateForButton(); } catch (Exception ex) { Diag.Swallowed(ex); }
            // If THIS player session turned the webcam on (via the pre-play
            // prompt), turn it off on the way out so we leave the system the
            // way we found it. Webcams the user had running before opening
            // the player are NOT touched.
            try
            {
                if (_playerStartedWebcam && App.Webcam?.IsRunning == true)
                    App.Webcam.Stop();
            }
            catch (Exception ex) { App.Logger?.Debug("Player webcam auto-stop failed: {Error}", ex.Message); }
            try { UnbindEngineIfRunning(); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { _player.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }

            try
            {
                // No _videoBrowserReady guard: if CoreWebView2 finished
                // initializing between the subscribe site's IsInitialized check
                // and here, the handlers exist regardless of the flag. Each -=
                // is independently try/catch'd so a missing one can't strand
                // the others.
                var cw = VideoBrowser?.CoreWebView2;
                if (cw != null)
                {
                    try { cw.NavigationStarting -= OnVideoNavStarting; } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { cw.NavigationCompleted -= OnVideoNavCompleted; } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { cw.ContainsFullScreenElementChanged -= OnVideoFullscreenChanged; } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { cw.WebMessageReceived -= OnVideoWebMessageReceived; } catch (Exception ex) { Diag.Swallowed(ex); }
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }

            try { _videoSource?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _videoSource = null;
            try { VideoBrowser?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private static string FormatTime(double seconds) => MediaDurationCache.Format(seconds);

        // -- Window bounds persistence -------------------------------------------

        private void RestoreWindowBounds(Models.AppSettings? s)
        {
            try
            {
                if (s == null) return;
                if (s.DeeperPlayerWindowWidth >= MinWidth && s.DeeperPlayerWindowHeight >= MinHeight)
                {
                    Width = s.DeeperPlayerWindowWidth;
                    Height = s.DeeperPlayerWindowHeight;
                }
                var virt = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (s.DeeperPlayerWindowWidth > 0
                    && IsRectUsableOnScreen(new Rect(s.DeeperPlayerWindowLeft, s.DeeperPlayerWindowTop, Width, Height), virt))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = s.DeeperPlayerWindowLeft;
                    Top = s.DeeperPlayerWindowTop;
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>
        /// A remembered position counts as usable when at least a 200x120 patch
        /// of the window (enough to grab the title bar) lies on the virtual desktop.
        /// </summary>
        internal static bool IsRectUsableOnScreen(Rect window, Rect virtualScreen)
        {
            if (window.IsEmpty || virtualScreen.IsEmpty || virtualScreen.Width <= 0 || virtualScreen.Height <= 0) return false;
            if (double.IsNaN(window.X) || double.IsNaN(window.Y)) return false;
            var inter = Rect.Intersect(window, virtualScreen);
            return !inter.IsEmpty && inter.Width >= 200 && inter.Height >= 120;
        }

        private void SaveWindowBounds()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null || WindowState != WindowState.Normal) return;
                if (Width < MinWidth || Height < MinHeight) return;
                s.DeeperPlayerWindowLeft = Left;
                s.DeeperPlayerWindowTop = Top;
                s.DeeperPlayerWindowWidth = Width;
                s.DeeperPlayerWindowHeight = Height;
                App.Settings?.Save();
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        // -- Keyboard shortcuts --------------------------------------------------
        // Space play/pause, Left/Right seek 5 s (Shift 30 s), Up/Down volume 5,
        // M mute, F fullscreen (video), Esc leave fullscreen / close; hold Esc
        // 3 s to stop playback and unbind the engine. The WPF window, the
        // fullscreen host and the page-side relay all land in HandlePlayerKeyDown.

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsTextInputFocused()) return;
            if (HandlePlayerKeyDown(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), e.IsRepeat))
                e.Handled = true;
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (IsTextInputFocused()) return;
            HandleEscapeUp();
            e.Handled = true;
        }

        private static bool IsTextInputFocused()
            => Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
               || Keyboard.FocusedElement is PasswordBox;

        private bool HandlePlayerKeyDown(Key key, bool shift, bool isRepeat)
        {
            switch (key)
            {
                case Key.Escape:
                    _lastEscSignal = DateTime.UtcNow;
                    if (!isRepeat) HandleEscapeDown();
                    return true;
                case Key.Space:
                    if (!isRepeat) TogglePlayPause();
                    return true;
                case Key.Left:  SeekRelative(shift ? -30 : -5); return true;
                case Key.Right: SeekRelative(shift ? 30 : 5); return true;
                case Key.Up:    AdjustVolume(+5); return true;
                case Key.Down:  AdjustVolume(-5); return true;
                case Key.M:
                    if (!isRepeat) ToggleMute();
                    return true;
                case Key.F:
                    if (!isRepeat) ToggleVideoFullscreen();
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryMapRelayKey(string name, out Key key)
        {
            switch (name)
            {
                case "Space": key = Key.Space; return true;
                case "Escape": key = Key.Escape; return true;
                case "ArrowLeft": key = Key.Left; return true;
                case "ArrowRight": key = Key.Right; return true;
                case "ArrowUp": key = Key.Up; return true;
                case "ArrowDown": key = Key.Down; return true;
                case "M": key = Key.M; return true;
                case "F": key = Key.F; return true;
                default: key = Key.None; return false;
            }
        }

        private void SeekRelative(double deltaSeconds)
        {
            try
            {
                if (_videoSource != null)
                {
                    var d = _videoSource.GetDurationSeconds();
                    var t = Math.Max(0, _videoSource.GetCurrentTimeSeconds() + deltaSeconds);
                    if (d > 0) t = Math.Min(t, d);
                    _videoSource.Seek(t);
                }
                else if (_player.DurationMs > 0)
                {
                    var total = _player.DurationMs / 1000.0;
                    var t = Math.Clamp(_player.CurrentTimeMs / 1000.0 + deltaSeconds, 0, total);
                    _player.Seek(t);
                    UpdatePlayhead(t / total);
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private int? _volumeBeforeMute;
        private bool _muteChanging;

        private void AdjustVolume(int delta)
        {
            try { SliderVolume.Value = Math.Clamp(SliderVolume.Value + delta, 0, 100); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void ToggleMute()
        {
            try
            {
                _muteChanging = true;
                if (_volumeBeforeMute is int restore)
                {
                    _volumeBeforeMute = null;
                    SliderVolume.Value = restore;
                }
                else
                {
                    _volumeBeforeMute = (int)Math.Round(SliderVolume.Value);
                    SliderVolume.Value = 0;
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
            finally { _muteChanging = false; }
        }

        private void ToggleVideoFullscreen()
        {
            if (_videoSource == null || VideoPane.Visibility != Visibility.Visible) return;
            if (_isVideoFullscreen || _videoFullscreenWindow != null) ExitFullscreenViaScript();
            else EnterVideoFullscreen();
        }

        // -- Hold-Esc escape hatch -----------------------------------------------
        // People get stranded in blink/gaze loops. Holding Esc for 3 s anywhere
        // in the player (window, fullscreen host, or the page via the relay)
        // stops playback and unbinds the engine. A tap still does the old thing
        // (leave fullscreen, else close). Both are off under a strict lock.

        private const double EscHoldSeconds = 3.0;
        private DispatcherTimer? _escHoldTimer;
        private DateTime _escHoldStart;
        private DateTime _lastEscSignal;
        private bool _escConsumed;

        private static bool IsUnderStrictLock()
            => App.Lockdown?.IsActive == true || App.Settings?.Current?.StrictLockEnabled == true;

        private void HandleEscapeDown()
        {
            if (ChangePopup?.IsOpen == true)
            {
                ChangePopup.IsOpen = false;
                _escConsumed = true;
                return;
            }
            if (_escHoldTimer != null) return;
            _escConsumed = false;
            if (_isVideoFullscreen || _videoFullscreenWindow != null)
            {
                ExitFullscreenViaScript();
                _escConsumed = true;
            }
            if (IsUnderStrictLock()) return;
            bool anythingRunning = _host.IsRunning || _player.IsPlaying || (_videoSource?.IsPlaying ?? false);
            if (!anythingRunning) return;
            _escHoldStart = DateTime.UtcNow;
            _escHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _escHoldTimer.Tick += EscHoldTimer_Tick;
            _escHoldTimer.Start();
            ShowHoldEscHint(0);
        }

        private void EscHoldTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            // A key-up can get lost across the fullscreen reparent or a focus
            // swap between the page and the window; a long silence from the key
            // (no repeat) counts as a release.
            if ((now - _lastEscSignal).TotalSeconds > 1.5) { CancelEscHold(); return; }
            var held = (now - _escHoldStart).TotalSeconds;
            ShowHoldEscHint(Math.Min(1, held / EscHoldSeconds));
            if (held < EscHoldSeconds) return;
            CancelEscHold();
            _escConsumed = true;
            EmergencyStop();
        }

        private void HandleEscapeUp()
        {
            CancelEscHold();
            if (_escConsumed) { _escConsumed = false; return; }
            if (IsUnderStrictLock()) return;
            if (_isVideoFullscreen || _videoFullscreenWindow != null) return;
            Close();
        }

        private void CancelEscHold()
        {
            try
            {
                if (_escHoldTimer != null)
                {
                    _escHoldTimer.Stop();
                    _escHoldTimer.Tick -= EscHoldTimer_Tick;
                    _escHoldTimer = null;
                }
                if (HoldEscHint != null) HoldEscHint.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void ShowHoldEscHint(double fraction)
        {
            try
            {
                if (HoldEscHint == null || HoldEscProgress == null) return;
                HoldEscProgress.Value = Math.Clamp(fraction, 0, 1);
                HoldEscHint.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void EmergencyStop()
        {
            try { ForceExitVideoFullscreen("esc hold"); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { BtnStop_Click(this, new RoutedEventArgs()); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { TxtStatus.Text = Loc.Get("deeper_player_status_escape_stopped"); } catch (Exception ex) { Diag.Swallowed(ex); }
            App.Logger?.Information("[DeeperPlayer] hold-Esc stop");
        }

        // Once per loaded local file, hand the measured length to the
        // duration cache so the library list shows it next time without a
        // probe. Remote URLs are rejected by the cache itself.
        private string? _durationRememberedFor;
        private void RememberDurationOnce(string? mediaPath, double seconds)
        {
            if (string.IsNullOrEmpty(mediaPath) || seconds <= 0) return;
            if (string.Equals(_durationRememberedFor, mediaPath, StringComparison.OrdinalIgnoreCase)) return;
            _durationRememberedFor = mediaPath;
            try { MediaDurationCache.Remember(mediaPath, seconds); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }
    }
}
