using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using LibVLCSharp.Shared;
using NAudio.Wave;
using Microsoft.Win32;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using VlcMedia = LibVLCSharp.Shared.Media;

namespace ConditioningControlPanel.Views.Deeper
{
    public partial class DeeperEditorWindow : Window
    {
        private Enhancement _enhancement = new();
        private string? _filePath;
        /// <summary>
        /// Exposes the on-disk path of the currently loaded project (null
        /// for a new/unsaved one). Read by the Player's "Open in editor"
        /// dedupe loop so it can activate an existing editor instead of
        /// opening a duplicate.
        /// </summary>
        public string? LoadedFilePath => _filePath;
        private bool _isDirty;
        private bool _suppressDirty;

        // Video playback
        private VlcMediaPlayer? _mediaPlayer;
        private VlcMedia? _vlcMedia;
        private EventHandler<LibVLCSharp.Shared.MediaPlayerLengthChangedEventArgs>? _vlcLengthChanged;
        private EventHandler<LibVLCSharp.Shared.MediaPlayerTimeChangedEventArgs>? _vlcTimeChanged;
        private EventHandler<EventArgs>? _vlcEndReached;
        private EventHandler<NAudio.Wave.StoppedEventArgs>? _waveOutStopped;

        // Audio playback
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioReader;
        private AudioWaveformResult? _waveformData;

        // Browser preview (HypnoTube etc.). Owns the WebView2's lifetime for
        // this editor window — separate from the main Browser tab so closing
        // the editor doesn't kill the user's browse session.
        private BrowserVideoTimeSource? _browserSource;
        private bool _browserInitInFlight;
        private bool _previewBrowserConfigured;
        private bool _isPreviewFullscreen;
        private Window? _previewFullscreenWindow;

        // Common
        private double _totalSeconds;
        private double _currentSeconds;
        private bool _isScrubbing;
        private DispatcherTimer? _playheadTimer;
        private DispatcherTimer? _validationTimer;

        // Regions
        private static readonly string[] RegionPalette =
        {
            "#7B5CFF", "#FF69B4", "#5CFFB7", "#FFC85C", "#5CC8FF", "#FF7B5C"
        };
        private Region? _selectedRegion;
        private readonly List<System.Windows.Shapes.Rectangle> _regionVisuals = new();
        private enum DragMode
        {
            None, Scrub, CreateRegion,
            ShiftHapticEvent, ResizeHapticStart, ResizeHapticEnd,
            DragRegion, ResizeRegionStart, ResizeRegionEnd,
            DragEffect, ResizeEffectStart, ResizeEffectEnd,
            RubberBand
        }
        private DragMode _dragMode = DragMode.None;
        private double _dragCreateStartSec;
        private System.Windows.Shapes.Rectangle? _dragCreatePreview;
        // Region drag/resize state
        private Region? _draggedRegion;
        private double _regionDragOffsetSec;
        private double _regionDragOriginalLength;
        // Effect segment drag/resize state
        private TimelineItem? _draggedEffect;
        private double _effectDragOffsetSec;
        private double _effectDragOriginalDuration;
        // Pixel band on left/right of a band where cursor switches to resize.
        private const double EdgeResizePx = 6.0;
        // Timeline zoom state. _zoomFactor of 1.0 = canvas fills the viewport
        // (default). Higher = horizontally scaled, with the ScrollViewer
        // showing a horizontal scrollbar.
        private double _zoomFactor = 1.0;
        private const double MinZoom = 1.0;
        private const double MaxZoom = 16.0;

        // Haptic events
        private const string DefaultTrackId = "primary";
        private HapticEvent? _selectedHaptic;
        private HapticTrack? _selectedHapticTrack;
        private readonly List<System.Windows.Shapes.Rectangle> _hapticVisuals = new();
        private double _hapticDragStartSec;
        private double _hapticDragOffsetSec;
        private HapticEvent? _draggedHaptic;
        private HapticTrack? _draggedHapticTrack;

        // Curve editor
        private const int CurveKeyframeCount = 5;
        private System.Windows.Shapes.Path? _curvePath;
        private readonly List<System.Windows.Shapes.Ellipse> _curveHandles = new();
        private int _draggingCurveIndex = -1;
        private bool _suppressPatternSync;

        // Rules
        private EnhancementRule? _selectedRule;
        private bool _suppressRuleSync;
        private GazePickerWindow? _gazePickerWindow;

        // Preview launches the standalone Deeper Player; the editor no longer
        // runs the engine in-place.

        public DeeperEditorWindow(Enhancement enhancement, string? filePath)
        {
            InitializeComponent();
            WindowChromeHelper.ApplyDarkTitleBar(this);
            WindowChromeHelper.RestoreOwnerOnClose(this);
            Loaded += DeeperEditorWindow_Loaded;
            KeyDown += DeeperEditorWindow_KeyDown;

            _validationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _validationTimer.Tick += (_, _) => { _validationTimer.Stop(); RefreshValidation(); };

            _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _playheadTimer.Tick += PlayheadTimer_Tick;

            BuildColorSwatches();
            BuildPatternCombo();
            LoadEnhancement(enhancement, filePath);
        }

        private void BuildPatternCombo()
        {
            CmbHapticPattern.Items.Clear();
            foreach (var name in StockHapticPatterns.Names)
                CmbHapticPattern.Items.Add(name);
            CmbHapticPattern.Items.Add(Loc.Get("deeper_editor_haptic_pattern_custom"));
        }

        private void BuildColorSwatches()
        {
            RegionColorSwatches.Children.Clear();
            foreach (var hex in RegionPalette)
            {
                var brush = TryParseBrush(hex) ?? Brushes.MediumPurple;
                var swatch = new Border
                {
                    Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = brush,
                    BorderBrush = (System.Windows.Media.Brush)FindResource("GlassBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Tag = hex,
                    ToolTip = hex
                };
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    if (_selectedRegion == null) return;
                    TxtRegionColor.Text = hex;
                };
                RegionColorSwatches.Children.Add(swatch);
            }
        }

        private static System.Windows.Media.Brush? TryParseBrush(string hex)
        {
            try
            {
                var conv = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                var brush = new System.Windows.Media.SolidColorBrush(conv);
                brush.Freeze();
                return brush;
            }
            catch { return null; }
        }

        private static System.Windows.Media.Color? TryParseColor(string hex)
        {
            try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        private void DeeperEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore the user's last sidebar width before InitializePreviewAsync
            // kicks off — fast and side-effect-free.
            ApplyPersistedSidebarWidth();

            _ = InitializePreviewAsync();

            // Legacy bus event kept for any other listeners; tutorial Part 2
            // start is dispatched from PendingPart2Tutorial below.
            try { TutorialEventBus.Emit("WindowLoaded:DeeperEditorWindow"); } catch { }

            // Interactive tutorial Part 2 dispatch. The dialog finished Part 1
            // (clicked Create with a valid source) and queued a TutorialType to
            // pick up here. Same machinery serves HT, Local Audio, and Local
            // Video walkthroughs. Deferred ~800ms so element bounds are laid
            // out before the first spotlight tries to compute its rect.
            try
            {
                if (TutorialEventBus.PendingPart2Tutorial is Services.TutorialType pendingType)
                {
                    TutorialEventBus.PendingPart2Tutorial = null;
                    var thisWindow = this;
                    // Wait ~800ms before starting Part 2:
                    //   1) lets the editor's layout fully settle so spotlight
                    //      bounds compute correctly on the very first step,
                    //   2) lets any deferred lambdas from Part 1's overlay
                    //      drain harmlessly while IsActive is still false
                    //      (e.g. the Background-priority skip-check from
                    //      OnTargetClosed) — otherwise they fire AFTER Part 2
                    //      starts and call Skip() on the new tutorial.
                    var startTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(800)
                    };
                    startTimer.Tick += (ts, te) =>
                    {
                        startTimer.Stop();
                        try
                        {
                            // Bail if the editor or app is closing — don't try to
                            // create a new overlay against a window that's about
                            // to disappear (would pin the dispatcher and zombie
                            // the process).
                            if (App.Tutorial == null) return;
                            if (Application.Current == null) return;
                            if (Application.Current.MainWindow == null) return;
                            if (!thisWindow.IsLoaded) return;

                            try { if (App.Tutorial.IsActive) App.Tutorial.Skip(); } catch { }
                            App.Tutorial.Start(pendingType);
                            var overlay = new ConditioningControlPanel.TutorialOverlay(thisWindow, App.Tutorial);
                            overlay.Show();
                        }
                        catch (Exception ex2)
                        {
                            App.Logger?.Warning(ex2, "Failed to start interactive tutorial Part 2");
                        }
                    };
                    // Stop the timer if the editor goes away before it fires —
                    // covers the case where the user closes mid-delay.
                    void StopIfEditorClosing(object? s, EventArgs ev) { try { startTimer.Stop(); } catch { } }
                    thisWindow.Closing += (s, ev) => StopIfEditorClosing(s, ev);
                    thisWindow.Closed += StopIfEditorClosing;
                    startTimer.Start();
                    return; // Don't auto-run editor coachmarks on top of Part 2.
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: Part 2 start skipped: {Error}", ex.Message);
            }

            // First-run editor coachmarks. Auto-launch once; the user can re-run
            // anytime via the "?" button. Wrap in a try so a tutorial failure
            // never blocks the editor itself.
            // Skipped when an interactive tutorial is already running so the two
            // don't fight for the overlay.
            try
            {
                if (App.Tutorial?.IsActive == true) return;

                var settings = App.Settings?.Current;
                if (settings != null && !settings.HasSeenDeeperEditorIntro)
                {
                    settings.HasSeenDeeperEditorIntro = true;
                    App.Settings?.Save();
                    // Defer slightly so the layout is fully settled before the
                    // overlay tries to compute spotlight bounds.
                    Dispatcher.BeginInvoke(new Action(StartEditorTutorial),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: auto-launch tutorial skipped: {Error}", ex.Message);
            }
        }

        private ConditioningControlPanel.TutorialOverlay? _editorTutorialOverlay;

        private void BtnEditorHelp_Click(object sender, RoutedEventArgs e)
        {
            StartEditorTutorial();
        }

        private void StartEditorTutorial()
        {
            if (_editorTutorialOverlay != null) return;
            try
            {
                App.Tutorial.Start(TutorialType.DeeperEditor);
                _editorTutorialOverlay = new ConditioningControlPanel.TutorialOverlay(this, App.Tutorial);
                _editorTutorialOverlay.Closed += (_, _) => _editorTutorialOverlay = null;
                _editorTutorialOverlay.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: failed to start tutorial: {Error}", ex.Message);
                _editorTutorialOverlay = null;
            }
        }

        private void LoadEnhancement(Enhancement enhancement, string? filePath)
        {
            _enhancement = enhancement;
            _filePath = filePath;
            _isDirty = false;

            _suppressDirty = true;
            try
            {
                TxtMetaName.Text = _enhancement.Metadata.Name;
                TxtMetaCreator.Text = _enhancement.Metadata.Creator;
                TxtMetaRemixer.Text = _enhancement.Metadata.Remixer ?? "";
                TxtMetaDescription.Text = _enhancement.Metadata.Description;
                TxtMetaTags.Text = string.Join(", ", _enhancement.Metadata.Tags);
                TxtMetaLicense.Text = _enhancement.Metadata.License;
            }
            finally { _suppressDirty = false; }

            UpdateTitle();
            RefreshLinkedFilesUi();
            UpdateCreatorLockUi();
            RefreshValidation();
            SelectNothing();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
            RebuildRuleVisuals();
            UpdateSelectionSummary();

            // Fire HT metadata auto-fill in the background. Hostname-gated inside
            // the fetcher; non-HT URLs are silent no-ops.
            _ = TryAutoFillFromHtAsync(_enhancement.MediaSource);
        }

        private async Task InitializePreviewAsync()
        {
            try
            {
                var source = _enhancement.MediaSource;

                // Remote http(s):// URL → WebView2 preview. VLC chokes on HT
                // (signed HLS, anti-hotlink), so for any remote video source
                // the editor embeds a real browser and drives the timeline
                // through BrowserVideoTimeSource. Audio remote sources still
                // hit the placeholder — there's no useful authoring surface
                // for streamed audio that we don't host.
                if (IsRemoteVideoUrl(source))
                {
                    await InitializeBrowserAsync(source);
                    return;
                }

                if (!IsLocalFile(source))
                {
                    ShowPlaceholder();
                    return;
                }

                if (_enhancement.MediaType == MediaTypes.Audio)
                    await InitializeAudioAsync(source);
                else
                    InitializeVideo(source);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: preview init failed");
                ShowPlaceholder();
            }
        }

        private bool IsRemoteVideoUrl(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (_enhancement.MediaType != MediaTypes.Video) return false;
            return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private async Task InitializeBrowserAsync(string url)
        {
            if (_browserInitInFlight) return;
            _browserInitInFlight = true;
            try
            {
                // Pre-validate the URL against the same allowlist NavigationStarting
                // enforces, so a malformed/disallowed source never reaches the
                // WebView2 in the first place.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var initialUri)
                    || initialUri.Scheme != Uri.UriSchemeHttps
                    || !IsAllowedPreviewHost(initialUri))
                {
                    ShowPlaceholder();
                    return;
                }

                VideoPreview.Visibility = Visibility.Collapsed;
                WaveformCanvas.Visibility = Visibility.Collapsed;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                BrowserPreview.Visibility = Visibility.Visible;

                // Separate user-data folder from the main browser tab. The
                // editor preview navigates user-pasted URLs that we don't fully
                // trust; sharing cookies/storage with the main tab would let a
                // hostile page (or HT phishing redirect) read the user's
                // signed-in HT cookies via document.cookie / authenticated fetch.
                var userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ConditioningControlPanel",
                    "browser_data_deeper_editor");
                System.IO.Directory.CreateDirectory(userDataFolder);

                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolder).ConfigureAwait(true);
                await BrowserPreview.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                if (BrowserPreview.CoreWebView2 == null) { ShowPlaceholder(); return; }

                // Settings hardening — JS stays on (BrowserVideoTimeSource needs
                // it to drive the <video> element) but we strip every other
                // attack surface that has no business inside an editor preview.
                var settings = BrowserPreview.CoreWebView2.Settings;
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsBuiltInErrorPageEnabled = false;

                // Allowlist navigations to https on the configured hosts only.
                // Block any redirect / link / script-driven nav that tries to
                // leave the allowlist (e.g. ad iframes, tracker beacons, a 302
                // into evil.com).
                BrowserPreview.CoreWebView2.NavigationStarting -= OnBrowserNavigationStarting;
                BrowserPreview.CoreWebView2.NavigationStarting += OnBrowserNavigationStarting;

                // One-time wiring: dblclick exit-fullscreen script + the
                // ContainsFullScreenElementChanged subscription. InitializeBrowserAsync
                // runs again whenever the user changes media_source, but the
                // CoreWebView2 instance is reused across calls — re-adding the
                // script and re-subscribing would double-fire on every nav.
                if (!_previewBrowserConfigured)
                {
                    _previewBrowserConfigured = true;

                    // Exit-fullscreen detector. JS-only `document.exitFullscreen()`
                    // is unreliable in WebView2; sometimes ContainsFullScreenElementChanged
                    // never fires, leaving the host stuck. Belt-and-suspenders:
                    // click-pair detector + dblclick safety net BOTH call tryExit,
                    // which calls exitFullscreen AND posts a 'ccp_exit_fullscreen'
                    // WebMessage. C# force-closes the fullscreen window on receipt
                    // independent of the page's actual fullscreen state.
                    await BrowserPreview.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
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
                            function dblHandler(e) {
                                if (!inAnyFs()) return;
                                if (e) {
                                    try { e.stopImmediatePropagation(); } catch (_) {}
                                    try { e.preventDefault(); } catch (_) {}
                                }
                                exitLoop(5);
                                postExit();
                            }
                            // Only the native dblclick event — not a click-pair
                            // detector, which would also fire on legitimate
                            // two-click sequences (timeline scrub + play, etc.).
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

                            // Ctrl+MouseWheel = page zoom. WebView2's built-in
                            // ZoomControlEnabled is off so we don't fight it.
                            // preventDefault stops the page from scrolling, and
                            // the WebMessage round-trips to C# which calls
                            // CoreWebView2.ZoomFactor (10% per notch).
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

                    // Without this, HT's video.requestFullscreen() just expands
                    // the video to fill the small preview pane within the
                    // editor's grid — the timeline below stays visible behind it
                    // and exit leaves focus trapped in the WebView2 so the
                    // timeline ScrollViewer's PreviewMouseWheel never fires.
                    BrowserPreview.CoreWebView2.ContainsFullScreenElementChanged += OnPreviewFullscreenChanged;
                    BrowserPreview.CoreWebView2.WebMessageReceived += OnPreviewWebMessageReceived;
                }

                BrowserPreview.CoreWebView2.Navigate(url);

                _browserSource = new BrowserVideoTimeSource(BrowserPreview);
                _browserSource.Attach();
                _browserSource.PlaybackTimeChanged += OnBrowserTimeChanged;

                // HT stacks promo banners above the <video>; without this the
                // editor preview lands at scrollTop=0 with the player offscreen.
                // Polls for the video element since HT injects it post-load.
                _ = BrowserPreview.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        var tries = 0;
                        var iv = setInterval(function() {
                            var v = document.querySelector('video');
                            if (v) {
                                v.scrollIntoView({ block: 'center', behavior: 'auto' });
                                clearInterval(iv);
                            } else if (++tries > 40) { clearInterval(iv); }
                        }, 100);
                    })();
                ");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: browser preview init failed");
                BrowserPreview.Visibility = Visibility.Collapsed;
                ShowPlaceholder();
            }
            finally
            {
                _browserInitInFlight = false;
            }
        }

        private static bool IsAllowedPreviewHost(Uri uri)
        {
            return UrlSafety.HostMatches(uri, DeeperConfig.PreviewHostAllowlist);
        }

        private void OnBrowserNavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            try
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps
                    || !IsAllowedPreviewHost(uri))
                {
                    e.Cancel = true;
                    App.Logger?.Debug("DeeperEditor: blocked preview navigation to {Url}", e.Uri);
                }
            }
            catch
            {
                e.Cancel = true;
            }
        }

        // -- Preview fullscreen reparent --------------------------------------
        // HT's HTML5 fullscreen on <video> would otherwise just stretch the
        // video to fill the WebView2's allocated cell (the small preview row),
        // leaving the editor's timeline + side panel visible behind it and
        // making "fullscreen" basically broken. Mirror EnhancementPlayerWindow:
        // reparent BrowserPreview into a borderless topmost window for the
        // duration of fullscreen, return it to PreviewHost on exit.

        private void OnPreviewWebMessageReceived(object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = e.TryGetWebMessageAsString();
                if (msg == "ccp_exit_fullscreen")
                {
                    App.Logger?.Information("DeeperEditor: ccp_exit_fullscreen received (forced FS active = {Active})", _isPreviewFullscreen);
                    if (_isPreviewFullscreen)
                    {
                        Dispatcher.BeginInvoke(() => { try { ExitPreviewFullscreen(); } catch { } });
                    }
                }
                else if (msg == "ccp_zoom_in")
                {
                    Dispatcher.BeginInvoke(() => { try { AdjustPreviewZoom(+0.10); } catch { } });
                }
                else if (msg == "ccp_zoom_out")
                {
                    Dispatcher.BeginInvoke(() => { try { AdjustPreviewZoom(-0.10); } catch { } });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: web message handler error: {Error}", ex.Message);
            }
        }

        private void OnPreviewFullscreenChanged(object? sender, object? e)
        {
            try
            {
                var contains = BrowserPreview?.CoreWebView2?.ContainsFullScreenElement ?? false;
                if (contains == _isPreviewFullscreen) return;
                if (contains) EnterPreviewFullscreen();
                else ExitPreviewFullscreen();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: preview fullscreen toggle failed");
            }
        }

        private void EnterPreviewFullscreen()
        {
            if (_isPreviewFullscreen || BrowserPreview == null) return;
            try
            {
                _isPreviewFullscreen = true;

                var screen = System.Windows.Forms.Screen.FromHandle(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle);

                if (BrowserPreview.Parent is Panel panel)
                {
                    panel.Children.Remove(BrowserPreview);
                }

                _previewFullscreenWindow = new Window
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
                    Content = BrowserPreview
                };

                _previewFullscreenWindow.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Escape || args.Key == Key.F11)
                    {
                        ExitPreviewFullscreenViaScript();
                        args.Handled = true;
                    }
                };

                _previewFullscreenWindow.Closing += (_, _) =>
                {
                    if (_previewFullscreenWindow != null)
                        _previewFullscreenWindow.Content = null;
                };

                _previewFullscreenWindow.Closed += (_, _) =>
                {
                    try
                    {
                        if (BrowserPreview != null
                            && PreviewHost != null
                            && !PreviewHost.Children.Contains(BrowserPreview))
                        {
                            PreviewHost.Children.Insert(0, BrowserPreview);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("DeeperEditor: preview re-parent on close failed: {Error}", ex.Message);
                    }
                    _previewFullscreenWindow = null;

                    // After reparent the underlying Chromium HWND is in a
                    // state where the HT page no longer receives input (mouse
                    // wheel scroll, clicks) until the user clicks into it.
                    // Focusing the WebView wakes the inner HWND back up so the
                    // page is scrollable. Mouse.Capture(null) clears any stuck
                    // capture so timeline PreviewMouseWheel works too — wheel
                    // events are hit-test based, so focus on the WebView
                    // doesn't steal them when the cursor is over the timeline.
                    try { Mouse.Capture(null); } catch { }
                    try { BrowserPreview?.Focus(); } catch { }
                };

                _previewFullscreenWindow.Show();
                _previewFullscreenWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                _previewFullscreenWindow.WindowState = WindowState.Maximized;

                // Flag the page so dblclick / click-pair handlers fire even
                // when HTML5 fullscreen state was lost during reparent.
                try { _ = BrowserPreview.CoreWebView2.ExecuteScriptAsync("window._ccpForcedFs = true;"); }
                catch { }

                App.Logger?.Information("DeeperEditor: preview entered fullscreen on {Screen}", screen.DeviceName);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "DeeperEditor: failed to enter preview fullscreen");
                ExitPreviewFullscreen();
            }
        }

        private void ExitPreviewFullscreen()
        {
            if (!_isPreviewFullscreen) return;
            try
            {
                _isPreviewFullscreen = false;
                try
                {
                    if (BrowserPreview?.CoreWebView2 != null)
                    {
                        _ = BrowserPreview.CoreWebView2.ExecuteScriptAsync(
                            "window._ccpForcedFs = false; try { if (document.exitFullscreen && document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
                    }
                }
                catch { }
                if (_previewFullscreenWindow != null)
                {
                    try { _previewFullscreenWindow.Close(); }
                    catch (Exception ex) { App.Logger?.Debug("DeeperEditor: preview fullscreen window close failed: {Error}", ex.Message); }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "DeeperEditor: failed to exit preview fullscreen");
            }
        }

        private void ExitPreviewFullscreenViaScript()
        {
            // Driving exitFullscreen from the page side fires
            // ContainsFullScreenElementChanged again, which routes through
            // OnPreviewFullscreenChanged → ExitPreviewFullscreen and keeps the
            // page and window in sync (vs closing the window first and leaving
            // the page in fullscreen state).
            try
            {
                if (BrowserPreview?.CoreWebView2 != null)
                {
                    _ = BrowserPreview.CoreWebView2.ExecuteScriptAsync(
                        "(function(){if(document.fullscreenElement)document.exitFullscreen();})();");
                }
                else
                {
                    ExitPreviewFullscreen();
                }
            }
            catch
            {
                ExitPreviewFullscreen();
            }
        }

        private void OnBrowserTimeChanged(double seconds)
        {
            // BrowserVideoTimeSource fires on its own poll timer; marshal to
            // the UI thread to update the playhead + duration. Skip while
            // the user is mid-scrub on the timeline.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<double>(OnBrowserTimeChanged), seconds);
                return;
            }
            if (_isScrubbing) return;
            if (_browserSource == null) return;
            var dur = _browserSource.GetDurationSeconds();
            if (dur > 0 && Math.Abs(dur - _totalSeconds) > 0.5)
            {
                _totalSeconds = dur;
                TxtTotalTime.Text = FormatTime(_totalSeconds);
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
            }
            _currentSeconds = Math.Max(0, seconds);
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            UpdatePlayheadPosition();
            BtnPlayPause.Content = _browserSource.IsPlaying ? "⏸" : "▶";
        }

        private void InitializeVideo(string path)
        {
            if (!VideoService.WaitForLibVLC(3000) || VideoService.SharedLibVLC == null)
            {
                ShowPlaceholder();
                return;
            }

            VideoPreview.Visibility = Visibility.Visible;
            WaveformCanvas.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;

            _mediaPlayer = new VlcMediaPlayer(VideoService.SharedLibVLC);
            App.Audio?.ApplyPreferredDevice(_mediaPlayer);
            VideoPreview.MediaPlayer = _mediaPlayer;

            // Hold the Media as a field instead of `using var`. Assigning a
            // disposed Media to MediaPlayer.Media is undefined behavior in
            // LibVLCSharp; the editor was previously getting away with it
            // because LibVLC reads the source eagerly during Play(), but it's
            // not contractual.
            _vlcMedia = new VlcMedia(VideoService.SharedLibVLC, path, FromType.FromPath);
            _mediaPlayer.Media = _vlcMedia;

            // Named handlers so DisposePlayback can detach them. Anonymous
            // lambdas can't be -=ed, which would leave the lambdas (closing
            // over `this`) rooted in LibVLC's internal callback queue.
            _vlcLengthChanged = OnVlcLengthChanged;
            _vlcTimeChanged = OnVlcTimeChanged;
            _vlcEndReached = OnVlcEndReached;
            _mediaPlayer.LengthChanged += _vlcLengthChanged;
            _mediaPlayer.TimeChanged += _vlcTimeChanged;
            _mediaPlayer.EndReached += _vlcEndReached;

            // We need Play() to fire LengthChanged + render the first frame,
            // but we want the editor to open paused so the user can scrub /
            // place effects without the clip playing on its own. The previous
            // Play(); Pause(); pair was racy: LibVLC processes both asyncly,
            // and Pause() right after Play() is a no-op when the player is
            // still in Opening state — leaving the video to play on. Instead,
            // hook the Playing event and pause as soon as the player actually
            // reaches that state, then unhook so user-driven Play() works
            // normally.
            EventHandler<EventArgs>? pauseOnce = null;
            pauseOnce = (_, _) =>
            {
                try { _mediaPlayer?.Pause(); } catch { }
                Dispatcher.InvokeAsync(() =>
                {
                    try { BtnPlayPause.Content = "▶"; } catch { }
                });
                try { if (_mediaPlayer != null && pauseOnce != null) _mediaPlayer.Playing -= pauseOnce; } catch { }
            };
            _mediaPlayer.Playing += pauseOnce;

            _mediaPlayer.Play();
            BtnPlayPause.Content = "▶";
        }

        private void OnVlcLengthChanged(object? sender, LibVLCSharp.Shared.MediaPlayerLengthChangedEventArgs args)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                _totalSeconds = args.Length / 1000.0;
                TxtTotalTime.Text = FormatTime(_totalSeconds);
                UpdatePlayheadPosition();
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
            });
        }

        private void OnVlcTimeChanged(object? sender, LibVLCSharp.Shared.MediaPlayerTimeChangedEventArgs args)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                if (_isScrubbing) return;
                _currentSeconds = args.Time / 1000.0;
                TxtCurrentTime.Text = FormatTime(_currentSeconds);
                UpdatePlayheadPosition();
            });
        }

        private void OnWaveOutPlaybackStopped(object? sender, NAudio.Wave.StoppedEventArgs args)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                BtnPlayPause.Content = "▶";
            });
        }

        private void OnVlcEndReached(object? sender, EventArgs args)
        {
            // After EndReached, LibVLC parks the player at end-of-stream and a
            // bare Play() is a no-op. Stop()+seek-to-zero means the next Play
            // (whether from Space or BtnPlayPause) actually replays — the
            // "replay-after-stop" promise from bb6f503.
            try { _mediaPlayer?.Stop(); } catch { }
            Dispatcher.InvokeAsync(() =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                _currentSeconds = 0;
                TxtCurrentTime.Text = FormatTime(_currentSeconds);
                UpdatePlayheadPosition();
                BtnPlayPause.Content = "▶";
            });
        }

        private async Task InitializeAudioAsync(string path)
        {
            VideoPreview.Visibility = Visibility.Collapsed;
            WaveformCanvas.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;

            try
            {
                _waveformData = await AudioWaveformCache.LoadAsync(path);
                _totalSeconds = _waveformData.DurationSeconds;
                TxtTotalTime.Text = FormatTime(_totalSeconds);
                UpdateWaveformPath();
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: waveform decode failed");
            }

            try
            {
                _audioReader = new AudioFileReader(path);
                _waveOut = new WaveOutEvent();
                App.Audio?.ApplyPreferredDevice(_waveOut);
                _waveOut.Init(_audioReader);
                _waveOutStopped = OnWaveOutPlaybackStopped;
                _waveOut.PlaybackStopped += _waveOutStopped;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: audio playback init failed");
                _waveOut?.Dispose(); _waveOut = null;
                _audioReader?.Dispose(); _audioReader = null;
            }

            UpdatePlayheadPosition();
        }

        private void ShowPlaceholder()
        {
            VideoPreview.Visibility = Visibility.Collapsed;
            WaveformCanvas.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;

            var src = _enhancement.MediaSource ?? "";
            // "*" or empty source = wildcard binding ("works on any media").
            // The editor can't preview wildcard sources because there's no
            // media file to load — but the file is perfectly valid; the user
            // just needs to test it via the Deeper Player. Same goes for HT/
            // remote URLs — VLC can't preview those reliably so we route the
            // user to the Browser tab.
            if (string.IsNullOrWhiteSpace(src) || src.Contains('*'))
            {
                TxtPlaceholderIcon.Text = "✱";
                TxtPlaceholderTitle.Text = Loc.Get("deeper_editor_wildcard_preview_unavailable");
            }
            else
            {
                TxtPlaceholderIcon.Text = "🌐";
                TxtPlaceholderTitle.Text = Loc.Get("deeper_editor_remote_preview_unavailable");
            }
            TxtPlaceholderSource.Text = src;
        }

        private static bool IsLocalFile(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (source.Contains("://")) return false;
            if (source.Contains('*')) return false;
            // Reject UNC and extended-length prefixes: a shared .ccpenh.json
            // pointing at \\attacker-smb\share\beacon would leak the user's
            // NTLM hash on first access. No legitimate authoring case needs
            // these — users with network files map them to drive letters.
            if (!UrlSafety.IsSafeLocalAbsolute(source)) return false;
            return File.Exists(source);
        }

        private void UpdateWaveformPath()
        {
            if (_waveformData == null || _waveformData.Peaks.Length == 0)
            {
                WaveformPath.Data = null;
                return;
            }

            var peaks = _waveformData.Peaks;
            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            // Render as vertical bars (one per peak) — classic Audacity look.
            // Each bar centered on its X column, from (midY - amp) to (midY + amp).
            var midY = height / 2.0;
            var stepX = width / peaks.Length;
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                for (int i = 0; i < peaks.Length; i++)
                {
                    var x = i * stepX + stepX / 2.0;
                    var amp = peaks[i] * (height * 0.46);
                    if (amp < 0.5) amp = 0.5;
                    ctx.BeginFigure(new Point(x, midY - amp), false, false);
                    ctx.LineTo(new Point(x, midY + amp), true, false);
                }
            }
            geom.Freeze();
            WaveformPath.Data = geom;
        }

        // -- Transport ---------------------------------------------------------

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_browserSource != null)
            {
                // Browser source updates BtnPlayPause.Content from its poll loop
                // (OnBrowserTimeChanged), so we don't flip it manually here.
                if (_browserSource.IsPlaying) _browserSource.Pause();
                else _browserSource.Play();
                return;
            }
            if (_mediaPlayer != null)
            {
                if (_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Pause();
                    BtnPlayPause.Content = "▶";
                    _playheadTimer?.Stop();
                }
                else
                {
                    _mediaPlayer.Play();
                    BtnPlayPause.Content = "⏸";
                    _playheadTimer?.Start();
                }
            }
            else if (_waveOut != null)
            {
                if (_waveOut.PlaybackState == PlaybackState.Playing)
                {
                    _waveOut.Pause();
                    BtnPlayPause.Content = "▶";
                    _playheadTimer?.Stop();
                }
                else
                {
                    _waveOut.Play();
                    BtnPlayPause.Content = "⏸";
                    _playheadTimer?.Start();
                }
            }
        }

        private void PlayheadTimer_Tick(object? sender, EventArgs e)
        {
            if (_isScrubbing) return;
            if (_audioReader != null)
            {
                _currentSeconds = _audioReader.CurrentTime.TotalSeconds;
                TxtCurrentTime.Text = FormatTime(_currentSeconds);
                UpdatePlayheadPosition();
            }
            // Video time updates come via MediaPlayer.TimeChanged event already.
        }

        private void SeekToFraction(double frac)
        {
            frac = Math.Clamp(frac, 0, 1);
            _currentSeconds = frac * _totalSeconds;
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            if (_browserSource != null && _totalSeconds > 0)
            {
                _browserSource.Seek(_currentSeconds);
            }
            else if (_mediaPlayer != null && _totalSeconds > 0)
            {
                _mediaPlayer.SeekTo(TimeSpan.FromSeconds(_currentSeconds));
            }
            else if (_audioReader != null)
            {
                try { _audioReader.CurrentTime = TimeSpan.FromSeconds(_currentSeconds); }
                catch { /* unsupported on some streams */ }
            }
            UpdatePlayheadPosition();
        }

        private void UpdatePlayheadPosition()
        {
            if (TimelineCanvas.ActualWidth <= 0) return;
            double frac = _totalSeconds > 0 ? Math.Clamp(_currentSeconds / _totalSeconds, 0, 1) : 0;
            double x = frac * TimelineCanvas.ActualWidth;
            PlayheadLine.X1 = x;
            PlayheadLine.X2 = x;
            PlayheadLine.Y1 = 0;
            PlayheadLine.Y2 = TimelineCanvas.ActualHeight;
        }

        private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePlayheadPosition();
            UpdateWaveformPath();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
            RebuildRuleVisuals();
            RefreshLaneCounts();
        }

        // -- Timeline zoom -----------------------------------------------------

        private void TimelineScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Recompute Canvas.Width whenever the available viewport changes so
            // zoom=1 keeps the canvas stretched to fill, and higher zooms
            // proportionally expand it.
            ApplyZoom();
        }

        private void ApplyZoom(double? anchorViewportX = null)
        {
            if (TimelineScroll == null || TimelineCanvas == null) return;
            var viewportW = TimelineScroll.ViewportWidth;
            if (viewportW <= 0) return;

            // Capture the scroll-relative time of the anchor point (cursor X
            // for wheel zoom) so we can re-anchor after resize for a CapCut-
            // style "zoom centered on cursor" feel.
            double? timeAnchor = null;
            if (anchorViewportX is double ax && _totalSeconds > 0 && TimelineCanvas.ActualWidth > 0)
            {
                var contentX = TimelineScroll.HorizontalOffset + ax;
                timeAnchor = (contentX / TimelineCanvas.ActualWidth) * _totalSeconds;
            }

            double newWidth = viewportW * _zoomFactor;
            TimelineCanvas.Width = newWidth;

            if (TxtZoomLevel != null)
                TxtZoomLevel.Text = $"{(int)Math.Round(_zoomFactor * 100)}%";

            // Defer the rebuild + re-anchor until after layout sees the new
            // width, so ActualWidth reflects newWidth.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdatePlayheadPosition();
                UpdateWaveformPath();
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                RebuildRuleVisuals();

                if (timeAnchor is double ta && _totalSeconds > 0 && anchorViewportX is double ax2)
                {
                    var newContentX = (ta / _totalSeconds) * TimelineCanvas.ActualWidth;
                    var newOffset = newContentX - ax2;
                    TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, newOffset));
                }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void SetZoom(double newZoom, double? anchorViewportX = null)
        {
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - _zoomFactor) < 0.001) return;
            _zoomFactor = newZoom;
            ApplyZoom(anchorViewportX);
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomFactor * 1.5);
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomFactor / 1.5);

        // ±10% zoom on the embedded WebView2 preview (BrowserPreview). Distinct
        // from BtnZoomIn/Out above, which scale the timeline canvas — these
        // change CoreWebView2's page zoom for sites whose default rendering is
        // too small/large inside the preview pane. Clamped to keep the user
        // from zooming themselves into a stuck state.
        private void BtnPreviewZoomIn_Click(object sender, RoutedEventArgs e) => AdjustPreviewZoom(+0.10);
        private void BtnPreviewZoomOut_Click(object sender, RoutedEventArgs e) => AdjustPreviewZoom(-0.10);

        private void AdjustPreviewZoom(double delta)
        {
            try
            {
                if (BrowserPreview?.CoreWebView2 == null) return;
                var next = Math.Clamp(BrowserPreview.ZoomFactor + delta, 0.25, 5.0);
                BrowserPreview.ZoomFactor = next;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: preview zoom adjust failed: {Error}", ex.Message);
            }
        }

        private void TimelineScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (TimelineScroll == null) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Ctrl+wheel = zoom, anchored on the cursor position for a
                // CapCut-style experience.
                var pos = e.GetPosition(TimelineScroll);
                var factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
                SetZoom(_zoomFactor * factor, pos.X);
                e.Handled = true;
                return;
            }
            // Plain wheel = horizontal scroll along the timeline. Wheel down
            // advances time (HorizontalOffset += positive), wheel up rewinds.
            // Multiplier picked so one notch moves ~half a viewport at zoom 1.
            var newOffset = TimelineScroll.HorizontalOffset - e.Delta;
            if (newOffset < 0) newOffset = 0;
            var max = TimelineScroll.ExtentWidth - TimelineScroll.ViewportWidth;
            if (newOffset > max) newOffset = max;
            TimelineScroll.ScrollToHorizontalOffset(newOffset);
            e.Handled = true;
        }

        private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Shift+drag creates a region — power-user shortcut.
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && _totalSeconds > 0)
            {
                _dragMode = DragMode.CreateRegion;
                _dragCreateStartSec = MouseToSeconds(e);
                StartDragCreatePreview(_dragCreateStartSec, _dragCreateStartSec);
                TimelineCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Ctrl+drag = rubber-band multi-select (moved off the plain-drag
            // gesture so the latter can do player-style continuous scrubbing).
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _dragMode = DragMode.RubberBand;
                StartRubberBand(e.GetPosition(TimelineCanvas));
                TimelineCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Plain click+drag on empty timeline = scrub. Insta-seek on the
            // down-click and follow the cursor while dragging (any-video-
            // player behavior). Clears prior selection so the inspector
            // doesn't show a stale item.
            SelectNothing();
            _dragMode = DragMode.Scrub;
            _isScrubbing = true;
            TimelineCanvas.CaptureMouse();
            ApplyScrubFromMouse(e);
            e.Handled = true;
        }

        private void TimelineCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode == DragMode.RubberBand)
            {
                UpdateRubberBand(e.GetPosition(TimelineCanvas));
                return;
            }
            if (_dragMode == DragMode.CreateRegion)
            {
                UpdateDragCreatePreview(MouseToSeconds(e));
                return;
            }
            if (_dragMode == DragMode.ShiftHapticEvent && _draggedHaptic != null)
            {
                PushDragSnapshotOnce();
                var newStart = ComputeShift(_draggedHaptic, MouseToSeconds(e), _hapticDragOffsetSec, _draggedHaptic.Duration);
                _draggedHaptic.Start = newStart;
                MarkDirty();
                RebuildHapticVisuals();
                if (IsMultiDragActive)
                {
                    RebuildRegionVisuals();
                    RebuildEffectVisuals();
                }
                if (_selectedHaptic == _draggedHaptic) PopulateHapticEditor();
                return;
            }
            if (_dragMode == DragMode.ResizeHapticStart && _draggedHaptic != null)
            {
                PushDragSnapshotOnce();
                var endSec = _hapticDragStartSec + _draggedHaptic.Duration;
                var newStart = Math.Max(0, Math.Min(MouseToSeconds(e), endSec - 0.05));
                _draggedHaptic.Duration = endSec - newStart;
                _draggedHaptic.Start = newStart;
                MarkDirty();
                RebuildHapticVisuals();
                if (_selectedHaptic == _draggedHaptic) PopulateHapticEditor();
                return;
            }
            if (_dragMode == DragMode.ResizeHapticEnd && _draggedHaptic != null)
            {
                PushDragSnapshotOnce();
                var newEnd = Math.Min(_totalSeconds, Math.Max(MouseToSeconds(e), _draggedHaptic.Start + 0.05));
                _draggedHaptic.Duration = newEnd - _draggedHaptic.Start;
                MarkDirty();
                RebuildHapticVisuals();
                if (_selectedHaptic == _draggedHaptic) PopulateHapticEditor();
                return;
            }
            if (_dragMode == DragMode.DragRegion && _draggedRegion != null)
            {
                PushDragSnapshotOnce();
                var newStart = ComputeShift(_draggedRegion, MouseToSeconds(e), _regionDragOffsetSec, _regionDragOriginalLength);
                _draggedRegion.Start = newStart;
                _draggedRegion.End = newStart + _regionDragOriginalLength;
                MarkDirty();
                RebuildRegionVisuals();
                if (IsMultiDragActive)
                {
                    RebuildHapticVisuals();
                    RebuildEffectVisuals();
                }
                if (_selectedRegion == _draggedRegion) UpdateSelectedSidePanel();
                return;
            }
            if (_dragMode == DragMode.ResizeRegionStart && _draggedRegion != null)
            {
                PushDragSnapshotOnce();
                var newStart = Math.Max(0, Math.Min(MouseToSeconds(e), _draggedRegion.End - 0.05));
                _draggedRegion.Start = newStart;
                MarkDirty();
                RebuildRegionVisuals();
                if (_selectedRegion == _draggedRegion) UpdateSelectedSidePanel();
                return;
            }
            if (_dragMode == DragMode.ResizeRegionEnd && _draggedRegion != null)
            {
                PushDragSnapshotOnce();
                var newEnd = Math.Min(_totalSeconds, Math.Max(MouseToSeconds(e), _draggedRegion.Start + 0.05));
                _draggedRegion.End = newEnd;
                MarkDirty();
                RebuildRegionVisuals();
                if (_selectedRegion == _draggedRegion) UpdateSelectedSidePanel();
                return;
            }
            if (_dragMode == DragMode.DragEffect && _draggedEffect != null)
            {
                PushDragSnapshotOnce();
                var newStart = ComputeShift(_draggedEffect, MouseToSeconds(e), _effectDragOffsetSec, _effectDragOriginalDuration);
                _draggedEffect.Start = newStart;
                MarkDirty();
                RebuildEffectVisuals();
                if (IsMultiDragActive)
                {
                    RebuildRegionVisuals();
                    RebuildHapticVisuals();
                }
                if (_selectedEffect == _draggedEffect) UpdateSelectedSidePanelForEffect();
                return;
            }
            if (_dragMode == DragMode.ResizeEffectStart && _draggedEffect != null)
            {
                PushDragSnapshotOnce();
                var oldEnd = _draggedEffect.Start + Math.Max(0, _draggedEffect.Duration);
                var newStart = Math.Max(0, Math.Min(MouseToSeconds(e), oldEnd - 0.05));
                _draggedEffect.Duration = oldEnd - newStart;
                _draggedEffect.Start = newStart;
                _draggedEffect.EffectDurationMs = (int)Math.Max(50, _draggedEffect.Duration * 1000);
                MarkDirty();
                RebuildEffectVisuals();
                if (_selectedEffect == _draggedEffect) UpdateSelectedSidePanelForEffect();
                return;
            }
            if (_dragMode == DragMode.ResizeEffectEnd && _draggedEffect != null)
            {
                PushDragSnapshotOnce();
                var newEnd = Math.Min(_totalSeconds, Math.Max(MouseToSeconds(e), _draggedEffect.Start + 0.05));
                _draggedEffect.Duration = newEnd - _draggedEffect.Start;
                _draggedEffect.EffectDurationMs = (int)Math.Max(50, _draggedEffect.Duration * 1000);
                MarkDirty();
                RebuildEffectVisuals();
                if (_selectedEffect == _draggedEffect) UpdateSelectedSidePanelForEffect();
                return;
            }
            if (_dragMode == DragMode.Scrub && _isScrubbing) ApplyScrubFromMouse(e);
        }

        private void TimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode == DragMode.RubberBand)
            {
                FinishRubberBand(e.GetPosition(TimelineCanvas));
                TimelineCanvas.ReleaseMouseCapture();
                _dragMode = DragMode.None;
                // Ctrl+click without drag is a no-op — plain click handles
                // scrubbing via DragMode.Scrub, so no fallback needed here.
                return;
            }
            if (_dragMode == DragMode.CreateRegion)
            {
                var endSec = MouseToSeconds(e);
                FinishDragCreate(endSec);
                TimelineCanvas.ReleaseMouseCapture();
                _dragMode = DragMode.None;
                return;
            }
            if (_dragMode == DragMode.ShiftHapticEvent ||
                _dragMode == DragMode.ResizeHapticStart ||
                _dragMode == DragMode.ResizeHapticEnd)
            {
                _draggedHaptic = null;
                _draggedHapticTrack = null;
                _dragMode = DragMode.None;
                _dragSnapshotPushed = false;
                EndMultiDragCapture();
                TimelineCanvas.ReleaseMouseCapture();
                ScheduleValidation();
                BuildItemsList();
                return;
            }
            if (_dragMode == DragMode.DragRegion ||
                _dragMode == DragMode.ResizeRegionStart ||
                _dragMode == DragMode.ResizeRegionEnd)
            {
                _draggedRegion = null;
                _dragMode = DragMode.None;
                _dragSnapshotPushed = false;
                EndMultiDragCapture();
                TimelineCanvas.ReleaseMouseCapture();
                ScheduleValidation();
                BuildItemsList();
                return;
            }
            if (_dragMode == DragMode.DragEffect ||
                _dragMode == DragMode.ResizeEffectStart ||
                _dragMode == DragMode.ResizeEffectEnd)
            {
                _draggedEffect = null;
                _dragMode = DragMode.None;
                _dragSnapshotPushed = false;
                EndMultiDragCapture();
                TimelineCanvas.ReleaseMouseCapture();
                ScheduleValidation();
                BuildItemsList();
                return;
            }
            if (_dragMode == DragMode.Scrub)
            {
                _isScrubbing = false;
                _dragMode = DragMode.None;
                TimelineCanvas.ReleaseMouseCapture();
            }
        }

        // WPF doesn't fire MouseUp on a canvas that lost its mouse capture
        // mid-drag (Alt+Tab, popup steals focus, another control captures,
        // window deactivation). Without this hook the rubber-band rectangle
        // / drag-create preview stay on the canvas as orphan visuals, and
        // _dragMode stays stuck in a non-None state — both of which were
        // observed in the field on Ctrl+drag rubber-band selection.
        private void TimelineCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_rubberBandRect != null)
            {
                try { TimelineCanvas.Children.Remove(_rubberBandRect); } catch { }
                _rubberBandRect = null;
            }
            if (_dragCreatePreview != null)
            {
                try { TimelineCanvas.Children.Remove(_dragCreatePreview); } catch { }
                _dragCreatePreview = null;
            }
            _dragMode = DragMode.None;
            _isScrubbing = false;
            _dragSnapshotPushed = false;
            _draggedHaptic = null;
            _draggedHapticTrack = null;
            _draggedRegion = null;
            _draggedEffect = null;
            EndMultiDragCapture();
        }

        private void ApplyScrubFromMouse(MouseEventArgs e)
        {
            var pt = e.GetPosition(TimelineCanvas);
            var w = TimelineCanvas.ActualWidth;
            if (w <= 0) return;
            SeekToFraction(pt.X / w);
        }

        private double MouseToSeconds(MouseEventArgs e)
        {
            var pt = e.GetPosition(TimelineCanvas);
            var w = TimelineCanvas.ActualWidth;
            if (w <= 0 || _totalSeconds <= 0) return 0;
            var frac = Math.Clamp(pt.X / w, 0, 1);
            return frac * _totalSeconds;
        }

        // -- Region creation / selection --------------------------------------

        private void StartDragCreatePreview(double startSec, double endSec)
        {
            _dragCreatePreview = new System.Windows.Shapes.Rectangle
            {
                Fill = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush"),
                Stroke = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            TimelineCanvas.Children.Insert(0, _dragCreatePreview);
            UpdateDragCreatePreview(endSec);
        }

        private void UpdateDragCreatePreview(double endSec)
        {
            if (_dragCreatePreview == null) return;
            var w = TimelineCanvas.ActualWidth;
            var h = TimelineCanvas.ActualHeight;
            if (w <= 0 || _totalSeconds <= 0) return;

            var lo = Math.Min(_dragCreateStartSec, endSec);
            var hi = Math.Max(_dragCreateStartSec, endSec);
            var leftX = (lo / _totalSeconds) * w;
            var rightX = (hi / _totalSeconds) * w;

            _dragCreatePreview.Width = Math.Max(0, rightX - leftX);
            _dragCreatePreview.Height = h / 2.0;
            Canvas.SetLeft(_dragCreatePreview, leftX);
            Canvas.SetTop(_dragCreatePreview, 0);
        }

        private void FinishDragCreate(double endSec)
        {
            if (_dragCreatePreview != null)
            {
                TimelineCanvas.Children.Remove(_dragCreatePreview);
                _dragCreatePreview = null;
            }
            CreateRegion(_dragCreateStartSec, endSec);
        }

        private void CreateRegion(double a, double b)
        {
            var lo = Math.Min(a, b);
            var hi = Math.Max(a, b);
            if (hi - lo < 0.1) return;
            if (_totalSeconds > 0) hi = Math.Min(hi, _totalSeconds);
            lo = Math.Max(0, lo);
            PushUndoSnapshot();

            var region = new Region
            {
                Id = NextRegionId(),
                Start = lo,
                End = hi,
                Label = "",
                Color = NextRegionColor()
            };
            _enhancement.Regions.Add(region);
            MarkDirty();
            RebuildRegionVisuals();
            SelectRegion(region);
            ScheduleValidation();
        }

        private string NextRegionId()
        {
            int n = _enhancement.Regions.Count + 1;
            while (true)
            {
                var candidate = "r" + n;
                if (!_enhancement.Regions.Any(r => r.Id == candidate)) return candidate;
                n++;
            }
        }

        private string NextRegionColor()
        {
            return RegionPalette[_enhancement.Regions.Count % RegionPalette.Length];
        }

        private void SelectNothing()
        {
            _selectedRegion = null;
            _selectedHaptic = null;
            _selectedHapticTrack = null;
            _selectedRule = null;
            _selectedEffect = null;
            _selectionSet.Clear();
            EndGazePick(commit: false);
            UpdateSelectedSidePanel();
            ScrollInspectorToTop();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
            RefreshRulesList();
        }

        private void SelectRegion(Region? region)
        {
            _selectedRegion = region;
            _selectedHaptic = null;
            _selectedHapticTrack = null;
            _selectedRule = null;
            _selectedEffect = null;
            EndGazePick(commit: false);
            UpdateSelectedSidePanel();
            ScrollInspectorToTop();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
            RebuildRuleVisuals();
            RefreshRulesList();
        }

        // Finds the first rule whose RegionConstraint matches the given region
        // id. Used by the band-click handler to route to the rule editor when
        // the band represents a rule's spatial/temporal scope.
        private EnhancementRule? FindRuleByRegionConstraint(string? regionId)
        {
            if (string.IsNullOrEmpty(regionId)) return null;
            foreach (var rule in _enhancement.Rules)
            {
                if (rule != null && string.Equals(rule.RegionConstraint, regionId, StringComparison.Ordinal))
                    return rule;
            }
            return null;
        }

        private void SelectHaptic(HapticTrack track, HapticEvent ev)
        {
            _selectedRegion = null;
            _selectedHaptic = ev;
            _selectedHapticTrack = track;
            _selectedRule = null;
            _selectedEffect = null;
            EndGazePick(commit: false);
            UpdateSelectedSidePanel();
            ScrollInspectorToTop();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
            RebuildRuleVisuals();
            RefreshRulesList();
        }

        private void SelectRule(EnhancementRule? rule)
        {
            _selectedRegion = null;
            _selectedHaptic = null;
            _selectedHapticTrack = null;
            _selectedRule = rule;
            _selectedEffect = null;
            EndGazePick(commit: false);
            UpdateSelectedSidePanel();
            ScrollInspectorToTop();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
            RebuildRuleVisuals();
            RefreshRulesList();
        }

        private void UpdateSelectedSidePanel()
        {
            if (SelectedPlaceholder == null || RegionEditor == null || HapticEventEditor == null || RuleEditor == null) return;

            // Always reset the unified-editor groups; the unified path repopulates if needed.
            HideAllEditors();

            if (_selectedEffect != null)
            {
                UpdateSelectedSidePanelForEffect();
                return;
            }

            if (_selectedRegion != null)
            {
                SelectedPlaceholder.Visibility = Visibility.Collapsed;
                RegionEditor.Visibility = Visibility.Visible;
                HapticEventEditor.Visibility = Visibility.Collapsed;
                RuleEditor.Visibility = Visibility.Collapsed;

                _suppressDirty = true;
                try
                {
                    TxtRegionId.Text = _selectedRegion.Id;
                    TxtRegionLabel.Text = _selectedRegion.Label;
                    TxtRegionStart.Text = _selectedRegion.Start.ToString("0.##", CultureInfo.InvariantCulture);
                    TxtRegionEnd.Text = _selectedRegion.End.ToString("0.##", CultureInfo.InvariantCulture);
                    TxtRegionColor.Text = _selectedRegion.Color;
                    UpdateRegionColorSwatchPreview();
                }
                finally { _suppressDirty = false; }
                return;
            }

            if (_selectedHaptic != null && _selectedHapticTrack != null)
            {
                SelectedPlaceholder.Visibility = Visibility.Collapsed;
                RegionEditor.Visibility = Visibility.Collapsed;
                HapticEventEditor.Visibility = Visibility.Visible;
                RuleEditor.Visibility = Visibility.Collapsed;
                PopulateHapticEditor();
                return;
            }

            if (_selectedRule != null)
            {
                SelectedPlaceholder.Visibility = Visibility.Collapsed;
                RegionEditor.Visibility = Visibility.Collapsed;
                HapticEventEditor.Visibility = Visibility.Collapsed;
                RuleEditor.Visibility = Visibility.Visible;
                PopulateRuleEditor();
                return;
            }

            SelectedPlaceholder.Visibility = Visibility.Visible;
            RegionEditor.Visibility = Visibility.Collapsed;
            HapticEventEditor.Visibility = Visibility.Collapsed;
            RuleEditor.Visibility = Visibility.Collapsed;

            // Keep the selection summary strip in sync even when UpdateSelectedSidePanel
            // is called outside the SelectXxx path (e.g. drag-end). Cheap; safe to repeat.
            UpdateSelectionSummary();
        }

        private void UpdateRegionColorSwatchPreview()
        {
            var brush = TryParseBrush(_selectedRegion?.Color ?? "#7B5CFF");
            if (brush != null) RegionColorSwatch.Background = brush;
        }

        private void RegionField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDirty || _selectedRegion == null) return;
            _selectedRegion.Label = TxtRegionLabel.Text ?? "";
            if (double.TryParse(TxtRegionStart.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                _selectedRegion.Start = Math.Max(0, s);
            if (double.TryParse(TxtRegionEnd.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ev))
                _selectedRegion.End = ev;
            if (!string.IsNullOrWhiteSpace(TxtRegionColor.Text))
            {
                _selectedRegion.Color = TxtRegionColor.Text.Trim();
                UpdateRegionColorSwatchPreview();
            }
            MarkDirty();
            RebuildRegionVisuals();
            ScheduleValidation();
        }

        private void BtnDeleteRegion_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRegion == null) return;
            PushUndoSnapshot();
            _enhancement.Regions.Remove(_selectedRegion);
            SelectNothing();
            MarkDirty();
            RebuildRegionVisuals();
            ScheduleValidation();
        }

        // -- Region rendering --------------------------------------------------

        private void RebuildRegionVisuals()
        {
            if (TimelineCanvas == null) return;
            foreach (var r in _regionVisuals) TimelineCanvas.Children.Remove(r);
            _regionVisuals.Clear();

            if (_totalSeconds <= 0 || TimelineCanvas.ActualWidth <= 0)
            {
                EnsurePlayheadOnTop();
                return;
            }

            foreach (var region in _enhancement.Regions)
            {
                var rect = BuildRegionVisual(region);
                if (rect != null)
                {
                    TimelineCanvas.Children.Insert(0, rect);
                    _regionVisuals.Add(rect);
                }
            }
            EnsurePlayheadOnTop();
        }

        private System.Windows.Shapes.Rectangle? BuildRegionVisual(Region region)
        {
            var w = TimelineCanvas.ActualWidth;
            var h = TimelineCanvas.ActualHeight;
            if (w <= 0 || _totalSeconds <= 0) return null;

            var laneH = h / 2.0;
            var startX = Math.Max(0, (region.Start / _totalSeconds) * w);
            var endX = Math.Min(w, (region.End / _totalSeconds) * w);
            var width = Math.Max(0, endX - startX);

            var color = TryParseColor(region.Color) ?? Colors.MediumPurple;
            var fill = System.Windows.Media.Color.FromArgb(80, color.R, color.G, color.B);
            var isSelected = _selectedRegion == region || IsInSelectionSet(region);

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = laneH,
                Fill = new System.Windows.Media.SolidColorBrush(fill),
                Stroke = new System.Windows.Media.SolidColorBrush(color),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = Cursors.Hand,
                Tag = region,
                ToolTip = string.IsNullOrEmpty(region.Label) ? region.Id : $"{region.Id} — {region.Label}"
            };
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, 0);
            rect.MouseLeftButtonDown += RegionRect_MouseLeftButtonDown;
            rect.MouseMove += RegionRect_MouseMove;
            return rect;
        }

        private void RegionRect_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode != DragMode.None) return;
            if (sender is not System.Windows.Shapes.Rectangle r) return;
            var pos = e.GetPosition(r);
            r.Cursor = (pos.X <= EdgeResizePx || pos.X >= r.ActualWidth - EdgeResizePx)
                ? Cursors.SizeWE
                : Cursors.SizeAll;
        }

        private void EnsurePlayheadOnTop()
        {
            // Keep the playhead in front of dynamically inserted region/haptic/effect
            // visuals. (Pre-redesign: LaneDivider + lane labels were also kept on top;
            // those visual elements are gone now that the timeline is unified.)
            if (PlayheadLine == null) return;
            if (TimelineCanvas.Children.Contains(PlayheadLine))
                TimelineCanvas.Children.Remove(PlayheadLine);
            TimelineCanvas.Children.Add(PlayheadLine);
        }

        private void RegionRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Rectangle r || r.Tag is not Region region) return;

            // Snapshot pos + width BEFORE selecting — SelectRegion rebuilds visuals
            // which detaches `r` from the visual tree, after which e.GetPosition(r)
            // returns ~(0,0) and trips the left-edge resize check unconditionally.
            var pos = e.GetPosition(r);
            var rectWidth = r.ActualWidth;
            bool ctrl = IsCtrlDown();

            HandleSelectionClick(region);
            // Ctrl+Click is a pure selection toggle — no drag-init, no mouse
            // capture, no primary swap. Refresh visuals so the new selection-
            // set membership is reflected immediately.
            if (ctrl)
            {
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                e.Handled = true;
                return;
            }

            // If a rule constrains this region, treat the band as the rule's
            // visual representation: select the Rule (so trigger / action /
            // gaze rect / "Pick on video…" controls are reachable from a single
            // click) and let the rule editor's region-details sub-panel handle
            // label/color/start-end. Orphan regions still fall back to the
            // standalone Region editor.
            var attachedRule = FindRuleByRegionConstraint(region.Id);
            if (attachedRule != null)
            {
                SelectRule(attachedRule);
            }
            else
            {
                SelectRegion(region);
            }

            _draggedRegion = region;
            _regionDragOriginalLength = Math.Max(0, region.End - region.Start);
            BeginMultiDragCapture();

            if (pos.X <= EdgeResizePx)
            {
                _dragMode = DragMode.ResizeRegionStart;
            }
            else if (pos.X >= rectWidth - EdgeResizePx)
            {
                _dragMode = DragMode.ResizeRegionEnd;
            }
            else
            {
                _dragMode = DragMode.DragRegion;
                _regionDragOffsetSec = MouseToSeconds(e) - region.Start;
            }
            TimelineCanvas.CaptureMouse();
            e.Handled = true;
        }

        // -- Haptic events: rendering + interaction ---------------------------

        private HapticTrack EnsureDefaultTrack()
        {
            if (_enhancement.HapticTracks.Count == 0)
                _enhancement.HapticTracks.Add(new HapticTrack { Id = DefaultTrackId });
            return _enhancement.HapticTracks[0];
        }

        private void CreateHapticEventAtPlayhead()
        {
            if (_totalSeconds <= 0) return;
            var track = EnsureDefaultTrack();
            var start = _currentSeconds;
            var duration = 1.0;
            // Avoid overlapping: if would overlap, nudge to first free slot after current events.
            foreach (var existing in track.Events.OrderBy(x => x.Start))
            {
                if (start < existing.Start + existing.Duration && start + duration > existing.Start)
                    start = existing.Start + existing.Duration + 0.05;
            }
            if (start + duration > _totalSeconds) start = Math.Max(0, _totalSeconds - duration);

            // Snapshot BEFORE the mutation so Ctrl+Z restores the pre-create
            // state. Without this push, undo silently bypasses the H-keypress
            // create and pops back to the user's previous edit, leaving the
            // freshly added haptic in place.
            PushUndoSnapshot();

            var ev = new HapticEvent
            {
                Start = start,
                Duration = duration,
                Intensity = 1.0,
                PatternName = "Pulse"
            };
            track.Events.Add(ev);
            MarkDirty();
            RebuildHapticVisuals();
            SelectHaptic(track, ev);
            ScheduleValidation();
        }

        private void RebuildHapticVisuals()
        {
            if (TimelineCanvas == null) return;
            foreach (var v in _hapticVisuals) TimelineCanvas.Children.Remove(v);
            _hapticVisuals.Clear();

            if (_totalSeconds <= 0 || TimelineCanvas.ActualWidth <= 0)
            {
                EnsurePlayheadOnTop();
                return;
            }

            foreach (var track in _enhancement.HapticTracks)
            {
                foreach (var ev in track.Events)
                {
                    var rect = BuildHapticVisual(track, ev);
                    if (rect != null)
                    {
                        TimelineCanvas.Children.Insert(0, rect);
                        _hapticVisuals.Add(rect);
                    }
                }
            }
            EnsurePlayheadOnTop();
        }

        private System.Windows.Shapes.Rectangle? BuildHapticVisual(HapticTrack track, HapticEvent ev)
        {
            var w = TimelineCanvas.ActualWidth;
            var h = TimelineCanvas.ActualHeight;
            if (w <= 0 || _totalSeconds <= 0) return null;

            var laneTop = h / 2.0 + 2;
            var laneH = Math.Max(0, h / 2.0 - 4);
            var startX = Math.Max(0, (ev.Start / _totalSeconds) * w);
            var endX = Math.Min(w, ((ev.Start + ev.Duration) / _totalSeconds) * w);
            var width = Math.Max(0, endX - startX);

            var isSelected = _selectedHaptic == ev || IsInSelectionSet(ev);
            // Resolve DeeperAccent from the theme so a runtime palette
            // change reflows the haptic stroke too. Falls back to the
            // canonical violet if the resource lookup fails (e.g. unit
            // tests that don't load App.xaml).
            System.Windows.Media.Color accent;
            try
            {
                var brush = (System.Windows.Media.SolidColorBrush)Application.Current.FindResource("DeeperAccentBrush");
                accent = brush.Color;
            }
            catch
            {
                accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF7B5CFF");
            }
            var fill = System.Windows.Media.Color.FromArgb(isSelected ? (byte)180 : (byte)130, accent.R, accent.G, accent.B);

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = laneH,
                Fill = new System.Windows.Media.SolidColorBrush(fill),
                Stroke = new System.Windows.Media.SolidColorBrush(accent),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = Cursors.SizeAll,
                Tag = (track, ev),
                ToolTip = string.IsNullOrEmpty(ev.PatternName)
                    ? $"{track.Id} · custom · {ev.Duration:0.##}s"
                    : $"{track.Id} · {ev.PatternName} · {ev.Duration:0.##}s"
            };
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, laneTop);
            rect.MouseLeftButtonDown += HapticRect_MouseLeftButtonDown;
            rect.MouseMove += HapticRect_MouseMove;
            return rect;
        }

        // Update cursor as the pointer hovers near edges so the user can tell
        // resize from drag-move.
        private void HapticRect_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode != DragMode.None) return; // don't churn cursor mid-drag
            if (sender is not System.Windows.Shapes.Rectangle r) return;
            var pos = e.GetPosition(r);
            r.Cursor = (pos.X <= EdgeResizePx || pos.X >= r.ActualWidth - EdgeResizePx)
                ? Cursors.SizeWE
                : Cursors.SizeAll;
        }

        private void HapticRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Rectangle r || r.Tag is not ValueTuple<HapticTrack, HapticEvent> tuple)
                return;

            var (track, ev) = tuple;
            // Snapshot pos + width BEFORE selecting — SelectHaptic rebuilds visuals
            // which detaches `r` from the visual tree, after which e.GetPosition(r)
            // returns ~(0,0) and trips the left-edge resize check unconditionally.
            var pos = e.GetPosition(r);
            var rectWidth = r.ActualWidth;
            bool ctrl = IsCtrlDown();
            HandleSelectionClick(ev);
            // Ctrl+Click is a pure selection toggle — no drag-init, no mouse
            // capture, no primary swap.
            if (ctrl)
            {
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                e.Handled = true;
                return;
            }
            SelectHaptic(track, ev);

            // Begin drag-shift on pointer hold (no Shift modifier — that's region-create).
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                if (pos.X <= EdgeResizePx)
                {
                    _dragMode = DragMode.ResizeHapticStart;
                    _draggedHaptic = ev;
                    _draggedHapticTrack = track;
                    _hapticDragStartSec = ev.Start;
                    TimelineCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
                if (pos.X >= rectWidth - EdgeResizePx)
                {
                    _dragMode = DragMode.ResizeHapticEnd;
                    _draggedHaptic = ev;
                    _draggedHapticTrack = track;
                    _hapticDragStartSec = ev.Start;
                    TimelineCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }

            // Fall through to existing middle-drag (shift-position) logic.
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                _dragMode = DragMode.ShiftHapticEvent;
                _draggedHaptic = ev;
                _draggedHapticTrack = track;
                _hapticDragStartSec = ev.Start;
                _hapticDragOffsetSec = MouseToSeconds(e) - ev.Start;
                BeginMultiDragCapture();
                TimelineCanvas.CaptureMouse();
            }
            e.Handled = true;
        }

        // -- Haptic side panel + curve editor ---------------------------------

        private void PopulateHapticEditor()
        {
            if (_selectedHaptic == null || _selectedHapticTrack == null) return;

            _suppressDirty = true;
            _suppressPatternSync = true;
            try
            {
                TxtHapticTrackId.Text = _selectedHapticTrack.Id;
                TxtHapticStart.Text = _selectedHaptic.Start.ToString("0.##", CultureInfo.InvariantCulture);
                TxtHapticDuration.Text = _selectedHaptic.Duration.ToString("0.##", CultureInfo.InvariantCulture);
                SliderHapticIntensity.Value = Math.Clamp(_selectedHaptic.Intensity, 0.0, 1.0);
                TxtHapticIntensityValue.Text = $"{(int)(SliderHapticIntensity.Value * 100)}%";

                var isCustom = _selectedHaptic.CustomPattern != null && _selectedHaptic.CustomPattern.Count > 0;
                if (isCustom)
                {
                    CmbHapticPattern.SelectedIndex = StockHapticPatterns.Names.Count; // "Custom…"
                    CurveEditorPanel.Visibility = Visibility.Visible;
                    EnsureCurveSeed();
                    RebuildCurveEditor();
                }
                else
                {
                    var idx = -1;
                    if (!string.IsNullOrEmpty(_selectedHaptic.PatternName))
                    {
                        for (int i = 0; i < StockHapticPatterns.Names.Count; i++)
                            if (StockHapticPatterns.Names[i] == _selectedHaptic.PatternName) { idx = i; break; }
                    }
                    CmbHapticPattern.SelectedIndex = idx >= 0 ? idx : 0;
                    CurveEditorPanel.Visibility = Visibility.Collapsed;
                }
            }
            finally
            {
                _suppressDirty = false;
                _suppressPatternSync = false;
            }
        }

        private void HapticField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDirty || _selectedHaptic == null) return;
            if (double.TryParse(TxtHapticStart.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                _selectedHaptic.Start = Math.Max(0, s);
            if (double.TryParse(TxtHapticDuration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                _selectedHaptic.Duration = Math.Max(0.05, d);
            MarkDirty();
            RebuildHapticVisuals();
            ScheduleValidation();
        }

        private void SliderHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtHapticIntensityValue != null)
                TxtHapticIntensityValue.Text = $"{(int)(SliderHapticIntensity.Value * 100)}%";
            if (_suppressDirty || _selectedHaptic == null) return;
            _selectedHaptic.Intensity = Math.Clamp(SliderHapticIntensity.Value, 0.0, 1.0);
            MarkDirty();
            ScheduleValidation();
        }

        private void CmbHapticPattern_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPatternSync || _selectedHaptic == null) return;
            var idx = CmbHapticPattern.SelectedIndex;
            if (idx < 0) return;

            if (idx >= StockHapticPatterns.Names.Count)
            {
                // Custom...
                _selectedHaptic.CustomPattern ??= StockHapticPatterns.SeedCustomFrom(_selectedHaptic.PatternName);
                _selectedHaptic.PatternName = null;
                CurveEditorPanel.Visibility = Visibility.Visible;
                EnsureCurveSeed();
                RebuildCurveEditor();
            }
            else
            {
                _selectedHaptic.CustomPattern = null;
                _selectedHaptic.PatternName = StockHapticPatterns.Names[idx];
                CurveEditorPanel.Visibility = Visibility.Collapsed;
            }
            MarkDirty();
            RebuildHapticVisuals();
            ScheduleValidation();
        }

        private void EnsureCurveSeed()
        {
            if (_selectedHaptic == null) return;
            if (_selectedHaptic.CustomPattern == null || _selectedHaptic.CustomPattern.Count < 2)
                _selectedHaptic.CustomPattern = StockHapticPatterns.SeedCustomFrom(_selectedHaptic.PatternName);
        }

        private void CurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RebuildCurveEditor();

        private void RebuildCurveEditor()
        {
            if (CurveCanvas == null) return;
            CurveCanvas.Children.Clear();
            _curvePath = null;
            _curveHandles.Clear();

            if (_selectedHaptic?.CustomPattern == null || _selectedHaptic.CustomPattern.Count == 0) return;
            var w = CurveCanvas.ActualWidth;
            var h = CurveCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Background grid
            for (int i = 1; i <= 3; i++)
            {
                var y = h * i / 4.0;
                var line = new System.Windows.Shapes.Line
                {
                    X1 = 0, X2 = w, Y1 = y, Y2 = y,
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = 0.5,
                    IsHitTestVisible = false
                };
                CurveCanvas.Children.Add(line);
            }

            // Polyline through the keyframes
            var pts = _selectedHaptic.CustomPattern;
            var geom = new System.Windows.Media.StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(KeyframeToCanvas(pts[0], w, h), false, false);
                for (int i = 1; i < pts.Count; i++)
                    ctx.LineTo(KeyframeToCanvas(pts[i], w, h), true, false);
            }
            geom.Freeze();
            _curvePath = new System.Windows.Shapes.Path
            {
                Data = geom,
                Stroke = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                StrokeThickness = 1.6,
                IsHitTestVisible = false
            };
            CurveCanvas.Children.Add(_curvePath);

            // Handles (5 fixed-X dots, drag intensity vertically)
            for (int i = 0; i < pts.Count; i++)
            {
                var pt = KeyframeToCanvas(pts[i], w, h);
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 10, Height = 10,
                    Fill = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1.2,
                    Cursor = Cursors.SizeNS,
                    Tag = i
                };
                Canvas.SetLeft(dot, pt.X - 5);
                Canvas.SetTop(dot, pt.Y - 5);
                dot.MouseLeftButtonDown += CurveHandle_MouseDown;
                dot.MouseMove += CurveHandle_MouseMove;
                dot.MouseLeftButtonUp += CurveHandle_MouseUp;
                CurveCanvas.Children.Add(dot);
                _curveHandles.Add(dot);
            }
        }

        private static Point KeyframeToCanvas(double[] kf, double w, double h)
        {
            var t = Math.Clamp(kf.Length > 0 ? kf[0] : 0, 0, 1);
            var v = Math.Clamp(kf.Length > 1 ? kf[1] : 0, 0, 1);
            return new Point(t * w, h - (v * h));
        }

        private void CurveHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Ellipse el && el.Tag is int idx)
            {
                _draggingCurveIndex = idx;
                el.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CurveHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingCurveIndex < 0 || _selectedHaptic?.CustomPattern == null) return;
            if (sender is not System.Windows.Shapes.Ellipse el) return;
            var pt = e.GetPosition(CurveCanvas);
            var h = CurveCanvas.ActualHeight;
            if (h <= 0) return;
            var v = Math.Clamp(1.0 - (pt.Y / h), 0.0, 1.0);
            var kf = _selectedHaptic.CustomPattern[_draggingCurveIndex];
            kf[1] = v;
            MarkDirty();
            RebuildCurveEditor();
            ScheduleValidation();
        }

        private void CurveHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Ellipse el) el.ReleaseMouseCapture();
            _draggingCurveIndex = -1;
        }

        private void BtnResetCurve_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHaptic == null) return;
            _selectedHaptic.CustomPattern = StockHapticPatterns.SeedCustomFrom(null);
            MarkDirty();
            RebuildCurveEditor();
            ScheduleValidation();
        }

        private async void BtnTestHaptic_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHaptic == null) return;

            IList<double[]>? kf = null;
            if (_selectedHaptic.CustomPattern != null && _selectedHaptic.CustomPattern.Count > 0)
                kf = _selectedHaptic.CustomPattern;
            else if (!string.IsNullOrEmpty(_selectedHaptic.PatternName)
                     && StockHapticPatterns.TryGet(_selectedHaptic.PatternName, out var named) && named != null)
                kf = named;

            if (kf == null) return;
            var durationMs = (int)Math.Max(50, _selectedHaptic.Duration * 1000);
            var samples = StockHapticPatterns.Sample(kf, _selectedHaptic.Intensity, durationMs);
            try
            {
                if (App.Haptics == null || !App.Haptics.IsConnected)
                {
                    TxtValidationSummary.Text = Loc.Get("deeper_editor_haptic_test_no_device");
                    TxtValidationSummary.Foreground = (System.Windows.Media.Brush)FindResource("PinkSoftBrush");
                    return;
                }
                await App.Haptics.SetSyncPatternAsync(samples, durationMs);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: haptic test failed");
            }
        }

        private void BtnDeleteHaptic_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHaptic == null || _selectedHapticTrack == null) return;
            PushUndoSnapshot();
            _selectedHapticTrack.Events.Remove(_selectedHaptic);
            // Remove now-empty default track to keep file clean.
            if (_selectedHapticTrack.Events.Count == 0 && _selectedHapticTrack.Id == DefaultTrackId)
                _enhancement.HapticTracks.Remove(_selectedHapticTrack);
            SelectNothing();
            MarkDirty();
            RebuildHapticVisuals();
            ScheduleValidation();
        }

        // -- Rules list -------------------------------------------------------

        private void RefreshRulesList()
        {
            // Refreshes the selection summary strip + the sidebar Items list.
            // All ~11 legacy call sites (add/delete/select/drag-end) flow
            // through here, so wiring one place keeps both views fresh
            // without touching every caller.
            UpdateSelectionSummary();
            BuildItemsList();
        }

        // Mission 1 commit 5 — BuildRuleRow + SummarizeRule deleted. They
        // populated the pre-unified-timeline standalone Rules section in
        // the sidebar (gone since the unified redesign); the only remaining
        // caller path was RefreshRulesList → BuildItemListRow, both of which
        // were removed by Mission 1 commits 3-5.

        // -- Friendly names for trigger / action types ------------------------

        private static string FriendlyTriggerName(string type) => type switch
        {
            TriggerTypes.GazeTarget    => Loc.Get("deeper_friendly_trigger_gaze_target"),
            TriggerTypes.GazeAvoid     => Loc.Get("deeper_friendly_trigger_gaze_avoid"),
            TriggerTypes.AttentionLost => Loc.Get("deeper_friendly_trigger_attention_lost"),
            TriggerTypes.BlinkDetected => Loc.Get("deeper_friendly_trigger_blink_detected"),
            TriggerTypes.MouthOpen     => Loc.Get("deeper_friendly_trigger_mouth_open"),
            TriggerTypes.TimeReached   => Loc.Get("deeper_friendly_trigger_time_reached"),
            TriggerTypes.RegionEntered => Loc.Get("deeper_friendly_trigger_region_entered"),
            TriggerTypes.RegionExited  => Loc.Get("deeper_friendly_trigger_region_exited"),
            _                          => string.IsNullOrEmpty(type) ? "?" : type
        };

        private static string FriendlyActionName(string type) => type switch
        {
            ActionTypes.Seek          => Loc.Get("deeper_friendly_action_seek"),
            ActionTypes.LoopRegion    => Loc.Get("deeper_friendly_action_loop_region"),
            ActionTypes.Pause         => Loc.Get("deeper_friendly_action_pause"),
            ActionTypes.PlayAudio     => Loc.Get("deeper_friendly_action_play_audio"),
            ActionTypes.TriggerHaptic => Loc.Get("deeper_friendly_action_trigger_haptic"),
            ActionTypes.TriggerEffect => Loc.Get("deeper_friendly_action_trigger_effect"),
            ActionTypes.ScreenShake   => Loc.Get("deeper_friendly_action_screen_shake"),
            ActionTypes.SetIntensity  => Loc.Get("deeper_friendly_action_set_intensity"),
            _                         => string.IsNullOrEmpty(type) ? "?" : type
        };

        // -- Help popups: list every trigger / action with its description ----

        private void BtnTriggerHelp_Click(object sender, RoutedEventArgs e)
        {
            var isAudio = _enhancement?.MediaType == MediaTypes.Audio;
            var types = isAudio ? TriggerTypesForAudio : TriggerTypesForVideo;
            var sb = new System.Text.StringBuilder();
            foreach (var t in types)
            {
                sb.Append("• ").AppendLine(FriendlyTriggerName(t));
                var desc = Loc.Get(TriggerDescriptionKey(t));
                if (!string.IsNullOrEmpty(desc) && desc != TriggerDescriptionKey(t))
                    sb.Append("  ").AppendLine(desc);
                sb.AppendLine();
            }
            MessageBox.Show(this, sb.ToString().TrimEnd(),
                Loc.Get("deeper_editor_help_browse_triggers"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnActionHelp_Click(object sender, RoutedEventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var a in AllActionTypes)
            {
                sb.Append("• ").AppendLine(FriendlyActionName(a));
                var desc = Loc.Get(ActionDescriptionKey(a));
                if (!string.IsNullOrEmpty(desc) && desc != ActionDescriptionKey(a))
                    sb.Append("  ").AppendLine(desc);
                sb.AppendLine();
            }
            MessageBox.Show(this, sb.ToString().TrimEnd(),
                Loc.Get("deeper_editor_help_browse_actions"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnRegionHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, Loc.Get("deeper_editor_help_region_body"),
                Loc.Get("deeper_editor_help_region"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Mission 1 commit 5 — BtnAddRule_Click deleted. No XAML referenced
        // it; superseded by BtnAddRuleHero_Click in Unified.cs:129 (which
        // uses AddRuleAt) and the right-click timeline "Add Rule →" menu.

        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRule == null) return;
            PushUndoSnapshot();
            _enhancement.Rules.Remove(_selectedRule);
            SelectNothing();
            MarkDirty();
            RebuildRuleVisuals();
            ScheduleValidation();
        }

        // -- Rule editor population -------------------------------------------

        private static readonly string[] TriggerTypesForVideo =
        {
            TriggerTypes.GazeTarget, TriggerTypes.GazeAvoid, TriggerTypes.AttentionLost,
            TriggerTypes.BlinkDetected, TriggerTypes.MouthOpen,
            TriggerTypes.TimeReached, TriggerTypes.RegionEntered, TriggerTypes.RegionExited
        };
        private static readonly string[] TriggerTypesForAudio =
        {
            TriggerTypes.TimeReached, TriggerTypes.RegionEntered, TriggerTypes.RegionExited
        };
        private static readonly string[] AllActionTypes =
        {
            ActionTypes.Seek, ActionTypes.LoopRegion, ActionTypes.Pause,
            ActionTypes.PlayAudio, ActionTypes.TriggerHaptic, ActionTypes.TriggerEffect,
            ActionTypes.ScreenShake, ActionTypes.SetIntensity
        };

        private void PopulateRuleEditor()
        {
            if (_selectedRule == null) return;
            _suppressRuleSync = true;
            try
            {
                var idx = _enhancement.Rules.IndexOf(_selectedRule);
                TxtRuleHeader.Text = idx >= 0 ? $"rule #{idx + 1}" : "rule";
                ChkRuleEnabled.IsChecked = _selectedRule.Enabled;

                // Trigger type combo (filtered by media_type). ComboBoxItem so
                // each entry shows a friendly name + a hover tooltip with the
                // long description; raw enum string lives in Tag.
                var isAudio = _enhancement.MediaType == MediaTypes.Audio;
                var triggerOptions = isAudio ? TriggerTypesForAudio : TriggerTypesForVideo;
                CmbTriggerType.Items.Clear();
                int trigIdx = 0;
                for (int i = 0; i < triggerOptions.Length; i++)
                {
                    var rawType = triggerOptions[i];
                    var item = new ComboBoxItem
                    {
                        Content = FriendlyTriggerName(rawType),
                        Tag = rawType,
                        ToolTip = Loc.Get(TriggerDescriptionKey(rawType))
                    };
                    CmbTriggerType.Items.Add(item);
                    if (rawType == (_selectedRule.Trigger?.Type ?? "")) trigIdx = i;
                }
                CmbTriggerType.SelectedIndex = trigIdx;

                // Action combo (same pattern).
                CmbActionType.Items.Clear();
                int actIdx = 0;
                for (int i = 0; i < AllActionTypes.Length; i++)
                {
                    var rawType = AllActionTypes[i];
                    var item = new ComboBoxItem
                    {
                        Content = FriendlyActionName(rawType),
                        Tag = rawType,
                        ToolTip = Loc.Get(ActionDescriptionKey(rawType))
                    };
                    CmbActionType.Items.Add(item);
                    if (rawType == (_selectedRule.Action?.Type ?? "")) actIdx = i;
                }
                CmbActionType.SelectedIndex = actIdx;

                // Region constraint combo.
                RebuildRegionConstraintCombo();

                TxtRuleCooldown.Text = _selectedRule.CooldownMs.ToString(CultureInfo.InvariantCulture);

                BuildTriggerFields();
                BuildActionFields();
                PopulateRuleBandDetails();
            }
            finally { _suppressRuleSync = false; }
        }

        // Sync the rule editor's "Region details" sub-panel with whichever
        // Region the current rule constrains to. Hidden when the rule has no
        // RegionConstraint (e.g. TimeReached) or the constraint id doesn't
        // resolve to a real region. Caller must hold _suppressRuleSync so the
        // sync callbacks below don't bounce back during populate.
        private void PopulateRuleBandDetails()
        {
            if (RuleRegionDetails == null) return;
            var region = ResolveRuleConstrainedRegion();
            if (region == null)
            {
                RuleRegionDetails.Visibility = Visibility.Collapsed;
                return;
            }

            RuleRegionDetails.Visibility = Visibility.Visible;
            TxtRuleBandLabel.Text = region.Label ?? "";
            TxtRuleBandStart.Text = region.Start.ToString("0.##", CultureInfo.InvariantCulture) + " s";
            TxtRuleBandEnd.Text = region.End.ToString("0.##", CultureInfo.InvariantCulture) + " s";
            TxtRuleBandColor.Text = region.Color ?? "";
            UpdateRuleBandColorSwatchPreview();
            BuildRuleBandColorSwatches();
        }

        private Region? ResolveRuleConstrainedRegion()
        {
            var id = _selectedRule?.RegionConstraint;
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var r in _enhancement.Regions)
            {
                if (r != null && string.Equals(r.Id, id, StringComparison.Ordinal)) return r;
            }
            return null;
        }

        private void RuleBandField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRuleSync) return;
            var region = ResolveRuleConstrainedRegion();
            if (region == null) return;
            try
            {
                if (sender == TxtRuleBandLabel)
                {
                    region.Label = TxtRuleBandLabel.Text;
                }
                else if (sender == TxtRuleBandColor)
                {
                    region.Color = TxtRuleBandColor.Text;
                    UpdateRuleBandColorSwatchPreview();
                }
                MarkDirty();
                RebuildRegionVisuals();
                ScheduleValidation();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RuleBandField sync error: {Error}", ex.Message);
            }
        }

        private void UpdateRuleBandColorSwatchPreview()
        {
            if (RuleBandColorSwatch == null) return;
            var brush = TryParseBrush(TxtRuleBandColor.Text) ?? Brushes.MediumPurple;
            RuleBandColorSwatch.Background = brush;
        }

        private void BuildRuleBandColorSwatches()
        {
            if (RuleBandColorSwatches == null) return;
            RuleBandColorSwatches.Children.Clear();
            foreach (var hex in RegionPalette)
            {
                var brush = TryParseBrush(hex) ?? Brushes.MediumPurple;
                var swatch = new Border
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = brush,
                    BorderBrush = (System.Windows.Media.Brush)FindResource("GlassBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Tag = hex,
                    ToolTip = hex
                };
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    if (_selectedRule == null) return;
                    TxtRuleBandColor.Text = hex;
                };
                RuleBandColorSwatches.Children.Add(swatch);
            }
        }

        private void RebuildRegionConstraintCombo()
        {
            CmbRuleRegion.Items.Clear();
            CmbRuleRegion.Items.Add(Loc.Get("deeper_editor_rule_region_none"));
            int selected = 0;
            for (int i = 0; i < _enhancement.Regions.Count; i++)
            {
                var r = _enhancement.Regions[i];
                CmbRuleRegion.Items.Add(string.IsNullOrEmpty(r.Label) ? r.Id : $"{r.Id} — {r.Label}");
                if (_selectedRule?.RegionConstraint == r.Id) selected = i + 1;
            }
            CmbRuleRegion.SelectedIndex = selected;
        }

        private void ChkRuleEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            _selectedRule.Enabled = ChkRuleEnabled.IsChecked == true;
            MarkDirty();
            RefreshRulesList();
            ScheduleValidation();
        }

        private void CmbTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            // Combo items are now ComboBoxItem with Content=friendly name and
            // Tag=raw type; old code that read SelectedItem as string broke.
            var picked = (CmbTriggerType.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(picked)) return;
            if (_selectedRule.Trigger?.Type == picked) return;

            _selectedRule.Trigger = picked switch
            {
                TriggerTypes.GazeTarget    => new GazeTargetTrigger(),
                TriggerTypes.GazeAvoid     => new GazeAvoidTrigger(),
                TriggerTypes.AttentionLost => new AttentionLostTrigger(),
                TriggerTypes.BlinkDetected => new BlinkDetectedTrigger(),
                TriggerTypes.MouthOpen     => new MouthOpenTrigger(),
                TriggerTypes.TimeReached   => new TimeReachedTrigger { Time = Math.Max(0, _currentSeconds) },
                TriggerTypes.RegionEntered => new RegionEnteredTrigger(),
                TriggerTypes.RegionExited  => new RegionExitedTrigger(),
                _                          => _selectedRule.Trigger
            };
            EndGazePick(commit: false);
            BuildTriggerFields();
            MarkDirty();
            RebuildRuleVisuals();
            RefreshRulesList();
            ScheduleValidation();
        }

        private void CmbActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            var picked = (CmbActionType.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(picked)) return;
            if (_selectedRule.Action?.Type == picked) return;

            _selectedRule.Action = picked switch
            {
                ActionTypes.Seek          => new SeekAction { Target = SeekTargets.Time, Time = 0 },
                ActionTypes.LoopRegion    => new LoopRegionAction(),
                ActionTypes.Pause         => new PauseAction(),
                ActionTypes.PlayAudio     => new PlayAudioAction(),
                ActionTypes.TriggerHaptic => new TriggerHapticAction { PatternName = "Pulse" },
                ActionTypes.TriggerEffect => new TriggerEffectAction { EffectType = EffectTypes.Haptic, PatternName = "Pulse" },
                ActionTypes.ScreenShake   => new ScreenShakeAction(),
                ActionTypes.SetIntensity  => new SetIntensityAction(),
                _                         => _selectedRule.Action
            };
            BuildActionFields();
            MarkDirty();
            RefreshRulesList();
            ScheduleValidation();
        }

        private void CmbRuleRegion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            var idx = CmbRuleRegion.SelectedIndex;
            if (idx <= 0)
            {
                _selectedRule.RegionConstraint = null;
            }
            else if (idx - 1 < _enhancement.Regions.Count)
            {
                _selectedRule.RegionConstraint = _enhancement.Regions[idx - 1].Id;
            }
            MarkDirty();
            ScheduleValidation();
        }

        private void TxtRuleCooldown_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            if (int.TryParse(TxtRuleCooldown.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                _selectedRule.CooldownMs = Math.Max(0, ms);
            MarkDirty();
            ScheduleValidation();
        }

        // -- Dynamic field builders (programmatic; one block per type) --------

        private void BuildTriggerFields()
        {
            TriggerFields.Children.Clear();
            if (_selectedRule?.Trigger == null) return;

            AddTypeDescription(TriggerFields, TriggerDescriptionKey(_selectedRule.Trigger.Type));

            switch (_selectedRule.Trigger)
            {
                case GazeTargetTrigger g:
                    AddRectFields(TriggerFields, g.Rect, () => g.Rect);
                    AddIntField(TriggerFields, Loc.Get("deeper_editor_trigger_min_dwell"),
                        g.MinDwellMs, v => g.MinDwellMs = Math.Max(0, v));
                    break;
                case GazeAvoidTrigger g:
                    AddRectFields(TriggerFields, g.Rect, () => g.Rect);
                    AddIntField(TriggerFields, Loc.Get("deeper_editor_trigger_min_dwell"),
                        g.MinDwellMs, v => g.MinDwellMs = Math.Max(0, v));
                    break;
                case AttentionLostTrigger a:
                    AddIntField(TriggerFields, Loc.Get("deeper_editor_trigger_min_duration"),
                        a.MinDurationMs, v => a.MinDurationMs = Math.Max(0, v));
                    break;
                case BlinkDetectedTrigger:
                case MouthOpenTrigger:
                    AddInfoText(TriggerFields, Loc.Get("deeper_editor_trigger_no_params"));
                    break;
                case TimeReachedTrigger tr:
                    AddDoubleField(TriggerFields, Loc.Get("deeper_editor_trigger_time"),
                        tr.Time, v => { tr.Time = Math.Max(0, v); RebuildRuleVisuals(); });
                    AssignNameToLastTextBox(TriggerFields, "TutorialTriggerTimeField");
                    break;
                case RegionEnteredTrigger re:
                    AddRegionPicker(TriggerFields, re.RegionId, id => re.RegionId = id);
                    break;
                case RegionExitedTrigger rx:
                    AddRegionPicker(TriggerFields, rx.RegionId, id => rx.RegionId = id);
                    break;
            }
        }

        private void BuildActionFields()
        {
            ActionFields.Children.Clear();
            if (_selectedRule?.Action == null) return;

            AddTypeDescription(ActionFields, ActionDescriptionKey(_selectedRule.Action.Type));

            switch (_selectedRule.Action)
            {
                case SeekAction seek:
                    AddSeekFields(seek);
                    break;
                case LoopRegionAction loop:
                    AddRegionPicker(ActionFields, loop.RegionId ?? "",
                        id => loop.RegionId = string.IsNullOrEmpty(id) ? null : id,
                        allowNone: true);
                    break;
                case PauseAction:
                    AddInfoText(ActionFields, Loc.Get("deeper_editor_action_pause_info"));
                    break;
                case PlayAudioAction pa:
                    AddTextField(ActionFields, Loc.Get("deeper_editor_action_audio_path"),
                        pa.Path, v => pa.Path = v);
                    AddIntField(ActionFields, Loc.Get("deeper_editor_action_volume"),
                        pa.Volume, v => pa.Volume = Math.Clamp(v, 0, 100));
                    AddBoolField(ActionFields, Loc.Get("deeper_editor_action_duck"),
                        pa.DuckOtherAudio, v => pa.DuckOtherAudio = v);
                    break;
                case TriggerHapticAction h:
                    AddHapticActionFields(h);
                    break;
                case TriggerEffectAction te:
                    AddTriggerEffectActionFields(te);
                    break;
                case ScreenShakeAction ss:
                    AddDoubleField(ActionFields, Loc.Get("deeper_editor_action_intensity"),
                        ss.Intensity, v => ss.Intensity = Math.Clamp(v, 0, 1));
                    AssignNameToLastTextBox(ActionFields, "TutorialActionIntensityField");
                    AddIntField(ActionFields, Loc.Get("deeper_editor_action_duration_ms"),
                        ss.DurationMs, v => ss.DurationMs = Math.Max(50, v));
                    break;
                case SetIntensityAction si:
                    AddDoubleField(ActionFields, Loc.Get("deeper_editor_action_value_0_1"),
                        si.Value, v => si.Value = Math.Clamp(v, 0, 1));
                    break;
            }
        }

        private void AddRectFields(Panel host, double[] rect, Func<double[]> getter)
        {
            var grid = new Grid();
            for (int i = 0; i < 4; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] labels = { "x", "y", "w", "h" };
            for (int i = 0; i < 4; i++)
            {
                int captured = i;
                var stack = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, 4, 0) };
                stack.Children.Add(new TextBlock
                {
                    Text = labels[i],
                    Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush"),
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                var tb = new TextBox
                {
                    Style = (Style)FindResource("EditorTextBox"),
                    Text = rect.Length > i ? rect[i].ToString("0.##", CultureInfo.InvariantCulture) : "0"
                };
                tb.TextChanged += (_, _) =>
                {
                    if (_suppressRuleSync) return;
                    var arr = getter();
                    if (arr.Length > captured && double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        arr[captured] = Math.Clamp(v, 0, 1);
                        MarkDirty();
                        ScheduleValidation();
                    }
                };
                stack.Children.Add(tb);
                Grid.SetColumn(stack, i);
                grid.Children.Add(stack);
            }
            grid.Margin = new Thickness(0, 0, 0, 4);
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_trigger_rect"),
                Style = (Style)FindResource("EditorLabel")
            });
            host.Children.Add(grid);

            // Quick-pick presets: 3×3 grid of corner/edge regions so the user
            // can say "bottom-left" with one click instead of dragging or
            // typing four numbers. Each preset covers a third of the screen.
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_trigger_quick_region"),
                Style = (Style)FindResource("EditorLabel")
            });
            var presetGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            for (int c = 0; c < 3; c++)
                presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < 3; r++)
                presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var presetLabels = new[,]
            {
                { "↖", "↑", "↗" },
                { "←", "·",  "→" },
                { "↙", "↓", "↘" }
            };
            var presetTooltips = new[,]
            {
                { "deeper_editor_quick_region_top_left",    "deeper_editor_quick_region_top",    "deeper_editor_quick_region_top_right"    },
                { "deeper_editor_quick_region_left",        "deeper_editor_quick_region_center", "deeper_editor_quick_region_right"        },
                { "deeper_editor_quick_region_bottom_left", "deeper_editor_quick_region_bottom", "deeper_editor_quick_region_bottom_right" }
            };
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int capturedRow = row;
                    int capturedCol = col;
                    var b = new Button
                    {
                        Content = presetLabels[row, col],
                        FontSize = 14,
                        Padding = new Thickness(0, 4, 0, 4),
                        Margin = new Thickness(col == 0 ? 0 : 2, row == 0 ? 0 : 2, 0, 0),
                        Cursor = Cursors.Hand,
                        Background = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent20Brush"),
                        Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush"),
                        BorderBrush = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush"),
                        BorderThickness = new Thickness(1),
                        ToolTip = Loc.Get(presetTooltips[row, col])
                    };
                    Grid.SetColumn(b, col);
                    Grid.SetRow(b, row);
                    b.Click += (_, _) =>
                    {
                        var arr = getter();
                        if (arr == null || arr.Length < 4) return;
                        // 0,0,0 = top-left; each cell is 1/3 of the screen.
                        arr[0] = capturedCol / 3.0;
                        arr[1] = capturedRow / 3.0;
                        arr[2] = 1.0 / 3.0;
                        arr[3] = 1.0 / 3.0;
                        MarkDirty();
                        if (_selectedRule != null) BuildTriggerFields();
                        ScheduleValidation();
                    };
                    presetGrid.Children.Add(b);
                }
            }
            host.Children.Add(presetGrid);

            var pickBtn = new Button
            {
                Content = Loc.Get("deeper_editor_trigger_pick_on_video"),
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = Cursors.Hand,
                Background = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent20Brush"),
                Foreground = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };
            pickBtn.Click += (_, _) => BeginGazePick(getter);
            host.Children.Add(pickBtn);
        }

        private void AddSeekFields(SeekAction seek)
        {
            ActionFields.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_action_seek_target"),
                Style = (Style)FindResource("EditorLabel")
            });
            var combo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            string[] targets = { SeekTargets.Time, SeekTargets.RegionStart, SeekTargets.RegionEnd };
            foreach (var t in targets) combo.Items.Add(t);
            var curIdx = Array.IndexOf(targets, seek.Target);
            combo.SelectedIndex = curIdx >= 0 ? curIdx : 0;

            var dynamicHost = new StackPanel();
            void RebuildDynamic()
            {
                dynamicHost.Children.Clear();
                if (seek.Target == SeekTargets.Time)
                {
                    AddDoubleField(dynamicHost, Loc.Get("deeper_editor_action_seek_time"),
                        seek.Time ?? 0, v => seek.Time = Math.Max(0, v));
                }
                else
                {
                    AddRegionPicker(dynamicHost, seek.RegionId ?? "",
                        id => seek.RegionId = string.IsNullOrEmpty(id) ? null : id);
                }
            }

            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                seek.Target = (combo.SelectedItem as string) ?? SeekTargets.Time;
                if (seek.Target == SeekTargets.Time && seek.Time == null) seek.Time = 0;
                RebuildDynamic();
                MarkDirty();
                ScheduleValidation();
            };

            ActionFields.Children.Add(combo);
            ActionFields.Children.Add(dynamicHost);
            RebuildDynamic();
        }

        private void AddHapticActionFields(TriggerHapticAction h)
        {
            ActionFields.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_haptic_pattern"),
                Style = (Style)FindResource("EditorLabel")
            });
            var combo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            foreach (var name in StockHapticPatterns.Names) combo.Items.Add(name);
            combo.Items.Add(Loc.Get("deeper_editor_haptic_pattern_custom"));

            var curveHost = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

            void SyncCurveVisibility()
            {
                bool isCustom = h.CustomPattern != null && h.CustomPattern.Count > 0;
                curveHost.Children.Clear();
                if (isCustom) BuildCurveEditor(curveHost, h);
            }

            int initialIdx = -1;
            bool initialCustom = h.CustomPattern != null && h.CustomPattern.Count > 0;
            if (initialCustom) initialIdx = StockHapticPatterns.Names.Count;
            else if (!string.IsNullOrEmpty(h.PatternName))
            {
                for (int i = 0; i < StockHapticPatterns.Names.Count; i++)
                    if (StockHapticPatterns.Names[i] == h.PatternName) { initialIdx = i; break; }
            }
            combo.SelectedIndex = initialIdx >= 0 ? initialIdx : 0;

            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                var idx = combo.SelectedIndex;
                if (idx < 0) return;
                if (idx < StockHapticPatterns.Names.Count)
                {
                    h.PatternName = StockHapticPatterns.Names[idx];
                    h.CustomPattern = null;
                }
                else
                {
                    h.CustomPattern ??= StockHapticPatterns.SeedCustomFrom(h.PatternName);
                    h.PatternName = null;
                }
                SyncCurveVisibility();
                MarkDirty();
                ScheduleValidation();
            };

            ActionFields.Children.Add(combo);
            ActionFields.Children.Add(curveHost);
            SyncCurveVisibility();

            AddDoubleField(ActionFields, Loc.Get("deeper_editor_action_intensity"),
                h.Intensity, v => h.Intensity = Math.Clamp(v, 0, 1));
            AddIntField(ActionFields, Loc.Get("deeper_editor_action_duration_ms"),
                h.DurationMs, v => h.DurationMs = Math.Max(50, v));
        }

        // Generic effect-firing action (TriggerEffect). Lets a rule fire any of
        // the five effect types (haptic/flash/bubble/subliminal/overlay) with
        // its own intensity / duration / type-specific settings - independent
        // of any TimelineItem on the timeline. Mirrors the per-type editor
        // panels so the user picks an effect kind and gets the matching
        // controls, no surprises.
        private void AddTriggerEffectActionFields(TriggerEffectAction te)
        {
            // Effect-type combo.
            ActionFields.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_action_effect_type"),
                Style = (Style)FindResource("EditorLabel")
            });
            var typeCombo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            string[] effectTypes =
            {
                EffectTypes.Haptic, EffectTypes.Flash, EffectTypes.Bubble,
                EffectTypes.Subliminal, EffectTypes.Overlay
            };
            foreach (var t in effectTypes)
            {
                typeCombo.Items.Add(new ComboBoxItem { Content = t, Tag = t });
            }
            var curIdx = Array.IndexOf(effectTypes, te.EffectType);
            typeCombo.SelectedIndex = curIdx >= 0 ? curIdx : 0;
            ActionFields.Children.Add(typeCombo);

            // Per-type fields rebuild on type switch.
            var typeFieldsHost = new StackPanel();
            ActionFields.Children.Add(typeFieldsHost);

            void RebuildTypeFields()
            {
                typeFieldsHost.Children.Clear();
                switch (te.EffectType)
                {
                    case EffectTypes.Haptic:
                        AddTriggerEffectHapticFields(typeFieldsHost, te);
                        break;
                    case EffectTypes.Flash:
                        AddIntField(typeFieldsHost, Loc.Get("deeper_editor_action_duration_ms"),
                            te.DurationMs, v => te.DurationMs = Math.Max(50, v));
                        AddBoolField(typeFieldsHost, Loc.Get("deeper_editor_action_play_sound"),
                            te.PlaySound, v => te.PlaySound = v);
                        break;
                    case EffectTypes.Bubble:
                        AddIntField(typeFieldsHost, Loc.Get("deeper_editor_action_max_bubbles"),
                            te.MaxBubbles, v => te.MaxBubbles = Math.Max(1, v));
                        AddDoubleField(typeFieldsHost, Loc.Get("deeper_editor_action_intensity"),
                            te.Intensity, v => te.Intensity = Math.Clamp(v, 0, 1));
                        AddIntField(typeFieldsHost, Loc.Get("deeper_editor_action_duration_ms"),
                            te.DurationMs, v => te.DurationMs = Math.Max(50, v));
                        break;
                    case EffectTypes.Subliminal:
                        AddTextField(typeFieldsHost, Loc.Get("deeper_editor_action_subliminal_text"),
                            te.Text ?? "", v => te.Text = v);
                        AddIntField(typeFieldsHost, Loc.Get("deeper_editor_action_duration_ms"),
                            te.DurationMs, v => te.DurationMs = Math.Max(50, v));
                        break;
                    case EffectTypes.Overlay:
                        AddOverlayKindCombo(typeFieldsHost, te);
                        AddDoubleField(typeFieldsHost, Loc.Get("deeper_editor_action_opacity"),
                            te.Opacity, v => te.Opacity = Math.Clamp(v, 0, 1));
                        AddIntField(typeFieldsHost, Loc.Get("deeper_editor_action_duration_ms"),
                            te.DurationMs, v => te.DurationMs = Math.Max(50, v));
                        break;
                }
            }
            RebuildTypeFields();

            typeCombo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                if (typeCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string newType
                    && newType != te.EffectType)
                {
                    te.EffectType = newType;
                    // Reset stale type-specific fields so a Flash→Haptic switch
                    // doesn't leak Flash defaults into the haptic interpretation.
                    if (newType == EffectTypes.Haptic && string.IsNullOrEmpty(te.PatternName))
                        te.PatternName = "Pulse";
                    if (newType == EffectTypes.Overlay && string.IsNullOrEmpty(te.OverlayKind))
                        te.OverlayKind = OverlayKinds.PinkFilter;
                    RebuildTypeFields();
                    MarkDirty();
                    ScheduleValidation();
                }
            };
        }

        // Haptic sub-fields for TriggerEffect. Pattern combo + intensity +
        // duration; mirrors AddHapticActionFields but bound to a
        // TriggerEffectAction's flat fields instead of TriggerHapticAction.
        private void AddTriggerEffectHapticFields(Panel host, TriggerEffectAction te)
        {
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_haptic_pattern"),
                Style = (Style)FindResource("EditorLabel")
            });
            var combo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            foreach (var name in StockHapticPatterns.Names) combo.Items.Add(name);
            int idx = -1;
            if (!string.IsNullOrEmpty(te.PatternName))
            {
                for (int i = 0; i < StockHapticPatterns.Names.Count; i++)
                    if (StockHapticPatterns.Names[i] == te.PatternName) { idx = i; break; }
            }
            combo.SelectedIndex = idx >= 0 ? idx : 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                var i = combo.SelectedIndex;
                if (i >= 0 && i < StockHapticPatterns.Names.Count)
                {
                    te.PatternName = StockHapticPatterns.Names[i];
                    MarkDirty();
                    ScheduleValidation();
                }
            };
            host.Children.Add(combo);

            AddDoubleField(host, Loc.Get("deeper_editor_action_intensity"),
                te.Intensity, v => te.Intensity = Math.Clamp(v, 0, 1));
            AddIntField(host, Loc.Get("deeper_editor_action_duration_ms"),
                te.DurationMs, v => te.DurationMs = Math.Max(50, v));
        }

        // Overlay-kind combo for TriggerEffect. Pink Filter / Spiral / Brain
        // Drain; matches the inline overlay editor's CmbOverlayKind.
        private void AddOverlayKindCombo(Panel host, TriggerEffectAction te)
        {
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_action_overlay_kind"),
                Style = (Style)FindResource("EditorLabel")
            });
            var combo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            string[] kinds = { OverlayKinds.PinkFilter, OverlayKinds.Spiral, OverlayKinds.BrainDrain };
            foreach (var k in kinds)
            {
                combo.Items.Add(new ComboBoxItem { Content = k, Tag = k });
            }
            var curK = te.OverlayKind ?? OverlayKinds.PinkFilter;
            var idx = Array.IndexOf(kinds, curK);
            combo.SelectedIndex = idx >= 0 ? idx : 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                if (combo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string k)
                {
                    te.OverlayKind = k;
                    MarkDirty();
                    ScheduleValidation();
                }
            };
            host.Children.Add(combo);
        }

        // Self-contained curve editor for any IHapticPatternTarget. Builds its
        // own canvas + handles + reset button into <paramref name="host"/>; safe
        // to use independently of the haptic-event editor's XAML CurveCanvas.
        private void BuildCurveEditor(Panel host, IHapticPatternTarget target)
        {
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_haptic_curve"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x10, 0, 0, 0x20)),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3)
            };
            var canvas = new Canvas
            {
                Height = 100,
                Background = System.Windows.Media.Brushes.Transparent,
                ClipToBounds = true
            };
            border.Child = canvas;
            host.Children.Add(border);

            int draggingIdx = -1;

            void Render()
            {
                canvas.Children.Clear();
                if (target.CustomPattern == null || target.CustomPattern.Count == 0) return;
                var w = canvas.ActualWidth;
                var hgt = canvas.ActualHeight;
                if (w <= 0 || hgt <= 0) return;

                for (int i = 1; i <= 3; i++)
                {
                    var y = hgt * i / 4.0;
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = 0, X2 = w, Y1 = y, Y2 = y,
                        Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)),
                        StrokeThickness = 0.5,
                        IsHitTestVisible = false
                    });
                }

                var pts = target.CustomPattern;
                var geom = new System.Windows.Media.StreamGeometry();
                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(KeyframeToCanvas(pts[0], w, hgt), false, false);
                    for (int i = 1; i < pts.Count; i++)
                        ctx.LineTo(KeyframeToCanvas(pts[i], w, hgt), true, false);
                }
                geom.Freeze();
                canvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = geom,
                    Stroke = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false
                });

                for (int i = 0; i < pts.Count; i++)
                {
                    var pt = KeyframeToCanvas(pts[i], w, hgt);
                    var dot = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10, Height = 10,
                        Fill = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                        Stroke = System.Windows.Media.Brushes.White,
                        StrokeThickness = 1.2,
                        Cursor = Cursors.SizeNS,
                        Tag = i
                    };
                    Canvas.SetLeft(dot, pt.X - 5);
                    Canvas.SetTop(dot, pt.Y - 5);
                    dot.MouseLeftButtonDown += (s, ev) =>
                    {
                        if (s is System.Windows.Shapes.Ellipse el && el.Tag is int idx)
                        {
                            draggingIdx = idx;
                            el.CaptureMouse();
                            ev.Handled = true;
                        }
                    };
                    dot.MouseMove += (s, ev) =>
                    {
                        if (draggingIdx < 0 || target.CustomPattern == null) return;
                        var hh = canvas.ActualHeight;
                        if (hh <= 0) return;
                        var pos = ev.GetPosition(canvas);
                        var v = Math.Clamp(1.0 - (pos.Y / hh), 0.0, 1.0);
                        target.CustomPattern[draggingIdx][1] = v;
                        MarkDirty();
                        Render();
                        ScheduleValidation();
                    };
                    dot.MouseLeftButtonUp += (s, ev) =>
                    {
                        if (s is System.Windows.Shapes.Ellipse el) el.ReleaseMouseCapture();
                        draggingIdx = -1;
                    };
                    canvas.Children.Add(dot);
                }
            }

            canvas.SizeChanged += (_, _) => Render();
            Render();

            var resetBtn = new Button
            {
                Content = Loc.Get("deeper_editor_haptic_curve_reset"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand,
                FontSize = 11,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush")
            };
            resetBtn.Click += (_, _) =>
            {
                target.CustomPattern = StockHapticPatterns.SeedCustomFrom(null);
                MarkDirty();
                Render();
                ScheduleValidation();
            };
            host.Children.Add(resetBtn);
        }

        // -- Tiny field helpers ------------------------------------------------

        private void AddIntField(Panel host, string label, int value, Action<int> setter)
        {
            host.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("EditorLabel") });
            var tb = new TextBox
            {
                Style = (Style)FindResource("EditorTextBox"),
                Text = value.ToString(CultureInfo.InvariantCulture)
            };
            tb.TextChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                if (int.TryParse(tb.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                {
                    setter(v); MarkDirty(); ScheduleValidation();
                }
            };
            host.Children.Add(tb);
        }

        private void AddDoubleField(Panel host, string label, double value, Action<double> setter)
        {
            host.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("EditorLabel") });
            var tb = new TextBox
            {
                Style = (Style)FindResource("EditorTextBox"),
                Text = value.ToString("0.###", CultureInfo.InvariantCulture)
            };
            tb.TextChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                if (double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    setter(v); MarkDirty(); ScheduleValidation();
                }
            };
            host.Children.Add(tb);
        }

        private void AddTextField(Panel host, string label, string value, Action<string> setter)
        {
            host.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("EditorLabel") });
            var tb = new TextBox
            {
                Style = (Style)FindResource("EditorTextBox"),
                Text = value ?? ""
            };
            tb.TextChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                setter(tb.Text ?? "");
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(tb);
        }

        private void AddBoolField(Panel host, string label, bool value, Action<bool> setter)
        {
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            cb.Click += (_, _) =>
            {
                if (_suppressRuleSync) return;
                setter(cb.IsChecked == true);
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(cb);
        }

        // Assigns x:Name to the most recently-added TextBox in a dynamic field
        // host so the interactive tutorial can spotlight + gate on it. Safe to
        // call even when the panel has no TextBox children (no-op).
        private static void AssignNameToLastTextBox(Panel host, string name)
        {
            for (int i = host.Children.Count - 1; i >= 0; i--)
            {
                if (host.Children[i] is TextBox tb)
                {
                    tb.Name = name;
                    return;
                }
            }
        }

        private void AddInfoText(Panel host, string text)
        {
            host.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                FontSize = 11, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        // Renders the localized one-line description for the currently-picked
        // trigger or action type. Updates each time BuildTriggerFields /
        // BuildActionFields runs, so the user sees what each type does the
        // moment they pick it.
        private void AddTypeDescription(Panel host, string locKey)
        {
            var text = Loc.Get(locKey);
            if (string.IsNullOrEmpty(text) || text == locKey) return; // no key = skip
            host.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent20Brush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        private static string TriggerDescriptionKey(string type) => type switch
        {
            TriggerTypes.GazeTarget    => "deeper_desc_trigger_gaze_target",
            TriggerTypes.GazeAvoid     => "deeper_desc_trigger_gaze_avoid",
            TriggerTypes.AttentionLost => "deeper_desc_trigger_attention_lost",
            TriggerTypes.BlinkDetected => "deeper_desc_trigger_blink_detected",
            TriggerTypes.MouthOpen     => "deeper_desc_trigger_mouth_open",
            TriggerTypes.TimeReached   => "deeper_desc_trigger_time_reached",
            TriggerTypes.RegionEntered => "deeper_desc_trigger_region_entered",
            TriggerTypes.RegionExited  => "deeper_desc_trigger_region_exited",
            _                          => ""
        };

        private static string ActionDescriptionKey(string type) => type switch
        {
            ActionTypes.Seek          => "deeper_desc_action_seek",
            ActionTypes.LoopRegion    => "deeper_desc_action_loop_region",
            ActionTypes.Pause         => "deeper_desc_action_pause",
            ActionTypes.PlayAudio     => "deeper_desc_action_play_audio",
            ActionTypes.TriggerHaptic => "deeper_desc_action_trigger_haptic",
            ActionTypes.TriggerEffect => "deeper_desc_action_trigger_effect",
            ActionTypes.ScreenShake   => "deeper_desc_action_screen_shake",
            ActionTypes.SetIntensity  => "deeper_desc_action_set_intensity",
            _                         => ""
        };

        private void AddRegionPicker(Panel host, string currentId, Action<string> setter, bool allowNone = false)
        {
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_rule_region_id"),
                Style = (Style)FindResource("EditorLabel")
            });
            var combo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            int selected = -1;
            if (allowNone) combo.Items.Add(Loc.Get("deeper_editor_rule_region_current"));
            for (int i = 0; i < _enhancement.Regions.Count; i++)
            {
                var r = _enhancement.Regions[i];
                combo.Items.Add(string.IsNullOrEmpty(r.Label) ? r.Id : $"{r.Id} — {r.Label}");
                if (r.Id == currentId) selected = i + (allowNone ? 1 : 0);
            }
            if (selected < 0) selected = combo.Items.Count > 0 ? 0 : -1;
            combo.SelectedIndex = selected;

            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                var idx = combo.SelectedIndex;
                if (allowNone && idx == 0) { setter(""); }
                else if (idx >= 0)
                {
                    var regionIdx = allowNone ? idx - 1 : idx;
                    if (regionIdx >= 0 && regionIdx < _enhancement.Regions.Count)
                        setter(_enhancement.Regions[regionIdx].Id);
                }
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(combo);
        }

        // -- Preview: open standalone Deeper Player ---------------------------

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.DeeperPlayer == null || App.DeeperHost == null)
                {
                    MessageBox.Show(this,
                        Loc.Get("deeper_editor_preview_not_initialized"),
                        Loc.Get("deeper_editor_preview_title"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // Heal band-style rules whose trigger.RegionId is empty but whose
                // RegionConstraint already points to a real region (older buggy
                // path left RegionId blank). Without this, validation rejects
                // the in-memory enhancement and the player opens with no media —
                // exactly what the user reported when Preview "did nothing".
                HealEmptyTriggerRegionIds();
                var win = new EnhancementPlayerWindow(
                    App.DeeperPlayer, App.DeeperHost, _enhancement, "editor-preview")
                { Owner = this };
                win.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Deeper editor: opening Player from Preview failed");
                MessageBox.Show(this,
                    string.Format(Loc.Get("deeper_editor_preview_open_failed_fmt"), $"{ex.GetType().Name}: {ex.Message}"),
                    Loc.Get("deeper_editor_preview_failed_title"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HealEmptyTriggerRegionIds()
        {
            try
            {
                var regionIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var r in _enhancement.Regions)
                    if (!string.IsNullOrEmpty(r?.Id)) regionIds.Add(r.Id);

                foreach (var rule in _enhancement.Rules)
                {
                    if (rule == null) continue;
                    var fallbackId = rule.RegionConstraint;
                    if (string.IsNullOrEmpty(fallbackId) || !regionIds.Contains(fallbackId)) continue;

                    if (rule.Trigger is RegionEnteredTrigger re && string.IsNullOrEmpty(re.RegionId))
                        re.RegionId = fallbackId;
                    else if (rule.Trigger is RegionExitedTrigger rx && string.IsNullOrEmpty(rx.RegionId))
                        rx.RegionId = fallbackId;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: HealEmptyTriggerRegionIds error: {Error}", ex.Message);
            }
        }

        // -- Gaze rect picker --------------------------------------------------
        // Picker lives in a separate transparent Window because LibVLC's
        // VideoView renders in a native child HWND that wins WPF airspace; an
        // inline overlay would render behind the video and be invisible.

        private void BeginGazePick(Func<double[]> rectGetter)
        {
            EndGazePick(commit: false);

            var current = rectGetter();
            if (current == null || current.Length < 4)
                current = new[] { 0.25, 0.25, 0.5, 0.5 };

            // Position the picker exactly over the editor's preview host so
            // its normalized rect maps 1:1 to the video display rect.
            try
            {
                var origin = PreviewHost.PointToScreen(new Point(0, 0));
                var farCorner = PreviewHost.PointToScreen(new Point(PreviewHost.ActualWidth, PreviewHost.ActualHeight));
                var dpi = VisualTreeHelper.GetDpi(this);

                _gazePickerWindow = new GazePickerWindow(current)
                {
                    Owner = this,
                    Left = origin.X / dpi.DpiScaleX,
                    Top = origin.Y / dpi.DpiScaleY,
                    Width = (farCorner.X - origin.X) / dpi.DpiScaleX,
                    Height = (farCorner.Y - origin.Y) / dpi.DpiScaleY
                };
                _gazePickerWindow.Closed += (_, _) =>
                {
                    var w = _gazePickerWindow;
                    _gazePickerWindow = null;
                    if (w != null && w.Committed)
                    {
                        var result = w.ResultRect;
                        // Write back into the trigger's rect array via the same
                        // reference the caller handed us. Both arrays may be
                        // distinct (Window cloned the input) so copy elementwise.
                        var tgt = rectGetter();
                        if (tgt != null && tgt.Length >= 4 && result.Length >= 4)
                        {
                            tgt[0] = result[0];
                            tgt[1] = result[1];
                            tgt[2] = result[2];
                            tgt[3] = result[3];
                        }
                        MarkDirty();
                        if (_selectedRule != null) BuildTriggerFields();
                        ScheduleValidation();
                    }
                };
                _gazePickerWindow.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BeginGazePick failed: {Error}", ex.Message);
            }
        }

        private void EndGazePick(bool commit)
        {
            // Force-close any open picker. The Closed handler reads Committed
            // (set by the picker's own Done/Cancel/Esc/Enter handlers); calling
            // Close here without committing leaves Committed=false → no apply.
            if (_gazePickerWindow == null) return;
            try { _gazePickerWindow.Close(); }
            catch { }
            _gazePickerWindow = null;
        }

        // -- Metadata sync -----------------------------------------------------

        private void MetadataField_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressDirty) return;
            _enhancement.Metadata.Name = TxtMetaName.Text ?? "";
            _enhancement.Metadata.Creator = TxtMetaCreator.Text ?? "";
            _enhancement.Metadata.Remixer = string.IsNullOrWhiteSpace(TxtMetaRemixer?.Text) ? null : TxtMetaRemixer.Text;
            _enhancement.Metadata.Description = TxtMetaDescription.Text ?? "";
            _enhancement.Metadata.Tags = (TxtMetaTags.Text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            _enhancement.Metadata.License = TxtMetaLicense.Text ?? "";
            MarkDirty();
            ScheduleValidation();
        }

        private void MarkDirty()
        {
            if (_suppressDirty) return;
            _isDirty = true;
            TxtDirty.Visibility = Visibility.Visible;
            // Mission 1 sidebar restructure: the items overview list is gone;
            // the selection summary strip is fed from the SelectXxx setters
            // instead, so MarkDirty has no per-mutation list to refresh.
            // Lane counts piggyback here (cheap) so the chrome stays in sync
            // with any add/remove/edit that touches the data model.
            RefreshLaneCounts();
        }

        private void ScheduleValidation()
        {
            _validationTimer?.Stop();
            _validationTimer?.Start();
        }

        // Backing list for the click-to-expand popup (XAML: ValidationDetailsPopup).
        // Refreshed in lockstep with the summary so the popup always shows the
        // current set; close-then-reopen rebuilds against latest.
        private List<Services.Deeper.ValidationError> _lastValidationIssues = new();

        private void RefreshValidation()
        {
            _lastValidationIssues = EnhancementValidator.Validate(_enhancement);
            int errorCount = _lastValidationIssues.Count(x => x.Severity == ValidationSeverity.Error);
            int warningCount = _lastValidationIssues.Count(x => x.Severity == ValidationSeverity.Warning);
            if (errorCount == 0 && warningCount == 0)
            {
                TxtValidationSummary.Text = Loc.Get("deeper_editor_validation_clean");
                TxtValidationSummary.Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush");
                TxtValidationSummary.Cursor = null;
                TxtValidationSummary.TextDecorations = null;
                if (ValidationDetailsPopup != null) ValidationDetailsPopup.IsOpen = false;
            }
            else
            {
                var bits = new System.Collections.Generic.List<string>();
                if (errorCount > 0) bits.Add(string.Format(Loc.Get("deeper_editor_validation_errors_fmt"), errorCount));
                if (warningCount > 0) bits.Add(string.Format(Loc.Get("deeper_editor_validation_warnings_fmt"), warningCount));
                TxtValidationSummary.Text = string.Join("  ·  ", bits)
                    + "  " + Loc.Get("deeper_editor_validation_click_for_details_hint");
                TxtValidationSummary.Foreground = errorCount > 0
                    ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                    : (System.Windows.Media.Brush)FindResource("PinkSoftBrush");
                TxtValidationSummary.Cursor = System.Windows.Input.Cursors.Hand;
                TxtValidationSummary.TextDecorations = System.Windows.TextDecorations.Underline;
                TxtValidationSummary.ToolTip = Loc.Get("deeper_editor_validation_click_for_details_tip");

                // If the popup is already open (user toggled it then made an
                // edit), refresh its contents in place rather than closing it.
                if (ValidationDetailsPopup?.IsOpen == true) PopulateValidationPopup();
            }
        }

        private void TxtValidationSummary_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // No issues = nothing to expand. Skip silently (RefreshValidation
            // also clears the Cursor=Hand affordance in that case).
            if (_lastValidationIssues == null || _lastValidationIssues.Count == 0) return;
            if (ValidationDetailsPopup == null) return;
            if (ValidationDetailsPopup.IsOpen)
            {
                ValidationDetailsPopup.IsOpen = false;
                return;
            }
            PopulateValidationPopup();
            ValidationDetailsPopup.IsOpen = true;
            e.Handled = true;
        }

        // Builds the row VMs for LstValidationIssues. Pulls fresh from
        // _lastValidationIssues so a popup re-open after edits always shows
        // current state.
        private void PopulateValidationPopup()
        {
            if (LstValidationIssues == null) return;
            int errorCount = _lastValidationIssues.Count(x => x.Severity == ValidationSeverity.Error);
            int warningCount = _lastValidationIssues.Count(x => x.Severity == ValidationSeverity.Warning);
            if (TxtValidationPopupHeader != null)
                TxtValidationPopupHeader.Text = string.Format(
                    Loc.Get("deeper_editor_validation_popup_header_fmt"),
                    errorCount, warningCount);

            var vms = _lastValidationIssues
                .OrderBy(i => i.Severity == ValidationSeverity.Error ? 0 : 1)
                .Select(i => new
                {
                    Glyph = i.Severity == ValidationSeverity.Error ? "✕" : "⚠",
                    Brush = (System.Windows.Media.Brush)FindResource(
                        i.Severity == ValidationSeverity.Error ? "DangerBrush" : "PinkSoftBrush"),
                    Message = i.Message ?? "",
                })
                .ToList();
            LstValidationIssues.ItemsSource = vms;
        }

        // -- File ops ----------------------------------------------------------

        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                MenuSaveAs_Click(sender, e);
                return;
            }
            SaveTo(_filePath!);
        }

        private void MenuSaveAs_Click(object sender, RoutedEventArgs e)
        {
            var library = App.EnhancementLibrary;
            var dialog = new SaveFileDialog
            {
                Title = Loc.Get("deeper_editor_save_dialog_title"),
                Filter = $"Deeper Enhancement (*{EnhancementLibrary.FileSuffix})|*{EnhancementLibrary.FileSuffix}",
                FileName = library?.SuggestedFileName(_enhancement) ?? ("Untitled" + EnhancementLibrary.FileSuffix),
                AddExtension = false
            };
            var lastDir = library?.LastDirectory;
            if (!string.IsNullOrEmpty(lastDir)) dialog.InitialDirectory = lastDir;
            if (dialog.ShowDialog(this) == true)
            {
                var path = dialog.FileName;
                if (!path.EndsWith(EnhancementLibrary.FileSuffix, StringComparison.OrdinalIgnoreCase))
                    path += EnhancementLibrary.FileSuffix;
                SaveTo(path);
            }
        }

        private void SaveTo(string path)
        {
            try
            {
                // Force a synchronous validation pass so we don't ship the
                // user a file we know is broken. Errors get a "save anyway?"
                // prompt; warnings pass silently (already shown in the
                // editor's validation strip).
                var issues = EnhancementValidator.Validate(_enhancement);
                int errorCount = issues.Count(i => i.Severity == ValidationSeverity.Error);
                if (errorCount > 0)
                {
                    var result = MessageBox.Show(this,
                        string.Format(Loc.Get("deeper_editor_save_invalid_prompt_fmt"), errorCount),
                        Loc.Get("deeper_editor_save_invalid_title"),
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return;
                }

                // Refresh hardware-gating auto-tags so the catalogue browser can
                // surface them on the card without having to re-scan files.
                if (_enhancement.Metadata != null)
                    _enhancement.Metadata.AutoTags = EnhancementAutoTagger.Detect(_enhancement);

                App.EnhancementLibrary?.Save(_enhancement, path);
                _filePath = path;
                _isDirty = false;
                TxtDirty.Visibility = Visibility.Collapsed;
                UpdateTitle();

                // Notify the interactive tutorial bus so the HT walkthrough can advance
                // to its follow-up card and surface the saved path.
                try
                {
                    TutorialEventBus.LastSavedEnhancementPath = path;
                    TutorialEventBus.Emit("FileSaved");
                }
                catch { }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: save failed");
                MessageBox.Show(this, string.Format(Loc.Get("deeper_editor_save_failed_fmt"), ex.Message),
                    Loc.Get("deeper_editor_save_dialog_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MenuExportEnhanced_Click(object sender, RoutedEventArgs e)
        {
            // Block on hard validation errors. The bundled file is meant to
            // be shareable, so shipping a known-broken enhancement to other
            // users would be worse than refusing the export.
            var issues = EnhancementValidator.Validate(_enhancement);
            int errorCount = issues.Count(i => i.Severity == ValidationSeverity.Error);
            if (errorCount > 0)
            {
                MessageBox.Show(this,
                    string.Format(Loc.Get("deeper_editor_export_validation_blocked_fmt"), errorCount),
                    Loc.Get("deeper_editor_export_dialog_title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var library = App.EnhancementLibrary;

            var pickSrc = new OpenFileDialog
            {
                Title = Loc.Get("deeper_editor_export_pick_source_title"),
                Filter = "Media files|*.mp4;*.m4v;*.mov;*.m4a;*.mp3;*.wav|All files|*.*",
                CheckFileExists = true
            };
            var lastDir = library?.LastDirectory;
            if (!string.IsNullOrEmpty(lastDir)) pickSrc.InitialDirectory = lastDir;
            if (pickSrc.ShowDialog(this) != true) return;

            var sourcePath = pickSrc.FileName;
            if (!EnhancementMediaBundler.IsSupportedExtension(sourcePath))
            {
                MessageBox.Show(this,
                    Loc.Get("deeper_editor_export_unsupported_format"),
                    Loc.Get("deeper_editor_export_dialog_title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var srcExt = System.IO.Path.GetExtension(sourcePath);
            var srcDir = System.IO.Path.GetDirectoryName(sourcePath) ?? "";
            var srcBase = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            var defaultName = srcBase + " (CCP)" + srcExt;

            var pickDest = new SaveFileDialog
            {
                Title = Loc.Get("deeper_editor_export_save_dialog_title"),
                // Pin the filter to the source extension so the user can't
                // accidentally pick a destination format that doesn't match
                // the source bytes (e.g. saving an MP3 body with a .wav
                // extension would produce an unplayable file).
                Filter = $"Media (*{srcExt})|*{srcExt}",
                FileName = defaultName,
                AddExtension = true,
                DefaultExt = srcExt
            };
            if (!string.IsNullOrEmpty(srcDir)) pickDest.InitialDirectory = srcDir;
            if (pickDest.ShowDialog(this) != true) return;

            var destPath = pickDest.FileName;
            if (string.Equals(System.IO.Path.GetFullPath(destPath), System.IO.Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this,
                    Loc.Get("deeper_editor_export_same_file_error"),
                    Loc.Get("deeper_editor_export_dialog_title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = EnhancementMediaBundler.Export(_enhancement, sourcePath, destPath);
            if (result.Success)
            {
                MessageBox.Show(this,
                    string.Format(Loc.Get("deeper_editor_export_success_fmt"), result.OutputPath),
                    Loc.Get("deeper_editor_export_dialog_title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    string.Format(Loc.Get("deeper_editor_export_failed_fmt"), result.Error ?? "(unknown)"),
                    Loc.Get("deeper_editor_export_dialog_title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MenuClose_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateTitle()
        {
            var name = string.IsNullOrEmpty(_enhancement.Metadata.Name)
                ? Loc.Get("deeper_editor_untitled") : _enhancement.Metadata.Name;
            TxtTitle.Text = name;
            Title = $"Deeper — {name}";
            // Linked-files strip shows the file path; keep it in sync.
            RefreshLinkedFilesUi();
            // Metadata drawer subtitle ("Metadata · {name}") shown when collapsed.
            UpdateMetadataDrawerSubtitle();
        }

        // -- Linked Files strip ----------------------------------------------

        private void RefreshLinkedFilesUi()
        {
            // JSON side
            if (string.IsNullOrEmpty(_filePath))
            {
                TxtLinkedJsonName.Text = Loc.Get("deeper_editor_unsaved");
                TxtLinkedJsonPath.Text = "";
                BtnLinkedJsonOpenFolder.IsEnabled = false;
            }
            else
            {
                TxtLinkedJsonName.Text = System.IO.Path.GetFileName(_filePath);
                TxtLinkedJsonPath.Text = _filePath;
                TxtLinkedJsonPath.ToolTip = _filePath;
                BtnLinkedJsonOpenFolder.IsEnabled = true;
            }

            // Media side
            var src = _enhancement?.MediaSource ?? "";
            var isUrl = src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            TxtLinkedMediaIcon.Text = _enhancement?.MediaType == MediaTypes.Audio ? "🎵" : "🎬";

            if (string.IsNullOrEmpty(src))
            {
                TxtLinkedMediaStatus.Text = "⚠";
                TxtLinkedMediaStatus.ToolTip = Loc.Get("deeper_editor_linked_status_local_missing");
                TxtLinkedMediaName.Text = Loc.Get("deeper_editor_linked_no_media");
                TxtLinkedMediaPath.Text = "";
                BtnLinkedMediaClear.IsEnabled = false;
            }
            else if (isUrl)
            {
                TxtLinkedMediaStatus.Text = "🌐";
                TxtLinkedMediaStatus.ToolTip = Loc.Get("deeper_editor_linked_status_url");
                string display;
                try { display = new Uri(src).Host; } catch { display = src; }
                TxtLinkedMediaName.Text = display;
                TxtLinkedMediaPath.Text = src;
                TxtLinkedMediaPath.ToolTip = src;
                BtnLinkedMediaClear.IsEnabled = true;
            }
            else
            {
                var exists = false;
                try { exists = File.Exists(src); } catch { }
                TxtLinkedMediaStatus.Text = exists ? "✓" : "⚠";
                TxtLinkedMediaStatus.ToolTip = exists
                    ? Loc.Get("deeper_editor_linked_status_local_ok")
                    : Loc.Get("deeper_editor_linked_status_local_missing");
                TxtLinkedMediaName.Text = System.IO.Path.GetFileName(src);
                TxtLinkedMediaPath.Text = src;
                TxtLinkedMediaPath.ToolTip = src;
                BtnLinkedMediaClear.IsEnabled = true;
            }
        }

        private void BtnLinkedJsonOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_filePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: open folder failed: {Error}", ex.Message);
            }
        }

        private void BtnLinkedJsonSwap_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardChanges()) return;

            var library = App.EnhancementLibrary;
            var dlg = new OpenFileDialog
            {
                Title = Loc.Get("deeper_editor_linked_swap_json_btn"),
                Filter = $"Deeper Enhancement (*{EnhancementLibrary.FileSuffix})|*{EnhancementLibrary.FileSuffix}|All files (*.*)|*.*",
                InitialDirectory = library?.LibraryFolder ?? ""
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var enhancement = library?.Open(dlg.FileName);
                if (enhancement == null) return;
                TeardownPreview();
                LoadEnhancement(enhancement, dlg.FileName);
                _ = InitializePreviewAsync();
            }
            catch (EnhancementLoadException ex)
            {
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: swap JSON failed");
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnLinkedMediaChange_Click(object sender, RoutedEventArgs e)
        {
            MediaChangePopup.IsOpen = true;
        }

        private void BtnChangeMediaLocal_Click(object sender, RoutedEventArgs e)
        {
            MediaChangePopup.IsOpen = false;
            var dlg = new OpenFileDialog
            {
                Title = Loc.Get("deeper_editor_linked_change_media_btn"),
                Filter =
                    "Media (audio + video)|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.mp4;*.webm;*.mkv;*.mov;*.avi;*.m4v"
                    + "|Audio (*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg)|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg"
                    + "|Video (*.mp4;*.webm;*.mkv;*.mov;*.avi;*.m4v)|*.mp4;*.webm;*.mkv;*.mov;*.avi;*.m4v"
                    + "|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            ApplyChangedMedia(dlg.FileName, isLocal: true);
        }

        private void BtnChangeMediaUrl_Click(object sender, RoutedEventArgs e)
        {
            MediaChangePopup.IsOpen = false;
            var prompt = new ConditioningControlPanel.Views.Deeper.UrlPromptDialog { Owner = this };
            if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Result)) return;
            ApplyChangedMedia(prompt.Result, isLocal: false);
        }

        // -- Drag & drop -----------------------------------------------------
        // Accepts media (audio/video) and *.ccpenh.json files dropped onto the
        // window. Media drops route through ApplyChangedMedia (embedded /
        // sidecar / library auto-detection included). Enhancement drops
        // route through LoadEnhancement with an unsaved-changes prompt.
        // WebView2's HwndHost surface eats drop events over the embedded
        // browser preview, but the lanes/timeline/sidebar still accept drops.

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                e.Effects = DragDropEffects.None;
                if (e.Data.GetDataPresent(DataFormats.FileDrop)
                    && e.Data.GetData(DataFormats.FileDrop) is string[] files
                    && files.Any(IsDroppableEditorPath))
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

                // Enhancement (.ccpenh.json) wins over raw media so a drop with
                // both a media file AND its sidecar opens the project rather
                // than just relinking the media.
                var enhPath = files.FirstOrDefault(IsEnhancementJsonPath);
                if (!string.IsNullOrEmpty(enhPath))
                {
                    LoadEnhancementFromDrop(enhPath);
                    return;
                }
                var mediaPath = files.FirstOrDefault(IsLocalMediaFile);
                if (!string.IsNullOrEmpty(mediaPath))
                {
                    ApplyChangedMedia(mediaPath, isLocal: true);
                    return;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: drop handler failed");
            }
        }

        private void LoadEnhancementFromDrop(string ccpenhJsonPath)
        {
            try
            {
                if (!ConfirmDiscardChanges()) return;
                var enhancement = Services.Deeper.EnhancementSerializer.LoadFromFile(ccpenhJsonPath);
                TeardownPreview();
                LoadEnhancement(enhancement, ccpenhJsonPath);
                _ = InitializePreviewAsync();
            }
            catch (Services.Deeper.EnhancementLoadException ex)
            {
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: drop-load enhancement failed");
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static bool IsDroppableEditorPath(string path)
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
            return path.EndsWith(".ccpenh.json", System.StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyChangedMedia(string newSource, bool isLocal)
        {
            // Detect a matching enhancement (embedded JSON, sidecar, or library)
            // for the new media. If found, offer to switch projects too.
            Enhancement? matched = null;
            string? matchedSource = null;

            try
            {
                if (isLocal && EnhancementMediaBundler.IsSupportedExtension(newSource)
                    && EnhancementMediaBundler.TryExtract(newSource, out var embedded, out _)
                    && embedded != null)
                {
                    matched = embedded;
                    matchedSource = newSource; // embedded source = the media itself
                }
            }
            catch (Exception ex) { App.Logger?.Debug("DeeperEditor: embedded probe failed: {Error}", ex.Message); }

            if (matched == null && isLocal)
            {
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(newSource);
                    var stem = System.IO.Path.GetFileNameWithoutExtension(newSource);
                    if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(stem))
                    {
                        var sidecar = System.IO.Path.Combine(dir, stem + EnhancementLibrary.FileSuffix);
                        if (File.Exists(sidecar))
                        {
                            matched = EnhancementSerializer.LoadFromFile(sidecar);
                            matchedSource = sidecar;
                        }
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("DeeperEditor: sidecar probe failed: {Error}", ex.Message); }
            }

            if (matched == null)
            {
                try
                {
                    var mediaTypeFilter = isLocal && IsLocalVideoFile(newSource)
                        ? MediaTypes.Video
                        : (isLocal ? MediaTypes.Audio : (string?)null);
                    var libMatch = App.EnhancementLibrary?.FindMatch(newSource, mediaTypeFilter);
                    if (libMatch != null)
                    {
                        matched = EnhancementSerializer.LoadFromFile(libMatch.FilePath);
                        matchedSource = libMatch.FilePath;
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("DeeperEditor: library probe failed: {Error}", ex.Message); }
            }

            if (matched != null && !string.IsNullOrEmpty(matchedSource))
            {
                var result = MessageBox.Show(this,
                    Loc.Get("deeper_editor_linked_replace_project_q"),
                    "Deeper",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes)
                {
                    if (!ConfirmDiscardChanges()) return;
                    // Use the matching project; treat embedded-source case as
                    // unsaved (filePath=null) so a save would write a new file
                    // rather than overwrite the media in place.
                    var loadPath = matchedSource.EndsWith(EnhancementLibrary.FileSuffix, StringComparison.OrdinalIgnoreCase)
                        ? matchedSource
                        : null;
                    TeardownPreview();
                    LoadEnhancement(matched, loadPath);
                    _ = InitializePreviewAsync();
                    return;
                }
                // No → fall through to relink media on the current project.
            }

            // Just relink: update the current enhancement's media source.
            _enhancement.MediaSource = newSource;
            if (isLocal)
            {
                _enhancement.MediaType = IsLocalVideoFile(newSource) ? MediaTypes.Video : MediaTypes.Audio;
            }
            else
            {
                _enhancement.MediaType = MediaTypes.Video;
            }
            MarkDirty();
            RefreshLinkedFilesUi();
            TeardownPreview();
            _ = InitializePreviewAsync();
            _ = TryAutoFillFromHtAsync(_enhancement.MediaSource);
        }

        private void BtnLinkedMediaClear_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_enhancement?.MediaSource)) return;
            var result = MessageBox.Show(this,
                Loc.Get("deeper_editor_linked_clear_confirm"),
                "Deeper",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;
            _enhancement.MediaSource = "";
            MarkDirty();
            TeardownPreview();
            ShowPlaceholder();
            RefreshLinkedFilesUi();
        }

        // Best-effort dirty-prompt for swap/load flows. Returns true if the
        // caller should proceed (user saved, or chose to discard, or there
        // were no unsaved changes); false if the user cancelled.
        private bool ConfirmDiscardChanges()
        {
            if (!_isDirty) return true;
            var result = MessageBox.Show(this,
                Loc.Get("deeper_editor_unsaved_prompt"),
                Loc.Get("deeper_editor_unsaved_prompt_title"),
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes)
            {
                MenuSave_Click(this, new RoutedEventArgs());
                // If save was cancelled (e.g. SaveAs dialog), _isDirty is still true.
                return !_isDirty;
            }
            return true; // discard
        }

        private static bool IsLocalVideoFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi" or ".m4v";
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        // -- Window lifecycle --------------------------------------------------

        private void DeeperEditorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Don't hijack typing inside text fields.
            var inTextBox = Keyboard.FocusedElement is System.Windows.Controls.TextBox;

            if (e.Key == Key.Space && !inTextBox)
            {
                BtnPlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Home && !inTextBox)
            {
                SeekToFraction(0);
                e.Handled = true;
            }
            else if (e.Key == Key.R && !inTextBox && _totalSeconds > 0)
            {
                var start = _currentSeconds;
                var end = Math.Min(_totalSeconds, start + 5);
                CreateRegion(start, end);
                e.Handled = true;
            }
            else if (e.Key == Key.H && !inTextBox && _totalSeconds > 0)
            {
                CreateHapticEventAtPlayhead();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !inTextBox && _selectionSet.Count > 1)
            {
                // Multi-select takes priority — bulk delete everything in the set.
                DeleteSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !inTextBox && _selectedRegion != null)
            {
                BtnDeleteRegion_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !inTextBox && _selectedHaptic != null)
            {
                BtnDeleteHaptic_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !inTextBox && _selectedEffect != null)
            {
                BtnDeleteEffect_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && !inTextBox && (_selectedRegion != null || _selectedHaptic != null || _selectionSet.Count > 0))
            {
                SelectNothing();
                e.Handled = true;
            }
            // Ctrl+Z / Ctrl+Shift+Z (or Ctrl+Y) — undo / redo. Editor-wide; the
            // !inTextBox guard keeps standard text-box undo intact when typing.
            else if (e.Key == Key.Z && !inTextBox && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    Redo();
                else
                    Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y && !inTextBox && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Redo();
                e.Handled = true;
            }
            // Ctrl+C / Ctrl+X / Ctrl+V — clipboard ops on the current selection.
            else if (e.Key == Key.C && !inTextBox && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && _selectionSet.Count > 0)
            {
                CopySelection();
                e.Handled = true;
            }
            else if (e.Key == Key.X && !inTextBox && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && _selectionSet.Count > 0)
            {
                CutSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.V && !inTextBox && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteFromClipboard();
                e.Handled = true;
            }
            // Ctrl+A — select every region/haptic/effect on the timeline.
            else if (e.Key == Key.A && !inTextBox && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SelectAllOnTimeline();
                e.Handled = true;
            }
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    MenuSaveAs_Click(this, new RoutedEventArgs());
                else
                    MenuSave_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.E && !inTextBox && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                MenuExportEnhanced_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // Set by MainWindow.OnClosing to skip the unsaved-changes prompt during
        // app shutdown. A user-initiated cancel there would block the whole
        // app from exiting and zombie the process.
        private bool _suppressDirtyPromptOnClose;

        public void ForceClose()
        {
            _suppressDirtyPromptOnClose = true;
            try { Close(); } catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isDirty && !_suppressDirtyPromptOnClose)
            {
                var result = MessageBox.Show(this,
                    Loc.Get("deeper_editor_close_unsaved_prompt"),
                    Loc.Get("deeper_editor_close_unsaved_title"),
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (result == MessageBoxResult.Yes)
                {
                    if (string.IsNullOrEmpty(_filePath)) MenuSaveAs_Click(this, new RoutedEventArgs());
                    else SaveTo(_filePath!);
                    if (_isDirty) { e.Cancel = true; return; }
                }
            }

            EndGazePick(commit: false);
            try { _htFetchCts?.Cancel(); _htFetchCts?.Dispose(); _htFetchCts = null; } catch { }
            // Force the tutorial overlay closed so its OnClosed teardown runs;
            // otherwise its static TutorialEventBus.Event subscription would
            // outlive the editor and pin the closed window.
            try { _editorTutorialOverlay?.Close(); } catch { }
            _editorTutorialOverlay = null;

            // Exit preview fullscreen synchronously so the reparent-on-Closed
            // lambda releases BrowserPreview back to PreviewHost BEFORE
            // DisposePlayback tries to dispose it.
            try { if (_isPreviewFullscreen) ExitPreviewFullscreen(); } catch { }

            DisposePlayback();
        }

        // Stops/disposes the playback resources tied to the *current* media so
        // a new media link can be initialized cleanly. Unlike DisposePlayback
        // (window-close path), this leaves the timers and WebView2 control
        // intact — the editor is still alive and may re-initialize them.
        private void TeardownPreview()
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    if (VideoPreview != null) VideoPreview.MediaPlayer = null;
                    try { if (_vlcLengthChanged != null) _mediaPlayer.LengthChanged -= _vlcLengthChanged; } catch { }
                    try { if (_vlcTimeChanged != null) _mediaPlayer.TimeChanged -= _vlcTimeChanged; } catch { }
                    try { if (_vlcEndReached != null) _mediaPlayer.EndReached -= _vlcEndReached; } catch { }
                    _vlcLengthChanged = null;
                    _vlcTimeChanged = null;
                    _vlcEndReached = null;
                    try { _mediaPlayer.Stop(); } catch { }
                    try { _mediaPlayer.Dispose(); } catch { }
                    _mediaPlayer = null;
                }
                if (_vlcMedia != null)
                {
                    try { _vlcMedia.Dispose(); } catch { }
                    _vlcMedia = null;
                }
                if (_waveOut != null)
                {
                    try { if (_waveOutStopped != null) _waveOut.PlaybackStopped -= _waveOutStopped; } catch { }
                    _waveOutStopped = null;
                    try { _waveOut.Stop(); } catch { }
                    try { _waveOut.Dispose(); } catch { }
                    _waveOut = null;
                }
                _audioReader?.Dispose();
                _audioReader = null;
                if (_browserSource != null)
                {
                    try { _browserSource.PlaybackTimeChanged -= OnBrowserTimeChanged; } catch { }
                    try { _browserSource.Dispose(); } catch { }
                    _browserSource = null;
                }
                _totalSeconds = 0;
                _currentSeconds = 0;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: teardown preview warning: {Error}", ex.Message);
            }
        }

        private void DisposePlayback()
        {
            try
            {
                _playheadTimer?.Stop();
                _validationTimer?.Stop();

                if (_mediaPlayer != null)
                {
                    if (VideoPreview != null) VideoPreview.MediaPlayer = null;
                    try { if (_vlcLengthChanged != null) _mediaPlayer.LengthChanged -= _vlcLengthChanged; } catch { }
                    try { if (_vlcTimeChanged != null) _mediaPlayer.TimeChanged -= _vlcTimeChanged; } catch { }
                    try { if (_vlcEndReached != null) _mediaPlayer.EndReached -= _vlcEndReached; } catch { }
                    _vlcLengthChanged = null;
                    _vlcTimeChanged = null;
                    _vlcEndReached = null;
                    _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                }
                if (_vlcMedia != null)
                {
                    try { _vlcMedia.Dispose(); } catch { }
                    _vlcMedia = null;
                }

                if (_waveOut != null)
                {
                    try { if (_waveOutStopped != null) _waveOut.PlaybackStopped -= _waveOutStopped; } catch { }
                    _waveOutStopped = null;
                    try { _waveOut.Stop(); } catch { }
                    _waveOut.Dispose();
                    _waveOut = null;
                }
                _audioReader?.Dispose();
                _audioReader = null;

                try
                {
                    if (_previewBrowserConfigured && BrowserPreview?.CoreWebView2 != null)
                    {
                        BrowserPreview.CoreWebView2.ContainsFullScreenElementChanged -= OnPreviewFullscreenChanged;
                        BrowserPreview.CoreWebView2.WebMessageReceived -= OnPreviewWebMessageReceived;
                    }
                }
                catch { }

                if (_browserSource != null)
                {
                    _browserSource.PlaybackTimeChanged -= OnBrowserTimeChanged;
                    _browserSource.Dispose();
                    _browserSource = null;
                }
                try
                {
                    if (BrowserPreview?.CoreWebView2 != null)
                        BrowserPreview.CoreWebView2.NavigationStarting -= OnBrowserNavigationStarting;
                }
                catch { }
                try { BrowserPreview?.Dispose(); } catch { }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: dispose playback warning: {Error}", ex.Message);
            }
        }
    }
}
