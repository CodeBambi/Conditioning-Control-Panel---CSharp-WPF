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
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
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
    ///   <item><b>The browser preview.</b> Real: the https pre-flight, a per-navigation host
    ///         fence, and the page's own &lt;video&gt; driven and read back through
    ///         <c>WebHost.InvokeScriptAsync</c>, so duration, playhead, play/pause and seek
    ///         describe the video rather than a free-running timer. Two things WPF had are NOT
    ///         reproduced and both WIDEN what it allowed: the first hop is pinned to the project
    ///         URL's own host instead of <c>DeeperConfig.PreviewHostAllowlist</c> (internal to
    ///         Core), and there is no per-instance user-data folder, so the preview shares a cookie
    ///         jar with every other WebHost. Stubbed because NativeWebView has no counterpart at
    ///         all: ZoomFactor, ContainsFullScreenElementChanged (and the fullscreen reparent it
    ///         drove), and AddScriptToExecuteOnDocumentCreatedAsync.</item>
    ///   <item><b>Win32.</b> <c>WindowChromeHelper.ApplyDarkTitleBar</c> (DwmSetWindowAttribute),
    ///         <c>RestoreOwnerOnClose</c>, <c>System.Windows.Forms.Screen.FromHandle</c> +
    ///         <c>WindowInteropHelper</c> (the borderless-fullscreen preview window) and
    ///         <c>VisualTreeHelper.GetDpi</c> (the gaze picker's screen placement).</item>
    ///   <item><b>App services.</b> App.Tutorial + TutorialOverlay, App.DeeperPlayer /
    ///         App.DeeperHost, the haptics bus and GazePickerWindow. App.EnhancementLibrary is
    ///         head-only too, but the five members the editor needs from it are inlined in the
    ///         file-ops region, so Save / Save As / Export / Swap / Change media / drag-drop are
    ///         real, and the unsaved-changes guard on swap, drop-load and close asks
    ///         Save / Discard / Cancel through <see cref="UnsavedChangesDialog"/> at the foot of
    ///         this file.</item>
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

        // ponytail: needs LibVLCSharp (video) and NAudio (audio + AudioWaveformResult). LOCAL
        // media still has no player, so the two numbers every drawing path reads are driven
        // directly; the remote branch no longer is - it polls the page (PollBrowserTimeAsync).
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
                // A swap from a URL to a local file must blank the old page first, or its audio
                // keeps playing behind the waveform this method is about to show.
                StopBrowserPreview();

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
        /// Remote https preview. WPF built a WebView2 environment in its own user-data folder,
        /// gated navigation on <c>DeeperConfig.PreviewHostAllowlist</c>, injected a
        /// document-created script that posted the &lt;video&gt;'s currentTime back over
        /// WebMessage, and handled fullscreen. Two of those four cross.
        ///
        /// <para><b>The fence crosses.</b> <c>WebHost.AllowNavigation</c> runs for every navigation
        /// the engine starts - the first hop and every redirect, link and script-driven hop after
        /// it - which is what NavigationStarting was for. What it is pinned TO is weaker on that
        /// first hop: <c>UrlSafety.HostMatches</c> and <c>DeeperConfig.PreviewHostAllowlist</c> are
        /// both <c>internal</c> to CCP.Core and this head is not in Core's InternalsVisibleTo, so
        /// the fence pins the engine to whatever host the project's own URL named. A shared
        /// .ccpenh.json can therefore aim the preview at an https host WPF would have refused;
        /// every hop after it is fenced, which is the leg WPF's pre-flight never saw. Copying the
        /// allowlist into this head would be a second copy of a security rule, which is why the
        /// sibling player refused it too.</para>
        ///
        /// <para><b>The profile does not.</b> WPF gave this preview its own
        /// <c>browser_data_deeper_editor</c> folder so a hostile page could not read the main
        /// browser tab's signed-in cookies. NativeWebView takes no per-instance data directory, so
        /// the preview shares the process cookie jar with every other WebHost on this head. There
        /// is no line of code here to point at; it is a capability the wrapper does not have.</para>
        ///
        /// <para><b>The time source crosses.</b> No document-created injection exists, but
        /// <c>WebHost.InvokeScriptAsync</c> does, so <see cref="PollBrowserTimeAsync"/> reads
        /// currentTime/duration/paused off the page on the playhead timer instead of receiving them
        /// over WebMessage. Fullscreen and zoom do not - see the stub region at the foot of the
        /// file.</para>
        /// </summary>
        private Task InitializeBrowserAsync(string url)
        {
            if (_browserInitInFlight) return Task.CompletedTask;
            _browserInitInFlight = true;
            try
            {
                // Pre-flight, as WPF did: a source that could not pass the fence never reaches the
                // engine. Host only in the log - a signed media URL's query string must not land in
                // crash.log, which is the same rule WebHost's own blocked-navigation line follows.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                {
                    Log.Warning("DeeperEditor: preview source refused ({Host})", uri?.Host ?? "unparseable");
                    ShowPlaceholder();
                    return Task.CompletedTask;
                }

                VideoPreview.IsVisible = false;
                WaveformCanvas.IsVisible = false;
                PreviewPlaceholder.IsVisible = false;
                BrowserPreview.IsVisible = true;

                // No engine on this machine (the default on a fresh Linux box, and every headless
                // render): WebHost draws its own panel naming the missing package. Do NOT start the
                // poll - it would question a control that has no page.
                if (!BrowserPreview.HasEngine) return Task.CompletedTask;

                // Assigned BEFORE Source: the gate is read at navigation time and a Source set
                // first can start navigating before the predicate is in place.
                var pinned = uri.Host;
                BrowserPreview.AllowNavigation = u =>
                    u.Scheme == Uri.UriSchemeHttps && HostsMatchIgnoringWww(u.Host, pinned);
                BrowserPreview.Source = uri;
                _browserNavigated = true;
                _browserPollDisabled = false;

                // Runs regardless of _isPlaying, unlike the local-media path: the page starts and
                // stops itself (autoplay, an ad, the site's own controls) and the poll is the only
                // thing that would notice.
                _playheadTimer?.Start();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DeeperEditor: browser preview init failed");
                BrowserPreview.IsVisible = false;
                ShowPlaceholder();
            }
            finally { _browserInitInFlight = false; }
            return Task.CompletedTask;
        }

        /// <summary>Host equality that ignores a leading "www.", the same fence the player uses.
        /// Deliberately NOT a domain-suffix match: a subdomain is a different host here, because
        /// this pins to one page's host rather than admitting a whole domain the way the
        /// allowlist did.</summary>
        private static bool HostsMatchIgnoringWww(string? a, string? b)
        {
            static string Strip(string? h) =>
                (h ?? "").StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h![4..] : (h ?? "");
            var (x, y) = (Strip(a), Strip(b));
            return x.Length > 0 && x.Equals(y, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Addresses the page's LARGEST &lt;video&gt;, which was BrowserVideoTimeSource's rule too:
        /// a HypnoTube page carries preview thumbnails that are also video elements, and the first
        /// in document order is routinely not the one being watched. Returns "" when the page has
        /// no video yet, so a caller can tell "did nothing" from "did it".
        ///
        /// The one-shot <c>_ccpScrolled</c> flag replaces WPF's injected scrollIntoView poller: HT
        /// stacks promo banners above the player, so without it the preview lands at scrollTop=0
        /// with the video offscreen. It lives on the element, so a navigation resets it for free.
        /// </summary>
        private static string BrowserVideoScript(string tail) =>
            "(function(){var l=null,a=0;document.querySelectorAll('video').forEach(function(v){"
            + "var s=(v.clientWidth||0)*(v.clientHeight||0); if(s>=a){a=s;l=v;}});"
            + "if(!l)return '';"
            + "if(!l._ccpScrolled){l._ccpScrolled=1;try{l.scrollIntoView({block:'center'});}catch(e){}}"
            + "return " + tail + ";})()";

        /// <summary>
        /// BrowserVideoTimeSource's poll, minus the WebView2 binding: read currentTime, duration and
        /// paused off the page each tick so the read-out, the playhead and every duration-derived
        /// visual describe the video rather than a field nothing writes.
        ///
        /// Re-entrancy matters - the tick is 80 ms and a script round-trip through the adapter is
        /// not promised to be faster - so a poll in flight skips the next tick rather than queueing.
        /// A poll that throws stops polling for good: twelve failures a second in the log help
        /// nobody, and the read-out simply stops moving.
        /// </summary>
        private async Task PollBrowserTimeAsync()
        {
            if (_browserPollInFlight || _browserPollDisabled) return;
            _browserPollInFlight = true;
            try
            {
                var raw = await BrowserPreview.InvokeScriptAsync(
                    BrowserVideoScript("l.currentTime+'|'+(l.duration||0)+'|'+(l.paused?0:1)"));

                // Re-checked AFTER the await, not only before it: a late answer from a page that
                // has since been blanked would rebuild the timeline against a dead duration, and a
                // scrub that began during the round-trip would have it yanked out from under the
                // pointer. WPF's handler took the same three guards before touching anything.
                if (!_browserNavigated || _isScrubbing || _fsTransitionInFlight) return;

                var parts = Unquote(raw).Split('|');
                if (parts.Length != 3) return;

                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)
                    && dur > 0 && !double.IsInfinity(dur) && Math.Abs(dur - _totalSeconds) > 0.5)
                {
                    _totalSeconds = dur;
                    TxtTotalTime.Text = FormatTime(_totalSeconds);
                    RebuildTimelineRuler();
                    RebuildRegionVisuals();
                    RebuildHapticVisuals();
                    RebuildEffectVisuals();
                    // NOT in WPF's set, which left TimeReached pins at the old duration's x until
                    // the next resize happened to rebuild them. One call, and they move with
                    // everything else.
                    RebuildRuleVisuals();
                }

                // The page plays and pauses without us - autoplay, an ad, the site's own controls.
                // Follow it, or the glyph says "paused" over a running video.
                var playing = parts[2] == "1";
                if (playing != _isPlaying)
                {
                    _isPlaying = playing;
                    BtnPlayPause.Content = playing ? "⏸" : "▶";
                }

                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var cur))
                    OnBrowserTimeChanged(cur);
            }
            catch (Exception ex)
            {
                _browserPollDisabled = true;
                Log.Debug("DeeperEditor: preview time poll disabled after {Error}", ex.Message);
            }
            finally { _browserPollInFlight = false; }
        }

        /// <summary>WebView2 hands back a JSON literal (a string arrives quoted); WebKitGTK hands
        /// back the raw value. Strip one layer of quotes so the caller sees the same on both.</summary>
        private static string Unquote(string? s)
            => string.IsNullOrEmpty(s) ? "" : (s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s);

        /// <summary>
        /// Blanks the preview page and closes the fence behind it. The gate is narrowed to
        /// <c>about:</c> BEFORE the assignment, so the navigation that clears the page is the only
        /// one it will still admit - a gate left permissive while the engine tears a document down
        /// is a gate that is open for the last navigation.
        /// </summary>
        private void StopBrowserPreview()
        {
            if (!_browserNavigated) return;
            _browserNavigated = false;
            // The page owned _isPlaying while it was up. Leaving it true would make the next press
            // of a local-media transport toggle straight to "paused" and do nothing visible.
            _isPlaying = false;
            BtnPlayPause.Content = "▶";
            try
            {
                BrowserPreview.AllowNavigation = u => u.Scheme == "about";
                BrowserPreview.Source = new Uri("about:blank");
            }
            catch (Exception ex) { Log.Debug("DeeperEditor: preview blank failed: {Error}", ex.Message); }
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

        /// <summary>
        /// In browser mode this drives the page's own &lt;video&gt; and sets NOTHING locally: the
        /// poll flips the glyph off the page's real state a tick later, so a press on a page that
        /// has no video element yet (still loading, an error page, a navigation the fence refused)
        /// reads as having done nothing - which is what happened.
        ///
        /// <para>ponytail: LOCAL media still has no player, so there play/pause toggles the glyph
        /// and the playhead timer and the timeline animates against the project's own
        /// duration.</para>
        /// </summary>
        private void BtnPlayPause_Click(object? sender, RoutedEventArgs e)
        {
            if (_browserNavigated)
            {
                _ = BrowserPreview.InvokeScriptAsync(
                    BrowserVideoScript(_isPlaying ? "(l.pause(),'ok')" : "(l.play(),'ok')"));
                return;
            }
            _isPlaying = !_isPlaying;
            BtnPlayPause.Content = _isPlaying ? "⏸" : "▶";
            if (_isPlaying) _playheadTimer?.Start(); else _playheadTimer?.Stop();
        }

        private void PlayheadTimer_Tick(object? sender, EventArgs e)
        {
            // Browser mode: the page owns the clock AND the play state, so the tick is a poll and
            // runs whether or not we think anything is playing.
            if (_browserNavigated) { _ = PollBrowserTimeAsync(); return; }
            if (_isScrubbing) return;
            if (!_isPlaying || _totalSeconds <= 0) return;
            // ponytail: LOCAL media only. The WPF tick read AudioFileReader.CurrentTime (video time
            // arrived on MediaPlayer.TimeChanged). With no player, advance by the timer interval so
            // the playhead, the readout and any time-driven redraw stay honest about elapsed time.
            _currentSeconds = Math.Min(_totalSeconds, _currentSeconds + 0.08);
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            UpdatePlayheadPosition();
        }

        private void SeekToFraction(double frac)
        {
            frac = Math.Clamp(frac, 0, 1);
            _currentSeconds = frac * _totalSeconds;
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            if (_browserNavigated && _totalSeconds > 0)
            {
                _ = BrowserPreview.InvokeScriptAsync(BrowserVideoScript(string.Format(
                    CultureInfo.InvariantCulture, "(l.currentTime={0:0.###},'ok')", _currentSeconds)));
            }
            // ponytail: LOCAL media needs the player's Seek - the WPF version pushed the new
            // position to MediaPlayer.SeekTo / AudioFileReader.CurrentTime on these two branches.
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
        private void BtnPreviewZoomIn_Click(object? sender, RoutedEventArgs e) => AdjustPreviewZoom(+0.10);
        private void BtnPreviewZoomOut_Click(object? sender, RoutedEventArgs e) => AdjustPreviewZoom(-0.10);

        /// <summary>
        /// ponytail: NativeWebView genuinely has no zoom factor - one of the three CoreWebView2
        /// members with no counterpart at all, and NOT a missing script channel, since
        /// InvokeScriptAsync works and everything else in this region uses it. CSS <c>zoom</c>
        /// through that channel is the obvious substitute and is not one: it is per-document, so it
        /// is lost on the next navigation, and it does not scale a fullscreened video. So WPF's
        /// +/-10% clamped to [0.25, 5.0] stays lost, and these two buttons only log.
        /// </summary>
        private void AdjustPreviewZoom(double delta)
        {
            Log.Debug("DeeperEditor: preview zoom {Delta:+0.00;-0.00} ignored; NativeWebView has no zoom", delta);
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
        // The library's file conventions are inlined below rather than taken from
        // ConditioningControlPanel/Services/Deeper/EnhancementLibrary.cs, which is head-only
        // (App.UserDataPath, App.Settings, App.Logger, FileSystemWatcher, DispatcherTimer). Only
        // five of its members matter to this window - FileSuffix, LibraryFolder, LastDirectory,
        // SuggestedFileName and Save - and every one of them reduces to CorePaths, CoreSettings and
        // Core's EnhancementSerializer.
        //
        // ponytail: five inlined members beats blocking the whole file-ops group on a service move
        // this layer does not own. When EnhancementLibrary reaches Core, delete this region and
        // call it. Deliberately NOT inlined: LibraryChanged (nothing on this head listens - the
        // catalogue browser is not ported), the demo seeding, and ScanLibrary/FindMatch, whose
        // absence is visible in exactly one place and noted there.
        // ---------------------------------------------------------------------------------

        private const string EnhancementFileSuffix = ".ccpenh.json";
        private const int MaxRecentFiles = 10;

        private static readonly string[] AudioPatterns = { "*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg" };
        private static readonly string[] VideoPatterns = { "*.mp4", "*.webm", "*.mkv", "*.mov", "*.avi", "*.m4v" };

        /// <summary>EnhancementLibrary.LibraryFolder.</summary>
        private static string LibraryFolder => IOPath.Combine(CorePaths.UserData, "enhancements");

        /// <summary>EnhancementLibrary.LastDirectory: where the last save or open happened,
        /// falling back to the library folder.</summary>
        private static string LastDirectory
        {
            get
            {
                var d = CoreSettings.Current.DeeperLastDirectory;
                return !string.IsNullOrEmpty(d) && Directory.Exists(d) ? d : LibraryFolder;
            }
        }

        /// <summary>EnhancementLibrary.SuggestedFileName.</summary>
        private static string SuggestedProjectFileName(Enhancement e)
        {
            var name = e.Metadata?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = "Untitled";
            foreach (var c in IOPath.GetInvalidFileNameChars()) name = name!.Replace(c, '_');
            return name + EnhancementFileSuffix;
        }

        /// <summary>EnhancementLibrary.Save, with its TouchRecent and RememberDirectory folded in -
        /// those two writes are what puts a saved project in the recent list and seeds the next
        /// picker, so dropping them would quietly lose behaviour the strip depends on.</summary>
        private static void WriteProjectFile(Enhancement e, string path)
        {
            var dir = IOPath.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);
            File.WriteAllText(path, EnhancementSerializer.Save(e));

            try
            {
                var settings = CoreSettings.Current;
                var canonical = IOPath.GetFullPath(path);
                var recent = settings.DeeperRecentFiles
                    .Where(x => !string.Equals(x, canonical, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                recent.Insert(0, canonical);
                if (recent.Count > MaxRecentFiles) recent = recent.Take(MaxRecentFiles).ToList();
                settings.DeeperRecentFiles = recent;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir!)) settings.DeeperLastDirectory = dir!;
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Warning(ex, "DeeperEditor: recording the saved project failed"); }
        }

        /// <summary>A picker start folder from a path, or null when it does not resolve. The
        /// library folder does not exist until the first save, so a null here is normal and just
        /// means the picker opens wherever the OS last left it.</summary>
        private async Task<IStorageFolder?> TryFolderAsync(string? dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            try { return await StorageProvider.TryGetFolderFromPathAsync(dir!); }
            catch { return null; }
        }

        private async void MenuSave_Click(object? sender, RoutedEventArgs e)
        {
            try { await SaveCurrentAsync(); }
            catch (Exception ex) { Log.Warning(ex, "DeeperEditor: save failed"); }
        }

        /// <summary>What Ctrl+S does, as something a caller can await. Both unsaved-changes
        /// prompts have to let the save finish before they read <see cref="_isDirty"/> back for
        /// their answer, and <see cref="MenuSave_Click"/> is an <c>async void</c> handler that
        /// cannot be awaited - WPF got away with calling it because its save was synchronous.
        /// </summary>
        private Task SaveCurrentAsync()
            => string.IsNullOrEmpty(_filePath) ? SaveAsAsync() : SaveToAsync(_filePath!);

        private async void MenuSaveAs_Click(object? sender, RoutedEventArgs e)
        {
            try { await SaveAsAsync(); }
            catch (Exception ex) { Log.Warning(ex, "DeeperEditor: save-as failed"); }
        }

        private async Task SaveAsAsync()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get("deeper_editor_save_dialog_title"),
                SuggestedFileName = SuggestedProjectFileName(_enhancement),
                SuggestedStartLocation = await TryFolderAsync(LastDirectory),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Deeper Enhancement") { Patterns = new[] { "*" + EnhancementFileSuffix } },
                },
            });
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;
            // WPF set AddExtension=false and appended the double suffix itself, because
            // ".ccpenh.json" is not an extension any picker will complete. Same here.
            if (!path!.EndsWith(EnhancementFileSuffix, StringComparison.OrdinalIgnoreCase))
                path += EnhancementFileSuffix;
            await SaveToAsync(path);
        }

        private async Task SaveToAsync(string path)
        {
            try
            {
                // A synchronous validation pass so the user is not handed a file we already know
                // is broken. Errors get a "save anyway?" prompt; warnings pass silently, they are
                // already on the validation strip.
                var errorCount = EnhancementValidator.Validate(_enhancement)
                    .Count(i => i.Severity == ValidationSeverity.Error);
                if (errorCount > 0 && !await MessageDialog.ConfirmAsync(this,
                        Loc.Get("deeper_editor_save_invalid_title"),
                        Loc.GetF("deeper_editor_save_invalid_prompt_fmt", errorCount)))
                    return;

                // Refresh the hardware-gating auto-tags so the catalogue browser can show them on
                // the card without re-reading the file.
                if (_enhancement.Metadata != null)
                    _enhancement.Metadata.AutoTags = EnhancementAutoTagger.Detect(_enhancement);

                WriteProjectFile(_enhancement, path);
                _filePath = path;
                _isDirty = false;
                TxtDirty.IsVisible = false;
                // UpdateTitle only writes TextBlocks and the linked-files strip, so none of
                // Avalonia's deferred TextChanged handlers fire and the flag stays clear.
                UpdateTitle();

                // ponytail: WPF also set TutorialEventBus.LastSavedEnhancementPath and emitted
                // "FileSaved" so the HT walkthrough could advance to its follow-up card.
                // TutorialEventBus is head-only (ConditioningControlPanel/Services/TutorialEventBus.cs)
                // and no tutorial runs on this head, so there is nothing listening to notify.
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DeeperEditor: save failed");
                await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_save_dialog_title"),
                    Loc.GetF("deeper_editor_save_failed_fmt", ex.Message));
            }
        }

        private async void MenuExportEnhanced_Click(object? sender, RoutedEventArgs e)
        {
            try { await ExportEnhancedAsync(); }
            catch (Exception ex)
            {
                Log.Warning(ex, "DeeperEditor: export failed");
                await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_export_dialog_title"),
                    Loc.GetF("deeper_editor_export_failed_fmt", ex.Message));
            }
        }

        private async Task ExportEnhancedAsync()
        {
            // Hard errors block the export outright rather than prompting: the bundled file is
            // meant to be shared, so handing someone a known-broken enhancement is worse than
            // refusing.
            var errorCount = EnhancementValidator.Validate(_enhancement)
                .Count(i => i.Severity == ValidationSeverity.Error);
            if (errorCount > 0)
            {
                await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_export_dialog_title"),
                    Loc.GetF("deeper_editor_export_validation_blocked_fmt", errorCount));
                return;
            }

            // A project already backed by a local file the bundler can write into needs no picker.
            // URLs, missing files and containers the bundler cannot write (webm, mkv) fall through.
            string? sourcePath = null;
            var currentSrc = _enhancement.MediaSource ?? "";
            var srcIsUrl = currentSrc.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || currentSrc.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (!srcIsUrl && EnhancementMediaBundler.IsSupportedExtension(currentSrc))
            {
                try { if (File.Exists(currentSrc)) sourcePath = currentSrc; } catch { }
            }

            if (sourcePath == null)
            {
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("deeper_editor_export_pick_source_title"),
                    AllowMultiple = false,
                    SuggestedStartLocation = await TryFolderAsync(LastDirectory),
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Media files")
                            { Patterns = new[] { "*.mp4", "*.m4v", "*.mov", "*.m4a", "*.mp3", "*.wav" } },
                        FilePickerFileTypes.All,
                    },
                });
                sourcePath = picked.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrEmpty(sourcePath)) return;
            }

            if (!EnhancementMediaBundler.IsSupportedExtension(sourcePath!))
            {
                await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_export_dialog_title"),
                    Loc.Get("deeper_editor_export_unsupported_format"));
                return;
            }

            var srcExt = IOPath.GetExtension(sourcePath!);
            var defaultName = IOPath.GetFileNameWithoutExtension(sourcePath!) + " (CCP)" + srcExt;

            var dest = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get("deeper_editor_export_save_dialog_title"),
                SuggestedFileName = defaultName,
                DefaultExtension = srcExt.TrimStart('.'),
                SuggestedStartLocation = await TryFolderAsync(IOPath.GetDirectoryName(sourcePath!)),
                // Pinned to the source extension. A destination in another container would hold the
                // source bytes under a name no player can open.
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Media (" + srcExt + ")") { Patterns = new[] { "*" + srcExt } },
                },
            });
            var destPath = dest?.TryGetLocalPath();
            if (string.IsNullOrEmpty(destPath)) return;

            if (string.Equals(IOPath.GetFullPath(destPath!), IOPath.GetFullPath(sourcePath!),
                              StringComparison.OrdinalIgnoreCase))
            {
                await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_export_dialog_title"),
                    Loc.Get("deeper_editor_export_same_file_error"));
                return;
            }

            var result = EnhancementMediaBundler.Export(_enhancement, sourcePath!, destPath!);
            await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_export_dialog_title"),
                result.Success
                    ? Loc.GetF("deeper_editor_export_success_fmt", result.OutputPath ?? destPath!)
                    : Loc.GetF("deeper_editor_export_failed_fmt", result.Error ?? "(unknown)"));
        }

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

        /// <summary>
        /// WPF called Helpers.ExplorerLauncher.RevealInExplorer, a Win32 explorer/cmd chain.
        /// Avalonia's TopLevel.Launcher is the native twin and opens the platform file manager -
        /// xdg-open on Linux, Explorer on Windows. It opens the containing FOLDER rather than
        /// selecting the file, which is the most the cross-platform API offers.
        /// </summary>
        private async void BtnLinkedJsonOpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            try
            {
                var dir = IOPath.GetDirectoryName(_filePath!);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(dir!));
            }
            catch (Exception ex) { Log.Warning(ex, "DeeperEditor: could not open the project folder"); }
        }

        private async void BtnLinkedJsonSwap_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await ConfirmDiscardChangesAsync()) return;
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("deeper_editor_linked_swap_json_btn"),
                    AllowMultiple = false,
                    SuggestedStartLocation = await TryFolderAsync(LibraryFolder),
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Deeper Enhancement") { Patterns = new[] { "*" + EnhancementFileSuffix } },
                        FilePickerFileTypes.All,
                    },
                });
                var path = picked.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) await OpenProjectAsync(path!);
            }
            catch (Exception ex) { Log.Warning(ex, "DeeperEditor: swap project failed"); }
        }

        /// <summary>EnhancementLibrary.Open plus the editor's reload: read the file, tear the
        /// preview down, swap the model in, start the new preview. The one shared body behind the
        /// swap button and the drag-and-drop path, so a bad file reports the same way from both.
        /// </summary>
        private async Task OpenProjectAsync(string path)
        {
            try
            {
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                TeardownPreview();
                LoadEnhancement(enhancement, path);
                _ = InitializePreviewAsync();
            }
            catch (EnhancementLoadException ex)
            {
                await MessageDialog.ShowAsync(this, "Deeper", ex.Message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DeeperEditor: open project failed: {Path}", path);
                await MessageDialog.ShowAsync(this, "Deeper", ex.Message);
            }
        }

        private void BtnLinkedMediaChange_Click(object? sender, RoutedEventArgs e)
        {
            if (MediaChangePopup != null) MediaChangePopup.IsOpen = true;
        }

        private async void BtnChangeMediaLocal_Click(object? sender, RoutedEventArgs e)
        {
            if (MediaChangePopup != null) MediaChangePopup.IsOpen = false;
            try
            {
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("deeper_editor_linked_change_media_btn"),
                    AllowMultiple = false,
                    SuggestedStartLocation = await TryFolderAsync(LastDirectory),
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Media (audio + video)")
                            { Patterns = AudioPatterns.Concat(VideoPatterns).ToArray() },
                        new FilePickerFileType("Audio") { Patterns = AudioPatterns },
                        new FilePickerFileType("Video") { Patterns = VideoPatterns },
                        FilePickerFileTypes.All,
                    },
                });
                var path = picked.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) await ApplyChangedMediaAsync(path!, isLocal: true);
            }
            catch (Exception ex) { Log.Warning(ex, "DeeperEditor: change media (local) failed"); }
        }

        /// <summary>UrlPromptDialog closes with a bare <c>Close()</c> and reports through its
        /// <c>Result</c> property, so this awaits the dialog and then reads it rather than reading
        /// a dialog result.</summary>
        private async void BtnChangeMediaUrl_Click(object? sender, RoutedEventArgs e)
        {
            if (MediaChangePopup != null) MediaChangePopup.IsOpen = false;
            try
            {
                var prompt = new UrlPromptDialog();
                await prompt.ShowDialog(this);
                if (string.IsNullOrEmpty(prompt.Result)) return;
                await ApplyChangedMediaAsync(prompt.Result!, isLocal: false);
            }
            catch (Exception ex) { Log.Warning(ex, "DeeperEditor: change media (URL) failed"); }
        }

        /// <summary>
        /// Relinks the project to a new media source, first offering to switch to whichever
        /// project that media already carries.
        ///
        /// PORTED whole apart from one probe and one button. The embedded probe
        /// (EnhancementMediaBundler.TryExtract) and the sidecar probe are both Core-backed and run
        /// for real. ponytail: the third probe - App.EnhancementLibrary.FindMatch over a scan of
        /// the enhancements folder - does not. FindMatch is the one member of that head-only
        /// service this window cannot reduce to a few lines, so a project that lives in the library
        /// but is neither embedded in nor sitting beside the media is not offered here.
        ///
        /// WPF's "use the matching project?" prompt was Yes/No/Cancel; MessageDialog is OK/Cancel,
        /// so OK still switches projects and Cancel now means WPF's No - relink the open project.
        /// The lost branch is Cancel-out-entirely; a user who wanted that re-picks the old media.
        /// Nothing is written to disk on either path, so no work is lost either way.
        /// </summary>
        private async Task ApplyChangedMediaAsync(string newSource, bool isLocal)
        {
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
            catch (Exception ex) { Log.Debug("DeeperEditor: embedded probe failed: {Error}", ex.Message); }

            if (matched == null && isLocal)
            {
                try
                {
                    var dir = IOPath.GetDirectoryName(newSource);
                    var stem = IOPath.GetFileNameWithoutExtension(newSource);
                    if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(stem))
                    {
                        var sidecar = IOPath.Combine(dir!, stem + EnhancementFileSuffix);
                        if (File.Exists(sidecar))
                        {
                            matched = EnhancementSerializer.LoadFromFile(sidecar);
                            matchedSource = sidecar;
                        }
                    }
                }
                catch (Exception ex) { Log.Debug("DeeperEditor: sidecar probe failed: {Error}", ex.Message); }
            }

            if (matched != null && !string.IsNullOrEmpty(matchedSource)
                && await MessageDialog.ConfirmAsync(this, "Deeper",
                       Loc.Get("deeper_editor_linked_replace_project_q")))
            {
                if (!await ConfirmDiscardChangesAsync()) return;
                // An embedded project is not a file: load it with no path, so a later save writes
                // a new .ccpenh.json instead of overwriting the media in place.
                var loadPath = matchedSource!.EndsWith(EnhancementFileSuffix, StringComparison.OrdinalIgnoreCase)
                    ? matchedSource
                    : null;
                TeardownPreview();
                LoadEnhancement(matched, loadPath);
                _ = InitializePreviewAsync();
                return;
            }

            // Just relink: point the open project at the new media.
            _enhancement.MediaSource = newSource;
            _enhancement.MediaType = isLocal && !IsLocalVideoFile(newSource) ? MediaTypes.Audio : MediaTypes.Video;
            MarkDirty();
            RefreshLinkedFilesUi();
            TeardownPreview();
            _ = InitializePreviewAsync();
            _ = TryAutoFillFromHtAsync(_enhancement.MediaSource);
        }

        /// <summary>The clear confirm is back: WPF asked before dropping the media link and the
        /// straight port had lost the question, so the button used to wipe the link on one click.
        /// </summary>
        private async void BtnLinkedMediaClear_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_enhancement?.MediaSource)) return;
            if (!await MessageDialog.ConfirmAsync(this, "Deeper",
                    Loc.Get("deeper_editor_linked_clear_confirm"))) return;
            PushUndoSnapshot();
            _enhancement!.MediaSource = "";
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

        private async void Window_Drop(object? sender, DragEventArgs e)
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files == null) return;
            try
            {
                foreach (var f in files)
                {
                    var p = f.TryGetLocalPath();
                    if (p == null) continue;
                    if (IsEnhancementJsonPath(p)) { await LoadEnhancementFromDropAsync(p); return; }
                    if (IsLocalMediaFile(p)) { await ApplyChangedMediaAsync(p, isLocal: true); return; }
                }
            }
            catch (Exception ex) { Log.Warning(ex, "DeeperEditor: drop failed"); }
        }

        private async Task LoadEnhancementFromDropAsync(string ccpenhJsonPath)
        {
            if (!await ConfirmDiscardChangesAsync()) return;
            await OpenProjectAsync(ccpenhJsonPath);
        }

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

        /// <summary>
        /// The gate in front of anything that throws the loaded project away: swap-JSON,
        /// drop-load and Close. True means "carry on", false means the user backed out.
        ///
        /// Ported from WPF's MessageBoxButton.YesNoCancel, including its post-condition: after
        /// Save the answer is <c>!_isDirty</c>, not <c>true</c>, so a save the user cancelled at
        /// the file picker aborts the caller instead of discarding the work anyway.
        /// </summary>
        private async Task<bool> ConfirmDiscardChangesAsync()
        {
            if (!_isDirty) return true;
            var choice = await UnsavedChangesDialog.AskAsync(this,
                Loc.Get("deeper_editor_unsaved_prompt_title"),
                Loc.Get("deeper_editor_unsaved_prompt"));
            if (choice == UnsavedChangesDialog.Choice.Cancel) return false;
            if (choice == UnsavedChangesDialog.Choice.Save)
            {
                try { await SaveCurrentAsync(); }
                catch (Exception ex) { Log.Warning(ex, "DeeperEditor: save from the unsaved-changes prompt failed"); }
                return !_isDirty;
            }
            return true; // discard
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

        // Set once the user has answered the unsaved-changes prompt, so the Close() that
        // follows falls straight through to teardown instead of asking again.
        private bool _closeConfirmed;

        /// <summary>
        /// Avalonia reads <c>WindowClosingEventArgs.Cancel</c> synchronously, so the prompt cannot
        /// simply be awaited here the way WPF's blocking MessageBox was: this cancels the close
        /// FIRST, asks, and re-closes behind <see cref="_closeConfirmed"/>. Teardown therefore has
        /// to sit on the other side of that branch - running it before the answer would dispose
        /// the editor's timers under a user who chose Cancel.
        /// </summary>
        private async void Window_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (_isDirty && !_suppressDirtyPromptOnClose && !_closeConfirmed)
            {
                e.Cancel = true;                    // before the first await, or it is not read
                var choice = await UnsavedChangesDialog.AskAsync(this,
                    Loc.Get("deeper_editor_close_unsaved_title"),
                    Loc.Get("deeper_editor_close_unsaved_prompt"));
                if (choice == UnsavedChangesDialog.Choice.Cancel) return;
                if (choice == UnsavedChangesDialog.Choice.Save)
                {
                    try { await SaveCurrentAsync(); }
                    catch (Exception ex) { Log.Warning(ex, "DeeperEditor: save on close failed"); }
                    if (_isDirty) return;           // save cancelled or failed - stay open
                }
                _closeConfirmed = true;
                Close();
                return;
            }

            EndGazePick(commit: false);
            TeardownPreview();
            DisposePlayback();
        }

        /// <summary>WPF also detached the WebView2 handlers and disposed the CoreWebView2
        /// environment; WebHost owns its control's lifetime, so the page is blanked instead. The
        /// blank matters on a media swap, not on close - without it a swap away from a URL leaves
        /// the old page's audio running behind whatever replaces it.
        /// ponytail: exiting preview fullscreen first has nothing to exit - see the stub region at
        /// the foot of the file.</summary>
        private void TeardownPreview()
        {
            StopBrowserPreview();
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
        // Preview fullscreen / VLC / NAudio surface
        //
        // What is left here after the browser preview was wired: the fullscreen reparent and the
        // four player callbacks. They are kept named and typed against portable signatures rather
        // than deleted, so nothing silently disappears and the layer that brings a player has an
        // exact list of what to fill in. The framework-typed parameters
        // (MediaPlayerLengthChangedEventArgs, MediaPlayerTimeChangedEventArgs, StoppedEventArgs)
        // become object?/EventArgs, since naming them would require the very packages this head
        // must not reference.
        //
        // The FULLSCREEN half is stubbed for one reason and it is not a missing wrapper method:
        // ContainsFullScreenElementChanged has no counterpart in NativeWebView, so nothing on this
        // head can tell that the page went fullscreen. Without that signal every member below is
        // unreachable, whatever else were built. HT's video.requestFullscreen() therefore just
        // expands the video to fill the small preview cell, with the timeline still visible behind
        // it - degraded, but not a lie: no control here claims fullscreen is on.
        //
        // Two members that were stubbed for a WebView2 reason are GONE rather than stubbed, because
        // the gate in InitializeBrowserAsync now does their job: IsAllowedPreviewHost (a static
        // that returned false and had no caller) and OnBrowserNavigationStarting. The half of
        // IsAllowedPreviewHost that is still missing is the first-hop allowlist, and that is a Core
        // visibility change, not a member of this file.
        // ---------------------------------------------------------------------------------

        // The preview's own state. _fsTransitionInFlight and _isPreviewFullscreen are read by
        // PollBrowserTimeAsync / OnBrowserTimeChanged and written only by the fullscreen members
        // below, so they are permanently false until those are real - which is the honest state.
        private bool _browserInitInFlight;
        private bool _browserNavigated;
        private bool _browserPollInFlight;
        private bool _browserPollDisabled;
        private bool _isPreviewFullscreen;
        private bool _fsTransitionInFlight;
        private Window? _previewFullscreenWindow;

        /// <summary>ponytail: WPF's page-side bridge posted 'ccp_exit_fullscreen' / 'ccp_zoom_in' /
        /// 'ccp_zoom_out' from a document-created script. NativeWebView HAS WebMessageReceived, but
        /// it has no AddScriptToExecuteOnDocumentCreatedAsync to install the sender, and both of the
        /// three messages' destinations (the fullscreen exit, the page zoom) are themselves stubs -
        /// so a bridge would carry messages nobody could act on.</summary>
        private void OnPreviewWebMessageReceived(object? sender, EventArgs e) { }

        /// <summary>ponytail: needs ContainsFullScreenElementChanged, which NativeWebView does not
        /// have. This is the signal the whole reparent hangs off.</summary>
        private void OnPreviewFullscreenChanged(object? sender, EventArgs e) { }

        /// <summary>ponytail: part of the fullscreen reparent - detached the WebView2 from whichever
        /// panel/decorator currently owned it before re-hosting it in the fullscreen window.</summary>
        private static bool TryDetachFromUiParent(Control child) { _ = child; return false; }

        /// <summary>ponytail: needs Forms.Screen.FromHandle + WindowInteropHelper + a borderless
        /// topmost window. On Linux the Avalonia parts exist (Screens.ScreenFromVisual,
        /// SystemDecorations="None", Topmost, ShowInTaskbar=false) and WebHost is an ordinary
        /// control that could be moved between them, but nothing calls this: see
        /// <see cref="OnPreviewFullscreenChanged"/>.</summary>
        private void EnterPreviewFullscreen() { }

        /// <summary>ponytail: the recovery half of the reparent - put BrowserPreview back into
        /// PreviewHost if the fullscreen window died without an orderly exit.</summary>
        private void SafeRestoreBrowserPreviewToHost() { }

        /// <summary>ponytail: the orderly half of the reparent.</summary>
        private void ExitPreviewFullscreen() { }

        /// <summary>ponytail: asked the page itself to leave fullscreen so the site's own player
        /// chrome stayed consistent. The script channel for this DOES exist now
        /// (WebHost.InvokeScriptAsync); what does not is anything that would call it.</summary>
        private void ExitPreviewFullscreenViaScript() { }

        /// <summary>
        /// Time report from the browser preview, fed by <see cref="PollBrowserTimeAsync"/>. The
        /// playhead, the read-out and the mid-scrub guard are the WPF logic unchanged; only the
        /// source changed, from BrowserVideoTimeSource's WebMessage push to a script-channel poll.
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

    /// <summary>
    /// Save / Discard / Cancel - the one question this head's <c>MessageDialog</c> cannot ask.
    /// WPF used <c>MessageBoxButton.YesNoCancel</c>, which does not exist off Windows, and both
    /// two-way collapses of it lose something real: OK-as-Discard contradicts the prompt, and
    /// OK-as-Save leaves someone abandoning an experiment no exit but overwriting their original.
    ///
    /// <para><b>It lives here, not in Views/Dialogs/, only because of layer file ownership.</b>
    /// Nothing about it is editor-specific - the next layer that touches Views/Dialogs/ should
    /// move it there and point the two callers at the new namespace.</para>
    ///
    /// <para>The Discard label is a hardcoded English constant: CCP.Core's language files carry
    /// <c>btn_save</c> and <c>btn_cancel</c> but no <c>btn_discard</c>, and adding one is a Core
    /// change this layer does not own. WPF's third button was localized by Windows itself, so this
    /// is a real regression for the eight non-English languages - one JSON key wide.</para>
    ///
    /// Built in code rather than XAML for the same ownership reason (a .axaml would be a second
    /// new file). Chrome copied from MessageDialog.axaml: DarkerBgBrush window, PinkBrush 18-bold
    /// header, SecondaryButton/ActionButton footer, and a TextBlock inside every button because
    /// Avalonia parses "_" in Content as an access key.
    /// </summary>
    internal sealed class UnsavedChangesDialog : Window
    {
        internal enum Choice { Save, Discard, Cancel }

        // ponytail: hardcoded until CCP.Core's language files gain a btn_discard key.
        private const string DiscardLabel = "Discard";

        /// <summary>Render/design constructor, on the real close-path strings so the PNG proves
        /// the localization lookups as well as the layout. Internal, like MessageDialog's.</summary>
        internal UnsavedChangesDialog() : this(
            Loc.Get("deeper_editor_close_unsaved_title"),
            Loc.Get("deeper_editor_close_unsaved_prompt"))
        { }

        private UnsavedChangesDialog(string title, string message)
        {
            Title = title;
            Width = 460;
            MinHeight = 170;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Rsrc<IBrush>("DarkerBgBrush") ?? Brushes.Black;

            var save = MakeButton(Loc.Get("btn_save"), "ActionButton", Choice.Save);
            save.IsDefault = true;
            var discard = MakeButton(DiscardLabel, "SecondaryButton", Choice.Discard);
            discard.Margin = new Thickness(0, 0, 10, 0);
            var cancel = MakeButton(Loc.Get("btn_cancel"), "SecondaryButton", Choice.Cancel);
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(0, 0, 10, 0);

            var footer = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
                Margin = new Thickness(0, 20, 0, 0),
            };
            Grid.SetColumn(cancel, 1);
            Grid.SetColumn(discard, 2);
            Grid.SetColumn(save, 3);
            footer.Children.Add(cancel);
            footer.Children.Add(discard);
            footer.Children.Add(save);

            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
            var head = new TextBlock
            {
                Text = title,
                Foreground = Rsrc<IBrush>("PinkBrush") ?? Brushes.White,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            };
            var text = new TextBlock
            {
                Text = message,
                Foreground = Rsrc<IBrush>("TextLightBrush") ?? Brushes.White,
                FontSize = 14,
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(head, 0);
            Grid.SetRow(text, 1);
            Grid.SetRow(footer, 2);
            body.Children.Add(head);
            body.Children.Add(text);
            body.Children.Add(footer);
            Content = body;
        }

        private Button MakeButton(string label, string themeKey, Choice answer)
        {
            var b = new Button
            {
                Theme = Rsrc<ControlTheme>(themeKey),
                Content = new TextBlock { Text = label },
            };
            b.Click += (_, _) => Close(answer);
            return b;
        }

        /// <summary>The app dictionary, not this window's - nothing is merged locally, and the
        /// lookup runs in the constructor, before there is a visual tree to walk.</summary>
        private static T? Rsrc<T>(string key) where T : class
            => Application.Current is { } app && app.TryFindResource(key, out var v) ? v as T : null;

        /// <summary>Asks, and answers Cancel for the window X - the safe direction, since Cancel
        /// is the only answer that keeps the unsaved work reachable.</summary>
        internal static async Task<Choice> AskAsync(Window owner, string title, string message)
            => await new UnsavedChangesDialog(title, message).ShowDialog<Choice?>(owner) ?? Choice.Cancel;
    }
}
