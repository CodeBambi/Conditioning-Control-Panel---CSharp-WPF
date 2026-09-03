using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Deeper/DeeperEditorWindow.Unified.cs.
    /// New-design pieces of the editor: unified timeline (Effect dots + Rule pins rendered
    /// alongside the Region/HapticEvent visuals), the right-click context menu, the hero buttons,
    /// HypnoTube auto-fill, the Creator lock toggle and the five non-haptic effect editor panels.
    ///
    /// Deviations:
    ///   MouseButtonEventArgs / MouseEventArgs -> PointerPressedEventArgs / PointerEventArgs
    ///   IsCtrlDown()                          -> IsCtrlDown(e.KeyModifiers) (threaded, see MultiSelect)
    ///   FindResource("TimelineCtxMenu")       -> the menu is inline in the XAML on TimelineCanvas
    ///   FindResource("HeroAddEffectMenu")     -> a MenuFlyout on BtnAddEffectHero (opens itself)
    ///   ToolTip = "…"                         -> ToolTip.SetTip(control, "…")
    ///   Cursors.Hand / SizeAll / SizeWE       -> new Cursor(StandardCursorType.*)
    ///   Panel.SetZIndex(v, n)                 -> v.ZIndex = n
    ///   Visibility                            -> IsVisible
    ///   TutorialEventBus.Emit                 -> stubbed (App-level service)
    ///   App.Logger                            -> Serilog.Log
    /// </summary>
    public partial class DeeperEditorWindow
    {
        // -- State (new) -----------------------------------------------------------

        private TimelineItem? _selectedEffect;
        private double _rightClickSeconds;
        private bool _creatorLocked;
        private bool _suppressEffectFieldSync;
        private CancellationTokenSource? _htFetchCts;

        // Effect-type colours used by both the timeline dots and the picker labels. The Haptic
        // entry MUST match DeeperAccent in Theme/Colors.xaml - change both together. Kept as a
        // string literal (not a DynamicResource) because these colours get written into the saved
        // .ccpenh.json on every effect dot and need to round-trip stably across builds.
        private static readonly Dictionary<string, string> EffectColors =
            new()
            {
                [EffectTypes.Haptic]     = "#7B5CFF",
                [EffectTypes.Flash]      = "#FFC85C",
                [EffectTypes.Bubble]     = "#5CC8FF",
                [EffectTypes.Subliminal] = "#FF69B4",
                [EffectTypes.Overlay]    = "#5CFFB7",
                [EffectTypes.Speak]      = "#FF8A5C",
            };

        // -- Right-click context menu --------------------------------------------

        /// <summary>
        /// Captures the click position before the framework opens the context menu. Not marked
        /// handled, so Avalonia's automatic ContextMenu opening still fires; the menu items read
        /// <see cref="_rightClickSeconds"/> to compute the drop location.
        /// </summary>
        private void TimelineCanvas_MouseRightButtonDown(PointerPressedEventArgs e)
        {
            try
            {
                _rightClickSeconds = MouseToSeconds(e);

                // Move the playhead to the click point. Most users right-clicking on the timeline
                // want to drop something there.
                if (_totalSeconds > 0)
                {
                    var frac = Math.Clamp(_rightClickSeconds / _totalSeconds, 0, 1);
                    SeekToFraction(frac);
                }
                else
                {
                    // No duration yet. Move the playhead visually so the click registers.
                    var pt = e.GetPosition(TimelineCanvas);
                    PlayheadLine.StartPoint = new Point(pt.X, 0);
                    PlayheadLine.EndPoint = new Point(pt.X, TimelineCanvas.Bounds.Height);
                }

                // Refresh the audio-mode hide-of-video-only triggers per open.
                ApplyAudioModeToContextMenu(TimelineCtxMenu);
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: ctx menu prep error: {Error}", ex.Message);
            }
        }

        private void ApplyAudioModeToContextMenu(ContextMenu? menu)
        {
            if (menu == null) return;
            bool audio = _enhancement.MediaType == MediaTypes.Audio;
            // Walk the "Add Rule" submenu and hide video-only triggers when on audio.
            foreach (var top in menu.Items.OfType<MenuItem>())
            {
                foreach (var sub in top.Items.OfType<MenuItem>())
                {
                    var name = sub.Name ?? "";
                    bool videoOnly = name is "CtxRuleGazeTarget" or "CtxRuleGazeAvoid"
                        or "CtxRuleAttentionLost" or "CtxRuleBlinkDetected" or "CtxRuleMouthOpen";
                    if (videoOnly) sub.IsVisible = !audio;
                }
            }
        }

        private void CtxAddEffectHaptic_Click(object? sender, RoutedEventArgs e)     => AddEffectAt(EffectTypes.Haptic, _rightClickSeconds);
        private void CtxAddEffectFlash_Click(object? sender, RoutedEventArgs e)      => AddEffectAt(EffectTypes.Flash, _rightClickSeconds);
        private void CtxAddEffectBubble_Click(object? sender, RoutedEventArgs e)     => AddEffectAt(EffectTypes.Bubble, _rightClickSeconds);
        private void CtxAddEffectSubliminal_Click(object? sender, RoutedEventArgs e) => AddEffectAt(EffectTypes.Subliminal, _rightClickSeconds);
        private void CtxAddEffectOverlay_Click(object? sender, RoutedEventArgs e)    => AddEffectAt(EffectTypes.Overlay, _rightClickSeconds);
        private void CtxAddEffectSpeak_Click(object? sender, RoutedEventArgs e)      => AddEffectAt(EffectTypes.Speak, _rightClickSeconds);

        private void CtxAddRuleTimeReached_Click(object? sender, RoutedEventArgs e)    => AddRuleAt(TriggerTypes.TimeReached, _rightClickSeconds);
        private void CtxAddRuleBandEntered_Click(object? sender, RoutedEventArgs e)    => AddRuleAt(TriggerTypes.RegionEntered, _rightClickSeconds);
        private void CtxAddRuleBandExited_Click(object? sender, RoutedEventArgs e)     => AddRuleAt(TriggerTypes.RegionExited, _rightClickSeconds);
        private void CtxAddRuleGazeTarget_Click(object? sender, RoutedEventArgs e)     => AddRuleAt(TriggerTypes.GazeTarget, _rightClickSeconds);
        private void CtxAddRuleGazeAvoid_Click(object? sender, RoutedEventArgs e)      => AddRuleAt(TriggerTypes.GazeAvoid, _rightClickSeconds);
        private void CtxAddRuleAttentionLost_Click(object? sender, RoutedEventArgs e)  => AddRuleAt(TriggerTypes.AttentionLost, _rightClickSeconds);
        private void CtxAddRuleBlinkDetected_Click(object? sender, RoutedEventArgs e)  => AddRuleAt(TriggerTypes.BlinkDetected, _rightClickSeconds);
        private void CtxAddRuleMouthOpen_Click(object? sender, RoutedEventArgs e)      => AddRuleAt(TriggerTypes.MouthOpen, _rightClickSeconds);

        /// <summary>
        /// Same six options as the right-click menu, dropped at the playhead. WPF opened a
        /// ContextMenu resource by hand; the Avalonia MenuFlyout on the button opens itself, so all
        /// this has to do is point <see cref="_rightClickSeconds"/> at the playhead first.
        /// </summary>
        private void BtnAddEffectHero_Click(object? sender, RoutedEventArgs e)
        {
            _rightClickSeconds = Math.Max(0, _currentSeconds);
        }

        private void BtnAddRuleHero_Click(object? sender, RoutedEventArgs e)
        {
            AddRuleAt(TriggerTypes.TimeReached, _currentSeconds);
        }

        // -- Add Effect ------------------------------------------------------------

        private void AddEffectAt(string effectType, double seconds)
        {
            try
            {
                seconds = Math.Max(0, seconds);
                if (effectType == EffectTypes.Haptic)
                {
                    // Haptic effects continue to live on the legacy HapticTrack so the existing
                    // visualisation, drag-shift and curve editor wiring keep working unchanged.
                    AddHapticEventAt(seconds);
                    EmitTutorialEvent("EffectAdded");
                    return;
                }

                PushUndoSnapshot();

                var defaultDurationMs = effectType switch
                {
                    EffectTypes.Bubble => 5000,
                    EffectTypes.Overlay => 3000,
                    EffectTypes.Subliminal => 200,
                    EffectTypes.Flash => 800,
                    EffectTypes.Speak => 6000,
                    _ => 1000
                };
                var item = new TimelineItem
                {
                    Id = TimelineItem.NewId(),
                    Kind = TimelineItemKind.Effect,
                    Start = seconds,
                    Duration = defaultDurationMs / 1000.0,
                    EffectType = effectType,
                    EffectIntensity = 1.0,
                    EffectDurationMs = defaultDurationMs,
                    EffectMaxBubbles = 3,
                    EffectOpacity = 0.5,
                    EffectOverlayKind = OverlayKinds.PinkFilter,
                    EffectPlaySound = true,
                    Color = EffectColors.TryGetValue(effectType, out var c) ? c : null
                };
                if (effectType == EffectTypes.Speak)
                {
                    item.EffectSpeakTarget = "YES";
                    item.EffectSpeakCueMode = SpeakCueMode.Intermittent;
                    item.EffectSpeakCueIntervalMs = 250;
                    item.EffectSpeakRequiredReps = 1;
                    item.EffectSpeakCompletion = SpeakCompletion.UntilSatisfied;
                    item.EffectSpeakHoldMode = SpeakHoldMode.LoopRegion;
                    item.EffectSpeakCorrectMessage = "good girl";
                    item.EffectSpeakIncorrectMessage = "try again";
                }
                _enhancement.TimelineItems.Add(item);
                MarkDirty();
                RebuildEffectVisuals();
                SelectEffect(item);
                ScheduleValidation();
                EmitTutorialEvent("EffectAdded");
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: AddEffectAt error: {Error}", ex.Message);
            }
        }

        private void AddHapticEventAt(double seconds)
        {
            PushUndoSnapshot();
            var track = _enhancement.HapticTracks.FirstOrDefault();
            if (track == null)
            {
                track = new HapticTrack { Id = "primary" };
                _enhancement.HapticTracks.Add(track);
            }

            // Default 5 s when dropped fresh; clamp to the timeline length.
            double duration = 5.0;
            if (_totalSeconds > 0) duration = Math.Min(duration, Math.Max(0.1, _totalSeconds - seconds));

            var ev = new HapticEvent
            {
                Start = seconds,
                Duration = Math.Max(0.1, duration),
                Intensity = 1.0,
                PatternName = StockHapticPatterns.Names.FirstOrDefault() ?? "Pulse"
            };
            track.Events.Add(ev);
            MarkDirty();
            RebuildHapticVisuals();
            SelectHaptic(track, ev);
            ScheduleValidation();
        }

        private void AddRuleAt(string triggerType, double seconds)
        {
            try
            {
                if (_enhancement.MediaType == MediaTypes.Audio
                    && (triggerType is TriggerTypes.GazeTarget or TriggerTypes.GazeAvoid
                            or TriggerTypes.AttentionLost or TriggerTypes.BlinkDetected
                            or TriggerTypes.MouthOpen))
                {
                    Log.Debug("DeeperEditor: AddRuleAt skipped video-only trigger on audio enhancement: {T}", triggerType);
                    return;
                }

                seconds = Math.Max(0, seconds);
                double bandDuration = triggerType == TriggerTypes.TimeReached ? 0.0 : 5.0;
                if (_totalSeconds > 0 && bandDuration > 0)
                    bandDuration = Math.Min(bandDuration, Math.Max(0.5, _totalSeconds - seconds));

                PushUndoSnapshot();

                var rule = new EnhancementRule
                {
                    Trigger = BuildDefaultTrigger(triggerType, seconds),
                    Action = new NoOpEnhancementAction(),
                    CooldownMs = 1000,
                    Enabled = true
                };

                if (triggerType == TriggerTypes.TimeReached)
                {
                    // Point-style rule: no companion region - just a Rule that fires at the time.
                    _enhancement.Rules.Add(rule);
                    MarkDirty();
                    RebuildRuleVisuals();
                    SelectRule(rule);
                }
                else
                {
                    // Band-style rule: create a Region the rule constrains to, so the user can
                    // drag-resize the band and the rule fires only inside it.
                    var region = new Region
                    {
                        Id = NextRegionId(),
                        Start = seconds,
                        End = seconds + bandDuration,
                        Label = "",
                        Color = NextRegionColor()
                    };
                    _enhancement.Regions.Add(region);
                    rule.RegionConstraint = region.Id;
                    // Wire the trigger's RegionId to the same region so validation passes on first
                    // save/preview.
                    if (rule.Trigger is RegionEnteredTrigger reTrig) reTrig.RegionId = region.Id;
                    else if (rule.Trigger is RegionExitedTrigger rxTrig) rxTrig.RegionId = region.Id;
                    _enhancement.Rules.Add(rule);
                    MarkDirty();
                    RebuildRegionVisuals();
                    // Select the Rule (not the Region) so the user immediately sees the rule editor
                    // including the gaze rect inputs, the 3x3 quick-region grid and the picker.
                    SelectRule(rule);
                }
                ScheduleValidation();
                EmitTutorialEvent("RuleAdded");
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: AddRuleAt error: {Error}", ex.Message);
            }
        }

        private static EnhancementTrigger BuildDefaultTrigger(string triggerType, double seconds)
        {
            return triggerType switch
            {
                TriggerTypes.TimeReached    => new TimeReachedTrigger { Time = seconds },
                TriggerTypes.RegionEntered  => new RegionEnteredTrigger { RegionId = "" },
                TriggerTypes.RegionExited   => new RegionExitedTrigger { RegionId = "" },
                TriggerTypes.GazeTarget     => new GazeTargetTrigger(),
                TriggerTypes.GazeAvoid      => new GazeAvoidTrigger(),
                TriggerTypes.AttentionLost  => new AttentionLostTrigger(),
                TriggerTypes.BlinkDetected  => new BlinkDetectedTrigger(),
                TriggerTypes.MouthOpen      => new MouthOpenTrigger(),
                _ => new TimeReachedTrigger { Time = seconds }
            };
        }

        // -- Effect TimelineItem visualisation ------------------------------------

        private readonly List<Shape> _effectVisuals = new();

        /// <summary>
        /// Renders Effect TimelineItems (non-haptic) as dots or segments on the unified timeline.
        /// Tear-down/rebuild keeps the code simple.
        /// </summary>
        private void RebuildEffectVisuals()
        {
            try
            {
                foreach (var v in _effectVisuals)
                {
                    try { TimelineCanvas.Children.Remove(v); } catch { }
                }
                _effectVisuals.Clear();

                var w = TimelineCanvas.Bounds.Width;
                var h = TimelineCanvas.Bounds.Height;
                if (w <= 0 || h <= 0 || _totalSeconds <= 0) return;

                foreach (var item in _enhancement.TimelineItems)
                {
                    if (item == null || item.Kind != TimelineItemKind.Effect) continue;
                    if (item.EffectType == EffectTypes.Haptic) continue; // legacy path renders these
                    BuildEffectDot(item, w, h);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: RebuildEffectVisuals error: {Error}", ex.Message);
            }
        }

        private void BuildEffectDot(TimelineItem item, double canvasWidth, double canvasHeight)
        {
            // One-shot effects (Flash, Subliminal) render as a small dot - they have no meaningful
            // on-screen duration the user would drag. Ongoing effects (Bubble, Overlay, Speak)
            // render as draggable, resizable segments whose width matches their Duration.
            if (IsOneShotEffect(item.EffectType))
                BuildEffectPointDot(item, canvasWidth, canvasHeight);
            else
                BuildEffectSegment(item, canvasWidth, canvasHeight);
        }

        private static bool IsOneShotEffect(string? effectType) =>
            effectType == EffectTypes.Flash || effectType == EffectTypes.Subliminal;

        // One-shot dot: small ellipse, click-to-select only (no drag/resize).
        private void BuildEffectPointDot(TimelineItem item, double canvasWidth, double canvasHeight)
        {
            var brush = TryParseBrush(EffectColors.TryGetValue(item.EffectType ?? "", out var c) ? c : "#FFFFFF")
                        ?? Brushes.White;
            var isSelected = item == _selectedEffect || IsInSelectionSet(item);
            var dot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = brush,
                Stroke = isSelected ? Brushes.White : Brushes.Transparent,
                StrokeThickness = isSelected ? 2 : 0,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = item,
                ZIndex = 10
            };
            ToolTip.SetTip(dot, $"{item.EffectType} @ {item.Start:0.##}s");
            double x = (item.Start / _totalSeconds) * canvasWidth - 6;
            var (eTop, eH) = LaneBand(TimelineLane.Effects, canvasHeight);
            double y = eTop + eH / 2.0 - 6; // centre the 12px dot in the Effects lane
            Canvas.SetLeft(dot, x);
            Canvas.SetTop(dot, y);

            dot.PointerPressed += (s, e) =>
            {
                if (!e.GetCurrentPoint(dot).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                bool ctrl = IsCtrlDown(e.KeyModifiers);
                HandleSelectionClick(item, e.KeyModifiers);
                if (ctrl)
                {
                    // Pure toggle - refresh all lanes so the new selection-set membership shows.
                    RebuildRegionVisuals();
                    RebuildHapticVisuals();
                    RebuildEffectVisuals();
                    return;
                }
                SelectEffect(item);
            };

            TimelineCanvas.Children.Add(dot);
            _effectVisuals.Add(dot);
        }

        // Ongoing segment: rectangle with width = Duration, draggable + resizable.
        private void BuildEffectSegment(TimelineItem item, double canvasWidth, double canvasHeight)
        {
            var color = TryParseColor(EffectColors.TryGetValue(item.EffectType ?? "", out var c) ? c : "#FFFFFF")
                        ?? Colors.White;
            var fill = Color.FromArgb(140, color.R, color.G, color.B);
            var isSelected = item == _selectedEffect || IsInSelectionSet(item);

            // Segment width tracks Duration; minimum 8px so a near-zero segment is still clickable.
            double startX = Math.Max(0, (item.Start / _totalSeconds) * canvasWidth);
            double endX = Math.Min(canvasWidth, ((item.Start + Math.Max(0, item.Duration)) / _totalSeconds) * canvasWidth);
            double width = Math.Max(8, endX - startX);
            var (eTop, eH) = LaneBand(TimelineLane.Effects, canvasHeight);
            double height = Math.Min(18, Math.Max(0, eH - 2 * LaneInset));
            double y = eTop + (eH - height) / 2.0; // centre the segment in the Effects lane

            var rect = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(fill),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = new Cursor(StandardCursorType.SizeAll),
                Tag = item,
                ZIndex = 10
            };
            ToolTip.SetTip(rect, $"{item.EffectType} @ {item.Start:0.##}s · {item.Duration:0.##}s");

            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, y);

            rect.PointerPressed += EffectRect_PointerPressed;
            rect.PointerMoved += EffectRect_PointerMoved;

            TimelineCanvas.Children.Add(rect);
            _effectVisuals.Add(rect);
        }

        // Cursor feedback near segment edges so the user can tell resize from drag-move.
        private void EffectRect_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragMode != DragMode.None) return;
            if (sender is not Rectangle r) return;
            var pos = e.GetPosition(r);
            r.Cursor = ClassifyEdgeHit(pos.X, r.Bounds.Width) == EdgeHit.Body
                ? new Cursor(StandardCursorType.SizeAll)
                : new Cursor(StandardCursorType.SizeWestEast);
        }

        private void EffectRect_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Rectangle r || r.Tag is not TimelineItem item) return;
            if (!e.GetCurrentPoint(r).Properties.IsLeftButtonPressed) return;

            // Snapshot pos + width BEFORE selecting - SelectEffect rebuilds visuals, which detaches
            // `r` from the tree, after which GetPosition(r) returns ~(0,0) and trips the left-edge
            // resize check unconditionally.
            var pos = e.GetPosition(r);
            var rectWidth = r.Bounds.Width;
            bool ctrl = IsCtrlDown(e.KeyModifiers);
            HandleSelectionClick(item, e.KeyModifiers);
            // Ctrl+Click is a pure selection toggle - no drag-init, no capture, no primary swap.
            if (ctrl)
            {
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                e.Handled = true;
                return;
            }
            SelectEffect(item);

            _draggedEffect = item;
            _effectDragOriginalDuration = Math.Max(0, item.Duration);
            BeginMultiDragCapture();

            switch (ClassifyEdgeHit(pos.X, rectWidth))
            {
                case EdgeHit.Start: _dragMode = DragMode.ResizeEffectStart; break;
                case EdgeHit.End:   _dragMode = DragMode.ResizeEffectEnd; break;
                default:
                    _dragMode = DragMode.DragEffect;
                    _effectDragOffsetSec = MouseToSeconds(e) - item.Start;
                    break;
            }
            e.Pointer.Capture(TimelineCanvas);
            e.Handled = true;
        }

        // -- Selection (Effect TimelineItems) -------------------------------------

        private void SelectEffect(TimelineItem item)
        {
            _selectedEffect = item;
            // Clear other selection slots.
            _selectedRegion = null;
            _selectedHaptic = null;
            _selectedHapticTrack = null;
            _selectedRule = null;
            UpdateSelectedSidePanelForEffect();
            ScrollInspectorToTop();
            RebuildEffectVisuals();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildRuleVisuals();
            UpdateSelectionSummary();
            BuildItemsList();
        }

        private void UpdateSelectedSidePanelForEffect()
        {
            HideAllEditors();
            if (_selectedEffect == null)
            {
                if (SelectedPlaceholder != null) SelectedPlaceholder.IsVisible = true;
                return;
            }
            if (SelectedPlaceholder != null) SelectedPlaceholder.IsVisible = false;

            _suppressEffectFieldSync = true;
            try
            {
                switch (_selectedEffect.EffectType)
                {
                    case EffectTypes.Flash:
                        FlashEffectEditor.IsVisible = true;
                        TxtFlashStart.Text = _selectedEffect.Start.ToString("0.##", CultureInfo.InvariantCulture);
                        TxtFlashDuration.Text = _selectedEffect.EffectDurationMs.ToString(CultureInfo.InvariantCulture);
                        ChkFlashSuppressHaptic.IsChecked = _selectedEffect.EffectSuppressHaptic;
                        break;
                    case EffectTypes.Bubble:
                        BubbleEffectEditor.IsVisible = true;
                        TxtBubbleStart.Text = _selectedEffect.Start.ToString("0.##", CultureInfo.InvariantCulture);
                        TxtBubbleWindow.Text = (_selectedEffect.EffectDurationMs / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
                        SliderBubbleIntensity.Value = _selectedEffect.EffectIntensity;
                        break;
                    case EffectTypes.Subliminal:
                        SubliminalEffectEditor.IsVisible = true;
                        TxtSubliminalStart.Text = _selectedEffect.Start.ToString("0.##", CultureInfo.InvariantCulture);
                        TxtSubliminalText.Text = _selectedEffect.EffectText ?? "";
                        TxtSubliminalDuration.Text = _selectedEffect.EffectDurationMs.ToString(CultureInfo.InvariantCulture);
                        ChkSubliminalSuppressHaptic.IsChecked = _selectedEffect.EffectSuppressHaptic;
                        break;
                    case EffectTypes.Overlay:
                        OverlayEffectEditor.IsVisible = true;
                        TxtOverlayStart.Text = _selectedEffect.Start.ToString("0.##", CultureInfo.InvariantCulture);
                        SelectOverlayKindCombo(_selectedEffect.EffectOverlayKind);
                        TxtOverlayDuration.Text = _selectedEffect.EffectDurationMs.ToString(CultureInfo.InvariantCulture);
                        bool ramp = _selectedEffect.EffectOpacityStart.HasValue && _selectedEffect.EffectOpacityEnd.HasValue;
                        ChkOverlayRamp.IsChecked = ramp;
                        // When ramping, the main slider is the START opacity.
                        SliderOverlayOpacity.Value = ramp ? _selectedEffect.EffectOpacityStart!.Value : _selectedEffect.EffectOpacity;
                        SliderOverlayOpacityEnd.Value = ramp ? _selectedEffect.EffectOpacityEnd!.Value : _selectedEffect.EffectOpacity;
                        ApplyOverlayRampVisibility(ramp);
                        break;
                    case EffectTypes.Speak:
                        SpeakEffectEditor.IsVisible = true;
                        TxtSpeakStart.Text = _selectedEffect.Start.ToString("0.##", CultureInfo.InvariantCulture);
                        TxtSpeakLength.Text = (_selectedEffect.EffectDurationMs / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
                        TxtSpeakTarget.Text = _selectedEffect.EffectSpeakTarget ?? "";
                        TxtSpeakCue.Text = _selectedEffect.EffectSpeakCue ?? "";
                        TxtSpeakInterval.Text = _selectedEffect.EffectSpeakCueIntervalMs.ToString(CultureInfo.InvariantCulture);
                        TxtSpeakCorrect.Text = _selectedEffect.EffectSpeakCorrectMessage ?? "";
                        TxtSpeakIncorrect.Text = _selectedEffect.EffectSpeakIncorrectMessage ?? "";
                        SelectComboByTag(CmbSpeakCueMode, _selectedEffect.EffectSpeakCueMode.ToString());
                        SelectComboByTag(CmbSpeakReps, Math.Clamp(_selectedEffect.EffectSpeakRequiredReps, 1, 5).ToString(CultureInfo.InvariantCulture));
                        SelectComboByTag(CmbSpeakCompletion, _selectedEffect.EffectSpeakCompletion.ToString());
                        SelectComboByTag(CmbSpeakHold, _selectedEffect.EffectSpeakHoldMode.ToString());
                        ApplySpeakConditionalVisibility();
                        break;
                    default:
                        if (SelectedPlaceholder != null) SelectedPlaceholder.IsVisible = true;
                        break;
                }
            }
            finally { ClearWhenEventsDrained(() => _suppressEffectFieldSync = false); }
        }

        private void HideAllEditors()
        {
            if (RegionEditor != null) RegionEditor.IsVisible = false;
            if (HapticEventEditor != null) HapticEventEditor.IsVisible = false;
            if (RuleEditor != null) RuleEditor.IsVisible = false;
            if (FlashEffectEditor != null) FlashEffectEditor.IsVisible = false;
            if (BubbleEffectEditor != null) BubbleEffectEditor.IsVisible = false;
            if (SubliminalEffectEditor != null) SubliminalEffectEditor.IsVisible = false;
            if (OverlayEffectEditor != null) OverlayEffectEditor.IsVisible = false;
            if (SpeakEffectEditor != null) SpeakEffectEditor.IsVisible = false;
        }

        /// <summary>
        /// A withheld overlay kind is hidden from <c>CmbOverlayKind</c>, but creator content
        /// authored before it was withheld may already carry one. Without this the picker would
        /// fall through to index 0 and display "Pink Filter" over that effect - a lie the next edit
        /// would bake into the file. So inject a clearly-labelled entry for the current value only,
        /// and drop it again once the selection moves to a live kind.
        /// </summary>
        private void SyncWithheldOverlayKindItem(string? kind)
        {
            for (int i = CmbOverlayKind.Items.Count - 1; i >= 0; i--)
            {
                if (CmbOverlayKind.Items[i] is ComboBoxItem stale
                    && OverlayKinds.IsWithheld(stale.Tag as string))
                {
                    CmbOverlayKind.Items.RemoveAt(i);
                }
            }
            if (!OverlayKinds.IsWithheld(kind)) return;

            CmbOverlayKind.Items.Add(new ComboBoxItem
            {
                Content = kind == OverlayKinds.BrainDrainMelt
                    ? "Brain Melt (unavailable)"
                    : "Brain Drain (unavailable)",
                Tag = kind
            });
        }

        private void SelectOverlayKindCombo(string? kind)
        {
            if (CmbOverlayKind == null) return;
            SyncWithheldOverlayKindItem(kind);
            foreach (var raw in CmbOverlayKind.Items)
            {
                if (raw is ComboBoxItem cbi && (cbi.Tag as string) == (kind ?? OverlayKinds.PinkFilter))
                {
                    CmbOverlayKind.SelectedItem = cbi;
                    return;
                }
            }
            CmbOverlayKind.SelectedIndex = 0;
        }

        // -- Speak effect combos --------------------------------------------------

        private static void SelectComboByTag(ComboBox? combo, string? tag)
        {
            if (combo == null) return;
            foreach (var raw in combo.Items)
            {
                if (raw is ComboBoxItem cbi && (cbi.Tag as string) == tag) { combo.SelectedItem = cbi; return; }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static string? SelectedTag(ComboBox combo)
            => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

        // Interval row only matters for intermittent cues; hold-mode row only for
        // "until satisfied" completion.
        private void ApplySpeakConditionalVisibility()
        {
            if (_selectedEffect == null) return;
            bool intermittent = _selectedEffect.EffectSpeakCueMode == SpeakCueMode.Intermittent;
            if (LblSpeakInterval != null) LblSpeakInterval.IsVisible = intermittent;
            if (TxtSpeakInterval != null) TxtSpeakInterval.IsVisible = intermittent;

            bool untilSatisfied = _selectedEffect.EffectSpeakCompletion == SpeakCompletion.UntilSatisfied;
            if (LblSpeakHold != null) LblSpeakHold.IsVisible = untilSatisfied;
            if (CmbSpeakHold != null) CmbSpeakHold.IsVisible = untilSatisfied;
        }

        private void CmbSpeakCueMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            if (Enum.TryParse<SpeakCueMode>(SelectedTag(CmbSpeakCueMode), out var mode))
                _selectedEffect.EffectSpeakCueMode = mode;
            ApplySpeakConditionalVisibility();
            MarkDirty();
        }

        private void CmbSpeakReps_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            if (int.TryParse(SelectedTag(CmbSpeakReps), NumberStyles.Integer, CultureInfo.InvariantCulture, out var reps))
                _selectedEffect.EffectSpeakRequiredReps = Math.Clamp(reps, 1, 5);
            MarkDirty();
        }

        private void CmbSpeakCompletion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            if (Enum.TryParse<SpeakCompletion>(SelectedTag(CmbSpeakCompletion), out var c))
                _selectedEffect.EffectSpeakCompletion = c;
            ApplySpeakConditionalVisibility();
            MarkDirty();
        }

        private void CmbSpeakHold_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            if (Enum.TryParse<SpeakHoldMode>(SelectedTag(CmbSpeakHold), out var h))
                _selectedEffect.EffectSpeakHoldMode = h;
            MarkDirty();
        }

        // -- Effect editor field syncing ------------------------------------------

        private void EffectField_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            try
            {
                if (ReferenceEquals(sender, TxtFlashDuration) && TryParseInt(TxtFlashDuration.Text ?? "", out var fd))
                    _selectedEffect.EffectDurationMs = Math.Max(50, fd);
                else if ((ReferenceEquals(sender, TxtFlashStart) || ReferenceEquals(sender, TxtBubbleStart)
                          || ReferenceEquals(sender, TxtSubliminalStart) || ReferenceEquals(sender, TxtOverlayStart)
                          || ReferenceEquals(sender, TxtSpeakStart))
                         && TryParseDouble(((TextBox)sender!).Text ?? "", out var es))
                    _selectedEffect.Start = Math.Clamp(es, 0, _totalSeconds > 0 ? _totalSeconds : double.MaxValue);
                else if (ReferenceEquals(sender, TxtBubbleWindow) && TryParseDouble(TxtBubbleWindow.Text ?? "", out var bw))
                    _selectedEffect.EffectDurationMs = (int)Math.Max(50, bw * 1000);
                else if (ReferenceEquals(sender, TxtSubliminalText))
                    _selectedEffect.EffectText = TxtSubliminalText.Text;
                else if (ReferenceEquals(sender, TxtSubliminalDuration) && TryParseInt(TxtSubliminalDuration.Text ?? "", out var sd))
                    _selectedEffect.EffectDurationMs = Math.Max(50, sd);
                else if (ReferenceEquals(sender, TxtOverlayDuration) && TryParseInt(TxtOverlayDuration.Text ?? "", out var od))
                    _selectedEffect.EffectDurationMs = Math.Max(50, od);
                else if (ReferenceEquals(sender, TxtSpeakLength) && TryParseDouble(TxtSpeakLength.Text ?? "", out var sl))
                    _selectedEffect.EffectDurationMs = (int)Math.Max(500, sl * 1000);
                else if (ReferenceEquals(sender, TxtSpeakTarget))
                    _selectedEffect.EffectSpeakTarget = TxtSpeakTarget.Text;
                else if (ReferenceEquals(sender, TxtSpeakCue))
                    _selectedEffect.EffectSpeakCue = TxtSpeakCue.Text;
                else if (ReferenceEquals(sender, TxtSpeakInterval) && TryParseInt(TxtSpeakInterval.Text ?? "", out var si))
                    _selectedEffect.EffectSpeakCueIntervalMs = Math.Max(80, si);
                else if (ReferenceEquals(sender, TxtSpeakCorrect))
                    _selectedEffect.EffectSpeakCorrectMessage = TxtSpeakCorrect.Text;
                else if (ReferenceEquals(sender, TxtSpeakIncorrect))
                    _selectedEffect.EffectSpeakIncorrectMessage = TxtSpeakIncorrect.Text;

                // Mirror EffectDurationMs into Duration so the timeline segment width stays in
                // sync with the textbox value.
                _selectedEffect.Duration = _selectedEffect.EffectDurationMs / 1000.0;
                MarkDirty();
                RebuildEffectVisuals();
                ScheduleValidation();
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: EffectField sync error: {Error}", ex.Message);
            }
        }

        private void SliderBubbleIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            _selectedEffect.EffectIntensity = Math.Clamp(e.NewValue, 0, 1);
            MarkDirty();
        }

        // Shared by the flash + subliminal "Don't buzz on pop" checkboxes.
        private void ChkEffectSuppressHaptic_Changed(object? sender, RoutedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            _selectedEffect.EffectSuppressHaptic = (sender as CheckBox)?.IsChecked ?? false;
            MarkDirty();
        }

        private void SliderOverlayOpacity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            var v = Math.Clamp(e.NewValue, 0, 1);
            // Flat opacity always tracks this slider; when ramping it's also the start.
            _selectedEffect.EffectOpacity = v;
            if (ChkOverlayRamp.IsChecked == true)
                _selectedEffect.EffectOpacityStart = v;
            MarkDirty();
        }

        private void SliderOverlayOpacityEnd_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            if (ChkOverlayRamp.IsChecked == true)
                _selectedEffect.EffectOpacityEnd = Math.Clamp(e.NewValue, 0, 1);
            MarkDirty();
        }

        private void ChkOverlayRamp_Changed(object? sender, RoutedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            bool ramp = ChkOverlayRamp.IsChecked == true;
            if (ramp)
            {
                // Seed start = current flat opacity, end = current end-slider value.
                _selectedEffect.EffectOpacityStart = Math.Clamp(SliderOverlayOpacity.Value, 0, 1);
                _selectedEffect.EffectOpacityEnd = Math.Clamp(SliderOverlayOpacityEnd.Value, 0, 1);
            }
            else
            {
                // Drop the ramp -> fall back to flat EffectOpacity.
                _selectedEffect.EffectOpacityStart = null;
                _selectedEffect.EffectOpacityEnd = null;
            }
            ApplyOverlayRampVisibility(ramp);
            MarkDirty();
        }

        private void ApplyOverlayRampVisibility(bool ramp)
        {
            LblOverlayOpacityEnd.IsVisible = ramp;
            SliderOverlayOpacityEnd.IsVisible = ramp;
            LblOverlayOpacity.Text = ramp ? "Start opacity" : "Opacity";
        }

        private void CmbOverlayKind_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            if (CmbOverlayKind.SelectedItem is ComboBoxItem cbi && cbi.Tag is string kind)
            {
                _selectedEffect.EffectOverlayKind = kind;
                MarkDirty();
            }
        }

        private void BtnDeleteEffect_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedEffect == null) return;
            try
            {
                PushUndoSnapshot();
                _enhancement.TimelineItems.Remove(_selectedEffect);
                _selectedEffect = null;
                MarkDirty();
                RebuildEffectVisuals();
                HideAllEditors();
                if (SelectedPlaceholder != null) SelectedPlaceholder.IsVisible = true;
                RefreshRulesList();
                ScheduleValidation();
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: delete effect error: {Error}", ex.Message);
            }
        }

        // -- Creator lock toggle --------------------------------------------------

        private void BtnCreatorLockToggle_Click(object? sender, RoutedEventArgs e)
        {
            _creatorLocked = BtnCreatorLockToggle.IsChecked == true;
            UpdateCreatorLockUi();
        }

        private void UpdateCreatorLockUi()
        {
            if (TxtMetaCreator == null || BtnCreatorLockToggle == null) return;
            BtnCreatorLockToggle.IsChecked = _creatorLocked;
            BtnCreatorLockToggle.Content = _creatorLocked ? "🔒" : "🔓";
            TxtMetaCreator.IsReadOnly = _creatorLocked;
            TxtMetaCreator.Foreground = _creatorLocked ? Res("TextDimBrush") : Res("TextLightBrush");
        }

        // -- HypnoTube auto-fill --------------------------------------------------

        private async Task TryAutoFillFromHtAsync(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            try
            {
                // Cancel + dispose the previous CTS so back-to-back URL pastes don't accumulate
                // orphaned token sources holding kernel handles.
                var oldCts = _htFetchCts;
                _htFetchCts = new CancellationTokenSource();
                try { oldCts?.Cancel(); oldCts?.Dispose(); } catch { }

                var meta = await HtMetadataFetcher.FetchAsync(source!, _htFetchCts.Token);
                if (meta == null) return;
                // Window may have closed during the network round-trip; touching the TextBoxes
                // after teardown throws. (WPF also checked Dispatcher.HasShutdownStarted, which
                // has no Avalonia twin - IsLoaded covers the case that mattered.)
                if (!IsLoaded) return;
                ApplyHtMetadata(meta);
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: HT auto-fill error: {Error}", ex.Message);
            }
        }

        private void ApplyHtMetadata(HtVideoMetadata meta)
        {
            // Don't suppress the dirty flag - if auto-fill actually changes metadata, the user
            // should see the marker and get the unsaved-changes prompt on close.
            bool changed = false;
            try
            {
                // Creator: only fill if empty. The pre-fix code unconditionally overwrote whatever
                // the user typed AND auto-locked the field.
                if (!string.IsNullOrEmpty(meta.Uploader)
                    && string.IsNullOrWhiteSpace(TxtMetaCreator.Text))
                {
                    TxtMetaCreator.Text = meta.Uploader;
                    _enhancement.Metadata.Creator = meta.Uploader;
                    _creatorLocked = true;
                    UpdateCreatorLockUi();
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(TxtMetaName.Text) && !string.IsNullOrEmpty(meta.Title))
                {
                    TxtMetaName.Text = meta.Title;
                    _enhancement.Metadata.Name = meta.Title;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(TxtMetaDescription.Text) && !string.IsNullOrEmpty(meta.Description))
                {
                    TxtMetaDescription.Text = meta.Description;
                    _enhancement.Metadata.Description = meta.Description;
                    changed = true;
                }
                if (meta.Tags != null && meta.Tags.Count > 0)
                {
                    var existing = (TxtMetaTags.Text ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    foreach (var tag in meta.Tags)
                    {
                        if (!string.IsNullOrEmpty(tag) && !existing.Contains(tag, StringComparer.OrdinalIgnoreCase))
                            existing.Add(tag);
                    }
                    var merged = string.Join(", ", existing);
                    if (merged != TxtMetaTags.Text)
                    {
                        TxtMetaTags.Text = merged;
                        _enhancement.Metadata.Tags = existing;
                        changed = true;
                    }
                }
                UpdateTitle();
            }
            finally
            {
                if (changed) MarkDirty();
            }
        }

        // -- Rule indicators on timeline ------------------------------------------
        // TimeReached rules have no Region (so RebuildRegionVisuals skips them) and aren't Effects
        // either. Without a dedicated visual they were invisible on the timeline. This renders each
        // as a thin orange pin: a vertical dashed line at the trigger time topped with a small
        // pennant, click-to-select, with a wider transparent hit area.

        private readonly List<Control> _ruleVisuals = new();
        private static readonly Color RulePinColor = Color.FromRgb(0xFF, 0x8C, 0x00);

        private void RebuildRuleVisuals()
        {
            try
            {
                foreach (var v in _ruleVisuals)
                {
                    try { TimelineCanvas.Children.Remove(v); } catch { }
                }
                _ruleVisuals.Clear();

                var w = TimelineCanvas.Bounds.Width;
                var h = TimelineCanvas.Bounds.Height;
                if (w <= 0 || h <= 0 || _totalSeconds <= 0) return;

                int idx = 0;
                foreach (var rule in _enhancement.Rules)
                {
                    idx++;
                    if (rule?.Trigger is not TimeReachedTrigger tr) continue;
                    BuildRulePin(rule, tr, idx, w, h);
                }
                EnsurePlayheadOnTop();
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: RebuildRuleVisuals error: {Error}", ex.Message);
            }
        }

        private void BuildRulePin(EnhancementRule rule, TimeReachedTrigger tr, int oneBasedIndex,
            double canvasWidth, double canvasHeight)
        {
            double x = (Math.Max(0, tr.Time) / _totalSeconds) * canvasWidth;
            bool isSelected = rule == _selectedRule;

            var brush = new SolidColorBrush(RulePinColor);
            var selStroke = isSelected ? Brushes.White : null;

            // Vertical pin line
            var line = new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, canvasHeight),
                Stroke = brush,
                StrokeThickness = isSelected ? 2.5 : 1.5,
                StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                IsHitTestVisible = false,
                ZIndex = 9
            };
            TimelineCanvas.Children.Add(line);
            _ruleVisuals.Add(line);

            // Flag at top - a small filled triangle (right-pointing pennant) so it reads apart from
            // region rectangles and effect dots.
            var flag = new Polygon
            {
                Points = new global::Avalonia.Collections.AvaloniaList<Point>
                {
                    new Point(x, 2),
                    new Point(x + 12, 6),
                    new Point(x, 10)
                },
                Fill = brush,
                Stroke = selStroke,
                StrokeThickness = isSelected ? 1.5 : 0,
                IsHitTestVisible = false,
                ZIndex = 10
            };
            TimelineCanvas.Children.Add(flag);
            _ruleVisuals.Add(flag);

            // Wider transparent hit-rect so the user can click near the pin without pixel-precise
            // aiming.
            var hit = new Rectangle
            {
                Width = 14,
                Height = canvasHeight,
                Fill = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = rule,
                ZIndex = 11
            };
            ToolTip.SetTip(hit, $"Rule #{oneBasedIndex} · time {tr.Time:0.##}s");
            Canvas.SetLeft(hit, x - 7);
            Canvas.SetTop(hit, 0);
            hit.PointerPressed += (s, e) =>
            {
                if (!e.GetCurrentPoint(hit).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                SelectRule(rule);
            };
            TimelineCanvas.Children.Add(hit);
            _ruleVisuals.Add(hit);
        }

        // -- Helpers --------------------------------------------------------------

        private static bool TryParseInt(string s, out int v)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        private static bool TryParseDouble(string s, out double v)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        /// <summary>
        /// ponytail: needs TutorialEventBus (App-level service in the WPF head). The editor emitted
        /// "EffectAdded" / "RuleAdded" so the interactive tutorial could advance a step; nothing in
        /// the Avalonia head listens yet, so the calls are kept as named no-ops rather than deleted.
        /// </summary>
        private static void EmitTutorialEvent(string name) { _ = name; }
    }
}
