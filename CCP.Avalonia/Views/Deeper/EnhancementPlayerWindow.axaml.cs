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
    ///   <item><b>Services.</b> EnhancementLibrary and the editor jump are not here yet, so the
    ///         handlers that reached them are stubs. The host, the rule engine and the time source
    ///         ARE here: Core's EnhancementHostService + EnhancementEngine, bound in the Engine
    ///         partial, with audio on the shared LibVLC transport (DeeperLocalAudio) and the eye
    ///         tracking button on Platform/WebcamTracker. Still missing: the waveform peaks
    ///         (NAudio decode) and the library tier of auto-discovery.</item>
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

        // Video clock read-back. See PollVideoTimeAsync. _videoNavigated is the gate on every
        // script call: an engine that exists is not a page that was loaded, and the rejected-
        // MediaSource paths leave the pane up with nothing in it — polling that at 10 Hz is a
        // debug line ten times a second and a Play button that would flip on an empty view.
        private bool _pollInFlight;
        private bool _pollDisabled;
        private bool _videoNavigated;

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

        /// <summary>WPF EnhancementHostService.LoadedFilePath: the .ccpenh.json this player last
        /// loaded from disk, so the hub's ▶ on the same row only brings it forward.</summary>
        public string? LoadedFilePath { get; private set; }

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
            Closed += (_, _) => { try { _waveCts?.Cancel(); } catch { } };   // the peak decode stops with the window

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
            // Page zoom, Ctrl+wheel and HTML5 fullscreen: DeeperPageBridge. Released with the window.
            _pageBridge = new DeeperPageBridge(_videoBrowser, this);
            _pageBridge.FullscreenChanged += _ => OnVideoFullscreenChanged();
            Closed += (_, _) => _pageBridge.Dispose();
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
            HookEngine();
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
            LoadedFilePath = null;
            if (!File.Exists(ccpenhJsonPath)) { ReportLoadFailure($"File not found: {ccpenhJsonPath}"); return; }
            try
            {
                if (LoadEnhancementInMemory(EnhancementSerializer.LoadFromFile(ccpenhJsonPath), ccpenhJsonPath))
                    LoadedFilePath = ccpenhJsonPath;
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
            LoadedFilePath = null;   // LoadEnhancementFile sets it again after a successful load
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

        /// <summary>WPF LoadEnhancementFromMemory: catalogue downloads into the shared player.</summary>
        public void LoadEnhancementFromMemory(Enhancement enhancement, string sourceTag)
            => LoadEnhancementInMemory(enhancement, sourceTag);

        /// <summary>External launcher entry (file association, drag-drop dispatch).</summary>
        public void OpenLocalMediaFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            LoadedFilePath = null;
            if (DeeperPreview.IsLocalVideoFile(path)) LoadLocalVideo(path);
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
                if (DeeperPreview.IsLocalVideoFile(path)) LoadLocalVideo(path);
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
            _videoNavigated = false;
            _host.Unload();   // UnbindEngine first: every band, haptic and one-shot stops
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
                await dlg.ShowDialogSafe(this);
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
        /// Core's EnhancementResolver ladder (embedded -> sidecar -> library), as WPF calls it.
        ///
        /// ponytail: this head leaves EnhancementResolver.LibraryMatchProvider unset, so the library
        /// tier never hits; the auto-promotion of an embedded enhancement into the library and its
        /// 6 s toast need EnhancementLibrary, which has not moved. A media file whose enhancement
        /// lives only in the library lands in the "nothing found" branch and offers Create-new.
        /// </summary>
        private void TryAutoLoadEnhancement(string mediaPath)
        {
            _lastMediaPathForCreateNew = mediaPath;
            _btnCreateNewEnhancement.IsVisible = false;
            try
            {
                var resolved = EnhancementResolver.ResolveForLocalMedia(mediaPath);
                switch (resolved.Source)
                {
                    case EnhancementDiscoverySource.Embedded:
                        _lastDiscoverySource = DiscoverySource.Embedded;
                        LoadEnhancementInMemory(resolved.Enhancement!, "embedded:" + System.IO.Path.GetFileName(mediaPath));
                        return;

                    case EnhancementDiscoverySource.Sidecar:
                        _lastDiscoverySource = DiscoverySource.Sidecar;
                        LoadEnhancementFile(resolved.FilePath!);
                        return;

                    case EnhancementDiscoverySource.Library:
                        _lastDiscoverySource = DiscoverySource.Library;
                        LoadEnhancementFile(resolved.FilePath!);
                        return;
                }

                // Nothing found — offer to author against this media.
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
            // WPF EnhancementPlayerWindow.xaml.cs:595. EnhancementLibrary.CreateBlank is a bare
            // `new Enhancement { MediaType, MediaSource, Metadata = new() }`, inlined.
            try
            {
                var mediaType = DeeperPreview.IsLocalVideoFile(_lastMediaPathForCreateNew)
                    ? MediaTypes.Video : MediaTypes.Audio;
                var blank = new Enhancement { MediaType = mediaType, MediaSource = _lastMediaPathForCreateNew };
                new DeeperEditorWindow(blank, null).Show(this);
            }
            catch (Exception ex) { Log.Warning(ex, "EnhancementPlayer: open editor for new enhancement failed"); }
        }

        // ====================================================================================
        // Media loading
        // ====================================================================================

        private void LoadAudio(string path)
        {
            // WPF stopped playback, decoded peaks, called Play, then set TxtTotal / the play glyph /
            // deeper_player_status_playing. The transport is the editor's (DeeperLocalAudio, the
            // process's shared LibVLC); OpenAudioAsync plays it once it is open.
            // The peaks come from DeeperWaveform (LibVLC transcode off the UI thread, cached per file).
            _txtAudioPath.Text = path;
            _txtStatus.Text = Loc.Get("deeper_player_status_loading_audio");
            ShowMediaPaneFor(MediaTypes.Audio);
            _peaks = null;
            _waveformPath.Data = null;
            OpenAudioAsync(path);
            _ = LoadWaveformAsync(path);
        }

        private System.Threading.CancellationTokenSource? _waveCts;

        /// <summary>Tests: the peaks the strip is drawn from (null until decoded).</summary>
        internal float[]? WaveformPeaks => _peaks;

        /// <summary>WPF LoadWaveformAsync (:665). Decoded on a worker; a newer load or the window closing
        /// cancels it, and a result for a file that is no longer the loaded one is dropped.</summary>
        private async Task LoadWaveformAsync(string path)
        {
            try { _waveCts?.Cancel(); } catch { }
            var cts = _waveCts = new System.Threading.CancellationTokenSource();
            try
            {
                var data = await DeeperWaveform.LoadAsync(path, cts.Token);
                if (cts.IsCancellationRequested || !ReferenceEquals(_waveCts, cts)) return;
                if (!string.Equals(_txtAudioPath.Text, path, StringComparison.Ordinal)) return;
                _peaks = data?.Peaks;
                // Drawn once per load or resize into a cached bitmap, never per frame.
                _waveformPath.CacheMode ??= new BitmapCache();
                RenderWaveform();
            }
            catch (Exception ex) { Log.Debug("EnhancementPlayer: waveform decode failed: {Error}", ex.Message); }
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
                // Cleared up front so a load that is rejected below cannot inherit the previous
                // load's "a page is up" and leave Play driving whatever is still on screen.
                _videoNavigated = false;

                // Extension AND rooted-path check before anything reaches the engine. File.Exists
                // upstream only proves a file is there, not that it is media.
                if (!DeeperPreview.IsLocalVideoFile(path) || !System.IO.Path.IsPathRooted(path))
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
                _videoBrowser.AllowNavigation = u => u.IsFile && DeeperPreview.PathsEqual(u.LocalPath, full);
                _videoBrowser.Source = new Uri(full);
                _videoNavigated = true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: local video load failed");
                _txtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
            }
        }

        private void LoadVideoUrl(string url)
        {
            try
            {
                _txtVideoStatus.Text = Loc.Get("deeper_player_video_loading");
                _txtVideoStatus.IsVisible = true;
                // Cleared up front so a load that is rejected below cannot inherit the previous
                // load's "a page is up" and leave Play driving whatever is still on screen.
                _videoNavigated = false;

                // WPF pre-flight (EnhancementPlayerWindow.xaml.cs:1391): https on an allowlisted
                // host, so a hostile MediaSource in a shared .ccpenh.json never reaches the engine.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || !DeeperPreview.IsAllowedPreviewHost(uri))
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

                // WPF's NavigationStarting (xaml.cs:1796): every later hop passes the same fence.
                _videoBrowser.AllowNavigation = DeeperPreview.IsAllowedPreviewHost;
                _videoBrowser.Source = uri;
                _videoNavigated = true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: video load failed");
                _txtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
            }
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
        /// Audio plays on the Engine partial's transport (play / pause / resume). The rules fire in
        /// both modes: the tick binds Core's engine to this window's clock while media runs.
        /// </summary>
        private async void BtnPlayPause_Click()
        {
            if (_loadedEnhancement == null && _durationSec <= 0)
            {
                _txtStatus.Text = Loc.Get("deeper_player_status_pick_first");
                return;
            }

            if (!_isVideoMode)
            {
                // WPF play / pause / resume ladder on the audio transport.
                if (_audio == null) { _txtStatus.Text = Loc.Get("deeper_player_status_pick_first"); return; }
                if (_isPlaying) AudioPause(); else AudioPlay();
                return;
            }
            if (!_videoNavigated)
            {
                _txtStatus.Text = Loc.Get("deeper_player_status_pick_first");
                return;
            }

            // Awaited, and the state flips only on the script's "ok". A page with no <video> yet —
            // still loading, an error page, a navigation the gate refused — returns "" and the
            // press reads as having done nothing, which is what happened. Flipping first and
            // letting the poll correct it would show ⏸ / LIVE / "Playing" in the meantime, and on
            // a page that never grows a video element it would never be corrected at all.
            var want = !_isPlaying;
            if (DeeperPreview.Unquote(await _videoBrowser.InvokeScriptAsync(
                    DeeperPreview.Invoke(want ? "l.play();" : "l.pause();"))) != "ok")
                return;
            _isPlaying = want;
            _txtPlayPauseGlyph.Text = _isPlaying ? "⏸" : "▶";
            _txtStatus.Text = Loc.Get(_isPlaying ? "deeper_player_status_playing" : "deeper_player_status_stopped");
            UpdateStatusPill();
        }

        private void BtnStop_Click()
        {
            if (_isVideoMode && _videoNavigated)
                _ = _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("l.pause(); l.currentTime=0;"));
            // WPF Stop: the engine goes with the media, so no band or toy outlives the press.
            _host.UnbindEngine();
            if (!_isVideoMode && _audio != null) { _audio.Pause(); _audio.PositionSeconds = 0; }
            _isPlaying = false;
            _currentSec = 0;
            _txtPlayPauseGlyph.Text = "▶";
            _txtCurrent.Text = "0:00";
            UpdatePlayhead(0);
            _txtStatus.Text = Loc.Get("deeper_player_status_stopped");
            UpdateStatusPill();
        }

        /// <summary>WPF BtnEyeTracking_Click (:861) on the port tracker (Platform/WebcamTracker):
        /// stop when running; first time, point at the Webcam Setup card; else one awaited
        /// confirmation, then the camera. The body is ToggleEyeTrackingAsync (Engine partial).</summary>
        private void BtnEyeTracking_Click() => _ = ToggleEyeTrackingAsync();

        /// <summary>WPF AdjustVideoZoom (:832): +/-10 % clamped to [0.25, 5.0]. NativeWebView has no zoom factor, so the
        /// bridge sets a CSS zoom on the document and puts it back after every navigation. A fullscreened video
        /// is not scaled by it.</summary>
        private void AdjustVideoZoom(double delta) => _pageBridge.Adjust(delta);

        private readonly DeeperPageBridge _pageBridge;

        /// <summary>Tests: the page bridge (zoom factor, fullscreen state).</summary>
        internal DeeperPageBridge PageBridge => _pageBridge;

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
            var result = DeeperPreview.Unquote(await _videoBrowser.InvokeScriptAsync(
                DeeperPreview.LargestVideo(
                    "try{ if(document.pictureInPictureElement){document.exitPictureInPicture();return 'exit';}"
                    + "if(!document.pictureInPictureEnabled)return 'picture-in-picture not supported by this engine';"
                    + "l.requestPictureInPicture().catch(function(){});return 'enter';}"
                    + "catch(e){return e.name+': '+e.message;}")));
            // The shared finder answers "" for a page with no video where this site's own copy
            // answered 'no video on page'. Mapped back here rather than parameterising the finder
            // for one caller: the event-log line is what makes a press on a videoless page visible.
            if (result.Length == 0) result = "no video on page";
            // ponytail: requestPictureInPicture is a promise, so an ASYNC rejection (Chromium's
            // "requires user activation") is swallowed by that .catch and reads as 'enter' here.
            // Reporting it needs WebMessageReceived, which NativeWebView has — a bridge worth
            // building once something else needs one, not for this button alone.
            if (result is not ("enter" or "exit" or "")) IngestErrorLine("picture-in-picture: " + result);
        }

        private void SliderVolume_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != RangeBase.ValueProperty) return;
            // WPF EnhancementAudioPlayer.Volume (0..1). The panel is hidden in video mode.
            if (_audio != null) _audio.Volume = _sliderVolume.Value / 100.0;
            // The write on drag is the line above. Left from WPF: UpdateVolumeFromPlayer's read-back on
            // open (and its _suppressVolumeSync flag). Not routed to the video's volume: this panel is
            // hidden in video mode (ShowMediaPaneFor), so a viewer never sees it while a video plays.
        }

        // ====================================================================================
        // UI tick
        // ====================================================================================

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_miniScrubbing) return;

            if (_isVideoMode) _ = PollVideoTimeAsync();
            EngineTick();

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
            if (_pollInFlight || _pollDisabled || !_videoNavigated) return;
            _pollInFlight = true;
            try
            {
                var raw = await _videoBrowser.InvokeScriptAsync(DeeperPreview.ReadTime());

                var parts = DeeperPreview.Unquote(raw).Split('|');
                if (parts.Length != 3) return;
                // A video element answered, so the page is up: "Loading video…" is now false. WPF
                // hid this on NavigationCompleted; there the pane was Edge's own media viewer, so
                // the navigation finishing WAS the video appearing. Here the page can be a site
                // whose player mounts later, and the element answering is the honest signal.
                _txtVideoStatus.IsVisible = false;
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
            _currentSec = frac * _durationSec;
            AudioSeek(_currentSec);
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
            _browserZoomCluster.IsVisible = isVideo;   // WPF bound the cluster to VideoPane's visibility
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
        /// Now: the page reports its own fullscreenchange through DeeperPageBridge and the bridge has already
        /// put THIS window full screen (or back). No second window, no reparent: the page fills the video pane
        /// of a full-screen player, not the bare monitor.
        private void OnVideoFullscreenChanged()
        {
            Log.Debug("EnhancementPlayer: page fullscreen {On}", _pageBridge.PageFullscreen);
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
            // WPF EnhancementPlayerWindow.Mission3.cs:291. ruleId is unused there too.
            if (_loadedEnhancement == null) return;
            var path = _loadedFilePath;

            // In-memory only (editor preview / embedded tag): focus the owning editor if any.
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                if (Owner is DeeperEditorWindow owner) owner.Activate();
                return;
            }

            // Dedupe: one editor per file.
            foreach (var ed in DeeperEditorWindow.OpenEditors)
            {
                if (!string.Equals(ed.LoadedFilePath, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (ed.WindowState == WindowState.Minimized) ed.WindowState = WindowState.Normal;
                ed.Activate();
                return;
            }

            // MainWindow.OpenDeeperFile: re-read the file (never share this player's live object
            // with the editor), record it as recent, open the editor.
            try
            {
                var fresh = EnhancementSerializer.LoadFromFile(path!);
                DeeperEditorWindow.TouchRecent(path!);
                // WPF Owner=MainWindow (OpenDeeperEditor); the shell owns it when it is up.
                var editor = new DeeperEditorWindow(fresh, path);
                if (Windows.MainShellWindow.Current is { IsVisible: true } shell) editor.Show(shell);
                else editor.Show();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Player: open-in-editor route failed");
                _ = Dialogs.MessageDialog.ShowAsync(this, "Deeper", ex.Message);
            }
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
            // is not. Audio seeks the transport (AudioSeek); before that, in audio mode the
            // playhead moves and nothing else does.
            if (_isVideoMode && _videoNavigated)
            {
                _ = _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke(
                    "l.currentTime=" + _currentSec.ToString("0.###", CultureInfo.InvariantCulture) + ";"));
            }
            else if (!_isVideoMode) AudioSeek(_currentSec);
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
            CreditDeeperMinutes(false);   // the last partial minute of play (permanent_resident)
            try { _uiTimer?.Stop(); } catch { }
            try { if (_uiTimer != null) _uiTimer.Tick -= UiTimer_Tick; } catch { }
            _uiTimer = null;
            // The poll rides the tick, so stopping the timer stops it; the flag makes a poll that
            // is already in flight a no-op rather than a write into a closing window.
            _pollDisabled = true;
            try { _fetcher?.Dispose(); } catch { }
            _fetcher = null;
            // Engine, audio and the tracker subscription: every started effect stops with the window.
            CloseEngine();
            // A camera this player started is handed back in CloseEngine (HandBackEyeTracking).
            // ponytail: the WPF teardown also disposed the video time source and force-closed the
            // borderless fullscreen host. Those are WebView2's; neither exists here. The web view
            // itself is disposed with the visual tree.
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
