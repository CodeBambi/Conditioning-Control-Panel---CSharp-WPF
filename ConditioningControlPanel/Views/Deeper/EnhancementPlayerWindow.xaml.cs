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
        private bool _isScrubbing;
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

        // Tracks WHICH branch of TryAutoLoadEnhancement supplied the current
        // enhancement (or whether the user picked it manually). Drives the
        // small "From library / Embedded in media / ..." badge under TxtEnhPath.
        private enum DiscoverySource { Manual, Library, Sidecar, Embedded, Url, PromotedFromEmbedded }
        private DiscoverySource _lastDiscoverySource = DiscoverySource.Manual;
        // Path of the most recently loaded media in the player. Lets the
        // "Create new enhancement..." button hand the editor a pre-linked
        // media so the user starts authoring against the right file.
        private string? _lastMediaPathForCreateNew;

        // Mission 3: structured event log replaces the flat string collection.
        // Implementation lives in EnhancementPlayerWindow.Mission3.cs.

        // Fullscreen state for the embedded video. Mirrors MainWindow's browser-fullscreen
        // path: when HT (or any HTML5 video) requests fullscreen via the WebView2 API, we
        // pop the WebView out into a borderless topmost window covering the whole monitor.
        private Window? _videoFullscreenWindow;
        private bool _isVideoFullscreen;
        private bool _fsTransitionInFlight;
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

            UpdateVolumeFromPlayer();
            SubscribeWebcamStateForButton();
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
            catch { }
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

        private static bool IsLocalVideoFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi" or ".m4v";
        }

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

                ShowMediaPaneFor(MediaTypes.Video);
                TxtVideoStatus.Text = Loc.Get("deeper_player_video_loading");
                TxtVideoStatus.Visibility = Visibility.Visible;

                if (!await EnsureVideoBrowserReadyAsync())
                {
                    TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                    return;
                }

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
            try
            {
                if (EnhancementMediaBundler.IsSupportedExtension(mediaPath)
                    && EnhancementMediaBundler.TryExtract(mediaPath, out var embedded, out _)
                    && embedded != null)
                {
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

                var dir = Path.GetDirectoryName(mediaPath);
                var baseName = Path.GetFileNameWithoutExtension(mediaPath);
                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(baseName))
                {
                    var candidate = Path.Combine(dir, baseName + ".ccpenh.json");
                    if (File.Exists(candidate))
                    {
                        _lastDiscoverySource = DiscoverySource.Sidecar;
                        _host.LoadFromFile(candidate);
                        return;
                    }
                }

                var mediaTypeForMatch = IsLocalVideoFile(mediaPath)
                    ? Models.Deeper.MediaTypes.Video
                    : Models.Deeper.MediaTypes.Audio;
                var match = App.EnhancementLibrary?.FindMatch(mediaPath, mediaTypeForMatch);
                if (match != null)
                {
                    _lastDiscoverySource = DiscoverySource.Library;
                    _host.LoadFromFile(match.FilePath);
                    return;
                }

                // Nothing found. Surface the "Create new enhancement..." button
                // so the user can author against this media in the editor.
                BtnCreateNewEnhancement.Visibility = Visibility.Visible;
                TxtStatus.Text = Loc.Get("deeper_player_no_enh_for_media");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: auto-load enhancement failed: {Error}", ex.Message);
            }
        }

        // Non-modal toast under the source badge, auto-clears after ~6s.
        private System.Windows.Threading.DispatcherTimer? _promotedClearTimer;
        private void ShowPromotedBanner(string filename)
        {
            try
            {
                TxtStatus.Text = string.Format(Loc.Get("deeper_player_promoted_to_library_fmt"), filename);
                _promotedClearTimer?.Stop();
                _promotedClearTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(6)
                };
                _promotedClearTimer.Tick += (_, _) =>
                {
                    _promotedClearTimer?.Stop();
                    _promotedClearTimer = null;
                };
                _promotedClearTimer.Start();
            }
            catch { }
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

                TxtAudioPath.Text = path;
                TxtStatus.Text = Loc.Get("deeper_player_status_loading_audio");
                await LoadWaveformAsync(path);

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

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
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
        // webcam-driven rules and the webcam isn't already running. No
        // return value — the user's choice doesn't block playback, it just
        // decides whether the webcam-gated rules will actually fire.
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

            // Mark BEFORE the dialog so a re-entrant Play click during the
            // modal doesn't re-trigger the prompt.
            _webcamPromptShownForCurrentEnh = true;

            var result = MessageBox.Show(this,
                Loc.Get("deeper_player_webcam_prompt_body"),
                Loc.Get("deeper_player_webcam_prompt_title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (!WebcamTrackingService.IsConsentCurrent())
                {
                    // Mirror the BtnEyeTracking_Click first-time path —
                    // consent lives in the Lab tab, not here.
                    MessageBox.Show(this,
                        Loc.Get("deeper_player_eye_tracking_first_time"),
                        Loc.Get("deeper_player_btn_eye_tracking_start"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (svc.Start())
                {
                    // Remember that THIS player session started the webcam so
                    // Window_Closing can put it back the way it found it.
                    // Webcams the user had running before opening the player
                    // are left alone.
                    _playerStartedWebcam = true;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: webcam start from play-prompt failed");
            }
        }

        // True when MaybePromptForWebcamBeforePlay actually started the camera
        // in this player session. Used by Window_Closing to stop it on exit.
        private bool _playerStartedWebcam;

        // Webcam dependency check: AutoTags is the primary signal (saved by the
        // EnhancementAutoTagger at file-write time). Falls back to scanning the
        // rules' Trigger.Type so enhancements written by pre-AutoTags builds
        // still trigger the prompt.
        private static bool EnhancementNeedsWebcam(Models.Deeper.Enhancement enh)
        {
            if (enh == null) return false;
            // AutoTags lives on Metadata, not the Enhancement root.
            if (enh.Metadata?.AutoTags?.Contains(EnhancementAutoTagger.TagWebcam) == true) return true;
            if (enh.Rules == null) return false;
            foreach (var rule in enh.Rules)
            {
                var t = rule?.Trigger?.Type;
                if (t == Models.Deeper.TriggerTypes.GazeTarget
                 || t == Models.Deeper.TriggerTypes.GazeAvoid
                 || t == Models.Deeper.TriggerTypes.AttentionLost
                 || t == Models.Deeper.TriggerTypes.BlinkDetected
                 || t == Models.Deeper.TriggerTypes.MouthOpen) return true;
            }
            return false;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_videoSource != null)
            {
                try { _videoSource.Pause(); _videoSource.Seek(0); } catch { }
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

        private void BtnEyeTracking_Click(object sender, RoutedEventArgs e)
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
                svc.Stop();
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
                if (!svc.Start())
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
            try { svc.OnTrackingStateChanged -= _onWebcamStateChanged; } catch { }
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
            _player.Volume = (int)e.NewValue;
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
            if (_isScrubbing) return;
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
                if (d > 0) TxtTotal.Text = FormatTime(d);
                BtnPlayPause.Content = _videoSource.IsPlaying ? "⏸" : "▶";
            }
            else
            {
                var ms = _player.CurrentTimeMs;
                TxtCurrent.Text = FormatTime(ms / 1000.0);
                UpdatePlayhead(_player.DurationMs > 0 ? (double)ms / _player.DurationMs : 0);
            }

            // Mission 3 mini-timeline + overlay + status pill updates.
            UpdateMiniPlayheadX();
            UpdateMiniTimelineReadout();
            RefreshNowRegionOverlay();
            UpdateStatusPill();
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
            catch { }
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

            if (enh == null)
            {
                TxtEnhPath.Text = Loc.Get("deeper_player_no_enh");
                TxtEnhMetadata.Text = "";
                BtnUnloadEnhancement.Visibility = Visibility.Collapsed;
                TxtEnhSource.Visibility = Visibility.Collapsed;
                UnbindEngineIfRunning();
                ShowMediaPaneFor(MediaTypes.Audio); // default back to audio UI
                return;
            }
            TxtEnhPath.Text = path ?? "";
            var creator = string.IsNullOrEmpty(enh.Metadata?.Creator) ? "" : $" — {enh.Metadata.Creator}";
            var name = string.IsNullOrEmpty(enh.Metadata?.Name) ? "(untitled)" : enh.Metadata!.Name;
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
            VolumePanel.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
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
            var src = _videoSource;
            _host.Bind(src,
                attach: () => src?.Attach(),
                detach: () => { try { src?.Detach(); } catch { } });
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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConditioningControlPanel",
                "browser_data_deeper_player");
            System.IO.Directory.CreateDirectory(userDataFolder);
            // --autoplay-policy=no-user-gesture-required: Chromium otherwise
            // rejects programmatic v.play() calls (the WPF Play button is not
            // a JS user gesture), which manifested as "I click Play and
            // nothing happens" on Editor → Preview → browser-video flows.
            var options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
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
                        Dispatcher.BeginInvoke(() => { try { ExitVideoFullscreen(); } catch { } });
                    }
                }
                else if (msg == "ccp_zoom_in")
                {
                    Dispatcher.BeginInvoke(() => { try { AdjustVideoZoom(+0.10); } catch { } });
                }
                else if (msg == "ccp_zoom_out")
                {
                    Dispatcher.BeginInvoke(() => { try { AdjustVideoZoom(-0.10); } catch { } });
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
                    App.Logger?.Debug("EnhancementPlayer: blocked nav to {Url}", e.Uri);
                }
            }
            catch
            {
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
                TxtVideoStatus.Visibility = Visibility.Collapsed;
                BtnPlayPause.Content = "⏸";
                TxtStatus.Text = Loc.Get("deeper_player_status_playing");

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
                _ = VideoBrowser.CoreWebView2.ExecuteScriptAsync(@"
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
                        try { App.ScreenMirror?.DisableMirror(); } catch { }
                        _isPlayerDualMonitorActive = false;
                    }
                    ExitVideoFullscreen();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: fullscreen toggle failed");
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
                catch { }

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
                KeyEventHandler keyHandler = (_, args) =>
                {
                    if (args.Key == Key.Escape || args.Key == Key.F11)
                    {
                        ExitFullscreenViaScript();
                        args.Handled = true;
                    }
                };

                System.ComponentModel.CancelEventHandler closingHandler = (_, _) =>
                {
                    if (_videoFullscreenWindow != null)
                        _videoFullscreenWindow.Content = null;
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

                    try { built!.KeyDown -= keyHandler; } catch { }
                    try { built!.Closing -= closingHandler; } catch { }
                    try { built!.Closed -= closedHandler!; } catch { }

                    _videoFullscreenWindow = null;

                    // After reparent the underlying Chromium HWND is in a state
                    // where the page no longer receives input events (mouse
                    // wheel scroll, clicks) until the user clicks into the
                    // WebView. Focusing the WebView programmatically wakes the
                    // inner HWND back up so the HT page is scrollable on
                    // return. Mouse.Capture(null) clears any stuck capture
                    // from the fullscreen click sequence.
                    try { Mouse.Capture(null); } catch { }
                    try { VideoBrowser?.Focus(); } catch { }
                };

                built.KeyDown += keyHandler;
                built.Closing += closingHandler;
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
                try { _ = VideoBrowser.CoreWebView2.ExecuteScriptAsync("window._ccpForcedFs = true;"); }
                catch { }

                // Commit state only after reparent is fully in place.
                _isVideoFullscreen = true;
                App.Logger?.Information("EnhancementPlayer: entered fullscreen on {Screen}", screen.DeviceName);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "EnhancementPlayer: failed to enter fullscreen");
                try
                {
                    if (built != null)
                    {
                        try { built.Content = null; } catch { }
                        try { built.Close(); } catch { }
                    }
                }
                catch { }
                _videoFullscreenWindow = null;
                SafeRestoreVideoBrowserToPane();
                try
                {
                    if (VideoBrowser?.CoreWebView2 != null)
                    {
                        _ = VideoBrowser.CoreWebView2.ExecuteScriptAsync(
                            "window._ccpForcedFs = false; try { if (document.exitFullscreen && document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
                    }
                }
                catch { }
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
                    catch { }
                }
                _fsTransitionInFlight = false;
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
                catch { }

                _isVideoFullscreen = false;
                // Clear the JS flag and best-effort exit any HTML5 fullscreen
                // that may still be active on the page. Both calls are no-ops
                // if the WebView is gone or the page already exited.
                try
                {
                    if (VideoBrowser?.CoreWebView2 != null)
                    {
                        _ = VideoBrowser.CoreWebView2.ExecuteScriptAsync(
                            "window._ccpForcedFs = false; try { if (document.exitFullscreen && document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
                    }
                }
                catch { }
                if (_videoFullscreenWindow != null)
                {
                    try { _videoFullscreenWindow.Close(); }
                    catch (Exception ex) { App.Logger?.Debug("EnhancementPlayer: fullscreen window close failed: {Error}", ex.Message); }
                }
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
                    catch { }
                }
                _fsTransitionInFlight = false;
            }
        }

        private void ExitFullscreenViaScript()
        {
            // Driving exitFullscreen from the page side fires
            // ContainsFullScreenElementChanged again, which routes through
            // OnVideoFullscreenChanged → ExitVideoFullscreen and keeps the page
            // and window in sync (vs closing the window first and leaving the
            // page in fullscreen state).
            try
            {
                if (VideoBrowser?.CoreWebView2 != null)
                {
                    _ = VideoBrowser.CoreWebView2.ExecuteScriptAsync(
                        "(function(){if(document.fullscreenElement)document.exitFullscreen();})();");
                }
                else
                {
                    ExitVideoFullscreen();
                }
            }
            catch
            {
                ExitVideoFullscreen();
            }
        }

        private void OnHostActionLogged(string line)
        {
            try
            {
                if (Dispatcher.CheckAccess()) IngestActionLine(line);
                else Dispatcher.BeginInvoke(() => IngestActionLine(line));
            }
            catch { }
        }

        private void OnHostDiagnostic(string line)
        {
            try
            {
                if (Dispatcher.CheckAccess()) IngestDiagnosticLine(line);
                else Dispatcher.BeginInvoke(() => IngestDiagnosticLine(line));
            }
            catch { }
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
            catch { }
        }

        // -- Cleanup -----------------------------------------------------------

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Per-step try/catch: a single catch-all around the whole teardown
            // means an early throw (e.g. ScreenMirror NRE) skips _uiTimer.Stop
            // and leaves dead delegates pinned on the App.* singletons. Stop
            // the tick timer first so no UI work is queued onto a dying window.
            try { _uiTimer?.Stop(); } catch { }
            try { if (_uiTimer != null) _uiTimer.Tick -= UiTimer_Tick; } catch { }
            _uiTimer = null;

            // DispatcherTimer is rooted by the Dispatcher while running; if the
            // banner is mid-display when the window closes, the timer's lambda
            // captures `this` and briefly pins the window until the 6s tick.
            try { _promotedClearTimer?.Stop(); } catch { }
            _promotedClearTimer = null;

            // Exit fullscreen synchronously so the reparent-on-Closed lambda
            // releases VideoBrowser back to VideoPane BEFORE we dispose it.
            // Check the window reference too — a partial prior exit can leave
            // the borderless host alive with the flag already cleared, and
            // skipping cleanup here would orphan it past the player's death.
            try { if (_isVideoFullscreen || _videoFullscreenWindow != null) ExitVideoFullscreen(); } catch { }

            try
            {
                if (_isPlayerDualMonitorActive)
                {
                    try { App.ScreenMirror?.DisableMirror(); } catch { }
                    _isPlayerDualMonitorActive = false;
                }
            }
            catch { }

            // Unsubscribe singleton-service events. Each in its own try so a
            // throw on one (e.g. _player already disposed) doesn't strand the
            // others as dead delegates on the app-lifetime singletons.
            try { _player.Loaded -= OnPlayerLoaded; } catch { }
            try { _player.Ended -= OnPlayerEnded; } catch { }
            try { _host.Loaded -= OnHostLoaded; } catch { }
            try { _host.LoadFailed -= OnHostLoadFailed; } catch { }
            try { _host.ActionLogged -= OnHostActionLogged; } catch { }
            try { _host.Diagnostic -= OnHostDiagnostic; } catch { }
            try { UnsubscribeWebcamStateForButton(); } catch { }
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
            try { UnbindEngineIfRunning(); } catch { }
            try { _player.Stop(); } catch { }

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
                    try { cw.NavigationStarting -= OnVideoNavStarting; } catch { }
                    try { cw.NavigationCompleted -= OnVideoNavCompleted; } catch { }
                    try { cw.ContainsFullScreenElementChanged -= OnVideoFullscreenChanged; } catch { }
                    try { cw.WebMessageReceived -= OnVideoWebMessageReceived; } catch { }
                }
            }
            catch { }

            try { _videoSource?.Dispose(); } catch { }
            _videoSource = null;
            try { VideoBrowser?.Dispose(); } catch { }
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes}:{ts.Seconds:00}";
        }
    }
}
