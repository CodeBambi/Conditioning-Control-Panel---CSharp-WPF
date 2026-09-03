using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using IOPath = System.IO.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// One row in the validation-details popup.
    ///
    /// PORTED from the anonymous type <c>PopulateValidationPopup</c> built in WPF. Compiled
    /// bindings need a named <c>x:DataType</c>, and an anonymous type cannot be one.
    /// </summary>
    public sealed class ValidationIssueRow
    {
        public string Glyph { get; init; } = "";
        public IBrush Brush { get; init; } = Brushes.Gray;
        public string Message { get; init; } = "";
    }

    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Deeper/DeeperEditorWindow.xaml.cs and its six
    /// partials (.ItemsList, .LaneChrome, .MetadataDrawer, .MultiSelect, .Ruler, .Unified), which
    /// are ported alongside this file under the same names. One extra partial,
    /// <c>DeeperEditorWindow.RuleEditor.cs</c>, carries the rule-inspector and dynamic field
    /// builders that lived in the tail of the WPF main file; nothing was dropped, only split.
    ///
    /// <para><b>What is real here.</b> Everything that is view state, data manipulation and
    /// drawing: the unified timeline (ruler, three lanes, region bands, haptic bands, effect dots
    /// and segments, TimeReached rule pins, playhead), click/drag/resize/rubber-band selection,
    /// multi-select with group drag, clipboard, snapshot undo/redo, the items list, the metadata
    /// drawer, the selection summary strip, every inspector panel including the programmatically
    /// built trigger/action fields, the haptic curve editor, and validation via
    /// <see cref="EnhancementValidator"/>. All of that runs off Core models, so it ports whole.</para>
    ///
    /// <para><b>What is stubbed, and why.</b> Each site carries a <c>ponytail:</c> comment.</para>
    /// <list type="bullet">
    ///   <item><b>Media playback.</b> LibVLCSharp.WPF (video) and NAudio (audio + waveform peaks)
    ///         are Windows/WPF-only assemblies and this layer may not add a package, so
    ///         <see cref="InitializeVideo"/>, <see cref="InitializeAudioAsync"/> and the transport's
    ///         play/pause/seek drive <see cref="_totalSeconds"/> / <see cref="_currentSeconds"/>
    ///         directly instead of a player. Everything downstream of those two numbers is real.</item>
    ///   <item><b>WebView2.</b> The WPF preview drove <c>CoreWebView2</c> for navigation, injected
    ///         JS, WebMessage time reporting, fullscreen and ZoomFactor.
    ///         <see cref="Controls.WebHost"/> exposes only <c>Source</c>, so navigation is a Source
    ///         assignment and the rest (BrowserVideoTimeSource, the fullscreen reparent,
    ///         ExecuteScriptAsync, AddScriptToExecuteOnDocumentCreatedAsync, WebMessageReceived,
    ///         ContainsFullScreenElementChanged, ZoomFactor) is a stub.</item>
    ///   <item><b>Win32.</b> <c>WindowChromeHelper.ApplyDarkTitleBar</c> (DwmSetWindowAttribute),
    ///         <c>RestoreOwnerOnClose</c>, <c>System.Windows.Forms.Screen.FromHandle</c> +
    ///         <c>WindowInteropHelper</c> (the borderless-fullscreen preview window) and
    ///         <c>VisualTreeHelper.GetDpi</c> (the gaze picker's screen placement).</item>
    ///   <item><b>App services.</b> App.Settings (sidebar width, first-run flag), App.Tutorial +
    ///         TutorialOverlay, App.EnhancementLibrary, App.DeeperPlayer / App.DeeperHost,
    ///         the haptics bus, ExplorerLauncher, GazePickerWindow, and every file dialog and
    ///         MessageBox. Save / Save As / Export / Swap / Change media / drag-drop all land on
    ///         those, so they log their intent and return.</item>
    /// </list>
    /// </summary>
    public partial class DeeperEditorWindow : Window
    {
        private Enhancement _enhancement = new();
        private string? _filePath;

        /// <summary>
        /// On-disk path of the currently loaded project (null for a new/unsaved one). Read by the
        /// Player's "Open in editor" dedupe loop so it can activate an existing editor instead of
        /// opening a duplicate.
        /// </summary>
        public string? LoadedFilePath => _filePath;

        private bool _isDirty;
        private bool _suppressDirty;

        // ponytail: needs LibVLCSharp (video), NAudio (audio + AudioWaveformResult) and
        // BrowserVideoTimeSource (WebView2 time source). The three player handles the WPF window
        // held are replaced by the two numbers every drawing path actually reads.
        private double[]? _waveformPeaks;
        private bool _isPlaying;

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
        private readonly List<Rectangle> _regionVisuals = new();

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
        private Rectangle? _dragCreatePreview;
        // Region drag/resize state
        private Region? _draggedRegion;
        private double _regionDragOffsetSec;
        private double _regionDragOriginalLength;
        // Effect segment drag/resize state
        private TimelineItem? _draggedEffect;
        private double _effectDragOffsetSec;
        private double _effectDragOriginalDuration;
        // Pixel band on left/right of a band where the cursor switches to resize.
        private const double EdgeResizePx = 6.0;
        // Narrower rects become pure drag-to-move so the user can always grab and reposition them.
        private const double MinResizableRectPx = 24.0;
        // Minimum visual width for region / haptic rectangles, so a tiny duration on a wide
        // timeline stays clickable.
        private const double MinBandVisualWidthPx = 8.0;

        private enum EdgeHit { Body, Start, End }

        private static EdgeHit ClassifyEdgeHit(double posX, double rectWidth)
        {
            if (rectWidth < MinResizableRectPx) return EdgeHit.Body;
            var edge = Math.Min(EdgeResizePx, rectWidth / 3.0);
            if (posX <= edge) return EdgeHit.Start;
            if (posX >= rectWidth - edge) return EdgeHit.End;
            return EdgeHit.Body;
        }

        // Timeline zoom state. 1.0 = canvas fills the viewport (default).
        private double _zoomFactor = 1.0;
        private const double MinZoom = 1.0;
        private const double MaxZoom = 16.0;

        // Haptic events
        private const string DefaultTrackId = "primary";
        private HapticEvent? _selectedHaptic;
        private HapticTrack? _selectedHapticTrack;
        private readonly List<Rectangle> _hapticVisuals = new();
        private double _hapticDragStartSec;
        private double _hapticDragOffsetSec;
        private HapticEvent? _draggedHaptic;
        private HapticTrack? _draggedHapticTrack;

        // Curve editor
        private const int CurveKeyframeCount = 5;
        private ShapePath? _curvePath;
        private readonly List<Ellipse> _curveHandles = new();
        private int _draggingCurveIndex = -1;
        private bool _suppressPatternSync;

        // Rules
        private EnhancementRule? _selectedRule;
        private bool _suppressRuleSync;

        // ---------------------------------------------------------------------------------
        // Construction
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Render/design constructor: a sample enhancement with two regions, a TimeReached rule, a
        /// haptic event and two effects, on a remote https:// source so the preview takes the
        /// browser branch and WebHost's fallback panel draws. Required by <c>--render-all</c>,
        /// which only discovers parameterless constructors.
        /// </summary>
        internal DeeperEditorWindow() : this(SampleEnhancement(), "/home/sample/deep-dive.ccpenh.json")
        {
            _totalSeconds = 180;
            _currentSeconds = 42;
            TxtTotalTime.Text = FormatTime(_totalSeconds);
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            MetadataDrawerToggle.IsChecked = true;
            // Select the sample rule so the inspector shows the rule editor with its
            // code-built TriggerFields / ActionFields rather than the empty placeholder.
            SelectRule(_enhancement.Rules.FirstOrDefault());
        }

        public DeeperEditorWindow(Enhancement enhancement, string? filePath)
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // ponytail: needs WindowChromeHelper.ApplyDarkTitleBar (DwmSetWindowAttribute) and
            // RestoreOwnerOnClose. The title-bar tint has no Linux equivalent short of
            // SystemDecorations="None" plus a hand-drawn bar, which would cost this resizable
            // window its native move/resize/maximize for a colour. Left native and untinted.
            Closed += (_, _) => { try { (Owner as Window)?.Activate(); } catch { } };

            Loaded += DeeperEditorWindow_Loaded;
            KeyDown += DeeperEditorWindow_KeyDown;
            Closing += Window_Closing;

            _validationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _validationTimer.Tick += (_, _) => { _validationTimer.Stop(); RefreshValidation(); };

            _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _playheadTimer.Tick += PlayheadTimer_Tick;

            WireEvents();

            BuildColorSwatches();
            BuildPatternCombo();
            FillHapticTargetCombo(CmbHapticTarget);
            LoadEnhancement(enhancement, filePath);
        }

        /// <summary>
        /// Every handler the WPF XAML attached with a Click= / TextChanged= attribute. Wired in
        /// code rather than in the .axaml for two reasons: the two menus are now an inline
        /// ContextMenu plus a MenuFlyout (so their items are reachable only by x:Name), and the
        /// timeline's mouse events collapse into three pointer events that fan out by button and
        /// drag mode.
        /// </summary>
        private void WireEvents()
        {
            // -- File menu + the two validation-strip buttons
            MenuSave.Click += MenuSave_Click;
            MenuSaveAs.Click += MenuSaveAs_Click;
            MenuExport.Click += MenuExportEnhanced_Click;
            MenuCloseItem.Click += MenuClose_Click;
            BtnEditorSave.Click += MenuSave_Click;
            BtnEditorExport.Click += MenuExportEnhanced_Click;
            BtnEditorHelp.Click += BtnEditorHelp_Click;
            TxtValidationSummary.PointerReleased += TxtValidationSummary_PointerReleased;

            // -- Linked files strip
            BtnPreviewZoomIn.Click += BtnPreviewZoomIn_Click;
            BtnPreviewZoomOut.Click += BtnPreviewZoomOut_Click;
            BtnLinkedJsonOpenFolder.Click += BtnLinkedJsonOpenFolder_Click;
            BtnLinkedJsonSwap.Click += BtnLinkedJsonSwap_Click;
            BtnLinkedMediaChange.Click += BtnLinkedMediaChange_Click;
            BtnLinkedMediaClear.Click += BtnLinkedMediaClear_Click;
            BtnChangeMediaLocal.Click += BtnChangeMediaLocal_Click;
            BtnChangeMediaUrl.Click += BtnChangeMediaUrl_Click;

            // -- Transport
            BtnPlayPause.Click += BtnPlayPause_Click;
            BtnZoomIn.Click += BtnZoomIn_Click;
            BtnZoomOut.Click += BtnZoomOut_Click;
            BtnPreview.Click += BtnPreview_Click;
            BtnAddRuleHero.Click += BtnAddRuleHero_Click;
            BtnAddEffectHero.Click += BtnAddEffectHero_Click;

            // -- Hero "+ Effect" flyout + the timeline context menu
            HeroEffectHaptic.Click += CtxAddEffectHaptic_Click;
            HeroEffectFlash.Click += CtxAddEffectFlash_Click;
            HeroEffectBubble.Click += CtxAddEffectBubble_Click;
            HeroEffectSubliminal.Click += CtxAddEffectSubliminal_Click;
            HeroEffectOverlay.Click += CtxAddEffectOverlay_Click;
            HeroEffectSpeak.Click += CtxAddEffectSpeak_Click;
            CtxEffectHaptic.Click += CtxAddEffectHaptic_Click;
            CtxEffectFlash.Click += CtxAddEffectFlash_Click;
            CtxEffectBubble.Click += CtxAddEffectBubble_Click;
            CtxEffectSubliminal.Click += CtxAddEffectSubliminal_Click;
            CtxEffectOverlay.Click += CtxAddEffectOverlay_Click;
            CtxEffectSpeak.Click += CtxAddEffectSpeak_Click;
            CtxRuleTimeReached.Click += CtxAddRuleTimeReached_Click;
            CtxRuleBandEntered.Click += CtxAddRuleBandEntered_Click;
            CtxRuleBandExited.Click += CtxAddRuleBandExited_Click;
            CtxRuleGazeTarget.Click += CtxAddRuleGazeTarget_Click;
            CtxRuleGazeAvoid.Click += CtxAddRuleGazeAvoid_Click;
            CtxRuleAttentionLost.Click += CtxAddRuleAttentionLost_Click;
            CtxRuleBlinkDetected.Click += CtxAddRuleBlinkDetected_Click;
            CtxRuleMouthOpen.Click += CtxAddRuleMouthOpen_Click;

            // -- Timeline canvas + scroller
            TimelineCanvas.PointerPressed += TimelineCanvas_PointerPressed;
            TimelineCanvas.PointerMoved += TimelineCanvas_PointerMoved;
            TimelineCanvas.PointerReleased += TimelineCanvas_PointerReleased;
            TimelineCanvas.PointerCaptureLost += TimelineCanvas_PointerCaptureLost;
            TimelineCanvas.SizeChanged += TimelineCanvas_SizeChanged;
            TimelineScroll.SizeChanged += TimelineScroll_SizeChanged;
            // WPF's PreviewMouseWheel: tunnelling, so it beats the ScrollViewer's own handling.
            TimelineScroll.AddHandler(PointerWheelChangedEvent, TimelineScroll_PointerWheelChanged,
                RoutingStrategies.Tunnel);

            // -- Sidebar: drawer, items list, splitter
            MetadataDrawerToggle.IsCheckedChanged += MetadataDrawerToggle_Changed;
            RbItemsSortTime.IsCheckedChanged += ItemsListSort_Changed;
            RbItemsSortKind.IsCheckedChanged += ItemsListSort_Changed;
            ItemsListBox.SelectionChanged += ItemsListBox_SelectionChanged;
            ItemsListBox.DoubleTapped += ItemsListBox_DoubleTapped;
            // The per-row delete button lives inside a compiled-binding DataTemplate, whose Click
            // handler would resolve against the row's TimelineListEntry, not this window. Catch it
            // on the way up by class instead.
            ItemsListBox.AddHandler(Button.ClickEvent, (object? s, RoutedEventArgs e) =>
            {
                if (e.Source is Button b && b.Classes.Contains("itemsListDelete"))
                    ItemsListDelete_Click(b, e);
            }, RoutingStrategies.Bubble);
            SidebarSplitter.DragCompleted += SidebarSplitter_DragCompleted;

            // -- Metadata fields
            foreach (var tb in new[] { TxtMetaName, TxtMetaCreator, TxtMetaRemixer,
                                       TxtMetaDescription, TxtMetaTags, TxtMetaLicense })
                tb.TextChanged += MetadataField_TextChanged;
            BtnCreatorLockToggle.Click += BtnCreatorLockToggle_Click;

            // -- Region editor
            TxtRegionLabel.TextChanged += RegionField_TextChanged;
            TxtRegionStart.TextChanged += RegionField_TextChanged;
            TxtRegionEnd.TextChanged += RegionField_TextChanged;
            TxtRegionColor.TextChanged += RegionField_TextChanged;
            BtnSnapRegionStartPrev.Click += BtnSnapRegionStartPrev_Click;
            BtnSnapRegionEndNext.Click += BtnSnapRegionEndNext_Click;
            BtnDeleteRegion.Click += BtnDeleteRegion_Click;

            // -- Haptic editor
            TxtHapticStart.TextChanged += HapticField_TextChanged;
            TxtHapticDuration.TextChanged += HapticField_TextChanged;
            SliderHapticIntensity.ValueChanged += SliderHapticIntensity_ValueChanged;
            CmbHapticPattern.SelectionChanged += CmbHapticPattern_SelectionChanged;
            CmbHapticTarget.SelectionChanged += CmbHapticTarget_SelectionChanged;
            CurveCanvas.SizeChanged += CurveCanvas_SizeChanged;
            BtnResetCurve.Click += BtnResetCurve_Click;
            BtnTestHaptic.Click += BtnTestHaptic_Click;
            BtnDeleteHaptic.Click += BtnDeleteHaptic_Click;

            // -- Rule editor
            BtnDeleteRule.Click += BtnDeleteRule_Click;
            ChkRuleEnabled.IsCheckedChanged += ChkRuleEnabled_Changed;
            BtnTriggerHelp.Click += BtnTriggerHelp_Click;
            BtnActionHelp.Click += BtnActionHelp_Click;
            BtnRegionHelp.Click += BtnRegionHelp_Click;
            CmbTriggerType.SelectionChanged += CmbTriggerType_SelectionChanged;
            CmbActionType.SelectionChanged += CmbActionType_SelectionChanged;
            CmbRuleRegion.SelectionChanged += CmbRuleRegion_SelectionChanged;
            TxtRuleCooldown.TextChanged += TxtRuleCooldown_TextChanged;
            TxtRuleBandLabel.TextChanged += RuleBandField_TextChanged;
            TxtRuleBandStart.TextChanged += RuleBandField_TextChanged;
            TxtRuleBandEnd.TextChanged += RuleBandField_TextChanged;
            TxtRuleBandColor.TextChanged += RuleBandField_TextChanged;

            // -- Effect editors
            foreach (var tb in new[] { TxtFlashStart, TxtFlashDuration,
                                       TxtBubbleStart, TxtBubbleWindow,
                                       TxtSubliminalStart, TxtSubliminalText, TxtSubliminalDuration,
                                       TxtOverlayStart, TxtOverlayDuration,
                                       TxtSpeakStart, TxtSpeakLength, TxtSpeakTarget, TxtSpeakCue,
                                       TxtSpeakInterval, TxtSpeakCorrect, TxtSpeakIncorrect })
                tb.TextChanged += EffectField_TextChanged;
            ChkFlashSuppressHaptic.IsCheckedChanged += ChkEffectSuppressHaptic_Changed;
            ChkSubliminalSuppressHaptic.IsCheckedChanged += ChkEffectSuppressHaptic_Changed;
            SliderBubbleIntensity.ValueChanged += SliderBubbleIntensity_ValueChanged;
            SliderOverlayOpacity.ValueChanged += SliderOverlayOpacity_ValueChanged;
            SliderOverlayOpacityEnd.ValueChanged += SliderOverlayOpacityEnd_ValueChanged;
            ChkOverlayRamp.IsCheckedChanged += ChkOverlayRamp_Changed;
            CmbOverlayKind.SelectionChanged += CmbOverlayKind_SelectionChanged;
            CmbSpeakCueMode.SelectionChanged += CmbSpeakCueMode_SelectionChanged;
            CmbSpeakReps.SelectionChanged += CmbSpeakReps_SelectionChanged;
            CmbSpeakCompletion.SelectionChanged += CmbSpeakCompletion_SelectionChanged;
            CmbSpeakHold.SelectionChanged += CmbSpeakHold_SelectionChanged;
            foreach (var b in new[] { BtnDeleteFlashEffect, BtnDeleteBubbleEffect,
                                      BtnDeleteSubliminalEffect, BtnDeleteOverlayEffect,
                                      BtnDeleteSpeakEffect })
                b.Click += BtnDeleteEffect_Click;

            // -- Drag & drop (WPF: Window DragOver / Drop attributes)
            AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            AddHandler(DragDrop.DropEvent, Window_Drop);
        }

        /// <summary>Resource brush lookup. WPF's <c>FindResource(k)</c> threw on a miss; this
        /// returns a visible fallback so a missing key is a wrong colour, never a crash.</summary>
        private IBrush Res(string key)
            => this.TryFindResource(key, out var v) && v is IBrush b ? b : Brushes.Gray;

        /// <summary>Sample project for the render constructor. Placeholder data, deliberately
        /// shaped so the PNG proves the timeline, the lanes, the items list and the rule
        /// inspector all draw.</summary>
        private static Enhancement SampleEnhancement()
        {
            var e = new Enhancement
            {
                MediaType = MediaTypes.Video,
                MediaSource = "https://example.com/video/sample-render-proof.mp4",
            };
            e.Metadata.Name = "Descent Sample";
            e.Metadata.Creator = "sample-creator";
            e.Metadata.Description = "Placeholder project used by the headless render proof.";
            e.Metadata.Tags = new List<string> { "sample", "render" };
            e.Metadata.License = "CC BY-NC 4.0";

            e.Regions.Add(new Region { Id = "r1", Start = 12, End = 48, Label = "Induction", Color = RegionPalette[0] });
            e.Regions.Add(new Region { Id = "r2", Start = 70, End = 132, Label = "Deepener", Color = RegionPalette[1] });

            e.Rules.Add(new EnhancementRule
            {
                Trigger = new TimeReachedTrigger { Time = 30 },
                Action = new ScreenShakeAction { Intensity = 0.4, DurationMs = 600 },
                CooldownMs = 1000,
                Enabled = true,
            });

            var track = new HapticTrack { Id = DefaultTrackId };
            track.Events.Add(new HapticEvent { Start = 90, Duration = 14, Intensity = 0.8, PatternName = "Pulse" });
            e.HapticTracks.Add(track);

            e.TimelineItems.Add(new TimelineItem
            {
                Id = TimelineItem.NewId(),
                Kind = TimelineItemKind.Effect,
                Start = 20,
                Duration = 0.8,
                EffectType = EffectTypes.Flash,
                EffectDurationMs = 800,
                EffectIntensity = 1.0,
                Color = "#FFC85C",
            });
            e.TimelineItems.Add(new TimelineItem
            {
                Id = TimelineItem.NewId(),
                Kind = TimelineItemKind.Effect,
                Start = 100,
                Duration = 24,
                EffectType = EffectTypes.Overlay,
                EffectDurationMs = 24000,
                EffectOpacity = 0.55,
                EffectOverlayKind = OverlayKinds.Spiral,
                Color = "#5CFFB7",
            });
            return e;
        }

        // ---------------------------------------------------------------------------------
        // Combos + swatches
        // ---------------------------------------------------------------------------------

        private void BuildPatternCombo()
        {
            CmbHapticPattern.Items.Clear();
            foreach (var name in StockHapticPatterns.Names)
                CmbHapticPattern.Items.Add(name);
            CmbHapticPattern.Items.Add(Loc.Get("deeper_editor_haptic_pattern_custom"));
        }

        // Index 0 is "All", stored as null so files stay clean.
        private static readonly ToyRole?[] HapticTargets = { null, ToyRole.Reward, ToyRole.Punish, ToyRole.Ambient };

        private static void FillHapticTargetCombo(ComboBox combo)
        {
            combo.Items.Clear();
            combo.Items.Add(Loc.Get("haptics_role_all"));
            combo.Items.Add(Loc.Get("haptics_role_reward"));
            combo.Items.Add(Loc.Get("haptics_role_punish"));
            combo.Items.Add(Loc.Get("haptics_role_ambient"));
        }

        private static int HapticTargetIndex(ToyRole? role)
        {
            var idx = Array.IndexOf(HapticTargets, role);
            return idx >= 0 ? idx : 0;
        }

        private static ToyRole? HapticTargetAt(int index)
            => index > 0 && index < HapticTargets.Length ? HapticTargets[index] : null;

        private void BuildColorSwatches()
        {
            RegionColorSwatches.Children.Clear();
            foreach (var hex in RegionPalette)
            {
                var captured = hex;
                var brush = TryParseBrush(hex) ?? Brushes.MediumPurple;
                var swatch = new Border
                {
                    Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = brush,
                    BorderBrush = Res("GlassBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = hex,
                };
                ToolTip.SetTip(swatch, hex);
                swatch.PointerReleased += (_, _) =>
                {
                    if (_selectedRegion == null) return;
                    TxtRegionColor.Text = captured;
                };
                RegionColorSwatches.Children.Add(swatch);
            }
        }

        private static IBrush? TryParseBrush(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try { return new SolidColorBrush(Color.Parse(hex!)); }
            catch { return null; }
        }

        private static Color? TryParseColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try { return Color.Parse(hex!); }
            catch { return null; }
        }

        // ---------------------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------------------

        private void DeeperEditorWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            // Restore the user's last sidebar width before the preview kicks off.
            ApplyPersistedSidebarWidth();

            _ = InitializePreviewAsync();

            // ponytail: needs TutorialEventBus + App.Tutorial + TutorialOverlay + App.Settings.
            // The WPF Loaded handler dispatched the interactive tutorial's Part 2 (queued by the
            // New Enhancement dialog) and, failing that, auto-launched the first-run editor
            // coachmarks once. Neither service exists on this head, so both are dropped here and
            // BtnEditorHelp is inert; nothing else in the editor depended on them.
        }

        private void BtnEditorHelp_Click(object? sender, RoutedEventArgs e)
        {
            StartEditorTutorial();
        }

        /// <summary>ponytail: needs App.Tutorial + TutorialOverlay, wired when the tutorial service
        /// moves to Core. The "?" button is present and hit-testable but opens nothing.</summary>
        private void StartEditorTutorial()
        {
            Log.Debug("DeeperEditor: editor tutorial requested; App.Tutorial is not on this head");
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
            finally { ClearWhenEventsDrained(() => _suppressDirty = false); }

            // Set from code, not {loc:Str}: Avalonia keeps a binding alive under a local value, so
            // the Preview button's label has to be chosen here once rather than bound and then
            // overwritten by BtnPreview_Click.
            TxtPreviewLabel.Text = Loc.Get("deeper_editor_preview_off");

            UpdateTitle();
            RefreshLinkedFilesUi();
            UpdateCreatorLockUi();
            RefreshValidation();
            SelectNothing();
            RebuildTimelineRuler();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
            RebuildRuleVisuals();
            UpdateSelectionSummary();

            // Fire HT metadata auto-fill in the background. Hostname-gated inside the fetcher;
            // non-HT URLs are silent no-ops.
            _ = TryAutoFillFromHtAsync(_enhancement.MediaSource);
        }

        // ---------------------------------------------------------------------------------
        // Preview
        // ---------------------------------------------------------------------------------

        private async Task InitializePreviewAsync()
        {
            try
            {
                var source = _enhancement.MediaSource;

                // Remote http(s):// URL -> embedded browser preview.
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
                Log.Warning(ex, "DeeperEditor: preview init failed");
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

        /// <summary>
        /// ponytail: needs the CoreWebView2 surface. The WPF version created the WebView2
        /// environment, gated navigation on IsAllowedPreviewHost, injected a script that posted the
        /// &lt;video&gt; element's currentTime back over WebMessage (driving BrowserVideoTimeSource
        /// and thus _totalSeconds / the playhead), and handled fullscreen. WebHost exposes only
        /// Source, so this navigates and stops; the timeline runs off whatever duration the project
        /// itself carries.
        /// </summary>
        private Task InitializeBrowserAsync(string url)
        {
            VideoPreview.IsVisible = false;
            WaveformCanvas.IsVisible = false;
            PreviewPlaceholder.IsVisible = false;
            BrowserPreview.IsVisible = true;
            try { BrowserPreview.Source = new Uri(url); }
            catch (Exception ex) { Log.Debug("DeeperEditor: preview URL rejected: {Error}", ex.Message); }
            return Task.CompletedTask;
        }

        /// <summary>ponytail: needs LibVLCSharp. The WPF path created a MediaPlayer on VideoView and
        /// hooked LengthChanged / TimeChanged / EndReached to drive _totalSeconds, _currentSeconds
        /// and the play/pause glyph.</summary>
        private void InitializeVideo(string path)
        {
            BrowserPreview.IsVisible = false;
            WaveformCanvas.IsVisible = false;
            PreviewPlaceholder.IsVisible = false;
            VideoPreview.IsVisible = true;
            Log.Debug("DeeperEditor: local video preview unavailable on this head: {Path}", path);
        }

        /// <summary>ponytail: needs NAudio (WaveOutEvent + AudioFileReader) and the waveform peak
        /// extractor. The canvas and its Path are here and <see cref="UpdateWaveformPath"/> is real,
        /// so wiring peaks in later is a one-line assignment to <c>_waveformPeaks</c>.</summary>
        private Task InitializeAudioAsync(string path)
        {
            BrowserPreview.IsVisible = false;
            VideoPreview.IsVisible = false;
            PreviewPlaceholder.IsVisible = false;
            WaveformCanvas.IsVisible = true;
            Log.Debug("DeeperEditor: local audio preview unavailable on this head: {Path}", path);
            return Task.CompletedTask;
        }

        private void ShowPlaceholder()
        {
            VideoPreview.IsVisible = false;
            WaveformCanvas.IsVisible = false;
            BrowserPreview.IsVisible = false;
            PreviewPlaceholder.IsVisible = true;

            var src = _enhancement.MediaSource ?? "";
            // "*" or empty source = wildcard binding ("works on any media"). The editor can't
            // preview a wildcard because there is no media file - the project is still valid.
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
            // Reject UNC and extended-length prefixes: a shared .ccpenh.json pointing at
            // \\attacker-smb\share\beacon would leak the user's NTLM hash on first access.
            // ponytail: Core's UrlSafety.IsSafeLocalAbsolute is internal and CCP.Avalonia is not in
            // its InternalsVisibleTo list, so the same two rejections are inlined here. Drop this
            // block and call UrlSafety the moment the head is added to that attribute.
            if (source.StartsWith("\\\\", StringComparison.Ordinal)) return false;   // UNC
            if (source.StartsWith("\\\\?\\", StringComparison.Ordinal)) return false; // extended-length
            if (!IOPath.IsPathRooted(source)) return false;
            return File.Exists(source);
        }

        private void UpdateWaveformPath()
        {
            if (_waveformPeaks == null || _waveformPeaks.Length == 0)
            {
                WaveformPath.Data = null;
                return;
            }

            var peaks = _waveformPeaks;
            var width = WaveformCanvas.Bounds.Width;
            var height = WaveformCanvas.Bounds.Height;
            if (width <= 0 || height <= 0) return;

            // Vertical bars (one per peak) - the classic editor look. Each bar is centred on its X
            // column, from (midY - amp) to (midY + amp).
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
                    ctx.BeginFigure(new Point(x, midY - amp), false);
                    ctx.LineTo(new Point(x, midY + amp));
                    ctx.EndFigure(false);
                }
            }
            WaveformPath.Data = geom;
        }

        // ---------------------------------------------------------------------------------
        // Transport
        // ---------------------------------------------------------------------------------

        /// <summary>ponytail: needs a media player. With none, play/pause toggles the glyph and the
        /// playhead timer so the timeline still animates against the project's own duration.</summary>
        private void BtnPlayPause_Click(object? sender, RoutedEventArgs e)
        {
            _isPlaying = !_isPlaying;
            BtnPlayPause.Content = _isPlaying ? "⏸" : "▶";
            if (_isPlaying) _playheadTimer?.Start(); else _playheadTimer?.Stop();
        }

        private void PlayheadTimer_Tick(object? sender, EventArgs e)
        {
            if (_isScrubbing) return;
            if (!_isPlaying || _totalSeconds <= 0) return;
            // ponytail: the WPF tick read AudioFileReader.CurrentTime (video time arrived on
            // MediaPlayer.TimeChanged). With no player, advance by the timer interval so the
            // playhead, the readout and any time-driven redraw stay honest about elapsed time.
            _currentSeconds = Math.Min(_totalSeconds, _currentSeconds + 0.08);
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            UpdatePlayheadPosition();
        }

        private void SeekToFraction(double frac)
        {
            frac = Math.Clamp(frac, 0, 1);
            _currentSeconds = frac * _totalSeconds;
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            // ponytail: needs the media player's Seek; the WPF version pushed the new position to
            // BrowserVideoTimeSource / MediaPlayer / AudioFileReader here.
            UpdatePlayheadPosition();
        }

        private void UpdatePlayheadPosition()
        {
            if (TimelineCanvas.Bounds.Width <= 0) return;
            double frac = _totalSeconds > 0 ? Math.Clamp(_currentSeconds / _totalSeconds, 0, 1) : 0;
            double x = frac * TimelineCanvas.Bounds.Width;
            PlayheadLine.StartPoint = new Point(x, 0);
            PlayheadLine.EndPoint = new Point(x, TimelineCanvas.Bounds.Height);
        }

        private void TimelineCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            UpdatePlayheadPosition();
            UpdateWaveformPath();
            RebuildTimelineRuler();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
            RebuildRuleVisuals();
            RefreshLaneCounts();
        }

        // ---------------------------------------------------------------------------------
        // Timeline zoom
        // ---------------------------------------------------------------------------------

        private void TimelineScroll_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // Recompute Canvas.Width whenever the viewport changes so zoom=1 keeps the canvas
            // stretched to fill and higher zooms proportionally expand it.
            ApplyZoom();
        }

        private void ApplyZoom(double? anchorViewportX = null)
        {
            if (TimelineScroll == null || TimelineCanvas == null) return;
            var viewportW = TimelineScroll.Viewport.Width;
            if (viewportW <= 0) return;

            // Capture the scroll-relative time of the anchor point (cursor X for wheel zoom) so we
            // can re-anchor after resize, for a "zoom centred on cursor" feel.
            double? timeAnchor = null;
            if (anchorViewportX is double ax && _totalSeconds > 0 && TimelineCanvas.Bounds.Width > 0)
            {
                var contentX = TimelineScroll.Offset.X + ax;
                timeAnchor = (contentX / TimelineCanvas.Bounds.Width) * _totalSeconds;
            }

            double newWidth = viewportW * _zoomFactor;
            TimelineCanvas.Width = newWidth;

            if (TxtZoomLevel != null)
                TxtZoomLevel.Text = $"{(int)Math.Round(_zoomFactor * 100)}%";

            // Defer the rebuild + re-anchor until layout has seen the new width.
            Dispatcher.UIThread.Post(() =>
            {
                UpdatePlayheadPosition();
                UpdateWaveformPath();
                RebuildTimelineRuler();
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                RebuildRuleVisuals();

                if (timeAnchor is double ta && _totalSeconds > 0 && anchorViewportX is double ax2)
                {
                    var newContentX = (ta / _totalSeconds) * TimelineCanvas.Bounds.Width;
                    var newOffset = newContentX - ax2;
                    TimelineScroll.Offset = new Vector(Math.Max(0, newOffset), TimelineScroll.Offset.Y);
                }
            }, DispatcherPriority.Render);
        }

        private void SetZoom(double newZoom, double? anchorViewportX = null)
        {
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - _zoomFactor) < 0.001) return;
            _zoomFactor = newZoom;
            ApplyZoom(anchorViewportX);
        }

        private void BtnZoomIn_Click(object? sender, RoutedEventArgs e) => SetZoom(_zoomFactor * 1.5);
        private void BtnZoomOut_Click(object? sender, RoutedEventArgs e) => SetZoom(_zoomFactor / 1.5);

        // +/-10% page zoom on the embedded browser preview. Distinct from BtnZoomIn/Out above,
        // which scale the timeline canvas.
        // ponytail: needs CoreWebView2.ZoomFactor; WebHost exposes only Source, so these two
        // buttons are inert until the wrapper grows a zoom property.
        private void BtnPreviewZoomIn_Click(object? sender, RoutedEventArgs e) => AdjustPreviewZoom(+0.10);
        private void BtnPreviewZoomOut_Click(object? sender, RoutedEventArgs e) => AdjustPreviewZoom(-0.10);

        private void AdjustPreviewZoom(double delta)
        {
            Log.Debug("DeeperEditor: preview zoom {Delta:+0.00;-0.00} ignored; WebHost has no ZoomFactor", delta);
        }

        private void TimelineScroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (TimelineScroll == null) return;
            if ((e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
            {
                // Ctrl+wheel = zoom, anchored on the cursor position.
                var pos = e.GetPosition(TimelineScroll);
                var factor = e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2;
                SetZoom(_zoomFactor * factor, pos.X);
                e.Handled = true;
                return;
            }
            // Plain wheel = horizontal scroll along the timeline. Wheel down advances time.
            // Avalonia reports the delta in notches rather than WPF's 120-unit steps, so the
            // multiplier restores roughly the same "half a viewport per notch" feel at zoom 1.
            var newOffset = TimelineScroll.Offset.X - e.Delta.Y * 120;
            if (newOffset < 0) newOffset = 0;
            var max = Math.Max(0, TimelineScroll.Extent.Width - TimelineScroll.Viewport.Width);
            if (newOffset > max) newOffset = max;
            TimelineScroll.Offset = new Vector(newOffset, TimelineScroll.Offset.Y);
            e.Handled = true;
        }

        // ---------------------------------------------------------------------------------
        // Timeline pointer handling
        //
        // WPF had five separate handlers (MouseLeftButtonDown/Move/Up, MouseRightButtonDown,
        // LostMouseCapture). Avalonia routes press/move/release through one event each, so the
        // button branch happens at the top of Pressed and everything below is unchanged.
        // ---------------------------------------------------------------------------------

        private void TimelineCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(TimelineCanvas).Properties;
            if (props.IsRightButtonPressed)
            {
                TimelineCanvas_MouseRightButtonDown(e);
                return; // not handled: Avalonia still opens the ContextMenu
            }
            if (!props.IsLeftButtonPressed) return;

            // Shift+drag creates a region - power-user shortcut.
            if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift && _totalSeconds > 0)
            {
                _dragMode = DragMode.CreateRegion;
                _dragCreateStartSec = MouseToSeconds(e);
                StartDragCreatePreview(_dragCreateStartSec, _dragCreateStartSec);
                e.Pointer.Capture(TimelineCanvas);
                e.Handled = true;
                return;
            }

            // Ctrl+drag = rubber-band multi-select (moved off the plain-drag gesture so the latter
            // can do player-style continuous scrubbing).
            if ((e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
            {
                _dragMode = DragMode.RubberBand;
                StartRubberBand(e.GetPosition(TimelineCanvas));
                e.Pointer.Capture(TimelineCanvas);
                e.Handled = true;
                return;
            }

            // Plain click+drag on empty timeline = scrub. Insta-seek on the down-click and follow
            // the cursor while dragging. Clears prior selection so the inspector isn't stale.
            SelectNothing();
            _dragMode = DragMode.Scrub;
            _isScrubbing = true;
            e.Pointer.Capture(TimelineCanvas);
            ApplyScrubFromMouse(e);
            e.Handled = true;
        }

        private void TimelineCanvas_PointerMoved(object? sender, PointerEventArgs e)
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

        private void TimelineCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;

            if (_dragMode == DragMode.RubberBand)
            {
                FinishRubberBand(e.GetPosition(TimelineCanvas));
                e.Pointer.Capture(null);
                _dragMode = DragMode.None;
                return;
            }
            if (_dragMode == DragMode.CreateRegion)
            {
                var endSec = MouseToSeconds(e);
                FinishDragCreate(endSec);
                e.Pointer.Capture(null);
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
                e.Pointer.Capture(null);
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
                e.Pointer.Capture(null);
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
                e.Pointer.Capture(null);
                ScheduleValidation();
                BuildItemsList();
                return;
            }
            if (_dragMode == DragMode.Scrub)
            {
                _isScrubbing = false;
                _dragMode = DragMode.None;
                e.Pointer.Capture(null);
            }
        }

        // A canvas that loses its pointer capture mid-drag (alt-tab, a popup steals focus, window
        // deactivation) never sees the release. Without this hook the rubber-band rectangle and the
        // drag-create preview stay on the canvas as orphan visuals and _dragMode stays stuck in a
        // non-None state - both observed in the field on Ctrl+drag rubber-band selection.
        private void TimelineCanvas_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
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

        private void ApplyScrubFromMouse(PointerEventArgs e)
        {
            var pt = e.GetPosition(TimelineCanvas);
            var w = TimelineCanvas.Bounds.Width;
            if (w <= 0) return;
            SeekToFraction(pt.X / w);
        }

        private double MouseToSeconds(PointerEventArgs e)
        {
            var pt = e.GetPosition(TimelineCanvas);
            var w = TimelineCanvas.Bounds.Width;
            if (w <= 0 || _totalSeconds <= 0) return 0;
            var frac = Math.Clamp(pt.X / w, 0, 1);
            return frac * _totalSeconds;
        }

        // ---------------------------------------------------------------------------------
        // Region creation
        // ---------------------------------------------------------------------------------

        private void StartDragCreatePreview(double startSec, double endSec)
        {
            _dragCreatePreview = new Rectangle
            {
                Fill = Res("DeeperAccentTransparent40Brush"),
                Stroke = Res("DeeperAccentBrush"),
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            TimelineCanvas.Children.Insert(0, _dragCreatePreview);
            UpdateDragCreatePreview(endSec);
        }

        private void UpdateDragCreatePreview(double endSec)
        {
            if (_dragCreatePreview == null) return;
            var w = TimelineCanvas.Bounds.Width;
            var h = TimelineCanvas.Bounds.Height;
            if (w <= 0 || _totalSeconds <= 0) return;

            var lo = Math.Min(_dragCreateStartSec, endSec);
            var hi = Math.Max(_dragCreateStartSec, endSec);
            var leftX = (lo / _totalSeconds) * w;
            var rightX = (hi / _totalSeconds) * w;

            var (regionTop, regionH) = LaneBandInset(TimelineLane.Regions, h);
            _dragCreatePreview.Width = Math.Max(0, rightX - leftX);
            _dragCreatePreview.Height = regionH;
            Canvas.SetLeft(_dragCreatePreview, leftX);
            Canvas.SetTop(_dragCreatePreview, regionTop);
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
            => RegionPalette[_enhancement.Regions.Count % RegionPalette.Length];

        // ---------------------------------------------------------------------------------
        // Selection
        // ---------------------------------------------------------------------------------

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

        // Finds the first rule whose RegionConstraint matches the given region id. Used by the
        // band-click handler to route to the rule editor when the band represents a rule's scope.
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
                SelectedPlaceholder.IsVisible = false;
                RegionEditor.IsVisible = true;

                _suppressDirty = true;
                try
                {
                    TxtRegionId.Text = _selectedRegion.Id;
                    TxtRegionLabel.Text = _selectedRegion.Label;
                    TxtRegionStart.Text = _selectedRegion.Start.ToString("0.##", CultureInfo.InvariantCulture);
                    TxtRegionEnd.Text = _selectedRegion.End.ToString("0.##", CultureInfo.InvariantCulture);
                    TxtRegionColor.Text = _selectedRegion.Color;
                    UpdateRegionColorSwatchPreview();
                    BtnSnapRegionStartPrev.IsEnabled = FindSnapPrevRegion() != null;
                    BtnSnapRegionEndNext.IsEnabled = FindSnapNextRegion() != null;
                }
                finally { ClearWhenEventsDrained(() => _suppressDirty = false); }
                return;
            }

            if (_selectedHaptic != null && _selectedHapticTrack != null)
            {
                SelectedPlaceholder.IsVisible = false;
                HapticEventEditor.IsVisible = true;
                PopulateHapticEditor();
                return;
            }

            if (_selectedRule != null)
            {
                SelectedPlaceholder.IsVisible = false;
                RuleEditor.IsVisible = true;
                PopulateRuleEditor();
                return;
            }

            SelectedPlaceholder.IsVisible = true;

            // Keep the selection summary strip in sync even when UpdateSelectedSidePanel is called
            // outside the SelectXxx path (e.g. drag-end). Cheap; safe to repeat.
            UpdateSelectionSummary();
        }

        private void UpdateRegionColorSwatchPreview()
        {
            var brush = TryParseBrush(_selectedRegion?.Color ?? "#7B5CFF");
            if (brush != null) RegionColorSwatch.Background = brush;
        }

        private void RegionField_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressDirty || _selectedRegion == null) return;
            _selectedRegion.Label = TxtRegionLabel.Text ?? "";
            if (double.TryParse(TxtRegionStart.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                _selectedRegion.Start = Math.Max(0, s);
            if (double.TryParse(TxtRegionEnd.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ev))
                _selectedRegion.End = ev;
            if (!string.IsNullOrWhiteSpace(TxtRegionColor.Text))
            {
                _selectedRegion.Color = TxtRegionColor.Text!.Trim();
                UpdateRegionColorSwatchPreview();
            }
            MarkDirty();
            RebuildRegionVisuals();
            ScheduleValidation();
        }

        // -- Region snap buttons ------------------------------------------------
        // "Region 4 ends at 93.52 -> region 5 starts at 93.52" without typing.

        /// <summary>The region whose End the selected region's Start can snap to, or null when
        /// there is none / it is already flush.</summary>
        private Region? FindSnapPrevRegion()
        {
            if (_selectedRegion == null) return null;
            Region? best = null;
            foreach (var r in _enhancement.Regions)
            {
                if (r == null || ReferenceEquals(r, _selectedRegion)) continue;
                if (r.End > _selectedRegion.Start + 0.001) continue;
                if (best == null || r.End > best.End) best = r;
            }
            if (best != null && Math.Abs(best.End - _selectedRegion.Start) < 0.0001) return null; // already flush
            return best;
        }

        private Region? FindSnapNextRegion()
        {
            if (_selectedRegion == null) return null;
            Region? best = null;
            foreach (var r in _enhancement.Regions)
            {
                if (r == null || ReferenceEquals(r, _selectedRegion)) continue;
                if (r.Start < _selectedRegion.End - 0.001) continue;
                if (best == null || r.Start < best.Start) best = r;
            }
            if (best != null && Math.Abs(best.Start - _selectedRegion.End) < 0.0001) return null; // already flush
            return best;
        }

        private void BtnSnapRegionStartPrev_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedRegion == null) return;
            var prev = FindSnapPrevRegion();
            if (prev == null) return;
            PushUndoSnapshot();
            _selectedRegion.Start = prev.End;
            _suppressDirty = true;
            try { TxtRegionStart.Text = _selectedRegion.Start.ToString("0.##", CultureInfo.InvariantCulture); }
            finally { ClearWhenEventsDrained(() => _suppressDirty = false); }
            BtnSnapRegionStartPrev.IsEnabled = FindSnapPrevRegion() != null;
            MarkDirty();
            RebuildRegionVisuals();
            ScheduleValidation();
        }

        private void BtnSnapRegionEndNext_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedRegion == null) return;
            var next = FindSnapNextRegion();
            if (next == null) return;
            PushUndoSnapshot();
            _selectedRegion.End = next.Start;
            _suppressDirty = true;
            try { TxtRegionEnd.Text = _selectedRegion.End.ToString("0.##", CultureInfo.InvariantCulture); }
            finally { ClearWhenEventsDrained(() => _suppressDirty = false); }
            BtnSnapRegionEndNext.IsEnabled = FindSnapNextRegion() != null;
            MarkDirty();
            RebuildRegionVisuals();
            ScheduleValidation();
        }

        private void BtnDeleteRegion_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedRegion == null) return;
            PushUndoSnapshot();
            _enhancement.Regions.Remove(_selectedRegion);
            SelectNothing();
            MarkDirty();
            RebuildRegionVisuals();
            ScheduleValidation();
        }

        // ---------------------------------------------------------------------------------
        // Region rendering
        // ---------------------------------------------------------------------------------

        private void RebuildRegionVisuals()
        {
            if (TimelineCanvas == null) return;
            foreach (var r in _regionVisuals) TimelineCanvas.Children.Remove(r);
            _regionVisuals.Clear();

            if (_totalSeconds <= 0 || TimelineCanvas.Bounds.Width <= 0)
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

        private Rectangle? BuildRegionVisual(Region region)
        {
            var w = TimelineCanvas.Bounds.Width;
            var h = TimelineCanvas.Bounds.Height;
            if (w <= 0 || _totalSeconds <= 0) return null;

            var (laneTop, laneH) = LaneBandInset(TimelineLane.Regions, h);
            var startX = Math.Max(0, (region.Start / _totalSeconds) * w);
            var endX = Math.Min(w, (region.End / _totalSeconds) * w);
            // Floor at MinBandVisualWidthPx so a region whose duration would otherwise render at
            // sub-pixel width stays clickable. The data model keeps the real Start/End.
            var width = Math.Max(MinBandVisualWidthPx, endX - startX);

            var color = TryParseColor(region.Color) ?? Colors.MediumPurple;
            var fill = Color.FromArgb(80, color.R, color.G, color.B);
            var isSelected = _selectedRegion == region || IsInSelectionSet(region);

            var rect = new Rectangle
            {
                Width = width,
                Height = laneH,
                Fill = new SolidColorBrush(fill),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = region,
            };
            ToolTip.SetTip(rect, string.IsNullOrEmpty(region.Label) ? region.Id : $"{region.Id} — {region.Label}");
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, laneTop);
            rect.PointerPressed += RegionRect_PointerPressed;
            rect.PointerMoved += RegionRect_PointerMoved;
            return rect;
        }

        private void RegionRect_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragMode != DragMode.None) return;
            if (sender is not Rectangle r) return;
            var pos = e.GetPosition(r);
            r.Cursor = ClassifyEdgeHit(pos.X, r.Bounds.Width) == EdgeHit.Body
                ? new Cursor(StandardCursorType.SizeAll)
                : new Cursor(StandardCursorType.SizeWestEast);
        }

        private void EnsurePlayheadOnTop()
        {
            // Keep the playhead in front of dynamically inserted region/haptic/effect visuals.
            if (PlayheadLine == null) return;
            if (TimelineCanvas.Children.Contains(PlayheadLine))
                TimelineCanvas.Children.Remove(PlayheadLine);
            TimelineCanvas.Children.Add(PlayheadLine);
        }

        private void RegionRect_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Rectangle r || r.Tag is not Region region) return;
            if (!e.GetCurrentPoint(r).Properties.IsLeftButtonPressed) return;

            // Snapshot pos + width BEFORE selecting - SelectRegion rebuilds visuals, which detaches
            // `r` from the tree, after which GetPosition(r) returns ~(0,0) and trips the left-edge
            // resize check unconditionally.
            var pos = e.GetPosition(r);
            var rectWidth = r.Bounds.Width;
            bool ctrl = IsCtrlDown(e.KeyModifiers);

            HandleSelectionClick(region, e.KeyModifiers);
            if (ctrl)
            {
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                e.Handled = true;
                return;
            }

            // If a rule constrains this region, treat the band as the rule's visual representation:
            // select the Rule (so trigger / action / gaze rect / picker are one click away) and let
            // the rule editor's region-details sub-panel handle label/colour/start-end. Orphan
            // regions still fall back to the standalone Region editor.
            var attachedRule = FindRuleByRegionConstraint(region.Id);
            if (attachedRule != null) SelectRule(attachedRule);
            else SelectRegion(region);

            _draggedRegion = region;
            _regionDragOriginalLength = Math.Max(0, region.End - region.Start);
            BeginMultiDragCapture();

            switch (ClassifyEdgeHit(pos.X, rectWidth))
            {
                case EdgeHit.Start: _dragMode = DragMode.ResizeRegionStart; break;
                case EdgeHit.End:   _dragMode = DragMode.ResizeRegionEnd; break;
                default:
                    _dragMode = DragMode.DragRegion;
                    _regionDragOffsetSec = MouseToSeconds(e) - region.Start;
                    break;
            }
            e.Pointer.Capture(TimelineCanvas);
            e.Handled = true;
        }

        // ---------------------------------------------------------------------------------
        // Haptic events: rendering + interaction
        // ---------------------------------------------------------------------------------

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
            // Avoid overlapping: nudge to the first free slot after the current events.
            foreach (var existing in track.Events.OrderBy(x => x.Start))
            {
                if (start < existing.Start + existing.Duration && start + duration > existing.Start)
                    start = existing.Start + existing.Duration + 0.05;
            }
            if (start + duration > _totalSeconds) start = Math.Max(0, _totalSeconds - duration);

            // Snapshot BEFORE the mutation so Ctrl+Z restores the pre-create state.
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

            if (_totalSeconds <= 0 || TimelineCanvas.Bounds.Width <= 0)
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

        private Rectangle? BuildHapticVisual(HapticTrack track, HapticEvent ev)
        {
            var w = TimelineCanvas.Bounds.Width;
            var h = TimelineCanvas.Bounds.Height;
            if (w <= 0 || _totalSeconds <= 0) return null;

            var (laneTop, laneH) = LaneBandInset(TimelineLane.Haptics, h);
            var startX = Math.Max(0, (ev.Start / _totalSeconds) * w);
            var endX = Math.Min(w, ((ev.Start + ev.Duration) / _totalSeconds) * w);
            var width = Math.Max(MinBandVisualWidthPx, endX - startX);

            var isSelected = _selectedHaptic == ev || IsInSelectionSet(ev);
            // Resolve DeeperAccent from the theme so a runtime palette change reflows the haptic
            // stroke too. Falls back to the canonical violet if the lookup fails.
            Color accent = (Res("DeeperAccentBrush") as ISolidColorBrush)?.Color
                           ?? Color.Parse("#FF7B5CFF");
            var fill = Color.FromArgb(isSelected ? (byte)180 : (byte)130, accent.R, accent.G, accent.B);

            var rect = new Rectangle
            {
                Width = width,
                Height = laneH,
                Fill = new SolidColorBrush(fill),
                Stroke = new SolidColorBrush(accent),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = new Cursor(StandardCursorType.SizeAll),
                Tag = (track, ev),
            };
            ToolTip.SetTip(rect, string.IsNullOrEmpty(ev.PatternName)
                ? $"{track.Id} · custom · {ev.Duration:0.##}s"
                : $"{track.Id} · {ev.PatternName} · {ev.Duration:0.##}s");
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, laneTop);
            rect.PointerPressed += HapticRect_PointerPressed;
            rect.PointerMoved += HapticRect_PointerMoved;
            return rect;
        }

        private void HapticRect_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragMode != DragMode.None) return; // don't churn the cursor mid-drag
            if (sender is not Rectangle r) return;
            var pos = e.GetPosition(r);
            r.Cursor = ClassifyEdgeHit(pos.X, r.Bounds.Width) == EdgeHit.Body
                ? new Cursor(StandardCursorType.SizeAll)
                : new Cursor(StandardCursorType.SizeWestEast);
        }

        private void HapticRect_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Rectangle r || r.Tag is not ValueTuple<HapticTrack, HapticEvent> tuple) return;
            if (!e.GetCurrentPoint(r).Properties.IsLeftButtonPressed) return;

            var (track, ev) = tuple;
            var pos = e.GetPosition(r);
            var rectWidth = r.Bounds.Width;
            bool ctrl = IsCtrlDown(e.KeyModifiers);
            HandleSelectionClick(ev, e.KeyModifiers);
            if (ctrl)
            {
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                e.Handled = true;
                return;
            }
            SelectHaptic(track, ev);

            // Begin drag-shift on hold (no Shift modifier - that's region-create).
            if ((e.KeyModifiers & KeyModifiers.Shift) != KeyModifiers.Shift)
            {
                var hit = ClassifyEdgeHit(pos.X, rectWidth);
                if (hit == EdgeHit.Start)
                {
                    _dragMode = DragMode.ResizeHapticStart;
                    _draggedHaptic = ev;
                    _draggedHapticTrack = track;
                    _hapticDragStartSec = ev.Start;
                    e.Pointer.Capture(TimelineCanvas);
                    e.Handled = true;
                    return;
                }
                if (hit == EdgeHit.End)
                {
                    _dragMode = DragMode.ResizeHapticEnd;
                    _draggedHaptic = ev;
                    _draggedHapticTrack = track;
                    _hapticDragStartSec = ev.Start;
                    e.Pointer.Capture(TimelineCanvas);
                    e.Handled = true;
                    return;
                }

                _dragMode = DragMode.ShiftHapticEvent;
                _draggedHaptic = ev;
                _draggedHapticTrack = track;
                _hapticDragStartSec = ev.Start;
                _hapticDragOffsetSec = MouseToSeconds(e) - ev.Start;
                BeginMultiDragCapture();
                e.Pointer.Capture(TimelineCanvas);
            }
            e.Handled = true;
        }

        // ---------------------------------------------------------------------------------
        // Metadata sync, dirty flag, validation
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// DEVIATION from the WPF handler, and a real bug the straight port inherited: Avalonia
        /// raises <c>TextBox.TextChanged</c> DEFERRED, not synchronously on the assignment. WPF's
        /// guard was "set _suppressDirty, assign the six boxes, clear it" in a try/finally - on
        /// Avalonia the event lands AFTER that finally has cleared the flag, so merely loading a
        /// project marked it dirty, lit the unsaved dot, and (once a close dialog exists) would
        /// have prompted to save on every exit. Caught by stack-tracing MarkDirty during the
        /// headless render, not by reading.
        ///
        /// The flag is kept for the synchronous callers that still rely on it; the decision to
        /// mark dirty is now made by COMPARING against the model rather than by trusting a timing
        /// window, so writing a field its current value is a no-op whenever the event arrives.
        /// </summary>
        private void MetadataField_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressDirty) return;

            var m = _enhancement.Metadata;
            var name = TxtMetaName.Text ?? "";
            var creator = TxtMetaCreator.Text ?? "";
            var remixer = string.IsNullOrWhiteSpace(TxtMetaRemixer?.Text) ? null : TxtMetaRemixer!.Text;
            var description = TxtMetaDescription.Text ?? "";
            var tags = (TxtMetaTags.Text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var license = TxtMetaLicense.Text ?? "";

            bool changed = m.Name != name
                || m.Creator != creator
                || m.Remixer != remixer
                || m.Description != description
                || m.License != license
                || !(m.Tags ?? new List<string>()).SequenceEqual(tags, StringComparer.Ordinal);
            if (!changed) return;

            m.Name = name;
            m.Creator = creator;
            m.Remixer = remixer;
            m.Description = description;
            m.Tags = tags;
            m.License = license;
            MarkDirty();
            ScheduleValidation();
        }


        /// <summary>
        /// Clears a "suppress my own change handlers" flag only AFTER Avalonia has drained the
        /// events that the just-completed assignments queued.
        ///
        /// This exists because of a framework difference that silently breaks the WPF idiom used
        /// throughout this window. WPF raises <c>TextBox.TextChanged</c> and
        /// <c>Selector.SelectionChanged</c> SYNCHRONOUSLY on the assignment, so the pattern
        /// "set flag - assign the controls - clear flag in finally" reliably swallowed the echo.
        /// Avalonia raises them DEFERRED, on a later dispatcher turn, by which time the finally has
        /// already cleared the flag - so every populate-the-inspector path fed its own echo back
        /// into the model, marked the project dirty and lit the unsaved-changes dot on load.
        /// Caught by stack-tracing MarkDirty during the headless render, not by reading.
        ///
        /// Posting the clear at Background priority puts it behind those queued events (input and
        /// layout priorities both run first), so the guard still ends, and it ends late enough.
        /// ponytail: one helper at every existing guard site rather than a value-comparison in each
        /// of ~30 handlers - same fix, a tenth of the diff.
        /// </summary>
        private static void ClearWhenEventsDrained(Action clear)
            => Dispatcher.UIThread.Post(clear, DispatcherPriority.Background);

        private void MarkDirty()
        {
            if (_suppressDirty) return;
            _isDirty = true;
            TxtDirty.IsVisible = true;
            // Lane counts piggyback here (cheap) so the chrome stays in sync with any
            // add/remove/edit that touches the data model.
            RefreshLaneCounts();
        }

        private void ScheduleValidation()
        {
            _validationTimer?.Stop();
            _validationTimer?.Start();
        }

        // Backing list for the click-to-expand popup. Refreshed in lockstep with the summary so the
        // popup always shows the current set.
        private List<ValidationError> _lastValidationIssues = new();

        private void RefreshValidation()
        {
            _lastValidationIssues = EnhancementValidator.Validate(_enhancement);
            int errorCount = _lastValidationIssues.Count(x => x.Severity == ValidationSeverity.Error);
            int warningCount = _lastValidationIssues.Count(x => x.Severity == ValidationSeverity.Warning);
            if (errorCount == 0 && warningCount == 0)
            {
                TxtValidationSummary.Text = Loc.Get("deeper_editor_validation_clean");
                TxtValidationSummary.Foreground = Res("TextLightBrush");
                TxtValidationSummary.Cursor = null;
                TxtValidationSummary.TextDecorations = null;
                if (ValidationDetailsPopup != null) ValidationDetailsPopup.IsOpen = false;
            }
            else
            {
                var bits = new List<string>();
                if (errorCount > 0) bits.Add(string.Format(Loc.Get("deeper_editor_validation_errors_fmt"), errorCount));
                if (warningCount > 0) bits.Add(string.Format(Loc.Get("deeper_editor_validation_warnings_fmt"), warningCount));
                TxtValidationSummary.Text = string.Join("  ·  ", bits)
                    + "  " + Loc.Get("deeper_editor_validation_click_for_details_hint");
                TxtValidationSummary.Foreground = errorCount > 0 ? Res("DangerBrush") : Res("PinkSoftBrush");
                TxtValidationSummary.Cursor = new Cursor(StandardCursorType.Hand);
                TxtValidationSummary.TextDecorations = TextDecorations.Underline;
                ToolTip.SetTip(TxtValidationSummary, Loc.Get("deeper_editor_validation_click_for_details_tip"));

                // If the popup is already open (user toggled it then made an edit), refresh its
                // contents in place rather than closing it.
                if (ValidationDetailsPopup?.IsOpen == true) PopulateValidationPopup();
            }
        }

        private void TxtValidationSummary_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            // No issues = nothing to expand.
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

        // Builds the row VMs for LstValidationIssues, fresh from _lastValidationIssues so a re-open
        // after edits always shows current state.
        private void PopulateValidationPopup()
        {
            if (LstValidationIssues == null) return;
            int errorCount = _lastValidationIssues.Count(x => x.Severity == ValidationSeverity.Error);
            int warningCount = _lastValidationIssues.Count(x => x.Severity == ValidationSeverity.Warning);
            if (TxtValidationPopupHeader != null)
                TxtValidationPopupHeader.Text = string.Format(
                    Loc.Get("deeper_editor_validation_popup_header_fmt"),
                    errorCount, warningCount);

            LstValidationIssues.ItemsSource = _lastValidationIssues
                .OrderBy(i => i.Severity == ValidationSeverity.Error ? 0 : 1)
                .Select(i => new ValidationIssueRow
                {
                    Glyph = i.Severity == ValidationSeverity.Error ? "✕" : "⚠",
                    Brush = Res(i.Severity == ValidationSeverity.Error ? "DangerBrush" : "PinkSoftBrush"),
                    Message = i.Message ?? "",
                })
                .ToList();
        }

        // ---------------------------------------------------------------------------------
        // File ops
        //
        // ponytail: every member below needs App.EnhancementLibrary, a file dialog
        // (IStorageProvider is available but the library's folder + suffix conventions are not),
        // EnhancementMediaBundler for export, and a MessageBox for the result. The WPF bodies are
        // ~450 lines of that plumbing; ported as named stubs that log what they would have done, so
        // no member silently disappears and the wiring above stays honest.
        // ---------------------------------------------------------------------------------

        private void MenuSave_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                MenuSaveAs_Click(sender, e);
                return;
            }
            SaveTo(_filePath!);
        }

        private void MenuSaveAs_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("DeeperEditor: Save As needs App.EnhancementLibrary + a save dialog");

        private void SaveTo(string path)
            => Log.Debug("DeeperEditor: Save needs EnhancementSerializer.Write + App.EnhancementLibrary: {Path}", path);

        private void MenuExportEnhanced_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("DeeperEditor: Export needs EnhancementMediaBundler + a save dialog");

        private void MenuClose_Click(object? sender, RoutedEventArgs e) => Close();

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

        // ---------------------------------------------------------------------------------
        // Linked Files strip
        // ---------------------------------------------------------------------------------

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
                TxtLinkedJsonName.Text = IOPath.GetFileName(_filePath);
                TxtLinkedJsonPath.Text = _filePath;
                ToolTip.SetTip(TxtLinkedJsonPath, _filePath);
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
                ToolTip.SetTip(TxtLinkedMediaStatus, Loc.Get("deeper_editor_linked_status_local_missing"));
                TxtLinkedMediaName.Text = Loc.Get("deeper_editor_linked_no_media");
                TxtLinkedMediaPath.Text = "";
                BtnLinkedMediaClear.IsEnabled = false;
            }
            else if (isUrl)
            {
                TxtLinkedMediaStatus.Text = "🌐";
                ToolTip.SetTip(TxtLinkedMediaStatus, Loc.Get("deeper_editor_linked_status_url"));
                string display;
                try { display = new Uri(src).Host; } catch { display = src; }
                TxtLinkedMediaName.Text = display;
                TxtLinkedMediaPath.Text = src;
                ToolTip.SetTip(TxtLinkedMediaPath, src);
                BtnLinkedMediaClear.IsEnabled = true;
            }
            else
            {
                var exists = false;
                try { exists = File.Exists(src); } catch { }
                TxtLinkedMediaStatus.Text = exists ? "✓" : "⚠";
                ToolTip.SetTip(TxtLinkedMediaStatus, exists
                    ? Loc.Get("deeper_editor_linked_status_local_ok")
                    : Loc.Get("deeper_editor_linked_status_local_missing"));
                TxtLinkedMediaName.Text = IOPath.GetFileName(src);
                TxtLinkedMediaPath.Text = src;
                ToolTip.SetTip(TxtLinkedMediaPath, src);
                BtnLinkedMediaClear.IsEnabled = true;
            }
        }

        /// <summary>ponytail: needs Helpers.ExplorerLauncher.RevealInExplorer (Windows shell).</summary>
        private void BtnLinkedJsonOpenFolder_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("DeeperEditor: reveal-in-file-manager needs a Linux launcher: {Path}", _filePath ?? "");

        /// <summary>ponytail: needs App.EnhancementLibrary + an open dialog.</summary>
        private void BtnLinkedJsonSwap_Click(object? sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardChanges()) return;
            Log.Debug("DeeperEditor: swap project needs App.EnhancementLibrary + an open dialog");
        }

        private void BtnLinkedMediaChange_Click(object? sender, RoutedEventArgs e)
        {
            if (MediaChangePopup != null) MediaChangePopup.IsOpen = true;
        }

        /// <summary>ponytail: needs an open-file dialog.</summary>
        private void BtnChangeMediaLocal_Click(object? sender, RoutedEventArgs e)
        {
            if (MediaChangePopup != null) MediaChangePopup.IsOpen = false;
            Log.Debug("DeeperEditor: change media (local) needs a file dialog");
        }

        /// <summary>ponytail: needs UrlPromptDialog.</summary>
        private void BtnChangeMediaUrl_Click(object? sender, RoutedEventArgs e)
        {
            if (MediaChangePopup != null) MediaChangePopup.IsOpen = false;
            Log.Debug("DeeperEditor: change media (URL) needs UrlPromptDialog");
        }

        /// <summary>ponytail: needs the media pipeline (re-probe duration, reload the preview) plus
        /// the same dialogs as the two callers above.</summary>
        private void ApplyChangedMedia(string newSource, bool isLocal)
            => Log.Debug("DeeperEditor: ApplyChangedMedia({Source}, local={Local}) needs the media pipeline", newSource, isLocal);

        private void BtnLinkedMediaClear_Click(object? sender, RoutedEventArgs e)
        {
            if (_enhancement == null) return;
            PushUndoSnapshot();
            _enhancement.MediaSource = "";
            MarkDirty();
            RefreshLinkedFilesUi();
            ShowPlaceholder();
            ScheduleValidation();
        }

        // ---------------------------------------------------------------------------------
        // Drag & drop
        // ---------------------------------------------------------------------------------

        private void Window_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.None;
            var files = e.DataTransfer.TryGetFiles();
            if (files == null) return;
            foreach (var f in files)
            {
                var p = f.TryGetLocalPath();
                if (p != null && IsDroppableEditorPath(p)) { e.DragEffects = DragDropEffects.Copy; return; }
            }
        }

        /// <summary>ponytail: the accept/reject half is real; loading the dropped file needs
        /// EnhancementSerializer.Read plus the media pipeline, so the drop logs and returns.</summary>
        private void Window_Drop(object? sender, DragEventArgs e)
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files == null) return;
            foreach (var f in files)
            {
                var p = f.TryGetLocalPath();
                if (p == null) continue;
                if (IsEnhancementJsonPath(p)) { LoadEnhancementFromDrop(p); return; }
                if (IsLocalMediaFile(p)) { ApplyChangedMedia(p, isLocal: true); return; }
            }
        }

        /// <summary>ponytail: needs EnhancementSerializer.Read + ConfirmDiscardChanges' dialog.</summary>
        private void LoadEnhancementFromDrop(string ccpenhJsonPath)
            => Log.Debug("DeeperEditor: drop-load needs EnhancementSerializer.Read: {Path}", ccpenhJsonPath);

        private static bool IsDroppableEditorPath(string path)
            => IsEnhancementJsonPath(path) || IsLocalMediaFile(path);

        private static bool IsLocalMediaFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = IOPath.GetExtension(path).ToLowerInvariant();
            return IsLocalVideoFile(path)
                || ext is ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac";
        }

        private static bool IsEnhancementJsonPath(string path)
            => !string.IsNullOrWhiteSpace(path)
               && path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase);

        /// <summary>ponytail: needs a modal confirm dialog. Returning true keeps the caller's flow
        /// intact; the unsaved-changes guard is lost until a dialog exists on this head.</summary>
        private bool ConfirmDiscardChanges()
        {
            if (!_isDirty) return true;
            Log.Debug("DeeperEditor: discard-changes confirm needs a modal dialog; proceeding");
            return true;
        }

        private static bool IsLocalVideoFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = IOPath.GetExtension(path).ToLowerInvariant();
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

        // ---------------------------------------------------------------------------------
        // Window lifecycle
        // ---------------------------------------------------------------------------------

        private void DeeperEditorWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            // Don't hijack typing inside text fields.
            var inTextBox = FocusManager?.GetFocusedElement() is TextBox;
            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;

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
                // Multi-select takes priority - bulk delete everything in the set.
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
            // Ctrl+Z / Ctrl+Shift+Z (or Ctrl+Y) - undo / redo. The !inTextBox guard keeps standard
            // text-box undo intact when typing.
            else if (e.Key == Key.Z && !inTextBox && ctrl)
            {
                if (shift) Redo(); else Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y && !inTextBox && ctrl)
            {
                Redo();
                e.Handled = true;
            }
            // Ctrl+C / Ctrl+X / Ctrl+V - clipboard ops on the current selection.
            else if (e.Key == Key.C && !inTextBox && ctrl && _selectionSet.Count > 0)
            {
                CopySelection();
                e.Handled = true;
            }
            else if (e.Key == Key.X && !inTextBox && ctrl && _selectionSet.Count > 0)
            {
                CutSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.V && !inTextBox && ctrl)
            {
                PasteFromClipboard();
                e.Handled = true;
            }
            // Ctrl+A - select every region/haptic/effect on the timeline.
            else if (e.Key == Key.A && !inTextBox && ctrl)
            {
                SelectAllOnTimeline();
                e.Handled = true;
            }
            else if (e.Key == Key.S && ctrl)
            {
                if (shift) MenuSaveAs_Click(this, new RoutedEventArgs());
                else MenuSave_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.E && !inTextBox && ctrl)
            {
                MenuExportEnhanced_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // Set by the app shell on shutdown to skip the unsaved-changes prompt. A user-initiated
        // cancel there would block the whole app from exiting.
        private bool _suppressDirtyPromptOnClose;

        public void ForceClose()
        {
            _suppressDirtyPromptOnClose = true;
            try { Close(); } catch { }
        }

        /// <summary>ponytail: the unsaved-changes prompt needs a modal dialog (WPF used
        /// MessageBox.Show with Yes/No/Cancel and could cancel the close). Without one the editor
        /// closes and the teardown below still runs, so nothing leaks - only the prompt is lost.</summary>
        private void Window_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (_isDirty && !_suppressDirtyPromptOnClose)
                Log.Debug("DeeperEditor: closing with unsaved changes; no modal prompt on this head");
            TeardownPreview();
            DisposePlayback();
        }

        /// <summary>ponytail: the WPF body detached the WebView2 handlers, exited preview
        /// fullscreen and disposed the CoreWebView2 environment. WebHost owns its own control's
        /// lifetime, so only the timers need stopping here.</summary>
        private void TeardownPreview()
        {
            try { _playheadTimer?.Stop(); } catch { }
            try { _validationTimer?.Stop(); } catch { }
            try { _htFetchCts?.Cancel(); _htFetchCts?.Dispose(); } catch { }
            _htFetchCts = null;
        }

        /// <summary>ponytail: needs LibVLCSharp / NAudio - the WPF body disposed MediaPlayer,
        /// Media, WaveOutEvent and AudioFileReader.</summary>
        private void DisposePlayback()
        {
            _isPlaying = false;
            _waveformPeaks = null;
        }

        // ---------------------------------------------------------------------------------
        // WebView2 / VLC / NAudio surface
        //
        // Every one of these is a member of the WPF window that has no counterpart on this head:
        // the CoreWebView2 event surface, the borderless-fullscreen preview window (Forms.Screen +
        // WindowInteropHelper + WS_EX_TOOLWINDOW), and the four player callbacks. They are kept
        // named and typed against portable signatures rather than deleted, so nothing silently
        // disappears and the layer that brings a player or a richer WebHost has an exact list of
        // what to fill in. The framework-typed parameters (CoreWebView2NavigationStartingEventArgs,
        // MediaPlayerLengthChangedEventArgs, MediaPlayerTimeChangedEventArgs, StoppedEventArgs)
        // become object?/EventArgs, since naming them would require the very packages this head
        // must not reference.
        // ---------------------------------------------------------------------------------

        // The WebView2 preview's own state. Held so the members below read as the port they are.
        private object? _browserSource;          // ponytail: BrowserVideoTimeSource
        private bool _browserInitInFlight;
        private bool _previewBrowserConfigured;
        private bool _isPreviewFullscreen;
        private bool _fsTransitionInFlight;
        private Window? _previewFullscreenWindow;

        /// <summary>ponytail: needs Core's UrlSafety.HostMatches + DeeperConfig.PreviewHostAllowlist,
        /// both <c>internal</c> to CCP.Core, which does not list CCP.Avalonia in its
        /// InternalsVisibleTo. Returning false is the safe direction: it denies rather than
        /// allows. One line to make real once the head is added to that attribute.</summary>
        private static bool IsAllowedPreviewHost(Uri uri) { _ = uri; return false; }

        /// <summary>ponytail: needs CoreWebView2.NavigationStarting. Gated navigation on
        /// <see cref="IsAllowedPreviewHost"/> and cancelled anything off the allow-list.</summary>
        private void OnBrowserNavigationStarting(object? sender, EventArgs e) { }

        /// <summary>ponytail: needs CoreWebView2.WebMessageReceived. Carried the page's
        /// &lt;video&gt; currentTime and the Ctrl+wheel zoom requests back from injected JS.</summary>
        private void OnPreviewWebMessageReceived(object? sender, EventArgs e) { }

        /// <summary>ponytail: needs CoreWebView2.ContainsFullScreenElementChanged.</summary>
        private void OnPreviewFullscreenChanged(object? sender, EventArgs e) { }

        /// <summary>ponytail: part of the fullscreen reparent - detached the WebView2 from whichever
        /// panel/decorator currently owned it before re-hosting it in the fullscreen window.</summary>
        private static bool TryDetachFromUiParent(Control child) { _ = child; return false; }

        /// <summary>ponytail: needs Forms.Screen.FromHandle + WindowInteropHelper + a borderless
        /// topmost window. On Linux the Avalonia parts exist (Screens.ScreenFromVisual,
        /// SystemDecorations="None", Topmost, ShowInTaskbar=false) but the WebView2 reparent this
        /// wrapped does not, so the whole path is a stub.</summary>
        private void EnterPreviewFullscreen() { }

        /// <summary>ponytail: the recovery half of the reparent - put BrowserPreview back into
        /// PreviewHost if the fullscreen window died without an orderly exit.</summary>
        private void SafeRestoreBrowserPreviewToHost() { }

        /// <summary>ponytail: the orderly half of the reparent.</summary>
        private void ExitPreviewFullscreen() { }

        /// <summary>ponytail: needs CoreWebView2.ExecuteScriptAsync - asked the page itself to leave
        /// fullscreen, so the site's own player chrome stayed consistent.</summary>
        private void ExitPreviewFullscreenViaScript() { }

        /// <summary>
        /// Time report from the browser preview. REAL apart from its source: the duration probe and
        /// the play/pause glyph came off BrowserVideoTimeSource, which is a WPF-head type, but the
        /// playhead, the read-out and the mid-scrub guard are the WPF logic unchanged. Wire a time
        /// source to this method and the browser-driven timeline works.
        /// </summary>
        private void OnBrowserTimeChanged(double seconds)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnBrowserTimeChanged(seconds));
                return;
            }
            if (_isScrubbing) return;
            // Reparent in progress - touching BrowserPreview now would race the swap.
            if (_fsTransitionInFlight) return;
            _currentSeconds = Math.Max(0, seconds);
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            UpdatePlayheadPosition();
        }

        /// <summary>ponytail: needs LibVLCSharp MediaPlayer.LengthChanged - set _totalSeconds and
        /// rebuilt every timeline visual against the new duration.</summary>
        private void OnVlcLengthChanged(object? sender, EventArgs args) { }

        /// <summary>ponytail: needs LibVLCSharp MediaPlayer.TimeChanged - the video half of what
        /// <see cref="PlayheadTimer_Tick"/> does for audio.</summary>
        private void OnVlcTimeChanged(object? sender, EventArgs args) { }

        /// <summary>ponytail: needs LibVLCSharp MediaPlayer.EndReached - reset the playhead and the
        /// play/pause glyph at end of media.</summary>
        private void OnVlcEndReached(object? sender, EventArgs args) { }

        /// <summary>ponytail: needs NAudio WaveOutEvent.PlaybackStopped - the audio twin of
        /// <see cref="OnVlcEndReached"/>.</summary>
        private void OnWaveOutPlaybackStopped(object? sender, EventArgs args) { }
    }
}
