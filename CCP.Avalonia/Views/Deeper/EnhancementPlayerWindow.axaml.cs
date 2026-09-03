using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ShapePath = Avalonia.Controls.Shapes.Path;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// One row in the player's event log.
    ///
    /// PORTED from the nested <c>EnhancementPlayerWindow.EventLogEntry</c> in
    /// ConditioningControlPanel/Views/Deeper/EnhancementPlayerWindow.Mission3.cs, lifted to the
    /// namespace so <c>x:DataType</c> on the row DataTemplate names it without nested-type syntax.
    /// <c>Visibility OpenInEditorVisibility</c> becomes <c>bool HasRuleId</c>, bound to IsVisible.
    /// </summary>
    public sealed class EventLogEntry
    {
        public enum EventLogCategory { Action, Engine, Error }

        public DateTime Timestamp { get; init; } = DateTime.Now;
        public EventLogCategory Category { get; init; }
        public string Description { get; init; } = "";
        public string? RuleId { get; init; }
        public string? RuleLabel { get; init; }

        public string TimestampDisplay => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        public string IconGlyph => Category switch
        {
            EventLogCategory.Action => "⚡",
            EventLogCategory.Engine => "⚙",
            EventLogCategory.Error => "⚠",
            _ => "•",
        };

        public IBrush IconBrush => Category switch
        {
            EventLogCategory.Action => Res("DeeperAccentBrush"),
            EventLogCategory.Engine => Res("TextMutedBrush"),
            EventLogCategory.Error => Res("DangerBrush"),
            _ => Brushes.Gray,
        };

        // Action rows render flat; Engine + Error get a faint tinted background so they don't
        // compete with effect-firing rows.
        public IBrush RowBgBrush => Category switch
        {
            EventLogCategory.Engine => Res("DeeperLaneHeaderBrush"),
            EventLogCategory.Error => Res("DeeperLaneHeaderBrush"),
            _ => Brushes.Transparent,
        };

        public IBrush RuleLabelBrush => Res("DeeperAccentSoftBrush");

        public bool HasRuleLabel => !string.IsNullOrEmpty(RuleLabel);

        // "Open in editor" only makes sense when we know which rule fired. Hidden in v1 since the
        // engine stream doesn't carry the rule id back — see the WPF mission report.
        public bool HasRuleId => !string.IsNullOrEmpty(RuleId);

        private static IBrush Res(string key) =>
            Application.Current is { } app
            && app.TryFindResource(key, out var v) && v is IBrush b
                ? b
                : Brushes.Gray;
    }

    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Deeper/EnhancementPlayerWindow.xaml.cs AND its
    /// Mission 3 partial (EnhancementPlayerWindow.Mission3.cs) — the WPF class is one type split
    /// across two files; here it is one file, since the view is one view.
    ///
    /// End-user runtime UI for Deeper enhancements: pick media, optionally pick (or auto-discover)
    /// a matching .ccpenh.json, press play.
    ///
    /// <para><b>What is real here.</b> Everything that is view state and drawing: the status pill
    /// state machine, the file context strip, the mini-timeline (region bands, TimeReached rule
    /// pins, playhead, drag-to-seek), the "Now: [region]" overlay, the waveform renderer, the
    /// structured event log with its four filter pills, counts, clear and collapse, and the
    /// transport read-out. Those run off <see cref="_loadedEnhancement"/>, <see cref="_currentSec"/>
    /// and <see cref="_durationSec"/> rather than off the services.</para>
    ///
    /// <para><b>What is stubbed, and why.</b> Three families, each marked <c>ponytail:</c> at the
    /// call site:</para>
    /// <list type="bullet">
    ///   <item><b>Services.</b> EnhancementAudioPlayer (NAudio), EnhancementLibrary, WebcamTracking
    ///         and the editor jump still live in the WPF head, so every handler that reached them is
    ///         a stub. EnhancementHostService and DeeperFetcher no longer block anything: the host's
    ///         load path is EnhancementSerializer + EnhancementValidator, both public in Core, and
    ///         App.DeeperFetcher IS Core's <c>EnhancementFetcher</c>. Both are wired below. What
    ///         does NOT come with them is the host's OTHER half — <c>Bind</c>, the rule engine and
    ///         the playback time source — so a loaded enhancement's rules never fire here.</item>
    ///   <item><b>WebView2.</b> The WPF pane hosted a <c>wv2:WebView2</c> driven through
    ///         <c>CoreWebView2</c>. <see cref="Controls.WebHost"/> covers more of that than the
    ///         first pass assumed: NavigationStarted carries a settable Cancel (so the allowlist
    ///         and the one-shot file:// pin are real) and InvokeScript is ExecuteScriptAsync. What
    ///         genuinely has no counterpart is ZoomFactor, AddScriptToExecuteOnDocumentCreatedAsync
    ///         and ContainsFullScreenElementChanged — zoom and HTML5 fullscreen stay stubs.</item>
    ///   <item><b>Win32.</b> <c>WindowChromeHelper.ApplyDarkTitleBar</c> (DwmSetWindowAttribute) and
    ///         the borderless-fullscreen reparent (WindowInteropHelper + Forms.Screen.FromHandle +
    ///         WS_EX_TOOLWINDOW/Topmost). Neither is P/Invoked here — see the notes on each member.</item>
    /// </list>
    /// </summary>
    public partial class EnhancementPlayerWindow : Window
    {
        // ---- view state that stands in for the services -------------------------------------
        // The WPF window read _player / _host / _videoSource every tick. Those are head services;
        // holding the same three facts as fields lets every drawing path below port unchanged.
        private Enhancement? _loadedEnhancement;
        private string? _loadedFilePath;
        private double _currentSec;
        private double _durationSec;
        private bool _isPlaying;
        private bool _isVideoMode;

        private DispatcherTimer? _uiTimer;
        private float[]? _peaks;

        // Video clock read-back. See PollVideoTimeAsync.
        private bool _pollInFlight;
        private bool _pollDisabled;

        // One per window so EnhancementFetcher's per-session cache is worth having; disposed in
        // Window_Closing because it owns an HttpClient and its guarded handler.
        private EnhancementFetcher? _fetcher;

        // Tracks WHICH branch supplied the current enhancement. Drives the source badge.
        private enum DiscoverySource { Manual, Library, Sidecar, Embedded, Url, PromotedFromEmbedded }
        private DiscoverySource _lastDiscoverySource = DiscoverySource.Manual;
        private string? _lastMediaPathForCreateNew;

        // -- Mission 3 event-log state -------------------------------------------------------
        private readonly ObservableCollection<EventLogEntry> _logEntries = new();
        private string _activeFilter = "all"; // all | action | engine | error
        private const int MaxLogEntries = 30;
        private bool _eventLogCollapsed;
        private double _eventLogExpandedHeight = 140;

        // -- Mini-timeline state -------------------------------------------------------------
        private Enhancement? _miniEnhancement;
        private double _miniTotalSeconds;
        private bool _miniScrubbing;

        // -- controls ------------------------------------------------------------------------
        private readonly Border _statusPill, _mediaTypeIconBg, _sourcePill, _audioPane, _videoPane,
                                _nowRegionPanel;
        private readonly TextBlock _statusPillText, _mediaTypeIcon, _txtEnhName, _txtEnhPath,
                                   _txtEnhMetadata, _txtEnhSource, _txtAudioPath, _txtVideoStatus,
                                   _txtNowRegion, _txtMiniTimelineReadout, _txtCurrent, _txtTotal,
                                   _txtStatus, _txtPlayPauseGlyph, _txtEyeTracking, _txtCollapseGlyph,
                                   _txtFilterCountAll, _txtFilterCountActions, _txtFilterCountEngine,
                                   _txtFilterCountErrors;
        private readonly Button _btnOpenInEditor, _btnChange, _btnCreateNewEnhancement,
                                _btnUnloadEnhancement, _btnPictureInPicture;
        private readonly Grid _audioFileRow;
        private readonly StackPanel _browserZoomCluster, _volumePanel, _miniTimelinePanel;
        private readonly Canvas _waveformCanvas, _miniTimelineCanvas;
        private readonly ShapePath _waveformPath;
        private readonly Line _playheadLine;
        private readonly Rectangle _nowRegionSwatch;
        private readonly Popup _changePopup;
        private readonly Slider _sliderVolume;
        private readonly ScrollViewer _eventScroll;
        private readonly Controls.WebHost _videoBrowser;
        private readonly ItemsControl _lstEvents;
        private readonly ToggleButton _pillAll, _pillActions, _pillEngine, _pillErrors;

        /// <summary>
        /// Render constructor. RenderProof needs a parameterless one, and the empty player draws
        /// almost nothing — no bands, no rows, no overlay — so the sample is a VIDEO enhancement:
        /// that is the mode which puts WebHost's fallback panel on screen, which is this wave's
        /// proof. Internal, so no production caller can ship the sample.
        /// </summary>
        internal EnhancementPlayerWindow() : this(SampleEnhancement(), "render-sample")
        {
            SeedSampleEventLog();
            _currentSec = 42;      // inside the second sample region, so the Now overlay draws
            _durationSec = 180;
            _isPlaying = true;
            UpdateStatusPill();
            UiTimer_Tick(null, EventArgs.Empty);
        }

        /// <summary>
        /// The WPF window took (EnhancementAudioPlayer, EnhancementHostService) and a second ctor
        /// added (Enhancement, sourceTag) for the editor's Preview button. Both services live in the
        /// WPF head, so only the second pair survives; pass (null, null) for the empty player.
        /// </summary>
        public EnhancementPlayerWindow(Enhancement? enhancement, string? sourceTag)
        {
            AvaloniaXamlLoader.Load(this);

            // ponytail: needs WindowChromeHelper.ApplyDarkTitleBar (DwmSetWindowAttribute) and
            // RestoreOwnerOnClose. The title-bar tint has no Linux equivalent short of
            // SystemDecorations="None" plus a hand-drawn bar, which would cost this resizable
            // window its native move/resize/maximize for a colour. Left native and untinted.
            Closed += (_, _) => { try { (Owner as Window)?.Activate(); } catch { } };

            _statusPill = this.FindControl<Border>("StatusPill")!;
            _statusPillText = this.FindControl<TextBlock>("StatusPillText")!;
            _mediaTypeIconBg = this.FindControl<Border>("MediaTypeIconBg")!;
            _mediaTypeIcon = this.FindControl<TextBlock>("MediaTypeIcon")!;
            _sourcePill = this.FindControl<Border>("SourcePill")!;
            _audioPane = this.FindControl<Border>("AudioPane")!;
            _videoPane = this.FindControl<Border>("VideoPane")!;
            _nowRegionPanel = this.FindControl<Border>("NowRegionPanel")!;
            _txtEnhName = this.FindControl<TextBlock>("TxtEnhName")!;
            _txtEnhPath = this.FindControl<TextBlock>("TxtEnhPath")!;
            _txtEnhMetadata = this.FindControl<TextBlock>("TxtEnhMetadata")!;
            _txtEnhSource = this.FindControl<TextBlock>("TxtEnhSource")!;
            _txtAudioPath = this.FindControl<TextBlock>("TxtAudioPath")!;
            _txtVideoStatus = this.FindControl<TextBlock>("TxtVideoStatus")!;
            _txtNowRegion = this.FindControl<TextBlock>("TxtNowRegion")!;
            _txtMiniTimelineReadout = this.FindControl<TextBlock>("TxtMiniTimelineReadout")!;
            _txtCurrent = this.FindControl<TextBlock>("TxtCurrent")!;
            _txtTotal = this.FindControl<TextBlock>("TxtTotal")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _txtPlayPauseGlyph = this.FindControl<TextBlock>("TxtPlayPauseGlyph")!;
            _txtEyeTracking = this.FindControl<TextBlock>("TxtEyeTracking")!;
            _txtCollapseGlyph = this.FindControl<TextBlock>("TxtCollapseGlyph")!;
            _txtFilterCountAll = this.FindControl<TextBlock>("TxtFilterCountAll")!;
            _txtFilterCountActions = this.FindControl<TextBlock>("TxtFilterCountActions")!;
            _txtFilterCountEngine = this.FindControl<TextBlock>("TxtFilterCountEngine")!;
            _txtFilterCountErrors = this.FindControl<TextBlock>("TxtFilterCountErrors")!;
            _btnOpenInEditor = this.FindControl<Button>("BtnOpenInEditor")!;
            _btnChange = this.FindControl<Button>("BtnChange")!;
            _btnCreateNewEnhancement = this.FindControl<Button>("BtnCreateNewEnhancement")!;
            _btnUnloadEnhancement = this.FindControl<Button>("BtnUnloadEnhancement")!;
            _btnPictureInPicture = this.FindControl<Button>("BtnPictureInPicture")!;
            _audioFileRow = this.FindControl<Grid>("AudioFileRow")!;
            _browserZoomCluster = this.FindControl<StackPanel>("BrowserZoomCluster")!;
            _volumePanel = this.FindControl<StackPanel>("VolumePanel")!;
            _miniTimelinePanel = this.FindControl<StackPanel>("MiniTimelinePanel")!;
            _waveformCanvas = this.FindControl<Canvas>("WaveformCanvas")!;
            _miniTimelineCanvas = this.FindControl<Canvas>("MiniTimelineCanvas")!;
            _waveformPath = this.FindControl<ShapePath>("WaveformPath")!;
            _playheadLine = this.FindControl<Line>("PlayheadLine")!;
            _nowRegionSwatch = this.FindControl<Rectangle>("NowRegionSwatch")!;
            _changePopup = this.FindControl<Popup>("ChangePopup")!;
            _sliderVolume = this.FindControl<Slider>("SliderVolume")!;
            _eventScroll = this.FindControl<ScrollViewer>("EventScroll")!;
            _videoBrowser = this.FindControl<Controls.WebHost>("VideoBrowser")!;
            _lstEvents = this.FindControl<ItemsControl>("LstEvents")!;
            _pillAll = this.FindControl<ToggleButton>("PillFilterAll")!;
            _pillActions = this.FindControl<ToggleButton>("PillFilterActions")!;
            _pillEngine = this.FindControl<ToggleButton>("PillFilterEngine")!;
            _pillErrors = this.FindControl<ToggleButton>("PillFilterErrors")!;

            // Static strings the code also writes: seeded here rather than bound in XAML, because
            // Avalonia keeps a {loc:Str} binding alive under a local value and would undo the write
            // on the next language change. Keys are the WPF originals.
            _statusPillText.Text = Loc.Get("deeper_player_pill_empty");
            _txtEnhName.Text = Loc.Get("deeper_player_no_enh");
            _txtAudioPath.Text = Loc.Get("deeper_player_no_media");
            _txtVideoStatus.Text = Loc.Get("deeper_player_video_loading");
            _txtStatus.Text = Loc.Get("deeper_player_status_idle");
            _txtEyeTracking.Text = Loc.Get("deeper_player_btn_eye_tracking_start");

            _changePopup.PlacementTarget = _btnChange;

            this.FindControl<Button>("BtnPlayPause")!.Click += (_, _) => BtnPlayPause_Click();
            this.FindControl<Button>("BtnStop")!.Click += (_, _) => BtnStop_Click();
            this.FindControl<Button>("BtnZoomIn")!.Click += (_, _) => AdjustVideoZoom(+0.10);
            this.FindControl<Button>("BtnZoomOut")!.Click += (_, _) => AdjustVideoZoom(-0.10);
            this.FindControl<Button>("BtnPickAudio")!.Click += (_, _) => BtnPickAudio_Click();
            this.FindControl<Button>("BtnPickEnhancement")!.Click += (_, _) => BtnPickEnhancement_Click();
            this.FindControl<Button>("BtnLoadUrl")!.Click += (_, _) => BtnLoadUrl_Click();
            this.FindControl<Button>("BtnClearEvents")!.Click += (_, _) => BtnClearEvents_Click();
            this.FindControl<Button>("BtnCollapseEventLog")!.Click += (_, _) => BtnCollapseEventLog_Click();
            _btnOpenInEditor.Click += (_, _) => JumpToEditorForCurrentEnhancement(ruleId: null);
            _btnChange.Click += (_, _) => BtnChange_Click();
            _btnCreateNewEnhancement.Click += (_, _) => BtnCreateNewEnhancement_Click();
            _btnUnloadEnhancement.Click += (_, _) => Unload();
            this.FindControl<Button>("BtnEyeTracking")!.Click += (_, _) => BtnEyeTracking_Click();
            _btnPictureInPicture.Click += (_, _) => BtnPictureInPicture_Click();
            _sliderVolume.PropertyChanged += SliderVolume_PropertyChanged;

            foreach (var pill in new[] { _pillAll, _pillActions, _pillEngine, _pillErrors })
                pill.Click += (s, _) => EventFilterPill_Click(s as ToggleButton);

            // Both canvases: WPF only hooked the mini one because WPF re-ran the waveform render on
            // its own layout pass. Here the first pass has zero bounds, so a canvas that is never
            // told it resized draws nothing at all.
            _miniTimelineCanvas.SizeChanged += (_, _) => RebuildMiniTimeline();
            _waveformCanvas.SizeChanged += (_, _) => { RenderWaveform(); UpdatePlayhead(Fraction()); };

            _miniTimelineCanvas.PointerPressed += MiniTimelineCanvas_PointerPressed;
            _miniTimelineCanvas.PointerMoved += MiniTimelineCanvas_PointerMoved;
            _miniTimelineCanvas.PointerReleased += MiniTimelineCanvas_PointerReleased;
            _miniTimelineCanvas.PointerCaptureLost += (_, _) => _miniScrubbing = false;
            _waveformCanvas.PointerPressed += WaveformCanvas_PointerPressed;

            AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            AddHandler(DragDrop.DropEvent, Window_Drop);

            RefreshEventList();
            UpdateFilterCounts();
            UpdateStatusPill();

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();
            Closing += (_, _) => Window_Closing();

            if (enhancement != null)
                UpdateHostUi(enhancement, sourceTag);
        }

        // ====================================================================================
        // Public entry points (WPF: LoadEnhancementFile / OpenLocalMediaFile)
        // ====================================================================================

        /// <summary>
        /// Launch the player on a .ccpenh.json file (the hub library row's ▶).
        ///
        /// This is EnhancementHostService.LoadFromFile minus the engine: read, validate, reject on
        /// the first Error-severity issue, then show it. Warnings do not block a load in the
        /// original either — they name rules that will never fire, which is the whole picture here.
        /// The WPF deferred-load queue (QueueDeferredLoad / OnDeferredLoadReady) existed because
        /// LoadLocalVideoAsync touched a WebView2 before the XAML was live; nothing here does.
        ///
        /// The caller owns <see cref="_lastDiscoverySource"/>: this used to force it to Library,
        /// which mislabelled every hand-picked and dropped file as "from library".
        /// </summary>
        public void LoadEnhancementFile(string ccpenhJsonPath)
        {
            if (string.IsNullOrWhiteSpace(ccpenhJsonPath)) return;
            if (!File.Exists(ccpenhJsonPath)) { ReportLoadFailure($"File not found: {ccpenhJsonPath}"); return; }
            try
            {
                LoadEnhancementInMemory(EnhancementSerializer.LoadFromFile(ccpenhJsonPath), ccpenhJsonPath);
            }
            catch (Exception ex)
            {
                // EnhancementLoadException carries the human-readable parse reason; anything else
                // is IO. Both end up in front of the user the same way the WPF LoadFailed did.
                ReportLoadFailure(ex.Message);
            }
        }

        /// <summary>
        /// EnhancementHostService.LoadFromMemory minus the engine. Validates even though the file
        /// path already did, because the URL path and the embedded-media path both land here with
        /// content that came off the network or out of someone else's file.
        /// </summary>
        private bool LoadEnhancementInMemory(Enhancement? enh, string? sourceTag)
        {
            if (enh == null) return false;
            try
            {
                var firstError = EnhancementValidator.Validate(enh)
                    .Find(i => i.Severity == ValidationSeverity.Error);
                if (firstError != null)
                {
                    ReportLoadFailure($"Validation failed: {firstError.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ReportLoadFailure(ex.Message);
                return false;
            }

            UpdateHostUi(enh, sourceTag);
            Log.Information("EnhancementPlayer(Avalonia): loaded {Name} ({Tag})",
                enh.Metadata?.Name ?? "(untitled)", sourceTag);
            return true;
        }

        /// <summary>WPF's OnHostLoadFailed: status line plus a durable row in the event log, so a
        /// failure does not vanish the moment the user picks another file.</summary>
        private void ReportLoadFailure(string reason)
        {
            _txtStatus.Text = Loc.GetF("deeper_player_status_enh_failed_fmt", reason);
            IngestErrorLine(reason);
        }

        /// <summary>External launcher entry (file association, drag-drop dispatch).</summary>
        public void OpenLocalMediaFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (IsLocalVideoFile(path)) LoadLocalVideo(path);
            else LoadAudio(path);
            TryAutoLoadEnhancement(path);
        }

        // ====================================================================================
        // Drag & drop
        // ====================================================================================

        private void Window_DragOver(object? sender, DragEventArgs e)
        {
            try
            {
                e.DragEffects = DragDropEffects.None;
                if (e.DataTransfer.TryGetFiles() is { } files
                    && files.Select(f => f.TryGetLocalPath()).Any(p => p != null && IsDroppablePlayerPath(p)))
                {
                    e.DragEffects = DragDropEffects.Copy;
                }
            }
            catch { }
            e.Handled = true;
        }

        private void Window_Drop(object? sender, DragEventArgs e)
        {
            try
            {
                var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToList();
                if (files == null || files.Count == 0) return;
                e.Handled = true;

                // Enhancement (.ccpenh.json) wins over raw media so a drop containing both opens
                // the project; the load then pulls the media in via MediaSource.
                var enhPath = files.FirstOrDefault(f => IsEnhancementJsonPath(f!));
                if (!string.IsNullOrEmpty(enhPath))
                {
                    _lastDiscoverySource = DiscoverySource.Manual;
                    LoadEnhancementFile(enhPath!);
                    return;
                }
                var mediaPath = files.FirstOrDefault(f => IsLocalMediaFile(f!));
                if (!string.IsNullOrEmpty(mediaPath)) OpenLocalMediaFile(mediaPath!);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: drop handler failed");
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
            => !string.IsNullOrWhiteSpace(path)
               && path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase);

        // The canonical check lives in EnhancementResolver so the player and the video bridge agree.
        private static bool IsLocalVideoFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi" or ".m4v";
        }

        // ====================================================================================
        // File pickers
        // ====================================================================================

        private async void BtnPickAudio_Click()
        {
            // Method name is historical — the picker takes audio and video; dispatch on extension.
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("deeper_player_pick_media"),
                    AllowMultiple = false,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new("Media (audio + video)")
                        {
                            Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg",
                                               "*.mp4", "*.webm", "*.mkv", "*.mov", "*.avi", "*.m4v" },
                        },
                        new("Audio") { Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg" } },
                        new("Video") { Patterns = new[] { "*.mp4", "*.webm", "*.mkv", "*.mov", "*.avi", "*.m4v" } },
                        FilePickerFileTypes.All,
                    },
                });
                if (files.Count == 0) return;
                var path = files[0].TryGetLocalPath() ?? files[0].Path.ToString();
                if (IsLocalVideoFile(path)) LoadLocalVideo(path);
                else LoadAudio(path);
                TryAutoLoadEnhancement(path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: media picker failed");
            }
        }

        private async void BtnPickEnhancement_Click()
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("deeper_player_pick_enh"),
                    AllowMultiple = false,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new("Deeper Enhancement") { Patterns = new[] { "*.ccpenh.json" } },
                        FilePickerFileTypes.All,
                    },
                });
                if (files.Count == 0) return;
                _lastDiscoverySource = DiscoverySource.Manual;
                LoadEnhancementFile(files[0].TryGetLocalPath() ?? files[0].Path.ToString());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: enhancement picker failed");
            }
        }

        private void Unload()
        {
            // EnhancementHostService.Unload was UnbindEngine + clear + raise Loaded(null). There is
            // no engine to unbind here, so the whole of it is UpdateHostUi(null, null).
            // Refusing every navigation is the right resting state for a host with nothing loaded:
            // whatever page is still up cannot follow a link or a redirect out of it.
            _videoBrowser.AllowNavigation = _ => false;
            UpdateHostUi(null, null);
        }

        /// <summary>
        /// Fetch a .ccpenh.json by URL. The note here used to say this needed three things to move
        /// to Core; none of that is true any more. UrlPromptDialog is ported (Views/Dialogs), and
        /// App.DeeperFetcher was always an <see cref="EnhancementFetcher"/>, which is in Core and
        /// public — SSRF guard, 256 KB cap, manual per-hop redirect revalidation and all.
        /// One fetcher for the window's life, so its per-session cache actually caches.
        /// </summary>
        private async void BtnLoadUrl_Click()
        {
            try
            {
                var dlg = new Dialogs.UrlPromptDialog();
                await dlg.ShowDialog(this);
                if (string.IsNullOrEmpty(dlg.Result)) return;

                _txtStatus.Text = Loc.Get("deeper_player_status_fetching_url");
                _fetcher ??= new EnhancementFetcher();
                var enh = await _fetcher.FetchAsync(dlg.Result);
                if (enh == null)
                {
                    _txtStatus.Text = Loc.Get("deeper_player_status_url_failed");
                    return;
                }
                _lastDiscoverySource = DiscoverySource.Url;
                if (LoadEnhancementInMemory(enh, dlg.Result))
                    _txtStatus.Text = Loc.Get("deeper_player_status_url_loaded");
            }
            catch (Exception ex)
            {
                Log.Debug("EnhancementPlayer: URL load error: {Error}", ex.Message);
                _txtStatus.Text = Loc.Get("deeper_player_status_url_failed");
            }
        }

        /// <summary>
        /// EnhancementResolver's ladder, walked here rather than called: the resolver itself is in
        /// the WPF head and its tier 2 reaches App.EnhancementLibrary, so calling it is not an
        /// option. Tiers 0 and 1 are — EnhancementMediaBundler is in Core and the sidecar is one
        /// Path.Combine — so two of the three tiers are real.
        ///
        /// ponytail: tier 2 (EnhancementLibrary.FindMatch by media_source pattern), the
        /// auto-promotion of an embedded enhancement into the library, and the 6 s "promoted to
        /// library" toast all need EnhancementLibrary, which is App.UserDataPath + a
        /// FileSystemWatcher + a WPF DispatcherTimer and has not moved. Until it does, a media file
        /// whose enhancement lives only in the library lands in the "nothing found" branch and
        /// offers Create-new — a real branch of the original, just reached more often than it
        /// should be.
        /// </summary>
        private void TryAutoLoadEnhancement(string mediaPath)
        {
            _lastMediaPathForCreateNew = mediaPath;
            _btnCreateNewEnhancement.IsVisible = false;
            try
            {
                // 0) Embedded box in the media file itself. First, because a self-contained export
                //    beats a stale sidecar that happens to share its basename.
                if (EnhancementMediaBundler.IsSupportedExtension(mediaPath)
                    && EnhancementMediaBundler.TryExtract(mediaPath, out var embedded, out _)
                    && embedded != null)
                {
                    _lastDiscoverySource = DiscoverySource.Embedded;
                    if (LoadEnhancementInMemory(embedded, "embedded:" + System.IO.Path.GetFileName(mediaPath)))
                        return;
                }

                // 1) Side-by-side sidecar: foo.mp4 -> foo.ccpenh.json next to it.
                var dir = System.IO.Path.GetDirectoryName(mediaPath);
                var baseName = System.IO.Path.GetFileNameWithoutExtension(mediaPath);
                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(baseName))
                {
                    var candidate = System.IO.Path.Combine(dir, baseName + ".ccpenh.json");
                    if (File.Exists(candidate))
                    {
                        _lastDiscoverySource = DiscoverySource.Sidecar;
                        LoadEnhancementFile(candidate);
                        return;
                    }
                }

                // 2) Nothing found — offer to author against this media.
                _btnCreateNewEnhancement.IsVisible = true;
                _txtStatus.Text = Loc.Get("deeper_player_no_enh_for_media");
            }
            catch (Exception ex)
            {
                // WPF swallowed IO errors here too: the button stays hidden and no status is set.
                Log.Debug("EnhancementPlayer: auto-load enhancement failed: {Error}", ex.Message);
            }
        }

        private void BtnCreateNewEnhancement_Click()
        {
            if (string.IsNullOrEmpty(_lastMediaPathForCreateNew)) return;
            // ponytail: needs EnhancementLibrary.CreateBlank + DeeperEditorWindow, wired when they
            // move to Core / are ported.
            Log.Debug("EnhancementPlayer(Avalonia): create-new is a stub for {Path}", _lastMediaPathForCreateNew);
        }

        // ====================================================================================
        // Media loading
        // ====================================================================================

        private void LoadAudio(string path)
        {
            // ponytail: needs EnhancementAudioPlayer (NAudio) + AudioWaveformCache, wired when they
            // move to Core. The WPF path stopped playback, decoded peaks, called Play, then set
            // TxtTotal / the play glyph / deeper_player_status_playing. Only the UI half runs here.
            _txtAudioPath.Text = path;
            _txtStatus.Text = Loc.Get("deeper_player_status_loading_audio");
            ShowMediaPaneFor(MediaTypes.Audio);
            _peaks = null;
            _waveformPath.Data = null;
        }

        /// <summary>
        /// The WPF path navigated the WebView2 to a file:// URL and let Edge's media viewer wrap it
        /// in a &lt;video&gt; element. Same shape here, with the same one-shot allowlist: the media
        /// path can come from a shared .ccpenh.json, so it is checked for BEING a video before the
        /// engine is pointed at it, and the navigation gate then pins the engine to that one file
        /// for the rest of the load. Without both, a MediaSource of "/etc/passwd" renders.
        /// </summary>
        private void LoadLocalVideo(string path)
        {
            try
            {
                ShowMediaPaneFor(MediaTypes.Video);
                _txtVideoStatus.Text = Loc.Get("deeper_player_video_loading");
                _txtVideoStatus.IsVisible = true;

                // Extension AND rooted-path check before anything reaches the engine. File.Exists
                // upstream only proves a file is there, not that it is media.
                if (!IsLocalVideoFile(path) || !System.IO.Path.IsPathRooted(path))
                {
                    Log.Warning("EnhancementPlayer: rejected non-video local MediaSource");
                    _txtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                    _txtStatus.Text = Loc.Get("deeper_player_status_host_not_allowed");
                    return;
                }

                // WPF's `if (!await EnsureVideoBrowserReadyAsync())` branch: no engine, no
                // navigation, and say so rather than sitting on "Loading video..." forever
                // underneath WebHost's own "web view unavailable" panel.
                if (!_videoBrowser.HasEngine)
                {
                    _txtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                    return;
                }

                // Full path, because the engine round-trips the URL through file:// encoding and
                // hands LocalPath back in a form that only matches after normalisation.
                var full = System.IO.Path.GetFullPath(path);
                _videoBrowser.AllowNavigation = u => u.IsFile && PathsEqual(u.LocalPath, full);
                _videoBrowser.Source = new Uri(full);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: local video load failed");
                _txtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
            }
        }

        private static bool PathsEqual(string? a, string b)
        {
            if (string.IsNullOrEmpty(a)) return false;
            try { return string.Equals(System.IO.Path.GetFullPath(a), b, StringComparison.Ordinal); }
            catch { return false; }
        }

        private void LoadVideoUrl(string url)
        {
            try
            {
                _txtVideoStatus.Text = Loc.Get("deeper_player_video_loading");
                _txtVideoStatus.IsVisible = true;

                // ponytail: the WPF pre-flight was scheme + UrlSafety.HostMatches(uri,
                // DeeperConfig.PreviewHostAllowlist). Both of those live in CCP.Core but are
                // `internal`, and Core's InternalsVisibleTo names the WPF app and the test projects,
                // not this head — so the FIRST navigation to an arbitrary https host out of a shared
                // .ccpenh.json is still allowed here. Only the scheme is checked. Widening Core's
                // InternalsVisibleTo (or making those two public) is its own layer; copying the
                // allowlist into this head would be the second copy of a security rule.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                {
                    Log.Warning("EnhancementPlayer: rejected video MediaSource {Host}", uri?.Host);
                    _txtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                    _txtStatus.Text = Loc.Get("deeper_player_status_host_not_allowed");
                    return;
                }

                if (!_videoBrowser.HasEngine)
                {
                    _txtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
                    return;
                }

                // What the gate CAN do without the allowlist: pin the engine to the host we were
                // asked to load. That is strictly narrower than what WPF's NavigationStarting
                // enforced — it does not vet the first hop — but it does close the redirect leg,
                // which is the one the pre-flight never saw. "www." is tolerated in both directions
                // because the sites in the original allowlist redirect between the two forms.
                var pinned = uri.Host;
                _videoBrowser.AllowNavigation = u =>
                    u.Scheme == Uri.UriSchemeHttps && HostsMatchIgnoringWww(u.Host, pinned);
                _videoBrowser.Source = uri;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: video load failed");
                _txtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
            }
        }

        /// <summary>Host equality that ignores a leading "www.". Not a domain-suffix match: a
        /// subdomain is a DIFFERENT host here on purpose, since this pins to one page's host rather
        /// than admitting a whole domain the way the allowlist did.</summary>
        private static bool HostsMatchIgnoringWww(string? a, string? b)
        {
            static string Strip(string? h) =>
                (h ?? "").StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h![4..] : (h ?? "");
            var (x, y) = (Strip(a), Strip(b));
            return x.Length > 0 && x.Equals(y, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRemoteVideoUrl(string? source)
            => !string.IsNullOrWhiteSpace(source)
               && (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        // ====================================================================================
        // Transport
        // ====================================================================================

        /// <summary>
        /// Transport. Video is real — the page's own &lt;video&gt; is played and paused through
        /// the script channel, and <see cref="PollVideoTimeAsync"/> reads its clock back — so the
        /// glyph, the status line, the pill and the playhead all describe something that is
        /// actually happening.
        ///
        /// Audio deliberately does NOT flip the state. Before this layer the media pickers could
        /// not load anything, so the "nothing to play" branch caught every press; now they can, and
        /// a press with no audio engine would have shown ⏸, the LIVE pill and "Playing" over
        /// silence. There is no NAudio here, so the honest answer is to refuse and say so in the
        /// event log rather than to draw a transport that is running.
        ///
        /// ponytail: needs EnhancementAudioPlayer for the audio play/pause/resume/replay ladder,
        /// and MaybePromptForWebcamBeforePlay for the consent gate — which is a second reason not
        /// to make Play do more than this: skipping a consent prompt is worse than not playing.
        /// Note that even in video mode the enhancement's RULES do not fire: that needs
        /// EnhancementHostService.Bind and the engine, both still in the WPF head.
        /// </summary>
        private void BtnPlayPause_Click()
        {
            if (_loadedEnhancement == null && _durationSec <= 0)
            {
                _txtStatus.Text = Loc.Get("deeper_player_status_pick_first");
                return;
            }

            if (!_isVideoMode || !_videoBrowser.HasEngine)
            {
                IngestErrorLine("playback engine unavailable on this platform: audio is not played here");
                return;
            }

            _isPlaying = !_isPlaying;
            _ = _videoBrowser.InvokeScriptAsync(VideoScript(_isPlaying ? "play()" : "pause()"));
            _txtPlayPauseGlyph.Text = _isPlaying ? "⏸" : "▶";
            _txtStatus.Text = Loc.Get(_isPlaying ? "deeper_player_status_playing" : "deeper_player_status_stopped");
            UpdateStatusPill();
        }

        private void BtnStop_Click()
        {
            if (_isVideoMode && _videoBrowser.HasEngine)
                _ = _videoBrowser.InvokeScriptAsync(VideoScript("pause(); l.currentTime=0"));
            _isPlaying = false;
            _currentSec = 0;
            _txtPlayPauseGlyph.Text = "▶";
            _txtCurrent.Text = "0:00";
            UpdatePlayhead(0);
            _txtStatus.Text = Loc.Get("deeper_player_status_stopped");
            UpdateStatusPill();
        }

        /// <summary>
        /// Addresses the page's largest &lt;video&gt;, which is BrowserVideoTimeSource's rule too:
        /// a HypnoTube page carries preview thumbnails that are also video elements, and the first
        /// one in document order is routinely not the one being watched.
        /// </summary>
        private static string VideoScript(string tail) =>
            "(function(){var l=null,a=0;document.querySelectorAll('video').forEach(function(v){"
            + "var s=(v.clientWidth||0)*(v.clientHeight||0); if(s>=a){a=s;l=v;}});"
            + "if(!l)return '';l." + tail + ";return 'ok';})()";

        private void BtnEyeTracking_Click()
        {
            // ponytail: needs WebcamTrackingService (start/stop, IsConsentCurrent). Left whole
            // rather than half-restored ON PURPOSE: the portable half of this button is the label
            // and the pill, and a button that reads "eye tracking: on" with no camera open — or,
            // worse, one that reaches a start path around the first-time consent prompt this head
            // has no MessageBox for — is worse than a button that does nothing. Restore the
            // consent gate and the camera together or not at all. Keys the WPF prompts use:
            // deeper_player_eye_tracking_unavailable / _first_time / _confirm_start / _start_failed_fmt.
            Log.Debug("EnhancementPlayer(Avalonia): eye tracking toggle is a stub");
        }

        private void AdjustVideoZoom(double delta)
        {
            // ponytail: needs a browser zoom factor. NativeWebView genuinely has none — this is one
            // of the three CoreWebView2 members with no counterpart (the others are
            // AddScriptToExecuteOnDocumentCreatedAsync and ContainsFullScreenElementChanged), and
            // it is NOT a missing script channel: InvokeScript works. CSS `zoom` through that
            // channel was the obvious substitute and is not one — it is per-document, so it is lost
            // on the next navigation, and it does not scale a fullscreened video at all. So the
            // ±10% clamp to [0.25, 5.0] and the Ctrl+MouseWheel bridge stay lost here.
            Log.Debug("EnhancementPlayer(Avalonia): browser zoom {Delta:+0.00;-0.00} is a stub", delta);
        }

        /// <summary>
        /// Toggles the page's own picture-in-picture, the same way the WPF handler's injected JS
        /// did. Whether it works is the engine's call, not ours: WebKitGTK may not implement the
        /// W3C API, and Chromium rejects requestPictureInPicture without user activation — a
        /// host-invoked script may not count as one. So the script reports what happened and the
        /// answer goes in the event log, rather than the button appearing to have worked.
        /// </summary>
        private async void BtnPictureInPicture_Click()
        {
            if (!_videoBrowser.HasEngine) return;
            var result = Unquote(await _videoBrowser.InvokeScriptAsync(
                "(function(){var l=null,a=0;document.querySelectorAll('video').forEach(function(v){"
                + "var s=(v.clientWidth||0)*(v.clientHeight||0); if(s>=a){a=s;l=v;}});"
                + "if(!l)return 'no video on page';"
                + "try{ if(document.pictureInPictureElement){document.exitPictureInPicture();return 'exit';}"
                + "if(!document.pictureInPictureEnabled)return 'picture-in-picture not supported by this engine';"
                + "l.requestPictureInPicture().catch(function(){});return 'enter';}"
                + "catch(e){return e.name+': '+e.message;}})()"));
            // ponytail: requestPictureInPicture is a promise, so an ASYNC rejection (Chromium's
            // "requires user activation") is swallowed by that .catch and reads as 'enter' here.
            // Reporting it needs WebMessageReceived, which NativeWebView has — a bridge worth
            // building once something else needs one, not for this button alone.
            if (result is not ("enter" or "exit" or "")) IngestErrorLine("picture-in-picture: " + result);
        }

        private void SliderVolume_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != RangeBase.ValueProperty) return;
            // ponytail: needs EnhancementAudioPlayer.Volume, wired when it moves to Core — both
            // directions: the write on drag, and UpdateVolumeFromPlayer's read-back on open, which
            // is why the WPF handler carried a _suppressVolumeSync re-entrancy flag. Not routed to
            // the video's volume instead: this panel is hidden in video mode (ShowMediaPaneFor), so
            // a viewer never sees it while a video is what is playing.
        }

        // ====================================================================================
        // UI tick
        // ====================================================================================

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_miniScrubbing) return;

            if (_isVideoMode) _ = PollVideoTimeAsync();

            _txtCurrent.Text = FormatTime(_currentSec);
            if (_durationSec > 0) _txtTotal.Text = FormatTime(_durationSec);
            if (!_isVideoMode) UpdatePlayhead(Fraction());

            UpdateMiniPlayheadX();
            UpdateMiniTimelineReadout();
            RefreshNowRegionOverlay();
            UpdateStatusPill();
        }

        private double Fraction() => _durationSec > 0 ? _currentSec / _durationSec : 0;

        /// <summary>
        /// BrowserVideoTimeSource's core, minus the engine binding: read currentTime/duration/paused
        /// off the page each tick so the read-out, playhead, region overlay and mini-timeline
        /// describe the video rather than a field nothing writes.
        ///
        /// Re-entrancy matters — the tick is 100 ms and a script round-trip through the adapter is
        /// not guaranteed to be faster — so a poll in flight skips the next tick rather than
        /// queueing. A poll that throws stops polling for good: 10 failures a second into the log
        /// helps nobody, and the read-out simply stops moving.
        /// </summary>
        private async Task PollVideoTimeAsync()
        {
            if (_pollInFlight || _pollDisabled || !_videoBrowser.HasEngine) return;
            _pollInFlight = true;
            try
            {
                var raw = await _videoBrowser.InvokeScriptAsync(
                    "(function(){var l=null,a=0;document.querySelectorAll('video').forEach(function(v){"
                    + "var s=(v.clientWidth||0)*(v.clientHeight||0); if(s>=a){a=s;l=v;}});"
                    + "return l?(l.currentTime+'|'+(l.duration||0)+'|'+(l.paused?0:1)):'';})()");

                var parts = Unquote(raw).Split('|');
                if (parts.Length != 3) return;
                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var cur))
                    _currentSec = cur;
                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)
                    && dur > 0 && !double.IsInfinity(dur))
                    _durationSec = dur;

                // The page can start or stop playing without us — an autoplay, an ad, the user
                // clicking the video's own controls. Follow it, or the pill lies the other way.
                var playing = parts[2] == "1";
                if (playing != _isPlaying)
                {
                    _isPlaying = playing;
                    _txtPlayPauseGlyph.Text = playing ? "⏸" : "▶";
                }
            }
            catch (Exception ex)
            {
                _pollDisabled = true;
                Log.Debug("EnhancementPlayer: video time poll disabled after {Error}", ex.Message);
            }
            finally { _pollInFlight = false; }
        }

        /// <summary>WebView2 returns a JSON literal (a string comes back quoted); WebKitGTK returns
        /// the raw value. Strip one layer of quotes so the caller sees the same thing on both.</summary>
        private static string Unquote(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
        }

        private void UpdateMiniTimelineReadout()
        {
            try
            {
                var totalSec = _durationSec > 0 ? _durationSec : _miniTotalSeconds;
                _txtMiniTimelineReadout.Text = $"{FormatTime(_currentSec)} / {FormatTime(totalSec)}";
            }
            catch { }
        }

        // ====================================================================================
        // Waveform render + scrub
        // ====================================================================================

        private void RenderWaveform()
        {
            if (_peaks == null || _peaks.Length == 0)
            {
                _waveformPath.Data = null;
                return;
            }

            var w = _waveformCanvas.Bounds.Width;
            var h = _waveformCanvas.Bounds.Height;
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
                    ctx.BeginFigure(new Point(x, midY - v * amp), isFilled: false);
                    ctx.LineTo(new Point(x, midY + v * amp));
                    ctx.EndFigure(false);
                }
            }
            _waveformPath.Data = geom;
        }

        private void UpdatePlayhead(double frac)
        {
            var w = _waveformCanvas.Bounds.Width;
            var h = _waveformCanvas.Bounds.Height;
            if (w <= 0 || h <= 0) return;
            var x = Math.Clamp(frac, 0, 1) * w;
            _playheadLine.StartPoint = new Point(x, 0);
            _playheadLine.EndPoint = new Point(x, h);
        }

        private void WaveformCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var w = _waveformCanvas.Bounds.Width;
            if (w <= 0 || _durationSec <= 0) return;
            var frac = Math.Clamp(e.GetPosition(_waveformCanvas).X / w, 0, 1);
            // ponytail: needs EnhancementAudioPlayer.Seek, wired when it moves to Core.
            _currentSec = frac * _durationSec;
            UpdatePlayhead(frac);
        }

        // ====================================================================================
        // Host UI (WPF: UpdateHostUi + RefreshFileContextStrip)
        // ====================================================================================

        private void UpdateHostUi(Enhancement? enh, string? path)
        {
            _loadedEnhancement = enh;
            _loadedFilePath = path;

            if (enh == null)
            {
                _txtEnhPath.Text = Loc.Get("deeper_player_no_enh");
                _txtEnhMetadata.Text = "";
                _btnUnloadEnhancement.IsVisible = false;
                _txtEnhSource.IsVisible = false;
                _sourcePill.IsVisible = false;
                ShowMediaPaneFor(MediaTypes.Audio); // default back to audio UI
                RefreshFileContextStrip(null, null);
                OnEnhancementLoadedForMini(null);
                UpdateStatusPill();
                return;
            }

            _txtEnhPath.Text = path ?? "";
            _btnUnloadEnhancement.IsVisible = true;
            _btnCreateNewEnhancement.IsVisible = false;

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
            _txtEnhSource.Text = Loc.Get(key);
            _txtEnhSource.IsVisible = true;

            ShowMediaPaneFor(enh.MediaType);
            if (enh.MediaType == MediaTypes.Video && IsRemoteVideoUrl(enh.MediaSource))
            {
                LoadVideoUrl(enh.MediaSource);
            }
            else if (enh.MediaType == MediaTypes.Video
                     && !string.IsNullOrEmpty(enh.MediaSource)
                     && File.Exists(enh.MediaSource))
            {
                LoadLocalVideo(enh.MediaSource);
            }

            RefreshFileContextStrip(enh, path);
            OnEnhancementLoadedForMini(enh);
            UpdateStatusPill();
        }

        private void RefreshFileContextStrip(Enhancement? enh, string? path)
        {
            if (enh == null)
            {
                _txtEnhName.Text = Loc.Get("deeper_player_no_enh");
                _txtEnhMetadata.Text = "";
                _txtEnhPath.IsVisible = false;
                _mediaTypeIcon.Text = "🎵";
                _mediaTypeIconBg.Background = Res("DeeperHubAudioBadgeBgBrush");
                _sourcePill.IsVisible = false;
                _btnOpenInEditor.IsVisible = false;
                return;
            }

            _txtEnhName.Text = string.IsNullOrEmpty(enh.Metadata?.Name) ? "(untitled)" : enh.Metadata!.Name;

            var isVideo = string.Equals(enh.MediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase);
            _mediaTypeIcon.Text = isVideo ? "🎬" : "🎵";
            _mediaTypeIconBg.Background = Res(isVideo ? "DeeperHubVideoBadgeBgBrush" : "DeeperHubAudioBadgeBgBrush");

            // Inline meta line: creator · sourceGlyph source · counts.
            var creator = enh.Metadata?.Creator;
            var (srcGlyph, srcText) = DescribeMediaSource(enh);
            int regions = enh.Regions?.Count ?? 0;
            int rules = enh.Rules?.Count ?? 0;
            int haptics = 0;
            if (enh.HapticTracks != null)
                foreach (var t in enh.HapticTracks) haptics += t?.Events?.Count ?? 0;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(creator)) parts.Add(creator!);
            if (!string.IsNullOrWhiteSpace(srcText)) parts.Add($"{srcGlyph} {srcText}");
            parts.Add(Loc.GetF("deeper_player_meta_counts_fmt", regions, rules, haptics));
            _txtEnhMetadata.Text = string.Join("  ·  ", parts);

            // TxtEnhPath stays hidden — the popover surfaces the raw path; the name's tooltip
            // carries it as a discoverable detail.
            _txtEnhPath.Text = path ?? "";
            _txtEnhPath.IsVisible = false;
            ToolTip.SetTip(_txtEnhName, path ?? "");

            _sourcePill.IsVisible = true;
            _btnOpenInEditor.IsVisible = true;
        }

        private static (string Glyph, string Text) DescribeMediaSource(Enhancement enh)
        {
            var src = enh.MediaSource;
            if (string.IsNullOrWhiteSpace(src)) return ("⚠", Loc.Get("deeper_player_source_missing"));
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { return ("🌐", new Uri(src).Host); } catch { return ("🌐", src); }
            }
            if (File.Exists(src)) return ("✓", System.IO.Path.GetFileName(src));
            return ("⚠", System.IO.Path.GetFileName(src));
        }

        private void ShowMediaPaneFor(string? mediaType)
        {
            var isVideo = string.Equals(mediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase);
            _isVideoMode = isVideo;
            _audioFileRow.IsVisible = !isVideo;
            _audioPane.IsVisible = !isVideo;
            _videoPane.IsVisible = isVideo;
            // The WPF cluster bound its Visibility to VideoPane's; same rule, set here.
            _browserZoomCluster.IsVisible = isVideo;
            _volumePanel.IsVisible = !isVideo;
            _btnPictureInPicture.IsVisible = isVideo;
        }

        // ====================================================================================
        // Fullscreen — Win32 in the original, absent here
        // ====================================================================================

        /// <summary>
        /// The WPF player reparented its WebView2 into a borderless topmost window covering the
        /// player's monitor whenever the page raised
        /// <c>CoreWebView2.ContainsFullScreenElementChanged</c> — a signal that only exists on
        /// WebView2, and which drove <c>EnterVideoFullscreen</c> / <c>ExitVideoFullscreen</c> /
        /// <c>ExitFullscreenViaScript</c> / <c>OnVideoWebMessageReceived</c> and the two injected
        /// scripts (dblclick-to-toggle-fullscreen, Ctrl+MouseWheel zoom).
        ///
        /// ponytail: needs a fullscreen signal. NativeWebView has no ContainsFullScreenElementChanged
        /// and no document-created script injection, so the page cannot be instrumented before its
        /// own scripts run — this stays a stub and HTML5 fullscreen inside the page cannot escape
        /// the pane. (WebMessageReceived DOES exist, so the dblclick-to-toggle half is buildable;
        /// the fullscreen-element signal it would have to replace is what is missing.) The
        /// window half maps cleanly when the signal arrives: Screens.ScreenFromVisual(this) for the
        /// monitor, WindowState.FullScreen (or the screen's Bounds), Topmost = true and
        /// ShowInTaskbar = false — no Win32, no WindowInteropHelper, no Forms.Screen.FromHandle,
        /// and no OverlayService z-order re-assert (that service is the WPF head's).
        /// </summary>
        private void OnVideoFullscreenChanged()
        {
            Log.Debug("EnhancementPlayer(Avalonia): browser fullscreen is a stub");
        }

        // ====================================================================================
        // Event log (Mission 3)
        // ====================================================================================

        private void IngestActionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            AppendLogEntry(new EventLogEntry
            {
                Category = EventLogEntry.EventLogCategory.Action,
                Description = line,
            });
        }

        private void IngestDiagnosticLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            // Best-effort: lines that loudly say "error" / "fail" land in the Error bucket even when
            // they came via the Diagnostic event.
            var lower = line.ToLowerInvariant();
            var cat = (lower.Contains("error") || lower.Contains("fail") || lower.Contains("rejected"))
                ? EventLogEntry.EventLogCategory.Error
                : EventLogEntry.EventLogCategory.Engine;
            AppendLogEntry(new EventLogEntry { Category = cat, Description = line });
        }

        private void IngestErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            AppendLogEntry(new EventLogEntry { Category = EventLogEntry.EventLogCategory.Error, Description = line });
        }

        private void AppendLogEntry(EventLogEntry entry)
        {
            _logEntries.Insert(0, entry);
            while (_logEntries.Count > MaxLogEntries)
                _logEntries.RemoveAt(_logEntries.Count - 1);
            RefreshEventList();
            UpdateFilterCounts();
        }

        /// <summary>
        /// WPF filtered through a CollectionViewSource with a Filter predicate. Avalonia has no
        /// ICollectionView, and the list is capped at 30 rows.
        /// ponytail: re-projects the whole list on every append; swap for an incremental view if it
        /// ever holds more than a screenful.
        /// </summary>
        private void RefreshEventList()
        {
            _lstEvents.ItemsSource = _logEntries.Where(LogEntryFilter).ToList();
        }

        private bool LogEntryFilter(EventLogEntry e) => _activeFilter switch
        {
            "action" => e.Category == EventLogEntry.EventLogCategory.Action,
            "engine" => e.Category == EventLogEntry.EventLogCategory.Engine,
            "error" => e.Category == EventLogEntry.EventLogCategory.Error,
            _ => true,
        };

        private void UpdateFilterCounts()
        {
            try
            {
                int action = 0, engine = 0, error = 0;
                foreach (var e in _logEntries)
                {
                    switch (e.Category)
                    {
                        case EventLogEntry.EventLogCategory.Action: action++; break;
                        case EventLogEntry.EventLogCategory.Engine: engine++; break;
                        case EventLogEntry.EventLogCategory.Error: error++; break;
                    }
                }
                _txtFilterCountAll.Text = _logEntries.Count.ToString(CultureInfo.InvariantCulture);
                _txtFilterCountActions.Text = action.ToString(CultureInfo.InvariantCulture);
                _txtFilterCountEngine.Text = engine.ToString(CultureInfo.InvariantCulture);
                _txtFilterCountErrors.Text = error.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
        }

        private void EventFilterPill_Click(ToggleButton? clicked)
        {
            if (clicked == null) return;
            // Force single-select: ignore unchecks (re-check the clicked pill if the user tried to
            // deselect the active filter), and uncheck the other three.
            if (clicked.IsChecked != true) { clicked.IsChecked = true; return; }
            _activeFilter = (clicked.Tag as string ?? "all").ToLowerInvariant();
            foreach (var pill in new[] { _pillAll, _pillActions, _pillEngine, _pillErrors })
            {
                if (ReferenceEquals(pill, clicked)) continue;
                pill.IsChecked = false;
            }
            RefreshEventList();
        }

        private void BtnClearEvents_Click()
        {
            _logEntries.Clear();
            RefreshEventList();
            UpdateFilterCounts();
        }

        private void BtnCollapseEventLog_Click()
        {
            _eventLogCollapsed = !_eventLogCollapsed;
            if (_eventLogCollapsed)
            {
                _eventLogExpandedHeight = _eventScroll.MaxHeight > 0 ? _eventScroll.MaxHeight : 140;
                _eventScroll.MaxHeight = 0;
                _eventScroll.IsVisible = false;
                _txtCollapseGlyph.Text = "▴";
            }
            else
            {
                _eventScroll.MaxHeight = _eventLogExpandedHeight;
                _eventScroll.IsVisible = true;
                _txtCollapseGlyph.Text = "▾";
            }
        }

        // ====================================================================================
        // Header: Open in editor + status pill + Change popover
        // ====================================================================================

        private void JumpToEditorForCurrentEnhancement(string? ruleId)
        {
            var enh = _loadedEnhancement;
            if (enh == null) return;
            // ponytail: needs DeeperEditorWindow (not ported) and MainWindow.OpenDeeperEditorFromPlayer,
            // wired when the editor is ported. The WPF path deduped against every open editor
            // window whose LoadedFilePath matched, then routed through the main window.
            Log.Debug("EnhancementPlayer(Avalonia): editor jump is a stub (path {Path}, rule {Rule})",
                _loadedFilePath, ruleId);
        }

        private void BtnChange_Click() => _changePopup.IsOpen = !_changePopup.IsOpen;

        /// <summary>Status pill state machine: Empty / Loaded / Live.</summary>
        private void UpdateStatusPill()
        {
            try
            {
                var enh = _loadedEnhancement;
                if (enh == null)
                {
                    _statusPillText.Text = Loc.Get("deeper_player_pill_empty");
                    _statusPillText.Foreground = Res("TextMutedBrush");
                    _statusPill.Background = Res("DeeperAccentTransparent20Brush");
                    _statusPill.BorderBrush = Res("DeeperAccentTransparent40Brush");
                    return;
                }

                if (_isPlaying)
                {
                    _statusPillText.Text = Loc.Get("deeper_player_pill_live");
                    var accent = Res("DeeperAccentBrush");
                    _statusPillText.Foreground = Brushes.White;
                    _statusPill.Background = accent;
                    _statusPill.BorderBrush = accent;
                }
                else
                {
                    _statusPillText.Text = Loc.Get("deeper_player_pill_loaded");
                    var soft = Res("DeeperAccentSoftBrush");
                    _statusPillText.Foreground = soft;
                    _statusPill.Background = Res("DeeperAccentTransparent20Brush");
                    _statusPill.BorderBrush = soft;
                }
            }
            catch { }
        }

        // ====================================================================================
        // Mini-timeline read-out (regions + rule pins + playhead)
        // ====================================================================================

        private void OnEnhancementLoadedForMini(Enhancement? enh)
        {
            _miniEnhancement = enh;
            if (enh == null)
            {
                _miniTimelinePanel.IsVisible = false;
                return;
            }
            _miniTimelinePanel.IsVisible = true;
            RebuildMiniTimeline();
        }

        private void RebuildMiniTimeline()
        {
            try
            {
                _miniTimelineCanvas.Children.Clear();
                var enh = _miniEnhancement;
                if (enh == null) return;
                var w = _miniTimelineCanvas.Bounds.Width;
                var h = _miniTimelineCanvas.Bounds.Height;
                if (w <= 0 || h <= 0) return;

                // Prefer the media duration so the canvas spans the whole clip; fall back to the
                // enhancement's own content extent (floored at 60s).
                var total = GetEffectiveTimelineTotal(enh);
                if (total <= 0) return;
                _miniTotalSeconds = total;

                // Region bands — full height, colour from metadata.
                if (enh.Regions != null)
                {
                    foreach (var r in enh.Regions)
                    {
                        if (r == null) continue;
                        double rs = Math.Max(0, r.Start);
                        double re = Math.Max(rs, r.End);
                        if (re <= 0) continue;
                        var x1 = (rs / total) * w;
                        var x2 = (re / total) * w;
                        var bw = Math.Max(2, x2 - x1);
                        var brush = ParseHexBrush(r.Color, fallbackResourceKey: "DeeperAccentBrush");
                        var fill = brush is ISolidColorBrush sb
                            ? new SolidColorBrush(Color.FromArgb(110, sb.Color.R, sb.Color.G, sb.Color.B))
                            : Res("DeeperAccentTransparent40Brush");
                        var rect = new Rectangle
                        {
                            Width = bw,
                            Height = Math.Max(0, h - 2),
                            Fill = fill,
                            Stroke = brush,
                            StrokeThickness = 1,
                            RadiusX = 2,
                            RadiusY = 2,
                        };
                        ToolTip.SetTip(rect, string.IsNullOrEmpty(r.Label) ? r.Id : r.Label);
                        Canvas.SetLeft(rect, x1);
                        Canvas.SetTop(rect, 1);
                        _miniTimelineCanvas.Children.Add(rect);

                        // Label printed on the band when there's room.
                        if (bw >= 40 && !string.IsNullOrEmpty(r.Label))
                        {
                            var tb = new TextBlock
                            {
                                Text = r.Label,
                                Foreground = Brushes.White,
                                FontSize = 9,
                                FontWeight = FontWeight.SemiBold,
                                IsHitTestVisible = false,
                            };
                            Canvas.SetLeft(tb, x1 + 4);
                            Canvas.SetTop(tb, (h - 12) / 2.0);
                            _miniTimelineCanvas.Children.Add(tb);
                        }
                    }
                }

                // Rule pins — TimeReached only. Other rule types are represented by their
                // constraint region's band.
                if (enh.Rules != null)
                {
                    foreach (var rule in enh.Rules)
                    {
                        if (rule?.Trigger is not TimeReachedTrigger tr) continue;
                        var t = Math.Max(0, tr.Time);
                        if (t > total) continue;
                        var x = (t / total) * w;
                        var line = new Line
                        {
                            StartPoint = new Point(x, 1),
                            EndPoint = new Point(x, h - 1),
                            Stroke = Brushes.Orange,
                            StrokeThickness = 1.5,
                            StrokeDashArray = new AvaloniaList<double> { 2, 2 },
                            IsHitTestVisible = false,
                        };
                        _miniTimelineCanvas.Children.Add(line);
                        // Small flag at top — 5x4 triangle.
                        var flag = new Polygon
                        {
                            Points = new List<Point>
                            {
                                new(x - 3, 1),
                                new(x + 3, 1),
                                new(x, 5),
                            },
                            Fill = Brushes.Orange,
                            IsHitTestVisible = false,
                        };
                        _miniTimelineCanvas.Children.Add(flag);
                    }
                }

                // Playhead — solid accent line.
                var ph = new Line
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, h),
                    Stroke = Res("DeeperAccentBrush"),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                    Tag = "playhead",
                };
                _miniTimelineCanvas.Children.Add(ph);

                UpdateMiniPlayheadX();
            }
            catch (Exception ex)
            {
                Log.Debug("Player: mini-timeline build failed: {Error}", ex.Message);
            }
        }

        private void UpdateMiniPlayheadX()
        {
            if (_miniEnhancement == null) return;
            var w = _miniTimelineCanvas.Bounds.Width;
            if (w <= 0) return;

            // Recompute the effective total each tick — a video's duration isn't known until its
            // metadata loads. Whenever the canonical total drifts more than ~1% from what the canvas
            // was last drawn against, force a rebuild so band positions catch up.
            var effective = GetEffectiveTimelineTotal(_miniEnhancement);
            if (effective <= 0) return;
            if (_miniTotalSeconds <= 0
                || Math.Abs(effective - _miniTotalSeconds) / Math.Max(effective, _miniTotalSeconds) > 0.01)
            {
                _miniTotalSeconds = effective;
                RebuildMiniTimeline();
                return; // rebuild placed a fresh playhead at x=0; next tick will move it
            }

            var x = Math.Clamp(_currentSec / _miniTotalSeconds, 0, 1) * w;
            foreach (var child in _miniTimelineCanvas.Children)
            {
                if (child is Line l && (l.Tag as string) == "playhead")
                {
                    l.StartPoint = new Point(x, l.StartPoint.Y);
                    l.EndPoint = new Point(x, l.EndPoint.Y);
                    break;
                }
            }
        }

        /// <summary>
        /// Canonical timeline-length resolver: the longer of the loaded media's duration and the
        /// enhancement's own content extent (which is itself floored at 60s), so a project with
        /// content past the media's end still surfaces its trailing rules and a project that ends
        /// early still spans the whole clip.
        /// </summary>
        private double GetEffectiveTimelineTotal(Enhancement enh)
            => Math.Max(_durationSec, ComputeMiniTotalSeconds(enh));

        private void MiniTimelineCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                if (_miniEnhancement == null) return;
                var w = _miniTimelineCanvas.Bounds.Width;
                if (w <= 0 || _miniTotalSeconds <= 0) return;

                e.Pointer.Capture(_miniTimelineCanvas);
                _miniScrubbing = true;
                SeekToMiniPosition(e.GetPosition(_miniTimelineCanvas).X);
                e.Handled = true;
            }
            catch { }
        }

        private void MiniTimelineCanvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_miniScrubbing) return;
            try { SeekToMiniPosition(e.GetPosition(_miniTimelineCanvas).X); } catch { }
        }

        private void MiniTimelineCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_miniScrubbing) return;
            try
            {
                _miniScrubbing = false;
                e.Pointer.Capture(null);
                // Final seek to the release point — covers the case where the last move fired
                // before the click landed.
                SeekToMiniPosition(e.GetPosition(_miniTimelineCanvas).X);
            }
            catch { }
        }

        private void SeekToMiniPosition(double xInCanvas)
        {
            var w = _miniTimelineCanvas.Bounds.Width;
            if (w <= 0 || _miniTotalSeconds <= 0) return;
            var frac = Math.Clamp(xInCanvas / w, 0, 1);
            _currentSec = frac * _miniTotalSeconds;
            UpdateMiniPlayheadX();
            // Video seeks for real; the next poll reads the page's clock back, so a seek the engine
            // clamps or refuses corrects itself rather than leaving the playhead somewhere the video
            // is not. ponytail: audio still needs EnhancementAudioPlayer.Seek, so in audio mode the
            // playhead moves and nothing else does.
            if (_isVideoMode && _videoBrowser.HasEngine)
            {
                _ = _videoBrowser.InvokeScriptAsync(VideoScript(
                    "currentTime=" + _currentSec.ToString("0.###", CultureInfo.InvariantCulture)));
            }
        }

        private static double ComputeMiniTotalSeconds(Enhancement enh)
        {
            // Total = max(any region.End, any rule TimeReached time, any haptic event end). Falls
            // back to 60s if nothing has a time (a genuinely empty new enhancement).
            double max = 0;
            if (enh.Regions != null)
                foreach (var r in enh.Regions)
                    if (r != null) max = Math.Max(max, r.End);
            if (enh.Rules != null)
                foreach (var rule in enh.Rules)
                    if (rule?.Trigger is TimeReachedTrigger tr) max = Math.Max(max, tr.Time);
            if (enh.HapticTracks != null)
                foreach (var t in enh.HapticTracks)
                    if (t?.Events != null)
                        foreach (var ev in t.Events)
                            if (ev != null) max = Math.Max(max, ev.Start + ev.Duration);
            return max > 0 ? max : 60.0;
        }

        // ====================================================================================
        // "Now: [region]" overlay
        // ====================================================================================

        private void RefreshNowRegionOverlay()
        {
            try
            {
                var enh = _miniEnhancement;
                if (enh?.Regions == null || enh.Regions.Count == 0)
                {
                    _nowRegionPanel.IsVisible = false;
                    return;
                }
                Region? hit = null;
                foreach (var r in enh.Regions)
                {
                    if (r == null) continue;
                    if (_currentSec >= r.Start && _currentSec <= r.End) { hit = r; break; }
                }
                if (hit == null)
                {
                    _nowRegionPanel.IsVisible = false;
                    return;
                }
                _txtNowRegion.Text = string.IsNullOrEmpty(hit.Label) ? hit.Id : hit.Label;
                _nowRegionSwatch.Fill = ParseHexBrush(hit.Color, fallbackResourceKey: "DeeperAccentBrush");
                _nowRegionPanel.IsVisible = true;
            }
            catch { }
        }

        // ====================================================================================
        // Cleanup
        // ====================================================================================

        private void Window_Closing()
        {
            // Stop the tick timer first so no UI work is queued onto a dying window — and because
            // --render-all opens and closes every view in one process, a timer left running would
            // accumulate one live 100ms tick per view.
            try { _uiTimer?.Stop(); } catch { }
            try { if (_uiTimer != null) _uiTimer.Tick -= UiTimer_Tick; } catch { }
            _uiTimer = null;
            // The poll rides the tick, so stopping the timer stops it; the flag makes a poll that
            // is already in flight a no-op rather than a write into a closing window.
            _pollDisabled = true;
            try { _fetcher?.Dispose(); } catch { }
            _fetcher = null;
            // ponytail: the WPF teardown also unsubscribed the player/webcam singletons, stopped a
            // webcam this session had started, disposed the video time source and force-closed the
            // borderless fullscreen host. Those are the head services' and WebView2's; none of them
            // exist here. The web view itself is disposed with the visual tree.
        }

        // ====================================================================================
        // Helpers
        // ====================================================================================

        private static IBrush ParseHexBrush(string? hex, string fallbackResourceKey)
        {
            if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c))
                return new SolidColorBrush(c);
            return Res(fallbackResourceKey);
        }

        private static IBrush Res(string key) =>
            Application.Current is { } app && app.TryFindResource(key, out var v) && v is IBrush b
                ? b
                : Brushes.Gray;

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes}:{ts.Seconds:00}";
        }

        // ====================================================================================
        // Render sample. Placeholder data, never reachable from a production caller.
        // ====================================================================================

        private static Enhancement SampleEnhancement() => new()
        {
            MediaType = MediaTypes.Video,
            MediaSource = "https://hypnotube.com/video/sample-117314.html",
            Metadata = new EnhancementMetadata { Name = "Sample Enhancement", Creator = "CC Labs" },
            Regions =
            {
                new Region { Id = "r1", Label = "Induction", Start = 0, End = 35, Color = "#7B5CFF" },
                new Region { Id = "r2", Label = "Deepener", Start = 35, End = 95, Color = "#FF69B4" },
                new Region { Id = "r3", Label = "Emergence", Start = 95, End = 180, Color = "#4FD1C5" },
            },
            Rules =
            {
                new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 60 } },
                new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 120 } },
            },
        };

        private void SeedSampleEventLog()
        {
            IngestActionLine("spiral overlay -> on (intensity 0.6)");
            IngestDiagnosticLine("engine bound to video time source");
            IngestDiagnosticLine("webcam rule skipped: tracking not running");
            IngestErrorLine("haptic device not connected");
            IngestActionLine("flash burst -> 3 frames");
        }
    }
}
