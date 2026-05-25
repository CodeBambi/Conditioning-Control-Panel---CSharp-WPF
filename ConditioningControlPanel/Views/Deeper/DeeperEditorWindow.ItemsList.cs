using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Views.Deeper
{
    // Sidebar Items list: shows every rule / effect / haptic / region on the
    // current enhancement, with two-way selection sync to the timeline and a
    // Time / Kind sort toggle.
    public partial class DeeperEditorWindow
    {
        private bool _itemsListSortByKind;
        private bool _suppressItemsListSelection;

        internal sealed class TimelineListEntry
        {
            public string Icon { get; init; } = "";
            public string KindLabel { get; init; } = "";
            public int KindOrder { get; init; }
            public string Label { get; init; } = "";
            public double TimeSeconds { get; init; }
            public object? Target { get; init; }
            public HapticTrack? HapticTrack { get; init; }
            public Brush KindBrush { get; init; } = Brushes.Gray;

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

        private void ItemsListSort_Changed(object sender, RoutedEventArgs e)
        {
            if (RbItemsSortKind == null || RbItemsSortTime == null) return;
            _itemsListSortByKind = RbItemsSortKind.IsChecked == true;
            BuildItemsList();
        }

        // Repopulate the list from current enhancement state and re-sync the
        // ListBox's selection to whatever the timeline is currently showing.
        // Cheap (O(rules+effects+haptics+regions)); safe to call from any
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
                if (match != null) ItemsListBox.ScrollIntoView(match);
            }
            finally { _suppressItemsListSelection = false; }

            if (TxtItemsListCount != null)
                TxtItemsListCount.Text = entries.Count == 0 ? "" : $"({entries.Count})";
            if (TxtItemsListEmpty != null)
                TxtItemsListEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool IsEntrySelected(TimelineListEntry e)
        {
            return ReferenceEquals(e.Target, _selectedRule)
                || ReferenceEquals(e.Target, _selectedRegion)
                || ReferenceEquals(e.Target, _selectedHaptic)
                || ReferenceEquals(e.Target, _selectedEffect);
        }

        private void ItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressItemsListSelection) return;
            if (ItemsListBox?.SelectedItem is not TimelineListEntry entry) return;
            ActivateEntry(entry);
        }

        // Row-level delete (× button). Routes through the same per-type delete
        // path each toolbar delete button uses so undo / cleanup / validation
        // behavior stays identical. e.Handled keeps the click from also
        // selecting the row.
        private void ItemsListDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.Tag is not TimelineListEntry entry) return;
            e.Handled = true;

            switch (entry.Target)
            {
                case Models.Deeper.EnhancementRule rule:
                    _selectedRule = rule;
                    BtnDeleteRule_Click(this, new RoutedEventArgs());
                    break;
                case Models.Deeper.Region region:
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

        // Double-click also seeks the playhead to the item's time so the user
        // can jump to where the effect fires.
        private void ItemsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
            // Time-reached fires at an explicit time; region-bound rules surface
            // at the start of their constrained region (so the list is scannable
            // chronologically). Others fall back to 0.
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
                return $"{kind}: {Truncate(item.EffectText, 36)}";
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

        private Brush? TryFindBrush(string resourceKey)
        {
            try { return TryFindResource(resourceKey) as Brush; }
            catch { return null; }
        }

        private static Brush? ParseHexBrush(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try
            {
                var converted = ColorConverter.ConvertFromString(hex);
                if (converted is Color c)
                {
                    var brush = new SolidColorBrush(c);
                    brush.Freeze();
                    return brush;
                }
            }
            catch { }
            return null;
        }
    }
}
