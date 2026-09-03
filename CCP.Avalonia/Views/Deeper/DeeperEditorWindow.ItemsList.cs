using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// One row in the sidebar Items list.
    ///
    /// PORTED from the nested <c>DeeperEditorWindow.TimelineListEntry</c> in
    /// ConditioningControlPanel/Views/Deeper/DeeperEditorWindow.ItemsList.cs, lifted to namespace
    /// scope so the row <c>DataTemplate</c> can name it in <c>x:DataType</c> without nested-type
    /// syntax (compiled bindings are on for this head).
    /// </summary>
    public sealed class TimelineListEntry
    {
        public string Icon { get; init; } = "";
        public string KindLabel { get; init; } = "";
        public int KindOrder { get; init; }
        public string Label { get; init; } = "";
        public double TimeSeconds { get; init; }
        public object? Target { get; init; }
        public HapticTrack? HapticTrack { get; init; }
        public IBrush KindBrush { get; init; } = Brushes.Gray;

        public string TimeText => FormatTimeShort(TimeSeconds);

        private static string FormatTimeShort(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }

    // PORTED from ConditioningControlPanel/Views/Deeper/DeeperEditorWindow.ItemsList.cs. Deviations:
    //   TimelineListEntry            -> lifted out of the class (see above)
    //   Visibility                   -> IsVisible
    //   ColorConverter.ConvertFromString -> Color.Parse
    //   TryFindResource(k) as Brush  -> this.TryFindResource(k, out v) is IBrush
    //   MouseDoubleClick             -> DoubleTapped
    //
    // Sidebar Items list: every rule / effect / haptic / region on the current enhancement, with
    // two-way selection sync to the timeline and a Time / Kind sort toggle.
    public partial class DeeperEditorWindow
    {
        private bool _itemsListSortByKind;
        private bool _suppressItemsListSelection;

        private void ItemsListSort_Changed(object? sender, RoutedEventArgs e)
        {
            if (RbItemsSortKind == null || RbItemsSortTime == null) return;
            _itemsListSortByKind = RbItemsSortKind.IsChecked == true;
            BuildItemsList();
        }

        // Repopulate the list from current enhancement state and re-sync the ListBox's selection to
        // whatever the timeline is currently showing. Cheap; safe to call from any
        // SelectXxx / Add / Delete / drag-end hook.
        internal void BuildItemsList()
        {
            if (ItemsListBox == null) return;

            var entries = new List<TimelineListEntry>();

            foreach (var rule in _enhancement.Rules)
            {
                if (rule == null) continue;
                entries.Add(new TimelineListEntry
                {
                    Icon = "🎯",
                    KindLabel = "Rule",
                    KindOrder = 0,
                    Label = DescribeRule(rule),
                    TimeSeconds = ExtractRuleTime(rule),
                    Target = rule,
                    KindBrush = TryFindBrush("DeeperAccentBrush") ?? Brushes.MediumPurple
                });
            }

            foreach (var region in _enhancement.Regions)
            {
                if (region == null) continue;
                entries.Add(new TimelineListEntry
                {
                    Icon = "▦",
                    KindLabel = "Region",
                    KindOrder = 1,
                    Label = string.IsNullOrWhiteSpace(region.Label) ? (region.Id ?? "(region)") : region.Label!,
                    TimeSeconds = region.Start,
                    Target = region,
                    KindBrush = ParseHexBrush(region.Color) ?? Brushes.MediumPurple
                });
            }

            foreach (var track in _enhancement.HapticTracks)
            {
                if (track?.Events == null) continue;
                foreach (var ev in track.Events)
                {
                    if (ev == null) continue;
                    entries.Add(new TimelineListEntry
                    {
                        Icon = "📳",
                        KindLabel = "Haptic",
                        KindOrder = 2,
                        Label = string.IsNullOrWhiteSpace(ev.PatternName) ? "Haptic" : ev.PatternName!,
                        TimeSeconds = ev.Start,
                        Target = ev,
                        HapticTrack = track,
                        KindBrush = ParseHexBrush("#7B5CFF") ?? Brushes.MediumPurple
                    });
                }
            }

            foreach (var item in _enhancement.TimelineItems)
            {
                if (item == null || item.Kind != TimelineItemKind.Effect) continue;
                if (item.EffectType == EffectTypes.Haptic) continue; // surfaced via HapticTracks
                entries.Add(new TimelineListEntry
                {
                    Icon = EffectIcon(item.EffectType),
                    KindLabel = NiceEffectName(item.EffectType),
                    KindOrder = 3 + EffectSubOrder(item.EffectType),
                    Label = DescribeEffect(item),
                    TimeSeconds = item.Start,
                    Target = item,
                    KindBrush = ParseHexBrush(item.Color
                        ?? (EffectColors.TryGetValue(item.EffectType ?? "", out var c) ? c : null))
                        ?? Brushes.MediumPurple
                });
            }

            if (_itemsListSortByKind)
                entries = entries.OrderBy(x => x.KindOrder).ThenBy(x => x.TimeSeconds).ToList();
            else
                entries = entries.OrderBy(x => x.TimeSeconds).ThenBy(x => x.KindOrder).ToList();

            _suppressItemsListSelection = true;
            try
            {
                ItemsListBox.ItemsSource = entries;
                var match = entries.FirstOrDefault(IsEntrySelected);
                ItemsListBox.SelectedItem = match;
                if (match != null) { try { ItemsListBox.ScrollIntoView(match); } catch { } }
            }
            finally { ClearWhenEventsDrained(() => _suppressItemsListSelection = false); }

            if (TxtItemsListCount != null)
                TxtItemsListCount.Text = entries.Count == 0 ? "" : $"({entries.Count})";
            if (TxtItemsListEmpty != null)
                TxtItemsListEmpty.IsVisible = entries.Count == 0;
        }

        private bool IsEntrySelected(TimelineListEntry e)
        {
            return ReferenceEquals(e.Target, _selectedRule)
                || ReferenceEquals(e.Target, _selectedRegion)
                || ReferenceEquals(e.Target, _selectedHaptic)
                || ReferenceEquals(e.Target, _selectedEffect);
        }

        private void ItemsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressItemsListSelection) return;
            if (ItemsListBox?.SelectedItem is not TimelineListEntry entry) return;
            ActivateEntry(entry);
        }

        // Row-level delete (x button). Routes through the same per-type delete path each toolbar
        // delete button uses so undo / cleanup / validation behaviour stays identical. Handled
        // keeps the click from also selecting the row.
        //
        // Wired from the constructor by walking the template's Button (Classes="itemsListDelete")
        // rather than a XAML Click= attribute, because Avalonia's compiled-binding DataTemplates
        // resolve a Click handler against the DataContext (a TimelineListEntry), not the Window.
        private void ItemsListDelete_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not TimelineListEntry entry) return;
            e.Handled = true;

            switch (entry.Target)
            {
                case EnhancementRule rule:
                    _selectedRule = rule;
                    BtnDeleteRule_Click(this, new RoutedEventArgs());
                    break;
                case Region region:
                    _selectedRegion = region;
                    BtnDeleteRegion_Click(this, new RoutedEventArgs());
                    break;
                case HapticEvent ev when entry.HapticTrack != null:
                    _selectedHaptic = ev;
                    _selectedHapticTrack = entry.HapticTrack;
                    BtnDeleteHaptic_Click(this, new RoutedEventArgs());
                    break;
                case TimelineItem ti:
                    _selectedEffect = ti;
                    BtnDeleteEffect_Click(this, new RoutedEventArgs());
                    break;
            }
        }

        // Double-click also seeks the playhead to the item's time so the user can jump to where
        // the effect fires.
        private void ItemsListBox_DoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (ItemsListBox?.SelectedItem is not TimelineListEntry entry) return;
            if (_totalSeconds <= 0) return;
            var frac = Math.Clamp(entry.TimeSeconds / _totalSeconds, 0, 1);
            SeekToFraction(frac);
        }

        private void ActivateEntry(TimelineListEntry entry)
        {
            switch (entry.Target)
            {
                case EnhancementRule rule:
                    SelectRule(rule);
                    break;
                case Region region:
                    SelectRegion(region);
                    break;
                case HapticEvent ev when entry.HapticTrack != null:
                    SelectHaptic(entry.HapticTrack, ev);
                    break;
                case TimelineItem ti:
                    SelectEffect(ti);
                    break;
            }
        }

        private static double ExtractRuleTime(EnhancementRule rule)
        {
            // Time-reached fires at an explicit time; everything else falls back to 0 so the list
            // is scannable chronologically.
            if (rule.Trigger is TimeReachedTrigger tr) return Math.Max(0, tr.Time);
            return 0;
        }

        private static string DescribeRule(EnhancementRule rule)
        {
            var trigger = FriendlyTriggerName(rule.Trigger?.Type ?? "");
            var action = FriendlyActionName(rule.Action?.Type ?? "");
            return $"{trigger} → {action}";
        }

        private static string DescribeEffect(TimelineItem item)
        {
            var kind = NiceEffectName(item.EffectType);
            if (item.EffectType == EffectTypes.Subliminal && !string.IsNullOrWhiteSpace(item.EffectText))
                return $"{kind}: {Truncate(item.EffectText!, 36)}";
            if (item.EffectType == EffectTypes.Overlay && !string.IsNullOrWhiteSpace(item.EffectOverlayKind))
                return $"{kind} · {item.EffectOverlayKind}";
            return kind;
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        private static string EffectIcon(string? type) => type switch
        {
            EffectTypes.Flash      => "⚡",
            EffectTypes.Bubble     => "🫧",
            EffectTypes.Subliminal => "💭",
            EffectTypes.Overlay    => "🟪",
            _                      => "✨"
        };

        private static string NiceEffectName(string? type) => type switch
        {
            EffectTypes.Flash      => "Flash",
            EffectTypes.Bubble     => "Bubble",
            EffectTypes.Subliminal => "Subliminal",
            EffectTypes.Overlay    => "Overlay",
            EffectTypes.Haptic     => "Haptic",
            _                      => string.IsNullOrEmpty(type) ? "Effect" : type!
        };

        private static int EffectSubOrder(string? type) => type switch
        {
            EffectTypes.Flash      => 0,
            EffectTypes.Bubble     => 1,
            EffectTypes.Subliminal => 2,
            EffectTypes.Overlay    => 3,
            _                      => 9
        };

        private IBrush? TryFindBrush(string resourceKey)
        {
            try
            {
                return this.TryFindResource(resourceKey, out var v) && v is IBrush b ? b : null;
            }
            catch { return null; }
        }

        private static IBrush? ParseHexBrush(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try { return new SolidColorBrush(Color.Parse(hex!)); }
            catch { return null; }
        }
    }
}
