using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// PORTED from the tail of ConditioningControlPanel/Views/Deeper/DeeperEditorWindow.xaml.cs -
    /// the haptic inspector, the curve editor, the rule inspector and every programmatically built
    /// trigger/action field. It is a SEPARATE FILE here only because the WPF main file is 5,159
    /// lines; no member was dropped, and the six WPF partials keep their own names alongside it.
    ///
    /// Deviations:
    ///   Style = (Style)FindResource(k)   -> Theme = (ControlTheme)this.FindResource(k)
    ///   Visibility                       -> IsVisible
    ///   ToolTip =                        -> ToolTip.SetTip(control, ...)
    ///   MouseLeftButtonDown/Move/Up      -> PointerPressed / PointerMoved / PointerReleased
    ///   Mouse capture on a handle        -> e.Pointer.Capture(handle)
    ///   ActualWidth/Height               -> Bounds.Width / Bounds.Height
    ///   CheckBox.Click                   -> IsCheckedChanged
    ///   MessageBox.Show                  -> Views/Dialogs/MessageDialog.ShowAsync, awaited. "Avalonia
    ///                                       has no MessageBox" was true when this file was written
    ///                                       and has not been since that dialog landed
    /// </summary>
    public partial class DeeperEditorWindow
    {
        private ControlTheme EditorTextBoxTheme => (ControlTheme)this.FindResource("EditorTextBox")!;
        private ControlTheme EditorLabelTheme => (ControlTheme)this.FindResource("EditorLabel")!;
        private ControlTheme EditorComboBoxTheme => (ControlTheme)this.FindResource("EditorComboBox")!;
        private ControlTheme EditorComboBoxItemTheme => (ControlTheme)this.FindResource("EditorComboBoxItem")!;

        private TextBox NewEditorTextBox(string text) => new()
        {
            Theme = EditorTextBoxTheme,
            Text = text,
        };

        private ComboBox NewEditorComboBox() => new()
        {
            Theme = EditorComboBoxTheme,
            ItemContainerTheme = EditorComboBoxItemTheme,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        private TextBlock NewEditorLabel(string text) => new()
        {
            Theme = EditorLabelTheme,
            Text = text,
        };

        // ---------------------------------------------------------------------------------
        // Haptic side panel
        // ---------------------------------------------------------------------------------

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
                CmbHapticTarget.SelectedIndex = HapticTargetIndex(_selectedHaptic.Target);

                var isCustom = _selectedHaptic.CustomPattern != null && _selectedHaptic.CustomPattern.Count > 0;
                if (isCustom)
                {
                    CmbHapticPattern.SelectedIndex = StockHapticPatterns.Names.Count; // "Custom..."
                    CurveEditorPanel.IsVisible = true;
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
                    CurveEditorPanel.IsVisible = false;
                }
            }
            finally
            {
                ClearWhenEventsDrained(() =>
                {
                    _suppressDirty = false;
                    _suppressPatternSync = false;
                });
            }
        }

        private void HapticField_TextChanged(object? sender, TextChangedEventArgs e)
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

        private void SliderHapticIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (TxtHapticIntensityValue != null)
                TxtHapticIntensityValue.Text = $"{(int)(e.NewValue * 100)}%";
            if (_suppressDirty || _selectedHaptic == null) return;
            _selectedHaptic.Intensity = Math.Clamp(e.NewValue, 0, 1);
            MarkDirty();
            RebuildHapticVisuals();
        }

        private void CmbHapticPattern_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressPatternSync || _suppressDirty || _selectedHaptic == null) return;
            var idx = CmbHapticPattern.SelectedIndex;
            if (idx < 0) return;
            if (idx < StockHapticPatterns.Names.Count)
            {
                _selectedHaptic.PatternName = StockHapticPatterns.Names[idx];
                _selectedHaptic.CustomPattern = null;
                CurveEditorPanel.IsVisible = false;
            }
            else
            {
                EnsureCurveSeed();
                _selectedHaptic.PatternName = null;
                CurveEditorPanel.IsVisible = true;
                RebuildCurveEditor();
            }
            MarkDirty();
            ScheduleValidation();
        }

        private void CmbHapticTarget_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressDirty || _selectedHaptic == null) return;
            _selectedHaptic.Target = HapticTargetAt(CmbHapticTarget.SelectedIndex);
            MarkDirty();
        }

        // ---------------------------------------------------------------------------------
        // Curve editor (the XAML CurveCanvas, for the selected haptic event)
        // ---------------------------------------------------------------------------------

        private void EnsureCurveSeed()
        {
            if (_selectedHaptic == null) return;
            if (_selectedHaptic.CustomPattern == null || _selectedHaptic.CustomPattern.Count < 2)
                _selectedHaptic.CustomPattern = StockHapticPatterns.SeedCustomFrom(_selectedHaptic.PatternName);
        }

        private void CurveCanvas_SizeChanged(object? sender, SizeChangedEventArgs e) => RebuildCurveEditor();

        private void RebuildCurveEditor()
        {
            if (CurveCanvas == null) return;
            CurveCanvas.Children.Clear();
            _curvePath = null;
            _curveHandles.Clear();

            if (_selectedHaptic?.CustomPattern == null || _selectedHaptic.CustomPattern.Count == 0) return;
            var w = CurveCanvas.Bounds.Width;
            var h = CurveCanvas.Bounds.Height;
            if (w <= 0 || h <= 0) return;

            // Background grid
            for (int i = 1; i <= 3; i++)
            {
                var y = h * i / 4.0;
                CurveCanvas.Children.Add(new Line
                {
                    StartPoint = new Point(0, y),
                    EndPoint = new Point(w, y),
                    Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = 0.5,
                    IsHitTestVisible = false
                });
            }

            // Polyline through the keyframes
            var pts = _selectedHaptic.CustomPattern;
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(KeyframeToCanvas(pts[0], w, h), false);
                for (int i = 1; i < pts.Count; i++)
                    ctx.LineTo(KeyframeToCanvas(pts[i], w, h));
                ctx.EndFigure(false);
            }
            _curvePath = new Path
            {
                Data = geom,
                Stroke = Res("DeeperAccentBrush"),
                StrokeThickness = 1.6,
                IsHitTestVisible = false
            };
            CurveCanvas.Children.Add(_curvePath);

            // Handles (fixed-X dots, drag intensity vertically)
            for (int i = 0; i < pts.Count; i++)
            {
                var pt = KeyframeToCanvas(pts[i], w, h);
                var dot = new Ellipse
                {
                    Width = 10, Height = 10,
                    Fill = Res("DeeperAccentBrush"),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.2,
                    Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                    Tag = i
                };
                Canvas.SetLeft(dot, pt.X - 5);
                Canvas.SetTop(dot, pt.Y - 5);
                dot.PointerPressed += CurveHandle_PointerPressed;
                dot.PointerMoved += CurveHandle_PointerMoved;
                dot.PointerReleased += CurveHandle_PointerReleased;
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

        private void CurveHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Ellipse el && el.Tag is int idx)
            {
                if (!e.GetCurrentPoint(el).Properties.IsLeftButtonPressed) return;
                _draggingCurveIndex = idx;
                e.Pointer.Capture(el);
                e.Handled = true;
            }
        }

        private void CurveHandle_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_draggingCurveIndex < 0 || _selectedHaptic?.CustomPattern == null) return;
            if (sender is not Ellipse el) return;
            var h = CurveCanvas.Bounds.Height;
            if (h <= 0) return;
            var y = e.GetPosition(CurveCanvas).Y;
            var v = Math.Clamp(1.0 - (y / h), 0, 1);
            var kf = _selectedHaptic.CustomPattern[_draggingCurveIndex];
            if (kf.Length > 1) kf[1] = v;
            Canvas.SetTop(el, Math.Clamp(y, 0, h) - 5);
            MarkDirty();
            RebuildCurveEditor();
        }

        private void CurveHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_draggingCurveIndex < 0) return;
            _draggingCurveIndex = -1;
            e.Pointer.Capture(null);
            ScheduleValidation();
        }

        private void BtnResetCurve_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedHaptic == null) return;
            PushUndoSnapshot();
            _selectedHaptic.CustomPattern = StockHapticPatterns.SeedCustomFrom(_selectedHaptic.PatternName);
            MarkDirty();
            RebuildCurveEditor();
        }

        /// <summary>ponytail: needs the haptics bus (App's device manager). The WPF handler fired a
        /// one-shot buzz on the selected pattern so the author could feel it.</summary>
        private void BtnTestHaptic_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("DeeperEditor: test-haptic needs the haptics device bus");

        private void BtnDeleteHaptic_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedHaptic == null || _selectedHapticTrack == null) return;
            PushUndoSnapshot();
            _selectedHapticTrack.Events.Remove(_selectedHaptic);
            // Remove a now-empty default track to keep the file clean.
            if (_selectedHapticTrack.Events.Count == 0 && _selectedHapticTrack.Id == DefaultTrackId)
                _enhancement.HapticTracks.Remove(_selectedHapticTrack);
            SelectNothing();
            MarkDirty();
            RebuildHapticVisuals();
            ScheduleValidation();
        }

        // ---------------------------------------------------------------------------------
        // Rules list
        // ---------------------------------------------------------------------------------

        private void RefreshRulesList()
        {
            // Refreshes the selection summary strip + the sidebar Items list. Every add / delete /
            // select / drag-end call site flows through here.
            UpdateSelectionSummary();
            BuildItemsList();
        }

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
            ActionTypes.NoOp          => Loc.Get("deeper_friendly_action_noop"),
            _                         => string.IsNullOrEmpty(type) ? "?" : type
        };

        // -- Help popups: list every trigger / action with its description ----
        // RESTORED. The note here said these "need a message box", which stopped being true when
        // Views/Dialogs/MessageDialog landed: three panels of user-facing help text were being
        // assembled in full and then written to the DEBUG LOG, so the three "?" buttons ran a
        // visible amount of work and showed the user nothing. MessageDialog scrolls and caps at
        // 640px, which is what these need - the trigger list is long. Argument order is WPF's
        // (message first, title second) re-stated as ShowAsync(owner, title, message).

        private async void BtnTriggerHelp_Click(object? sender, RoutedEventArgs e)
        {
            var isAudio = _enhancement?.MediaType == MediaTypes.Audio;
            var types = isAudio ? TriggerTypesForAudio : TriggerTypesForVideo;
            var sb = new StringBuilder();
            foreach (var t in types)
            {
                sb.Append("• ").AppendLine(FriendlyTriggerName(t));
                var desc = Loc.Get(TriggerDescriptionKey(t));
                if (!string.IsNullOrEmpty(desc) && desc != TriggerDescriptionKey(t))
                    sb.Append("  ").AppendLine(desc);
                sb.AppendLine();
            }
            await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_help_browse_triggers"),
                sb.ToString().TrimEnd());
        }

        private async void BtnActionHelp_Click(object? sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var a in AllActionTypes)
            {
                sb.Append("• ").AppendLine(FriendlyActionName(a));
                var desc = Loc.Get(ActionDescriptionKey(a));
                if (!string.IsNullOrEmpty(desc) && desc != ActionDescriptionKey(a))
                    sb.Append("  ").AppendLine(desc);
                sb.AppendLine();
            }
            await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_help_browse_actions"),
                sb.ToString().TrimEnd());
        }

        private async void BtnRegionHelp_Click(object? sender, RoutedEventArgs e)
            => await MessageDialog.ShowAsync(this, Loc.Get("deeper_editor_help_region"),
                Loc.Get("deeper_editor_help_region_body"));

        private void BtnDeleteRule_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedRule == null) return;
            PushUndoSnapshot();
            _enhancement.Rules.Remove(_selectedRule);
            SelectNothing();
            MarkDirty();
            RebuildRuleVisuals();
            ScheduleValidation();
        }

        // ---------------------------------------------------------------------------------
        // Rule editor population
        // ---------------------------------------------------------------------------------

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
        // NoOp first: rules created from the timeline default to NoOp, and PopulateRuleEditor falls
        // back to index 0 for unknown action types - both must display honestly as "Do nothing".
        private static readonly string[] AllActionTypes =
        {
            ActionTypes.NoOp,
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

                // Trigger type combo (filtered by media_type). ComboBoxItem so each entry shows a
                // friendly name + a hover tooltip with the long description; raw enum in Tag.
                var isAudio = _enhancement.MediaType == MediaTypes.Audio;
                var triggerOptions = isAudio ? TriggerTypesForAudio : TriggerTypesForVideo;
                CmbTriggerType.Items.Clear();
                int trigIdx = 0;
                for (int i = 0; i < triggerOptions.Length; i++)
                {
                    var rawType = triggerOptions[i];
                    var item = new ComboBoxItem
                    {
                        Theme = EditorComboBoxItemTheme,
                        Content = FriendlyTriggerName(rawType),
                        Tag = rawType,
                    };
                    ToolTip.SetTip(item, Loc.Get(TriggerDescriptionKey(rawType)));
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
                        Theme = EditorComboBoxItemTheme,
                        Content = FriendlyActionName(rawType),
                        Tag = rawType,
                    };
                    ToolTip.SetTip(item, Loc.Get(ActionDescriptionKey(rawType)));
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
            finally { ClearWhenEventsDrained(() => _suppressRuleSync = false); }
        }

        // Sync the rule editor's "Region details" sub-panel with whichever Region the current rule
        // constrains to. Hidden when the rule has no RegionConstraint or it doesn't resolve.
        private void PopulateRuleBandDetails()
        {
            if (RuleRegionDetails == null) return;
            var region = ResolveRuleConstrainedRegion();
            if (region == null)
            {
                RuleRegionDetails.IsVisible = false;
                return;
            }

            RuleRegionDetails.IsVisible = true;
            TxtRuleBandLabel.Text = region.Label ?? "";
            TxtRuleBandStart.Text = region.Start.ToString("0.##", CultureInfo.InvariantCulture);
            TxtRuleBandEnd.Text = region.End.ToString("0.##", CultureInfo.InvariantCulture);
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

        private void RuleBandField_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressRuleSync) return;
            var region = ResolveRuleConstrainedRegion();
            if (region == null) return;
            try
            {
                if (ReferenceEquals(sender, TxtRuleBandLabel))
                {
                    region.Label = TxtRuleBandLabel.Text;
                }
                else if (ReferenceEquals(sender, TxtRuleBandColor))
                {
                    region.Color = TxtRuleBandColor.Text;
                    UpdateRuleBandColorSwatchPreview();
                }
                else if (ReferenceEquals(sender, TxtRuleBandStart)
                         && double.TryParse(TxtRuleBandStart.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                {
                    region.Start = Math.Max(0, s);
                }
                else if (ReferenceEquals(sender, TxtRuleBandEnd)
                         && double.TryParse(TxtRuleBandEnd.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var en))
                {
                    region.End = en;
                }
                MarkDirty();
                RebuildRegionVisuals();
                ScheduleValidation();
            }
            catch (Exception ex)
            {
                Log.Debug("DeeperEditor: RuleBandField sync error: {Error}", ex.Message);
            }
        }

        private void UpdateRuleBandColorSwatchPreview()
        {
            if (RuleBandColorSwatch == null) return;
            RuleBandColorSwatch.Background = TryParseBrush(TxtRuleBandColor.Text) ?? Brushes.MediumPurple;
        }

        private void BuildRuleBandColorSwatches()
        {
            if (RuleBandColorSwatches == null) return;
            RuleBandColorSwatches.Children.Clear();
            foreach (var hex in RegionPalette)
            {
                var captured = hex;
                var swatch = new Border
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = TryParseBrush(hex) ?? Brushes.MediumPurple,
                    BorderBrush = Res("GlassBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = hex,
                };
                ToolTip.SetTip(swatch, hex);
                swatch.PointerReleased += (_, _) =>
                {
                    if (_selectedRule == null) return;
                    TxtRuleBandColor.Text = captured;
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

        private void ChkRuleEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            _selectedRule.Enabled = ChkRuleEnabled.IsChecked == true;
            MarkDirty();
            RefreshRulesList();
            ScheduleValidation();
        }

        private void CmbTriggerType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
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

        private void CmbActionType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            var picked = (CmbActionType.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(picked)) return;
            if (_selectedRule.Action?.Type == picked) return;

            _selectedRule.Action = picked switch
            {
                ActionTypes.NoOp          => new NoOpEnhancementAction(),
                // Default the jump target to the playhead, mirroring TimeReachedTrigger.
                ActionTypes.Seek          => new SeekAction { Target = SeekTargets.Time, Time = Math.Max(0, _currentSeconds) },
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

        private void CmbRuleRegion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
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

        private void TxtRuleCooldown_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            if (int.TryParse(TxtRuleCooldown.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                _selectedRule.CooldownMs = Math.Max(0, ms);
            MarkDirty();
            ScheduleValidation();
        }

        // ---------------------------------------------------------------------------------
        // Dynamic field builders (one block per trigger / action type)
        // ---------------------------------------------------------------------------------

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
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            for (int i = 0; i < 4; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            string[] labels = { "x", "y", "w", "h" };
            for (int i = 0; i < 4; i++)
            {
                int captured = i;
                var stack = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, 4, 0) };
                stack.Children.Add(new TextBlock
                {
                    Text = labels[i],
                    Foreground = Res("TextLightBrush"),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                var tb = NewEditorTextBox(rect.Length > i ? rect[i].ToString("0.##", CultureInfo.InvariantCulture) : "0");
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
            host.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_trigger_rect")));
            host.Children.Add(grid);

            // Quick-pick presets: a 3x3 grid of corner/edge regions so the user can say
            // "bottom-left" with one click instead of typing four numbers.
            host.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_trigger_quick_region")));
            var presetGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            for (int c = 0; c < 3; c++)
                presetGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            for (int r = 0; r < 3; r++)
                presetGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var presetLabels = new[,]
            {
                { "↖", "↑", "↗" },
                { "←", "·", "→" },
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
                        Cursor = new Cursor(StandardCursorType.Hand),
                        Background = Res("DeeperAccentTransparent20Brush"),
                        Foreground = Res("TextLightBrush"),
                        BorderBrush = Res("DeeperAccentTransparent40Brush"),
                        BorderThickness = new Thickness(1),
                    };
                    ToolTip.SetTip(b, Loc.Get(presetTooltips[row, col]));
                    Grid.SetColumn(b, col);
                    Grid.SetRow(b, row);
                    b.Click += (_, _) =>
                    {
                        var arr = getter();
                        if (arr == null || arr.Length < 4) return;
                        // 0,0 = top-left; each cell is 1/3 of the screen.
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
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Res("DeeperAccentTransparent20Brush"),
                Foreground = Res("DeeperAccentBrush"),
                BorderBrush = Res("DeeperAccentBrush"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };
            pickBtn.Click += (_, _) => BeginGazePick(getter);
            host.Children.Add(pickBtn);
        }

        private void AddSeekFields(SeekAction seek)
        {
            ActionFields.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_action_seek_target")));
            var combo = NewEditorComboBox();
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
            ActionFields.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_haptic_pattern")));
            var combo = NewEditorComboBox();
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
            AddHapticTargetField(ActionFields, h);
            ActionFields.Children.Add(curveHost);
            SyncCurveVisibility();

            AddDoubleField(ActionFields, Loc.Get("deeper_editor_action_intensity"),
                h.Intensity, v => h.Intensity = Math.Clamp(v, 0, 1));
            AddIntField(ActionFields, Loc.Get("deeper_editor_action_duration_ms"),
                h.DurationMs, v => h.DurationMs = Math.Max(50, v));
        }

        private void AddHapticTargetField(Panel host, IHapticPatternTarget h)
        {
            host.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_haptic_target")));
            var combo = NewEditorComboBox();
            FillHapticTargetCombo(combo);
            combo.SelectedIndex = HapticTargetIndex(h.Target);
            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                h.Target = HapticTargetAt(combo.SelectedIndex);
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(combo);
        }

        // Generic effect-firing action (TriggerEffect). Lets a rule fire any of the six effect
        // types with its own intensity / duration / type-specific settings, independent of any
        // TimelineItem. Mirrors the per-type editor panels.
        private void AddTriggerEffectActionFields(TriggerEffectAction te)
        {
            ActionFields.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_action_effect_type")));
            var typeCombo = NewEditorComboBox();
            string[] effectTypes =
            {
                EffectTypes.Haptic, EffectTypes.Flash, EffectTypes.Bubble,
                EffectTypes.Subliminal, EffectTypes.Overlay, EffectTypes.Speak
            };
            foreach (var t in effectTypes)
                typeCombo.Items.Add(new ComboBoxItem { Theme = EditorComboBoxItemTheme, Content = t, Tag = t });
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
                    case EffectTypes.Speak:
                        AddSpeakActionFields(typeFieldsHost, te);
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
                    // Reset stale type-specific fields so a Flash->Haptic switch doesn't leak Flash
                    // defaults into the haptic interpretation.
                    if (newType == EffectTypes.Haptic && string.IsNullOrEmpty(te.PatternName))
                        te.PatternName = "Pulse";
                    if (newType == EffectTypes.Overlay && string.IsNullOrEmpty(te.OverlayKind))
                        te.OverlayKind = OverlayKinds.PinkFilter;
                    if (newType == EffectTypes.Speak && string.IsNullOrWhiteSpace(te.SpeakTarget))
                    {
                        te.SpeakTarget = "YES";
                        te.SpeakCorrectMessage = "good girl";
                        te.SpeakIncorrectMessage = "try again";
                    }
                    RebuildTypeFields();
                    MarkDirty();
                    ScheduleValidation();
                }
            };
        }

        // Haptic sub-fields for TriggerEffect. Mirrors AddHapticActionFields but bound to a
        // TriggerEffectAction's flat fields instead of TriggerHapticAction.
        private void AddTriggerEffectHapticFields(Panel host, TriggerEffectAction te)
        {
            host.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_haptic_pattern")));
            var combo = NewEditorComboBox();
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

        // Overlay-kind combo for TriggerEffect. Same order, labels and Tag round-trip as the inline
        // CmbOverlayKind picker; an already-authored withheld kind stays selectable so a rule that
        // uses one still displays honestly and round-trips on save.
        private void AddOverlayKindCombo(Panel host, TriggerEffectAction te)
        {
            host.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_action_overlay_kind")));
            var combo = NewEditorComboBox();
            var kinds  = new List<string> { OverlayKinds.PinkFilter, OverlayKinds.Spiral,
                                            OverlayKinds.BrainDrain, OverlayKinds.BrainDrainMelt };
            var labels = new List<string> { "Pink Filter", "Spiral", "Brain Drain", "Brain Melt" };
            var curK = te.OverlayKind ?? OverlayKinds.PinkFilter;
            if (OverlayKinds.IsWithheld(curK))
            {
                kinds.Add(curK);
                labels.Add(curK == OverlayKinds.BrainDrainMelt
                    ? "Brain Melt (unavailable)"
                    : "Brain Drain (unavailable)");
            }
            for (int i = 0; i < kinds.Count; i++)
                combo.Items.Add(new ComboBoxItem { Theme = EditorComboBoxItemTheme, Content = labels[i], Tag = kinds[i] });
            var idx = kinds.IndexOf(curK);
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

        // Speak (voice prompt) sub-fields for a rule's TriggerEffect action.
        private void AddSpeakActionFields(Panel host, TriggerEffectAction te)
        {
            AddSpeakTextField(host, "Phrase to say (max 24)", te.SpeakTarget ?? "", 24, v => te.SpeakTarget = v);
            AddSpeakTextField(host, "On-screen cue (blank = \"Say {phrase}\")", te.SpeakCue ?? "", 40, v => te.SpeakCue = v);

            // Cue style + interval (interval only matters for intermittent, but keeping it
            // always-visible avoids a rebuild dance in the rule inspector).
            AddSpeakEnumCombo(host, "Cue style", te.SpeakCueMode,
                new[] { (SpeakCueMode.Intermittent, "Intermittent (flash)"), (SpeakCueMode.Persistent, "Persistent (stay on top)") },
                v => te.SpeakCueMode = v);
            AddIntField(host, "Flash interval (ms)", te.SpeakCueIntervalMs, v => te.SpeakCueIntervalMs = Math.Max(80, v));

            AddIntField(host, "Times to say it (1-5)", te.SpeakRequiredReps, v => te.SpeakRequiredReps = Math.Clamp(v, 1, 5));

            AddSpeakEnumCombo(host, "Completion", te.SpeakCompletion,
                new[] { (SpeakCompletion.UntilSatisfied, "Until satisfied"), (SpeakCompletion.Duration, "For the region (soft)") },
                v => te.SpeakCompletion = v);
            AddSpeakEnumCombo(host, "While waiting…", te.SpeakHoldMode,
                new[] { (SpeakHoldMode.LoopRegion, "Loop the region"), (SpeakHoldMode.Pause, "Pause the video"), (SpeakHoldMode.KeepPlaying, "Keep playing") },
                v => te.SpeakHoldMode = v);

            AddSpeakTextField(host, "Praise (on correct)", te.SpeakCorrectMessage ?? "", 40, v => te.SpeakCorrectMessage = v);
            AddSpeakTextField(host, "Scold (on miss)", te.SpeakIncorrectMessage ?? "", 40, v => te.SpeakIncorrectMessage = v);
        }

        private void AddSpeakTextField(Panel host, string label, string value, int maxLength, Action<string> setter)
        {
            host.Children.Add(NewEditorLabel(label));
            var tb = NewEditorTextBox(value ?? "");
            tb.MaxLength = maxLength;
            tb.TextChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                setter(tb.Text ?? "");
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(tb);
        }

        private void AddSpeakEnumCombo<TEnum>(Panel host, string label, TEnum current,
            (TEnum value, string display)[] options, Action<TEnum> setter) where TEnum : struct, Enum
        {
            host.Children.Add(NewEditorLabel(label));
            var combo = NewEditorComboBox();
            int sel = 0;
            for (int i = 0; i < options.Length; i++)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Theme = EditorComboBoxItemTheme,
                    Content = options[i].display,
                    Tag = options[i].value
                });
                if (EqualityComparer<TEnum>.Default.Equals(options[i].value, current)) sel = i;
            }
            combo.SelectedIndex = sel;
            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                if (combo.SelectedItem is ComboBoxItem cbi && cbi.Tag is TEnum v)
                {
                    setter(v);
                    MarkDirty();
                    ScheduleValidation();
                }
            };
            host.Children.Add(combo);
        }

        // Self-contained curve editor for any IHapticPatternTarget. Builds its own canvas +
        // handles + reset button into <paramref name="host"/>, independent of the haptic-event
        // editor's XAML CurveCanvas.
        private void BuildCurveEditor(Panel host, IHapticPatternTarget target)
        {
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_haptic_curve"),
                Foreground = Res("TextMutedBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var canvas = new Canvas { Height = 120, Background = Brushes.Transparent, ClipToBounds = true };
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0x20)),
                BorderBrush = Res("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = canvas
            };
            host.Children.Add(border);

            void Redraw()
            {
                canvas.Children.Clear();
                var pts = target.CustomPattern;
                if (pts == null || pts.Count == 0) return;
                var w = canvas.Bounds.Width;
                var h = canvas.Bounds.Height;
                if (w <= 0 || h <= 0) return;

                for (int i = 1; i <= 3; i++)
                {
                    var y = h * i / 4.0;
                    canvas.Children.Add(new Line
                    {
                        StartPoint = new Point(0, y),
                        EndPoint = new Point(w, y),
                        Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                        StrokeThickness = 0.5,
                        IsHitTestVisible = false
                    });
                }

                var geom = new StreamGeometry();
                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(KeyframeToCanvas(pts[0], w, h), false);
                    for (int i = 1; i < pts.Count; i++)
                        ctx.LineTo(KeyframeToCanvas(pts[i], w, h));
                    ctx.EndFigure(false);
                }
                canvas.Children.Add(new Path
                {
                    Data = geom,
                    Stroke = Res("DeeperAccentBrush"),
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false
                });

                for (int i = 0; i < pts.Count; i++)
                {
                    int captured = i;
                    var pt = KeyframeToCanvas(pts[i], w, h);
                    var dot = new Ellipse
                    {
                        Width = 10, Height = 10,
                        Fill = Res("DeeperAccentBrush"),
                        Stroke = Brushes.White,
                        StrokeThickness = 1.2,
                        Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                    };
                    Canvas.SetLeft(dot, pt.X - 5);
                    Canvas.SetTop(dot, pt.Y - 5);
                    bool dragging = false;
                    dot.PointerPressed += (_, ev) =>
                    {
                        if (!ev.GetCurrentPoint(dot).Properties.IsLeftButtonPressed) return;
                        dragging = true;
                        ev.Pointer.Capture(dot);
                        ev.Handled = true;
                    };
                    dot.PointerMoved += (_, ev) =>
                    {
                        if (!dragging) return;
                        var hh = canvas.Bounds.Height;
                        if (hh <= 0) return;
                        var y = ev.GetPosition(canvas).Y;
                        var v = Math.Clamp(1.0 - (y / hh), 0, 1);
                        var kf = target.CustomPattern![captured];
                        if (kf.Length > 1) kf[1] = v;
                        MarkDirty();
                        Redraw();
                    };
                    dot.PointerReleased += (_, ev) =>
                    {
                        if (!dragging) return;
                        dragging = false;
                        ev.Pointer.Capture(null);
                        ScheduleValidation();
                    };
                    canvas.Children.Add(dot);
                }
            }

            canvas.SizeChanged += (_, _) => Redraw();
            Redraw();

            var reset = new Button
            {
                Content = Loc.Get("deeper_editor_haptic_curve_reset"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                Foreground = Res("TextLightBrush"),
                BorderBrush = Res("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(1),
            };
            reset.Click += (_, _) =>
            {
                PushUndoSnapshot();
                target.CustomPattern = StockHapticPatterns.SeedCustomFrom(null);
                MarkDirty();
                Redraw();
                ScheduleValidation();
            };
            host.Children.Add(reset);
        }

        // -- Tiny field helpers ------------------------------------------------

        private void AddIntField(Panel host, string label, int value, Action<int> setter)
        {
            host.Children.Add(NewEditorLabel(label));
            var tb = NewEditorTextBox(value.ToString(CultureInfo.InvariantCulture));
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
            host.Children.Add(NewEditorLabel(label));
            var tb = NewEditorTextBox(value.ToString("0.###", CultureInfo.InvariantCulture));
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
            host.Children.Add(NewEditorLabel(label));
            var tb = NewEditorTextBox(value ?? "");
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
                Foreground = Res("TextLightBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            cb.IsCheckedChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                setter(cb.IsChecked == true);
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(cb);
        }

        // Assigns a name to the most recently-added TextBox in a dynamic field host so the
        // interactive tutorial can spotlight and gate on it. Avalonia refuses to rename an element
        // that is already in a tree, so this is best-effort - the try/catch is the port, not a
        // shortcut.
        private static void AssignNameToLastTextBox(Panel host, string name)
        {
            for (int i = host.Children.Count - 1; i >= 0; i--)
            {
                if (host.Children[i] is TextBox tb)
                {
                    try { tb.Name = name; } catch { }
                    return;
                }
            }
        }

        private void AddInfoText(Panel host, string text)
        {
            host.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = Res("TextMutedBrush"),
                FontSize = 11,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        // Renders the localized one-line description for the currently-picked trigger or action
        // type, so the user sees what each type does the moment they pick it.
        private void AddTypeDescription(Panel host, string locKey)
        {
            var text = Loc.Get(locKey);
            if (string.IsNullOrEmpty(text) || text == locKey) return; // no key = skip
            host.Children.Add(new Border
            {
                Background = Res("DeeperAccentTransparent20Brush"),
                BorderBrush = Res("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Res("TextLightBrush"),
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
            ActionTypes.NoOp          => "deeper_desc_action_noop",
            _                         => ""
        };

        private void AddRegionPicker(Panel host, string currentId, Action<string> setter, bool allowNone = false)
        {
            host.Children.Add(NewEditorLabel(Loc.Get("deeper_editor_rule_region_id")));
            var combo = NewEditorComboBox();
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

        // ---------------------------------------------------------------------------------
        // Preview button
        // ---------------------------------------------------------------------------------

        /// <summary>ponytail: needs App.DeeperPlayer + App.DeeperHost. The player window itself is
        /// already ported (EnhancementPlayerWindow), but it takes the live enhancement plus those
        /// two services, so opening it is left to the layer that moves them. HealEmptyTriggerRegionIds
        /// below is real and still runs, since it is pure data repair.</summary>
        private void BtnPreview_Click(object? sender, RoutedEventArgs e)
        {
            HealEmptyTriggerRegionIds();
            Log.Debug("DeeperEditor: preview needs App.DeeperPlayer + App.DeeperHost");
        }

        /// <summary>
        /// Heal band-style rules whose trigger.RegionId is empty but whose RegionConstraint already
        /// points at a real region (an older buggy path left RegionId blank). Without this,
        /// validation rejects the in-memory enhancement and the player opens with no media.
        /// </summary>
        private void HealEmptyTriggerRegionIds()
        {
            try
            {
                var regionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in _enhancement.Regions)
                    if (!string.IsNullOrEmpty(r?.Id)) regionIds.Add(r!.Id!);

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
                Log.Debug("DeeperEditor: HealEmptyTriggerRegionIds error: {Error}", ex.Message);
            }
        }

        // ---------------------------------------------------------------------------------
        // Gaze rect picker
        // ---------------------------------------------------------------------------------

        private GazePickerWindow? _gazePickerWindow;

        /// <summary>
        /// RESTORED. The note here claimed "the picker window itself is not ported"; it is, and it
        /// always was on this branch - <c>Views/Deeper/GazePickerWindow.axaml.cs</c>, right beside
        /// this file, complete with its eight resize handles, Done/Cancel and normalised
        /// <c>ResultRect</c>. Nothing was blocking this but the note.
        ///
        /// <para>Screen placement is the one real deviation. WPF read the two corners with
        /// <c>PreviewHost.PointToScreen</c> and divided by <c>VisualTreeHelper.GetDpi(this)</c> to
        /// get WPF's DIP <c>Left</c>/<c>Top</c>. Avalonia's <c>PointToScreen</c> already answers in
        /// device pixels and <c>Window.Position</c> takes device pixels, so the origin needs NO
        /// conversion - only the size does, because <c>Width</c>/<c>Height</c> are DIPs.
        /// <c>TopLevel.RenderScaling</c> is the DpiScale of that pair.</para>
        ///
        /// <para>The write-back is WPF's, elementwise into the array the caller's getter hands us:
        /// the picker cloned its input, so assigning the reference would update nothing.</para>
        /// </summary>
        private void BeginGazePick(Func<double[]> rectGetter)
        {
            EndGazePick(commit: false);

            var current = rectGetter();
            if (current == null || current.Length < 4)
                current = new[] { 0.25, 0.25, 0.5, 0.5 };

            try
            {
                var origin = PreviewHost.PointToScreen(new Point(0, 0));
                var far = PreviewHost.PointToScreen(
                    new Point(PreviewHost.Bounds.Width, PreviewHost.Bounds.Height));
                var scale = RenderScaling <= 0 ? 1.0 : RenderScaling;

                var picker = new GazePickerWindow(current)
                {
                    Position = origin,
                    Width = (far.X - origin.X) / scale,
                    Height = (far.Y - origin.Y) / scale,
                };
                _gazePickerWindow = picker;
                picker.Closed += (_, _) =>
                {
                    if (!ReferenceEquals(_gazePickerWindow, picker)) return;
                    _gazePickerWindow = null;
                    if (!picker.Committed) return;

                    var result = picker.ResultRect;
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
                };
                picker.Show(this);
            }
            catch (Exception ex)
            {
                _gazePickerWindow = null;
                Log.Debug("DeeperEditor: BeginGazePick failed: {Error}", ex.Message);
            }
        }

        /// <summary>Force-closes any open picker. The Closed handler reads <c>Committed</c>, which
        /// only the picker's own Done/Enter sets, so closing from here never applies a rect.</summary>
        private void EndGazePick(bool commit)
        {
            _ = commit;
            var w = _gazePickerWindow;
            if (w == null) return;
            _gazePickerWindow = null;
            try { w.Close(); }
            catch { /* a picker already gone is the state we wanted */ }
        }
    }
}
