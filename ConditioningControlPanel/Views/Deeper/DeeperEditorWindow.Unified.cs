using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Microsoft.Win32;

namespace ConditioningControlPanel.Views.Deeper
{
    /// <summary>
    /// New-design pieces of the Deeper editor: unified timeline (Effect dots +
    /// Rule bands rendered alongside the legacy Region/HapticEvent visuals),
    /// right-click context menu, hero buttons, HypnoTube auto-fill, Creator
    /// lock toggle, and the four non-haptic effect editor panels.
    /// </summary>
    public partial class DeeperEditorWindow
    {
        // -- State (new) -----------------------------------------------------------

        private TimelineItem? _selectedEffect;
        private double _rightClickSeconds;
        private bool _creatorLocked;
        private bool _suppressEffectFieldSync;
        private CancellationTokenSource? _htFetchCts;

        // Effect-type colors used by both the timeline dots and the picker labels.
        private static readonly System.Collections.Generic.Dictionary<string, string> EffectColors =
            new()
            {
                [EffectTypes.Haptic]     = "#7B5CFF",
                [EffectTypes.Flash]      = "#FFC85C",
                [EffectTypes.Bubble]     = "#5CC8FF",
                [EffectTypes.Subliminal] = "#FF69B4",
                [EffectTypes.Overlay]    = "#5CFFB7",
            };

        // -- Right-click context menu --------------------------------------------

        /// <summary>
        /// Captures the click position before the framework opens the context
        /// menu. We don't set <c>e.Handled</c> here so WPF's automatic context
        /// menu opening still fires; the menu items read <see cref="_rightClickSeconds"/>
        /// to compute the drop location.
        /// </summary>
        private void TimelineCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                _rightClickSeconds = MouseToSeconds(e);

                // Move the playhead to the click point. Most users right-clicking
                // on the timeline want to drop something there; seeking now keeps
                // the playback cursor in sync with their intent (and the hero
                // buttons remain the precise way to drop at the existing time).
                if (_totalSeconds > 0)
                {
                    var frac = Math.Clamp(_rightClickSeconds / _totalSeconds, 0, 1);
                    SeekToFraction(frac);
                }
                else
                {
                    // No duration yet (HT WebView2 hasn't reported length, or
                    // the media isn't loaded). Move the playhead visually so
                    // the user gets feedback that their click registered.
                    var pt = e.GetPosition(TimelineCanvas);
                    PlayheadLine.X1 = pt.X;
                    PlayheadLine.X2 = pt.X;
                }

                // Attach (or re-attach) the context menu resource so the audio-
                // mode hide-of-video-only triggers refreshes per open.
                var menu = (ContextMenu)FindResource("TimelineCtxMenu");
                ApplyAudioModeToContextMenu(menu);
                TimelineCanvas.ContextMenu = menu;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: ctx menu prep error: {Error}", ex.Message);
            }
        }

        private void ApplyAudioModeToContextMenu(ContextMenu menu)
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
                    if (videoOnly) sub.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        private void CtxAddEffectHaptic_Click(object sender, RoutedEventArgs e)     => AddEffectAt(EffectTypes.Haptic, _rightClickSeconds);
        private void CtxAddEffectFlash_Click(object sender, RoutedEventArgs e)      => AddEffectAt(EffectTypes.Flash, _rightClickSeconds);
        private void CtxAddEffectBubble_Click(object sender, RoutedEventArgs e)     => AddEffectAt(EffectTypes.Bubble, _rightClickSeconds);
        private void CtxAddEffectSubliminal_Click(object sender, RoutedEventArgs e) => AddEffectAt(EffectTypes.Subliminal, _rightClickSeconds);
        private void CtxAddEffectOverlay_Click(object sender, RoutedEventArgs e)    => AddEffectAt(EffectTypes.Overlay, _rightClickSeconds);

        private void CtxAddRuleTimeReached_Click(object sender, RoutedEventArgs e)    => AddRuleAt(TriggerTypes.TimeReached, _rightClickSeconds);
        private void CtxAddRuleBandEntered_Click(object sender, RoutedEventArgs e)    => AddRuleAt(TriggerTypes.RegionEntered, _rightClickSeconds);
        private void CtxAddRuleBandExited_Click(object sender, RoutedEventArgs e)     => AddRuleAt(TriggerTypes.RegionExited, _rightClickSeconds);
        private void CtxAddRuleGazeTarget_Click(object sender, RoutedEventArgs e)     => AddRuleAt(TriggerTypes.GazeTarget, _rightClickSeconds);
        private void CtxAddRuleGazeAvoid_Click(object sender, RoutedEventArgs e)      => AddRuleAt(TriggerTypes.GazeAvoid, _rightClickSeconds);
        private void CtxAddRuleAttentionLost_Click(object sender, RoutedEventArgs e)  => AddRuleAt(TriggerTypes.AttentionLost, _rightClickSeconds);
        private void CtxAddRuleBlinkDetected_Click(object sender, RoutedEventArgs e)  => AddRuleAt(TriggerTypes.BlinkDetected, _rightClickSeconds);
        private void CtxAddRuleMouthOpen_Click(object sender, RoutedEventArgs e)      => AddRuleAt(TriggerTypes.MouthOpen, _rightClickSeconds);

        private void BtnAddEffectHero_Click(object sender, RoutedEventArgs e)
        {
            // Default to haptic at the playhead. For non-haptic effects, the user
            // uses right-click for the granular menu; the hero button stays simple.
            AddEffectAt(EffectTypes.Haptic, _currentSeconds);
        }

        private void BtnAddRuleHero_Click(object sender, RoutedEventArgs e)
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
                    // Haptic effects continue to live on the legacy HapticTrack so
                    // existing visualization, drag-shift, and curve editor wiring
                    // keep working unchanged. Back-projection on save round-trips
                    // them into TimelineItems for older clients.
                    AddHapticEventAt(seconds);
                    try { TutorialEventBus.Emit("EffectAdded"); } catch { }
                    return;
                }

                PushUndoSnapshot();

                var defaultDurationMs = effectType switch
                {
                    EffectTypes.Bubble => 5000,
                    EffectTypes.Overlay => 3000,
                    EffectTypes.Subliminal => 200,
                    EffectTypes.Flash => 800,
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
                _enhancement.TimelineItems.Add(item);
                MarkDirty();
                RebuildEffectVisuals();
                SelectEffect(item);
                ScheduleValidation();
                try { TutorialEventBus.Emit("EffectAdded"); } catch { }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: AddEffectAt error: {Error}", ex.Message);
            }
        }

        private void AddHapticEventAt(double seconds)
        {
            PushUndoSnapshot();
            // Reuse the existing haptic track / event flow.
            var track = _enhancement.HapticTracks.FirstOrDefault();
            if (track == null)
            {
                track = new HapticTrack { Id = "primary" };
                _enhancement.HapticTracks.Add(track);
            }

            // Default 5 s per the user's spec when dropped fresh; clamp to total
            // and avoid overlap with neighbors by a thin margin.
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
                    App.Logger?.Debug("DeeperEditor: AddRuleAt skipped video-only trigger on audio enhancement: {T}", triggerType);
                    return;
                }

                seconds = Math.Max(0, seconds);
                double bandDuration = triggerType == TriggerTypes.TimeReached ? 0.0 : 5.0;
                if (_totalSeconds > 0 && bandDuration > 0)
                    bandDuration = Math.Min(bandDuration, Math.Max(0.5, _totalSeconds - seconds));

                PushUndoSnapshot();

                // Use existing region+rule scaffolding so the side-panel rule editor
                // (BuildTriggerFields/BuildActionFields) can stay unchanged.
                var rule = new EnhancementRule
                {
                    Trigger = BuildDefaultTrigger(triggerType, seconds),
                    Action = new NoOpEnhancementAction(),
                    CooldownMs = 1000,
                    Enabled = true
                };

                if (triggerType == TriggerTypes.TimeReached)
                {
                    // Point-style rule: no companion region — just a Rule that fires at the time.
                    _enhancement.Rules.Add(rule);
                    MarkDirty();
                    RebuildRuleVisuals();
                    SelectRule(rule);
                }
                else
                {
                    // Band-style rule: create a Region that the rule constrains to,
                    // so the user can drag-resize the band and the rule fires only
                    // inside it.
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
                    // Wire the trigger's RegionId to the same region so validation
                    // passes on first save/preview. BuildDefaultTrigger leaves it
                    // empty because it doesn't know which region we're about to
                    // create; setting it here keeps the band-style rule fully
                    // self-contained.
                    if (rule.Trigger is RegionEnteredTrigger reTrig) reTrig.RegionId = region.Id;
                    else if (rule.Trigger is RegionExitedTrigger rxTrig) rxTrig.RegionId = region.Id;
                    _enhancement.Rules.Add(rule);
                    MarkDirty();
                    RebuildRegionVisuals();
                    // Select the Rule (not the Region) so the user immediately
                    // sees the rule editor - including the GazeTarget/GazeAvoid
                    // rect inputs, 3x3 quick-region preset grid, and "Pick on
                    // video…" picker button. Selecting the region first hid all
                    // of that behind a band the user had to discover. They can
                    // still click the band on the timeline later to edit
                    // region details (label / color / drag-resize).
                    SelectRule(rule);
                }
                ScheduleValidation();
                try { TutorialEventBus.Emit("RuleAdded"); } catch { }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: AddRuleAt error: {Error}", ex.Message);
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

        // -- Effect TimelineItem visualization ------------------------------------

        private readonly System.Collections.Generic.List<System.Windows.Shapes.Shape> _effectVisuals = new();

        /// <summary>
        /// Renders Effect TimelineItems (non-haptic — haptic still uses the legacy
        /// haptic-track visuals) as dots on the unified timeline. Tear-down/rebuild
        /// keeps the code simple; the playhead timer and browser-time-changed
        /// handlers don't fire mid-drag for effect dots since dot drag isn't yet
        /// implemented (clicking selects only).
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

                var w = TimelineCanvas.ActualWidth;
                var h = TimelineCanvas.ActualHeight;
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
                App.Logger?.Debug("DeeperEditor: RebuildEffectVisuals error: {Error}", ex.Message);
            }
        }

        private void BuildEffectDot(TimelineItem item, double canvasWidth, double canvasHeight)
        {
            // One-shot effects (Flash, Subliminal) render as a small dot — they
            // don't have a meaningful on-screen duration the user would drag.
            // Ongoing effects (Bubble, Overlay) render as draggable, resizable
            // segments whose width matches their Duration. Haptic uses the
            // legacy haptic-track lane and never reaches this method.
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
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = brush,
                Stroke = isSelected ? Brushes.White : Brushes.Transparent,
                StrokeThickness = isSelected ? 2 : 0,
                Cursor = Cursors.Hand,
                Tag = item,
                ToolTip = $"{item.EffectType} @ {item.Start:0.##}s"
            };
            double x = (item.Start / _totalSeconds) * canvasWidth - 6;
            double y = canvasHeight - 18;
            Canvas.SetLeft(dot, x);
            Canvas.SetTop(dot, y);
            Panel.SetZIndex(dot, 10);

            dot.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                bool ctrl = IsCtrlDown();
                HandleSelectionClick(item);
                if (ctrl)
                {
                    // Pure toggle — refresh all lanes so the new selection-set
                    // membership is reflected. Don't promote to primary.
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
            var fill = System.Windows.Media.Color.FromArgb(140, color.R, color.G, color.B);
            var isSelected = item == _selectedEffect || IsInSelectionSet(item);

            // Segment width tracks Duration; minimum 8px so a near-zero segment
            // is still clickable for selection.
            double startX = Math.Max(0, (item.Start / _totalSeconds) * canvasWidth);
            double endX = Math.Min(canvasWidth, ((item.Start + Math.Max(0, item.Duration)) / _totalSeconds) * canvasWidth);
            double width = Math.Max(8, endX - startX);
            double y = canvasHeight - 22;          // dedicated effect lane near the bottom
            double height = 18;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = new System.Windows.Media.SolidColorBrush(fill),
                Stroke = new System.Windows.Media.SolidColorBrush(color),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = Cursors.SizeAll,
                Tag = item,
                ToolTip = $"{item.EffectType} @ {item.Start:0.##}s · {item.Duration:0.##}s"
            };

            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, y);
            Panel.SetZIndex(rect, 10);

            rect.MouseLeftButtonDown += EffectRect_MouseLeftButtonDown;
            rect.MouseMove += EffectRect_MouseMove;

            TimelineCanvas.Children.Add(rect);
            _effectVisuals.Add(rect);
        }

        // Cursor feedback near segment edges so the user can tell resize from drag-move.
        private void EffectRect_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode != DragMode.None) return;
            if (sender is not System.Windows.Shapes.Rectangle r) return;
            var pos = e.GetPosition(r);
            r.Cursor = (pos.X <= EdgeResizePx || pos.X >= r.ActualWidth - EdgeResizePx)
                ? Cursors.SizeWE
                : Cursors.SizeAll;
        }

        private void EffectRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Rectangle r || r.Tag is not TimelineItem item)
                return;

            // Snapshot pos + width BEFORE selecting — SelectEffect rebuilds visuals
            // which detaches `r` from the visual tree, after which e.GetPosition(r)
            // returns ~(0,0) and trips the left-edge resize check unconditionally.
            var pos = e.GetPosition(r);
            var rectWidth = r.ActualWidth;
            bool ctrl = IsCtrlDown();
            HandleSelectionClick(item);
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
            SelectEffect(item);

            _draggedEffect = item;
            _effectDragOriginalDuration = Math.Max(0, item.Duration);
            BeginMultiDragCapture();

            if (pos.X <= EdgeResizePx)
            {
                _dragMode = DragMode.ResizeEffectStart;
            }
            else if (pos.X >= rectWidth - EdgeResizePx)
            {
                _dragMode = DragMode.ResizeEffectEnd;
            }
            else
            {
                _dragMode = DragMode.DragEffect;
                _effectDragOffsetSec = MouseToSeconds(e) - item.Start;
            }
            TimelineCanvas.CaptureMouse();
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
        }

        private void UpdateSelectedSidePanelForEffect()
        {
            HideAllEditors();
            if (_selectedEffect == null)
            {
                if (SelectedPlaceholder != null) SelectedPlaceholder.Visibility = Visibility.Visible;
                return;
            }
            if (SelectedPlaceholder != null) SelectedPlaceholder.Visibility = Visibility.Collapsed;

            _suppressEffectFieldSync = true;
            try
            {
                switch (_selectedEffect.EffectType)
                {
                    case EffectTypes.Flash:
                        FlashEffectEditor.Visibility = Visibility.Visible;
                        TxtFlashDuration.Text = _selectedEffect.EffectDurationMs.ToString(CultureInfo.InvariantCulture);
                        break;
                    case EffectTypes.Bubble:
                        BubbleEffectEditor.Visibility = Visibility.Visible;
                        TxtBubbleWindow.Text = (_selectedEffect.EffectDurationMs / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
                        SliderBubbleIntensity.Value = _selectedEffect.EffectIntensity;
                        break;
                    case EffectTypes.Subliminal:
                        SubliminalEffectEditor.Visibility = Visibility.Visible;
                        TxtSubliminalText.Text = _selectedEffect.EffectText ?? "";
                        TxtSubliminalDuration.Text = _selectedEffect.EffectDurationMs.ToString(CultureInfo.InvariantCulture);
                        break;
                    case EffectTypes.Overlay:
                        OverlayEffectEditor.Visibility = Visibility.Visible;
                        SelectOverlayKindCombo(_selectedEffect.EffectOverlayKind);
                        TxtOverlayDuration.Text = _selectedEffect.EffectDurationMs.ToString(CultureInfo.InvariantCulture);
                        SliderOverlayOpacity.Value = _selectedEffect.EffectOpacity;
                        break;
                    default:
                        if (SelectedPlaceholder != null) SelectedPlaceholder.Visibility = Visibility.Visible;
                        break;
                }
            }
            finally { _suppressEffectFieldSync = false; }
        }

        private void HideAllEditors()
        {
            if (RegionEditor != null) RegionEditor.Visibility = Visibility.Collapsed;
            if (HapticEventEditor != null) HapticEventEditor.Visibility = Visibility.Collapsed;
            if (RuleEditor != null) RuleEditor.Visibility = Visibility.Collapsed;
            if (FlashEffectEditor != null) FlashEffectEditor.Visibility = Visibility.Collapsed;
            if (BubbleEffectEditor != null) BubbleEffectEditor.Visibility = Visibility.Collapsed;
            if (SubliminalEffectEditor != null) SubliminalEffectEditor.Visibility = Visibility.Collapsed;
            if (OverlayEffectEditor != null) OverlayEffectEditor.Visibility = Visibility.Collapsed;
        }

        private void SelectOverlayKindCombo(string? kind)
        {
            if (CmbOverlayKind == null) return;
            foreach (ComboBoxItem cbi in CmbOverlayKind.Items)
            {
                if ((cbi.Tag as string) == (kind ?? OverlayKinds.PinkFilter))
                {
                    CmbOverlayKind.SelectedItem = cbi;
                    return;
                }
            }
            CmbOverlayKind.SelectedIndex = 0;
        }

        // -- Effect editor field syncing ------------------------------------------

        private void EffectField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            try
            {
                if (sender == TxtFlashDuration && TryParseInt(TxtFlashDuration.Text, out var fd))
                    _selectedEffect.EffectDurationMs = Math.Max(50, fd);
                else if (sender == TxtBubbleWindow && TryParseDouble(TxtBubbleWindow.Text, out var bw))
                    _selectedEffect.EffectDurationMs = (int)Math.Max(50, bw * 1000);
                else if (sender == TxtSubliminalText)
                    _selectedEffect.EffectText = TxtSubliminalText.Text;
                else if (sender == TxtSubliminalDuration && TryParseInt(TxtSubliminalDuration.Text, out var sd))
                    _selectedEffect.EffectDurationMs = Math.Max(50, sd);
                else if (sender == TxtOverlayDuration && TryParseInt(TxtOverlayDuration.Text, out var od))
                    _selectedEffect.EffectDurationMs = Math.Max(50, od);

                // Mirror EffectDurationMs into Duration so the timeline segment
                // width stays in sync with the textbox value.
                _selectedEffect.Duration = _selectedEffect.EffectDurationMs / 1000.0;
                MarkDirty();
                RebuildEffectVisuals();
                ScheduleValidation();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: EffectField sync error: {Error}", ex.Message);
            }
        }

        private void SliderBubbleIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            _selectedEffect.EffectIntensity = Math.Clamp(e.NewValue, 0, 1);
            MarkDirty();
        }

        private void SliderOverlayOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            _selectedEffect.EffectOpacity = Math.Clamp(e.NewValue, 0, 1);
            MarkDirty();
        }

        private void CmbOverlayKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEffectFieldSync || _selectedEffect == null) return;
            if (CmbOverlayKind.SelectedItem is ComboBoxItem cbi && cbi.Tag is string kind)
            {
                _selectedEffect.EffectOverlayKind = kind;
                MarkDirty();
            }
        }


        private void BtnDeleteEffect_Click(object sender, RoutedEventArgs e)
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
                if (SelectedPlaceholder != null) SelectedPlaceholder.Visibility = Visibility.Visible;
                ScheduleValidation();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: delete effect error: {Error}", ex.Message);
            }
        }

        // -- Creator lock toggle --------------------------------------------------

        private void BtnCreatorLockToggle_Click(object sender, RoutedEventArgs e)
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
            TxtMetaCreator.Foreground = _creatorLocked
                ? (System.Windows.Media.Brush)FindResource("TextDimBrush")
                : (System.Windows.Media.Brush)FindResource("TextLightBrush");
        }

        // -- HypnoTube auto-fill --------------------------------------------------

        private async Task TryAutoFillFromHtAsync(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            try
            {
                // Cancel + dispose the previous CTS so back-to-back URL pastes
                // don't accumulate orphaned token sources holding kernel handles.
                var oldCts = _htFetchCts;
                _htFetchCts = new CancellationTokenSource();
                try { oldCts?.Cancel(); oldCts?.Dispose(); } catch { }

                var meta = await HtMetadataFetcher.FetchAsync(source!, _htFetchCts.Token);
                if (meta == null) return;
                // Window may have closed during the network round-trip; touching
                // the TextBoxes after teardown throws.
                if (!IsLoaded || Dispatcher.HasShutdownStarted) return;
                ApplyHtMetadata(meta);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: HT auto-fill error: {Error}", ex.Message);
            }
        }

        private void ApplyHtMetadata(HtVideoMetadata meta)
        {
            // Don't suppress the dirty flag — if auto-fill actually changes
            // metadata, the user should see the * marker and get the
            // unsaved-changes prompt on close. Without this, silent metadata
            // mutations were lost when users closed the editor without an
            // explicit save.
            bool changed = false;
            try
            {
                // Creator: only fill if empty (mirrors Name/Description below).
                // The previous code unconditionally overwrote whatever the user
                // typed AND auto-locked the field, so editing the URL silently
                // clobbered hand-entered credits.
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
        // TimeReached rules don't have a Region (so RebuildRegionVisuals skips
        // them), and they aren't Effects either. Without a dedicated visual they
        // were invisible on the timeline — the user could create one but had no
        // way to find or click it again. This renders each TimeReached rule as
        // a thin orange pin: a vertical line at the trigger time topped with a
        // small flag, click-to-select, with a wider transparent hit area.

        private readonly System.Collections.Generic.List<System.Windows.UIElement> _ruleVisuals = new();
        private static readonly System.Windows.Media.Color RulePinColor =
            System.Windows.Media.Color.FromRgb(0xFF, 0x8C, 0x00);

        private void RebuildRuleVisuals()
        {
            try
            {
                foreach (var v in _ruleVisuals)
                {
                    try { TimelineCanvas.Children.Remove(v); } catch { }
                }
                _ruleVisuals.Clear();

                var w = TimelineCanvas.ActualWidth;
                var h = TimelineCanvas.ActualHeight;
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
                App.Logger?.Debug("DeeperEditor: RebuildRuleVisuals error: {Error}", ex.Message);
            }
        }

        private void BuildRulePin(EnhancementRule rule, TimeReachedTrigger tr, int oneBasedIndex,
            double canvasWidth, double canvasHeight)
        {
            double x = (Math.Max(0, tr.Time) / _totalSeconds) * canvasWidth;
            bool isSelected = rule == _selectedRule;

            var brush = new System.Windows.Media.SolidColorBrush(RulePinColor);
            brush.Freeze();
            var selStroke = isSelected ? System.Windows.Media.Brushes.White : null;

            // Vertical pin line
            var line = new System.Windows.Shapes.Line
            {
                X1 = x, X2 = x,
                Y1 = 0, Y2 = canvasHeight,
                Stroke = brush,
                StrokeThickness = isSelected ? 2.5 : 1.5,
                StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            Panel.SetZIndex(line, 9);
            TimelineCanvas.Children.Add(line);
            _ruleVisuals.Add(line);

            // Flag at top — a small filled triangle (right-pointing pennant) so
            // it's distinguishable from region rectangles and effect dots.
            var flag = new System.Windows.Shapes.Polygon
            {
                Points = new System.Windows.Media.PointCollection
                {
                    new Point(x, 2),
                    new Point(x + 12, 6),
                    new Point(x, 10)
                },
                Fill = brush,
                Stroke = selStroke,
                StrokeThickness = isSelected ? 1.5 : 0,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(flag, 10);
            TimelineCanvas.Children.Add(flag);
            _ruleVisuals.Add(flag);

            // Wider transparent hit-rect so the user can click on or near the
            // pin without pixel-precise aiming.
            var hit = new System.Windows.Shapes.Rectangle
            {
                Width = 14,
                Height = canvasHeight,
                Fill = System.Windows.Media.Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = rule,
                ToolTip = $"Rule #{oneBasedIndex} · time {tr.Time:0.##}s"
            };
            Canvas.SetLeft(hit, x - 7);
            Canvas.SetTop(hit, 0);
            Panel.SetZIndex(hit, 11);
            hit.MouseLeftButtonDown += (s, e) =>
            {
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
    }
}
