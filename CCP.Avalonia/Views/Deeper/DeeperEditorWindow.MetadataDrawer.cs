using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    // PORTED from ConditioningControlPanel/Views/Deeper/DeeperEditorWindow.MetadataDrawer.cs.
    // Deviations:
    //   Visibility               -> IsVisible
    //   FontStyles.Italic        -> FontStyle.Italic
    //   FindResource(k)          -> Res(k) (TryFindResource + IBrush cast; see the main partial)
    //   UpdateLayout()           -> InvalidateMeasure() (Avalonia has no synchronous UpdateLayout
    //                               on Window; the drawer is measured on the next layout pass)
    //   ScrollViewer.ScrollToTop -> Offset = new Vector(x, 0)
    //   GridSplitter DragCompleted args -> Avalonia's Thumb.DragCompleted (VectorEventArgs)
    //   App.Settings             -> stubbed, see ApplyPersistedSidebarWidth
    //
    // Sidebar restructure plumbing: metadata drawer expand/collapse, the selection summary strip,
    // GridSplitter persistence and the inspector auto-scroll.
    public partial class DeeperEditorWindow
    {
        // -----------------------------------------------------------------
        // Metadata drawer
        // -----------------------------------------------------------------

        // Public so tutorial steps' PrepareTargetWindowAction can expand the drawer before
        // measuring a target field. Idempotent.
        public void ExpandMetadataDrawer()
        {
            try
            {
                if (MetadataDrawerToggle != null && MetadataDrawerToggle.IsChecked != true)
                    MetadataDrawerToggle.IsChecked = true; // fires IsCheckedChanged -> MetadataDrawerToggle_Changed
                else
                    ApplyMetadataDrawerState();
                InvalidateMeasure();
            }
            catch { }
        }

        private void MetadataDrawerToggle_Changed(object? sender, RoutedEventArgs e)
        {
            ApplyMetadataDrawerState();
        }

        private void ApplyMetadataDrawerState()
        {
            if (MetadataDrawerToggle == null || MetadataDrawerContent == null) return;
            bool open = MetadataDrawerToggle.IsChecked == true;
            MetadataDrawerContent.IsVisible = open;
            if (MetadataDrawerChevron != null)
                MetadataDrawerChevron.Text = open ? "▼" : "▶";
        }

        // Called from UpdateTitle() so the drawer chip shows the current project name even while
        // collapsed. Falls back to nothing if no name is set.
        private void UpdateMetadataDrawerSubtitle()
        {
            try
            {
                if (TxtMetadataDrawerSubtitle == null) return;
                var name = _enhancement?.Metadata?.Name;
                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrEmpty(_filePath))
                    name = Path.GetFileNameWithoutExtension(_filePath);
                TxtMetadataDrawerSubtitle.Text = string.IsNullOrWhiteSpace(name)
                    ? ""
                    : "· " + name;
            }
            catch { }
        }

        // -----------------------------------------------------------------
        // Selection summary strip
        // -----------------------------------------------------------------

        // Single source of truth for the 44-px strip above the inspector. Re-rendered on every
        // selection change (single OR multi).
        private void UpdateSelectionSummary()
        {
            if (TxtSelectionSummary == null) return;
            try
            {
                string text;
                bool active = false;

                if (_selectionSet != null && _selectionSet.Count > 1)
                {
                    text = BuildMultiSelectionSummary();
                    active = true;
                }
                else if (_selectedRule != null)
                {
                    text = BuildRuleSelectionSummary(_selectedRule);
                    active = true;
                }
                else if (_selectedRegion != null)
                {
                    text = BuildRegionSelectionSummary(_selectedRegion);
                    active = true;
                }
                else if (_selectedHaptic != null)
                {
                    text = BuildHapticSelectionSummary(_selectedHaptic);
                    active = true;
                }
                else if (_selectedEffect != null)
                {
                    text = BuildEffectSelectionSummary(_selectedEffect);
                    active = true;
                }
                else
                {
                    text = Loc.Get("deeper_editor_selection_none");
                }

                TxtSelectionSummary.Text = text;
                TxtSelectionSummary.FontStyle = active ? FontStyle.Normal : FontStyle.Italic;
                TxtSelectionSummary.Foreground = active ? Res("TextLightBrush") : Res("TextMutedBrush");
            }
            catch (Exception ex) { Log.Debug("DeeperEditor: UpdateSelectionSummary error: {Error}", ex.Message); }
        }

        private string BuildRuleSelectionSummary(EnhancementRule rule)
        {
            var trig = FriendlyTriggerName(rule.Trigger?.Type ?? "");
            if (rule.Trigger is TimeReachedTrigger trt)
                return $"{Loc.Get("deeper_editor_selection_kind_rule")} · {trig} @ {FormatTimeShort(trt.Time)}";
            if (!string.IsNullOrEmpty(rule.RegionConstraint))
            {
                var region = _enhancement?.Regions.FirstOrDefault(r => r != null && r.Id == rule.RegionConstraint);
                if (region != null)
                    return $"{Loc.Get("deeper_editor_selection_kind_rule")} · {trig} · {region.Label ?? region.Id}";
            }
            return $"{Loc.Get("deeper_editor_selection_kind_rule")} · {trig}";
        }

        private static string BuildRegionSelectionSummary(Region region)
        {
            var label = string.IsNullOrWhiteSpace(region.Label) ? region.Id : region.Label;
            return $"{Loc.Get("deeper_editor_selection_kind_region")} · {label} ({FormatTimeShort(region.Start)}–{FormatTimeShort(region.End)})";
        }

        private static string BuildHapticSelectionSummary(HapticEvent ev)
        {
            var pattern = string.IsNullOrWhiteSpace(ev.PatternName)
                ? Loc.Get("deeper_editor_haptic_pattern_custom")
                : ev.PatternName!;
            return $"{Loc.Get("deeper_editor_selection_kind_haptic")} · {pattern} · {ev.Duration.ToString("0.#", CultureInfo.InvariantCulture)}s";
        }

        private string BuildEffectSelectionSummary(TimelineItem item)
        {
            return $"{FriendlyEffectName(item.EffectType)} · {BuildEffectDetail(item)} @ {FormatTimeShort(item.Start)}";
        }

        private string BuildMultiSelectionSummary()
        {
            int rules = 0, regions = 0, haptics = 0, effects = 0;
            foreach (var sel in _selectionSet!)
            {
                switch (sel)
                {
                    case EnhancementRule: rules++; break;
                    case Region: regions++; break;
                    case HapticEvent: haptics++; break;
                    case TimelineItem ti when ti.Kind == TimelineItemKind.Effect: effects++; break;
                }
            }
            int total = rules + regions + haptics + effects;
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                Loc.Get("deeper_editor_selection_multi_total"), total);
            var parts = new System.Collections.Generic.List<string>();
            if (rules > 0)   parts.Add($"{rules} {Loc.Get("deeper_editor_selection_kind_rules_plural")}");
            if (regions > 0) parts.Add($"{regions} {Loc.Get("deeper_editor_selection_kind_regions_plural")}");
            if (haptics > 0) parts.Add($"{haptics} {Loc.Get("deeper_editor_selection_kind_haptics_plural")}");
            if (effects > 0) parts.Add($"{effects} {Loc.Get("deeper_editor_selection_kind_effects_plural")}");
            if (parts.Count > 0)
            {
                sb.Append(" · ");
                sb.Append(string.Join(", ", parts));
            }
            return sb.ToString();
        }

        // -----------------------------------------------------------------
        // Sidebar splitter - persist width
        // -----------------------------------------------------------------

        /// <summary>
        /// Applied during Loaded so the editor opens at the user's last chosen width.
        /// ponytail: needs App.Settings (AppSettings.DeeperEditorSidebarWidth), wired when the
        /// settings service moves to Core. Until then the column keeps its 380 px XAML default,
        /// which is what the WPF fallback used anyway.
        /// </summary>
        /// <summary>The resizable sidebar column. WPF reached it through its x:Name; Avalonia
        /// only generates fields for Controls, so it is indexed off the named body Grid.</summary>
        private ColumnDefinition? SidebarColumn =>
            MainBodyGrid?.ColumnDefinitions is { Count: > 2 } cols ? cols[2] : null;

        public void ApplyPersistedSidebarWidth()
        {
            try
            {
                var col = SidebarColumn;
                if (col == null) return;
                int w = 380;
                if (w < 320) w = 320;
                if (w > 520) w = 520;
                col.Width = new GridLength(w, GridUnitType.Pixel);
            }
            catch { }
        }

        /// <summary>
        /// ponytail: needs App.Settings to persist the dragged width; the drag itself is live
        /// (Avalonia's GridSplitter writes the ColumnDefinition), only the save is missing.
        /// </summary>
        private void SidebarSplitter_DragCompleted(object? sender, VectorEventArgs e)
        {
            try
            {
                var col = SidebarColumn;
                if (col == null) return;
                int w = (int)Math.Round(col.ActualWidth);
                if (w < 320) w = 320;
                if (w > 520) w = 520;
                // App.Settings.Current.DeeperEditorSidebarWidth = w; App.Settings.Save();
            }
            catch { }
        }

        // -----------------------------------------------------------------
        // Inspector auto-scroll
        // -----------------------------------------------------------------

        // Called after the inspector swaps in a new editor panel; brings the visible editor's
        // first row into view so the user doesn't have to hunt for it.
        public void ScrollInspectorToTop()
        {
            try
            {
                if (InspectorScroll != null)
                    InspectorScroll.Offset = new Vector(InspectorScroll.Offset.X, 0);
            }
            catch { }
        }

        // -----------------------------------------------------------------
        // Shared friendly-label helpers
        // -----------------------------------------------------------------

        private static string FriendlyEffectName(string? effectType) => effectType switch
        {
            EffectTypes.Flash      => Loc.Get("deeper_friendly_effect_flash"),
            EffectTypes.Bubble     => Loc.Get("deeper_friendly_effect_bubble"),
            EffectTypes.Subliminal => Loc.Get("deeper_friendly_effect_subliminal"),
            EffectTypes.Overlay    => Loc.Get("deeper_friendly_effect_overlay"),
            EffectTypes.Haptic     => Loc.Get("deeper_friendly_effect_haptic"),
            _ => effectType ?? "Effect"
        };

        private static string BuildEffectDetail(TimelineItem ti)
        {
            switch (ti.EffectType)
            {
                case EffectTypes.Subliminal:
                    return string.IsNullOrEmpty(ti.EffectText)
                        ? $"{ti.EffectDurationMs} ms"
                        : ti.EffectText!.Length > 32 ? ti.EffectText![..32] + "…" : ti.EffectText!;
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
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            int total = (int)Math.Round(seconds);
            int m = total / 60;
            int s = total % 60;
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}", m, s);
        }
    }
}
