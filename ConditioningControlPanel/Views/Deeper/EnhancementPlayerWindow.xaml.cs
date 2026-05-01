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
        private string? _pendingVideoUrl;
        // Sticky audio path so the user can press Play again after Stop
        // (the underlying player nulls its CurrentPath on Stop, by design).
        private string? _lastAudioPath;

        // Live event log (last N actions the engine fired). Newest first.
        private const int MaxEventLogEntries = 30;
        private readonly System.Collections.ObjectModel.ObservableCollection<string> _events = new();

        // Fullscreen state for the embedded video. Mirrors MainWindow's browser-fullscreen
        // path: when HT (or any HTML5 video) requests fullscreen via the WebView2 API, we
        // pop the WebView out into a borderless topmost window covering the whole monitor.
        private Window? _videoFullscreenWindow;
        private bool _isVideoFullscreen;
        private bool _isPlayerDualMonitorActive;

        public EnhancementPlayerWindow(EnhancementAudioPlayer player, EnhancementHostService host)
        {
            InitializeComponent();
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _host = host ?? throw new ArgumentNullException(nameof(host));

            _player.Loaded += OnPlayerLoaded;
            _player.Ended += OnPlayerEnded;
            _host.Loaded += OnHostLoaded;
            _host.LoadFailed += OnHostLoadFailed;
            _host.ActionLogged += OnHostActionLogged;

            LstEvents.ItemsSource = _events;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            UpdateVolumeFromPlayer();
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

                if (!_videoBrowserReady)
                {
                    var userDataFolder = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ConditioningControlPanel",
                        "browser_data");
                    System.IO.Directory.CreateDirectory(userDataFolder);
                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                        .CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
                    await VideoBrowser.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                    if (VideoBrowser.CoreWebView2 == null)
                    {
                        TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                        return;
                    }
                    _videoBrowserReady = true;
                    VideoBrowser.CoreWebView2.NavigationCompleted += OnVideoNavCompleted;
                    VideoBrowser.CoreWebView2.ContainsFullScreenElementChanged += OnVideoFullscreenChanged;
                }

                // file:/// URL navigation - WebView2 wraps a local media file in
                // its default media-viewer page with a real <video> element.
                var fileUri = new Uri(path).AbsoluteUri;
                _pendingVideoUrl = fileUri;
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
            // 1) Side-by-side: foo.mp3 → foo.ccpenh.json next to it.
            // 2) Library lookup by media_source pattern (Phase 10).
            try
            {
                var dir = Path.GetDirectoryName(mediaPath);
                var baseName = Path.GetFileNameWithoutExtension(mediaPath);
                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(baseName))
                {
                    var candidate = Path.Combine(dir, baseName + ".ccpenh.json");
                    if (File.Exists(candidate))
                    {
                        _host.LoadFromFile(candidate);
                        return;
                    }
                }

                var mediaType = IsLocalVideoFile(mediaPath)
                    ? Models.Deeper.MediaTypes.Video
                    : Models.Deeper.MediaTypes.Audio;
                var match = App.EnhancementLibrary?.FindMatch(mediaPath, mediaType);
                if (match != null) _host.LoadFromFile(match.FilePath);
            }
            catch { }
        }

        // -- Audio loading -----------------------------------------------------

        private async void LoadAudio(string path)
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
                _player.Resume();
                BtnPlayPause.Content = "⏸";
            }
            else if (!string.IsNullOrEmpty(_player.CurrentPath))
            {
                _player.Play(_player.CurrentPath);
                BtnPlayPause.Content = "⏸";
                BindEngineIfReady();
            }
            else if (!string.IsNullOrEmpty(_lastAudioPath))
            {
                // Resume after Stop: the underlying player cleared its handle,
                // but we kept the path so the user can hit Play to start over.
                LoadAudio(_lastAudioPath);
            }
            else
            {
                BtnPickAudio_Click(sender, e);
            }
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

            if (_videoSource != null)
            {
                var t = _videoSource.GetCurrentTimeSeconds();
                var d = _videoSource.GetDurationSeconds();
                TxtCurrent.Text = FormatTime(t);
                if (d > 0) TxtTotal.Text = FormatTime(d);
                BtnPlayPause.Content = _videoSource.IsPlaying ? "⏸" : "▶";
                return;
            }

            var ms = _player.CurrentTimeMs;
            TxtCurrent.Text = FormatTime(ms / 1000.0);
            UpdatePlayhead(_player.DurationMs > 0 ? (double)ms / _player.DurationMs : 0);
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
            catch { }
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
            catch { }
        }

        private void UpdateHostUi(Models.Deeper.Enhancement? enh, string? path)
        {
            if (enh == null)
            {
                TxtEnhPath.Text = Loc.Get("deeper_player_no_enh");
                TxtEnhMetadata.Text = "";
                BtnUnloadEnhancement.Visibility = Visibility.Collapsed;
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
        }

        // -- Pane swap + video loading ----------------------------------------

        private void ShowMediaPaneFor(string? mediaType)
        {
            var isVideo = string.Equals(mediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase);
            AudioFileRow.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
            AudioPane.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
            VideoPane.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            VolumePanel.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
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

                if (!_videoBrowserReady)
                {
                    // Reuse the main browser's user-data folder so HT cookies
                    // carry over (same pattern as DeeperEditorWindow.InitializeBrowserAsync).
                    var userDataFolder = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ConditioningControlPanel",
                        "browser_data");
                    System.IO.Directory.CreateDirectory(userDataFolder);
                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                        .CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
                    await VideoBrowser.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                    if (VideoBrowser.CoreWebView2 == null)
                    {
                        TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                        return;
                    }
                    _videoBrowserReady = true;
                    VideoBrowser.CoreWebView2.NavigationCompleted += OnVideoNavCompleted;
                    VideoBrowser.CoreWebView2.ContainsFullScreenElementChanged += OnVideoFullscreenChanged;
                }

                _pendingVideoUrl = url;
                VideoBrowser.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: video load failed");
                TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
            }
        }

        private void OnVideoNavCompleted(object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                if (_pendingVideoUrl == null) return;
                _pendingVideoUrl = null;

                // Build a fresh time source per navigation so seek/state is
                // tied to the actual loaded page's <video>, not stale state.
                _videoSource?.Dispose();
                _videoSource = new BrowserVideoTimeSource(VideoBrowser);

                var src = _videoSource;
                _host.Bind(src,
                    attach: () => src?.Attach(),
                    detach: () => { try { src?.Detach(); } catch { } });

                TxtVideoStatus.Visibility = Visibility.Collapsed;
                BtnPlayPause.Content = "⏸";
                TxtStatus.Text = Loc.Get("deeper_player_status_playing");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementPlayer: video bind failed");
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
            try
            {
                var contains = VideoBrowser?.CoreWebView2?.ContainsFullScreenElement ?? false;
                if (contains == _isVideoFullscreen) return;

                if (contains)
                {
                    // Mirror primary across all monitors when the user opted in.
                    var screens = App.GetAllScreensCached();
                    if (App.Settings?.Current?.DualMonitorEnabled == true && screens.Length > 1)
                    {
                        _isPlayerDualMonitorActive = App.ScreenMirror?.EnableMirror() ?? false;
                    }
                    EnterVideoFullscreen();
                }
                else
                {
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

        private void EnterVideoFullscreen()
        {
            if (_isVideoFullscreen || VideoBrowser == null) return;
            try
            {
                _isVideoFullscreen = true;

                // Find which monitor this Player is currently on so the fullscreen
                // window lands on the same screen the user was looking at.
                var screen = System.Windows.Forms.Screen.FromHandle(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle);

                // Detach the WebView from its current parent. Player layout uses a
                // Grid inside VideoPane (Border > Grid > WebView), so we walk up to
                // the Grid and remove it from there.
                if (VideoBrowser.Parent is System.Windows.Controls.Panel panel)
                {
                    panel.Children.Remove(VideoBrowser);
                }

                // Build the fullscreen window with borderless properties up front
                // (matches MainWindow.EnterBrowserFullscreen at MainWindow.xaml.cs:17675).
                _videoFullscreenWindow = new Window
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

                // Esc and F11 in the fullscreen window exit fullscreen (also clears
                // HTML5 fullscreen state on the page so the toggle round-trips).
                _videoFullscreenWindow.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Escape || args.Key == Key.F11)
                    {
                        ExitFullscreenViaScript();
                        args.Handled = true;
                    }
                };

                _videoFullscreenWindow.Closing += (_, _) =>
                {
                    if (_videoFullscreenWindow != null)
                        _videoFullscreenWindow.Content = null;
                };

                _videoFullscreenWindow.Closed += (_, _) =>
                {
                    // Return the WebView to the Player's VideoPane.
                    try
                    {
                        if (VideoBrowser != null && VideoPane.Child is Grid grid && !grid.Children.Contains(VideoBrowser))
                        {
                            grid.Children.Insert(0, VideoBrowser);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("EnhancementPlayer: WebView re-parent on close failed: {Error}", ex.Message);
                    }
                    _videoFullscreenWindow = null;
                };

                // Show small first, pump render queue, then maximize — matches the
                // pattern in MainWindow.EnterBrowserFullscreen which avoids a sizing
                // glitch on per-monitor DPI displays.
                _videoFullscreenWindow.Show();
                _videoFullscreenWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                _videoFullscreenWindow.WindowState = WindowState.Maximized;

                // After the reparent the page can lose its HTML5 fullscreen state
                // (the WebView sees a layout reset). Re-request on the <video>
                // element so the page renders the same way it did before the move.
                _ = ReRequestVideoFullscreenAsync();

                App.Logger?.Information("EnhancementPlayer: entered fullscreen on {Screen}", screen.DeviceName);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "EnhancementPlayer: failed to enter fullscreen");
                ExitVideoFullscreen();
            }
        }

        private void ExitVideoFullscreen()
        {
            if (!_isVideoFullscreen) return;
            try
            {
                _isVideoFullscreen = false;
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

        private async Task ReRequestVideoFullscreenAsync()
        {
            if (VideoBrowser?.CoreWebView2 == null) return;
            try
            {
                await Task.Delay(300);
                await VideoBrowser.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        var video = document.querySelector('video');
                        if (video) {
                            if (video.requestFullscreen) {
                                video.requestFullscreen();
                            } else if (video.webkitRequestFullscreen) {
                                video.webkitRequestFullscreen();
                            }
                        }
                    })();
                ");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "EnhancementPlayer: re-request fullscreen failed");
            }
        }

        private void OnHostActionLogged(string line)
        {
            try
            {
                if (Dispatcher.CheckAccess()) AppendEvent(line);
                else Dispatcher.BeginInvoke(() => AppendEvent(line));
            }
            catch { }
        }

        private void AppendEvent(string line)
        {
            _events.Insert(0, line);
            while (_events.Count > MaxEventLogEntries) _events.RemoveAt(_events.Count - 1);
        }

        private void OnHostLoadFailed(string reason)
        {
            try
            {
                if (Dispatcher.CheckAccess()) TxtStatus.Text = string.Format(Loc.Get("deeper_player_status_enh_failed_fmt"), reason);
                else Dispatcher.BeginInvoke(() => TxtStatus.Text = string.Format(Loc.Get("deeper_player_status_enh_failed_fmt"), reason));
            }
            catch { }
        }

        // -- Cleanup -----------------------------------------------------------

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Force-exit fullscreen first so we don't leak a topmost borderless
                // window after the Player is gone, and so display topology gets
                // restored if mirroring was active.
                if (_isVideoFullscreen) ExitVideoFullscreen();
                if (_isPlayerDualMonitorActive)
                {
                    try { App.ScreenMirror?.DisableMirror(); } catch { }
                    _isPlayerDualMonitorActive = false;
                }

                _uiTimer?.Stop();
                _uiTimer = null;
                _player.Loaded -= OnPlayerLoaded;
                _player.Ended -= OnPlayerEnded;
                _host.Loaded -= OnHostLoaded;
                _host.LoadFailed -= OnHostLoadFailed;
                _host.ActionLogged -= OnHostActionLogged;
                UnbindEngineIfRunning();
                _player.Stop();

                try
                {
                    if (_videoBrowserReady && VideoBrowser?.CoreWebView2 != null)
                    {
                        VideoBrowser.CoreWebView2.NavigationCompleted -= OnVideoNavCompleted;
                        VideoBrowser.CoreWebView2.ContainsFullScreenElementChanged -= OnVideoFullscreenChanged;
                    }
                }
                catch { }
                try { _videoSource?.Dispose(); } catch { }
                _videoSource = null;
                try { VideoBrowser?.Dispose(); } catch { }
            }
            catch { }
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
