using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Views.Deeper
{
    // Right-panel "Rules & Effects" overview: collapsible list of every
    // rule + effect (haptic + non-haptic) currently on the enhancement,
    // each row clickable to select on the timeline and with an inline
    // bin button for one-click delete. Replaces the old "scroll the
    // timeline + click each band" workflow for housekeeping.
    public partial class DeeperEditorWindow
    {
        private enum ItemRowKind { Rule, Haptic, Effect }

        private sealed class ItemRow
        {
            public ItemRowKind Kind;
            public double Start;          // for sort + display
            public string TypeLabel = ""; // e.g. "Rule", "Haptic", "Flash"
            public string DetailLabel = "";
            public Brush Swatch = Brushes.White;
            public Action OnSelect = () => { };
            public Action OnDelete = () => { };
            public bool IsSelected;
        }

        // --------------------------------------------------------------
        // Toggle (collapse / expand)
        // --------------------------------------------------------------

        private void ItemsListToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ItemsListToggle == null || ItemsListContent == null) return;
            bool open = ItemsListToggle.IsChecked == true;
            ItemsListContent.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (ItemsListChevron != null)
                ItemsListChevron.Text = open ? "▼" : "▶";
        }

        // --------------------------------------------------------------
        // Rebuild the list — called from MarkDirty (every mutation), from
        // selection changes (so the highlight follows), and from
        // LoadEnhancement once the file is parsed.
        // --------------------------------------------------------------

        private void RefreshItemsList()
        {
            if (LstAllItems == null) return;
            try
            {
                var rows = CollectItemRows();

                LstAllItems.Items.Clear();
                foreach (var row in rows)
                    LstAllItems.Items.Add(BuildItemListRow(row));

                if (TxtItemsListCount != null)
                    TxtItemsListCount.Text = rows.Count == 0
                        ? ""
                        : rows.Count.ToString(CultureInfo.InvariantCulture);

                if (TxtItemsListEmpty != null)
                    TxtItemsListEmpty.Visibility = rows.Count == 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: RefreshItemsList error: {Error}", ex.Message);
            }
        }

        private List<ItemRow> CollectItemRows()
        {
            var rows = new List<ItemRow>();
            if (_enhancement == null) return rows;

            // Rules.
            foreach (var rule in _enhancement.Rules)
            {
                if (rule == null) continue;
                var captured = rule;
                var typeStr = rule.Trigger?.Type ?? "";
                double start = ResolveRuleStart(rule);
                var color = ResolveRuleColor(rule);
                rows.Add(new ItemRow
                {
                    Kind = ItemRowKind.Rule,
                    Start = start,
                    TypeLabel = Loc.Get("deeper_editor_items_list_kind_rule"),
                    DetailLabel = FriendlyTriggerName(typeStr),
                    Swatch = color,
                    IsSelected = ReferenceEquals(_selectedRule, captured),
                    OnSelect = () => SelectRule(captured),
                    OnDelete = () => DeleteRuleViaList(captured),
                });
            }

            // Haptic events (live on legacy haptic tracks).
            foreach (var track in _enhancement.HapticTracks)
            {
                if (track == null) continue;
                foreach (var ev in track.Events)
                {
                    if (ev == null) continue;
                    var capturedTrack = track;
                    var capturedEv = ev;
                    var brush = TryParseBrush(EffectColors.TryGetValue(EffectTypes.Haptic, out var c) ? c : "#7B5CFF")
                                ?? Brushes.MediumPurple;
                    rows.Add(new ItemRow
                    {
                        Kind = ItemRowKind.Haptic,
                        Start = ev.Start,
                        TypeLabel = Loc.Get("deeper_editor_items_list_kind_haptic"),
                        DetailLabel = string.IsNullOrEmpty(ev.PatternName)
                            ? Loc.Get("deeper_editor_haptic_pattern_custom")
                            : ev.PatternName!,
                        Swatch = brush,
                        IsSelected = ReferenceEquals(_selectedHaptic, capturedEv),
                        OnSelect = () => SelectHaptic(capturedTrack, capturedEv),
                        OnDelete = () => DeleteHapticViaList(capturedTrack, capturedEv),
                    });
                }
            }

            // Non-haptic effect TimelineItems.
            foreach (var ti in _enhancement.TimelineItems)
            {
                if (ti == null || ti.Kind != TimelineItemKind.Effect) continue;
                if (ti.EffectType == EffectTypes.Haptic) continue; // already covered
                var captured = ti;
                var brush = TryParseBrush(EffectColors.TryGetValue(ti.EffectType ?? "", out var c) ? c : "#FFFFFF")
                            ?? Brushes.White;
                rows.Add(new ItemRow
                {
                    Kind = ItemRowKind.Effect,
                    Start = ti.Start,
                    TypeLabel = FriendlyEffectName(ti.EffectType),
                    DetailLabel = BuildEffectDetail(ti),
                    Swatch = brush,
                    IsSelected = ReferenceEquals(_selectedEffect, captured),
                    OnSelect = () => SelectEffect(captured),
                    OnDelete = () => DeleteEffectViaList(captured),
                });
            }

            // Sort by start time so the list reads left-to-right like the timeline.
            // Non-time-anchored entries (start=NaN) sink to the bottom.
            rows.Sort((a, b) =>
            {
                bool aHas = !double.IsNaN(a.Start);
                bool bHas = !double.IsNaN(b.Start);
                if (aHas && !bHas) return -1;
                if (!aHas && bHas) return 1;
                if (!aHas && !bHas) return 0;
                return a.Start.CompareTo(b.Start);
            });

            return rows;
        }

        // --------------------------------------------------------------
        // Per-row visual
        // --------------------------------------------------------------

        private FrameworkElement BuildItemListRow(ItemRow row)
        {
            var border = new Border
            {
                Background = row.IsSelected
                    ? (Brush)FindResource("DeeperAccentTransparent20Brush")
                    : Brushes.Transparent,
                BorderBrush = row.IsSelected
                    ? (Brush)FindResource("DeeperAccentBrush")
                    : (Brush)FindResource("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 6, 6, 6),
                Cursor = Cursors.Hand,
                Tag = row,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = row.Swatch,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(swatch, 0);

            var labelStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            labelStack.Children.Add(new TextBlock
            {
                Text = row.TypeLabel,
                Foreground = (Brush)FindResource("TextLightBrush"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrEmpty(row.DetailLabel))
            {
                labelStack.Children.Add(new TextBlock
                {
                    Text = row.DetailLabel,
                    Foreground = (Brush)FindResource("TextDimBrush"),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            Grid.SetColumn(labelStack, 1);

            var time = new TextBlock
            {
                Text = double.IsNaN(row.Start) ? "—" : FormatTimeShort(row.Start),
                Foreground = (Brush)FindResource("TextDimBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(time, 2);

            var deleteBtn = new Button
            {
                Content = "🗑",
                Width = 24,
                Height = 22,
                Padding = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("TextDimBrush"),
                BorderBrush = (Brush)FindResource("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(1),
                ToolTip = Loc.Get("deeper_editor_items_list_delete_tooltip"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            deleteBtn.Click += (_, e) =>
            {
                e.Handled = true;
                try { row.OnDelete(); } catch (Exception ex) { App.Logger?.Debug("DeeperEditor: row delete error: {Error}", ex.Message); }
            };
            Grid.SetColumn(deleteBtn, 3);

            grid.Children.Add(swatch);
            grid.Children.Add(labelStack);
            grid.Children.Add(time);
            grid.Children.Add(deleteBtn);

            border.Child = grid;
            border.MouseLeftButtonUp += (_, e) =>
            {
                // Ignore if the click bubbled up from the bin button.
                if (e.OriginalSource is DependencyObject src
                    && FindAncestor<Button>(src) != null) return;
                try { row.OnSelect(); }
                catch (Exception ex) { App.Logger?.Debug("DeeperEditor: row select error: {Error}", ex.Message); }
            };
            return border;
        }

        // --------------------------------------------------------------
        // Row-level delete handlers — mirror the existing in-editor delete
        // buttons so behaviour (undo snapshot, MarkDirty, validation,
        // visual rebuild, side-panel reset) stays consistent.
        // --------------------------------------------------------------

        private void DeleteRuleViaList(EnhancementRule rule)
        {
            PushUndoSnapshot();
            _enhancement.Rules.Remove(rule);
            // Drop the companion Region only if no other rule still references it.
            // Without this, deleting a band-style rule left an orphan region that
            // looked like a stray colored stripe with no editor surface.
            if (!string.IsNullOrEmpty(rule.RegionConstraint))
            {
                bool stillUsed = _enhancement.Rules.Any(r =>
                    r != null && string.Equals(r.RegionConstraint, rule.RegionConstraint, StringComparison.Ordinal));
                if (!stillUsed)
                    _enhancement.Regions.RemoveAll(r => r != null && r.Id == rule.RegionConstraint);
            }
            if (ReferenceEquals(_selectedRule, rule)) _selectedRule = null;
            SelectNothing();
            MarkDirty();
            ScheduleValidation();
        }

        private void DeleteHapticViaList(HapticTrack track, HapticEvent ev)
        {
            PushUndoSnapshot();
            track.Events.Remove(ev);
            if (track.Events.Count == 0 && track.Id == DefaultTrackId)
                _enhancement.HapticTracks.Remove(track);
            if (ReferenceEquals(_selectedHaptic, ev)) { _selectedHaptic = null; _selectedHapticTrack = null; }
            SelectNothing();
            MarkDirty();
            RebuildHapticVisuals();
            ScheduleValidation();
        }

        private void DeleteEffectViaList(TimelineItem item)
        {
            PushUndoSnapshot();
            _enhancement.TimelineItems.Remove(item);
            if (ReferenceEquals(_selectedEffect, item)) _selectedEffect = null;
            SelectNothing();
            MarkDirty();
            RebuildEffectVisuals();
            ScheduleValidation();
        }

        // --------------------------------------------------------------
        // Resolution helpers
        // --------------------------------------------------------------

        // Best-effort start time for a rule:
        //   1. TimeReached: trigger.Time
        //   2. region constraint: region.Start
        //   3. otherwise: NaN (sinks to bottom)
        private double ResolveRuleStart(EnhancementRule rule)
        {
            if (rule.Trigger is TimeReachedTrigger trt) return trt.Time;
            if (!string.IsNullOrEmpty(rule.RegionConstraint))
            {
                var region = _enhancement.Regions.FirstOrDefault(r => r != null && r.Id == rule.RegionConstraint);
                if (region != null) return region.Start;
            }
            return double.NaN;
        }

        private Brush ResolveRuleColor(EnhancementRule rule)
        {
            // Prefer the constrained region's color so the swatch matches the band
            // the user sees on the timeline; fall back to the editor's accent.
            if (!string.IsNullOrEmpty(rule.RegionConstraint))
            {
                var region = _enhancement.Regions.FirstOrDefault(r => r != null && r.Id == rule.RegionConstraint);
                if (region != null && !string.IsNullOrEmpty(region.Color))
                {
                    var b = TryParseBrush(region.Color);
                    if (b != null) return b;
                }
            }
            try { return (Brush)FindResource("DeeperAccentBrush"); }
            catch { return Brushes.MediumPurple; }
        }

        private static string FriendlyEffectName(string? effectType) => effectType switch
        {
            EffectTypes.Flash      => "Flash",
            EffectTypes.Bubble     => "Bubble",
            EffectTypes.Subliminal => "Subliminal",
            EffectTypes.Overlay    => "Overlay",
            EffectTypes.Haptic     => "Haptic",
            _ => effectType ?? "Effect"
        };

        private static string BuildEffectDetail(TimelineItem ti)
        {
            // Show whichever sub-config is most informative for the chosen effect.
            switch (ti.EffectType)
            {
                case EffectTypes.Subliminal:
                    return string.IsNullOrEmpty(ti.EffectText)
                        ? $"{ti.EffectDurationMs} ms"
                        : ti.EffectText!.Length > 32 ? ti.EffectText[..32] + "…" : ti.EffectText!;
                case EffectTypes.Overlay:
                    return $"{ti.EffectOverlayKind ?? OverlayKinds.PinkFilter} · {ti.EffectDurationMs} ms";
                case EffectTypes.Bubble:
                    return $"{(ti.EffectDurationMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} s";
                case EffectTypes.Flash:
                    return $"{ti.EffectDurationMs} ms";
                default:
                    return ti.EffectDurationMs > 0 ? $"{ti.EffectDurationMs} ms" : "";
            }
        }

        private static string FormatTimeShort(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Round(seconds);
            int m = total / 60;
            int s = total % 60;
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}", m, s);
        }

        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            }
            return null;
        }
    }
}
