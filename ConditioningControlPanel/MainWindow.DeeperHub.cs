using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel
{
    // Mission 2 (Deeper Hub redesign) — filter / sort / search plumbing and
    // row view-model projection. The XAML in MainWindow.xaml binds
    // DeeperLibraryList.ItemsSource to DeeperFilteredEntries; this partial
    // owns the source-of-truth list, the filter/sort state, the per-row
    // VM projection, and the wire-up for the search box, filter pills,
    // sort dropdown, and per-row action buttons.
    public partial class MainWindow
    {
        public enum DeeperMediaTypeFilter { All, Video, Audio }
        public enum DeeperSortMode { Recent, Name, Creator }

        // -------------------------------------------------------------------
        // Per-row view model. Pre-computed strings + brushes + visibilities so
        // the DataTemplate can stay pure-bind (no converters). Holds the
        // original Entry so action handlers can recover FilePath.
        // -------------------------------------------------------------------
        public sealed class DeeperLibraryRowVm
        {
            public EnhancementLibraryEntry Entry { get; init; } = new();

            // Identity / header
            public string Name => Entry.Name;
            public string MediaTypeIcon => Entry.MediaType == MediaTypes.Audio ? "🎵" : "🎬";
            public Brush MediaTypeBadgeBg { get; init; } = Brushes.Transparent;
            public Brush MediaTypeBadgeFg { get; init; } = Brushes.White;

            // Meta line
            public string CreatorDisplay { get; init; } = "";
            public Visibility ShowCreator { get; init; } = Visibility.Collapsed;

            public string MediaSourceLabel { get; init; } = "";
            public string MediaSourceGlyph { get; init; } = "";
            public Brush MediaSourceBrush { get; init; } = Brushes.Gray;
            public Visibility ShowMediaSource { get; init; } = Visibility.Collapsed;

            public string TimestampDisplay { get; init; } = "";
            public Visibility ShowTimestamp { get; init; } = Visibility.Collapsed;

            // Tag chips (small visual list)
            public List<DeeperAutoTagVm> Tags { get; init; } = new();
            public Visibility ShowTags { get; init; } = Visibility.Collapsed;

            // Action buttons
            public Visibility ShowSubmitButton { get; init; } = Visibility.Collapsed;
            public bool SubmitEnabled { get; init; }
            public string SubmitTooltip { get; init; } = "";
        }

        public sealed class DeeperAutoTagVm
        {
            public string Glyph { get; init; } = "";
            public string Label { get; init; } = "";
            public Brush Background { get; init; } = Brushes.Transparent;
            public Brush Foreground { get; init; } = Brushes.White;
        }

        // -------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------

        private readonly List<EnhancementLibraryEntry> _deeperAllEntries = new();
        public ObservableCollection<DeeperLibraryRowVm> DeeperFilteredEntries { get; } = new();

        private string _deeperSearchText = "";
        private DeeperMediaTypeFilter _deeperMediaTypeFilter = DeeperMediaTypeFilter.All;
        private bool _deeperFilterHaptics;
        private bool _deeperFilterWebcam;
        private DeeperSortMode _deeperSortMode = DeeperSortMode.Recent;

        private DispatcherTimer? _deeperSearchDebounceTimer;
        private const int DeeperSearchDebounceMs = 150;
        private bool _deeperHubInitDone;

        // -------------------------------------------------------------------
        // Filter + sort
        // -------------------------------------------------------------------

        private static bool DeeperEntryMatchesSearch(EnhancementLibraryEntry e, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            if (e == null) return false;
            if (Contains(e.Name, needle)) return true;
            if (Contains(e.Creator, needle)) return true;
            if (e.AutoTags != null)
                foreach (var tag in e.AutoTags) if (Contains(tag, needle)) return true;
            return false;
            static bool Contains(string? hay, string n) =>
                !string.IsNullOrEmpty(hay) && hay.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool DeeperEntryMatchesMediaType(EnhancementLibraryEntry e, DeeperMediaTypeFilter filter) => filter switch
        {
            DeeperMediaTypeFilter.All   => true,
            DeeperMediaTypeFilter.Video => string.Equals(e.MediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase),
            DeeperMediaTypeFilter.Audio => string.Equals(e.MediaType, MediaTypes.Audio, StringComparison.OrdinalIgnoreCase),
            _ => true,
        };

        private static bool DeeperEntryHasTag(EnhancementLibraryEntry e, string tag)
            => e.AutoTags != null && e.AutoTags.Contains(tag);

        private IEnumerable<EnhancementLibraryEntry> SortDeeperEntries(IEnumerable<EnhancementLibraryEntry> src) => _deeperSortMode switch
        {
            DeeperSortMode.Name    => src.OrderBy(e => e.Name ?? "", StringComparer.OrdinalIgnoreCase),
            DeeperSortMode.Creator => src.OrderBy(e => e.Creator ?? "", StringComparer.OrdinalIgnoreCase),
            _                      => src.OrderByDescending(e => e.LastModified),
        };

        private void ApplyDeeperFilterAndSort()
        {
            if (!_deeperHubInitDone) return;
            try
            {
                var needle = (_deeperSearchText ?? "").Trim();
                var pass = _deeperAllEntries.Where(e =>
                    DeeperEntryMatchesSearch(e, needle) &&
                    DeeperEntryMatchesMediaType(e, _deeperMediaTypeFilter) &&
                    (!_deeperFilterHaptics || DeeperEntryHasTag(e, EnhancementAutoTagger.TagHaptics)) &&
                    (!_deeperFilterWebcam  || DeeperEntryHasTag(e, EnhancementAutoTagger.TagWebcam)));

                var sorted = SortDeeperEntries(pass).Select(BuildRowVm).ToList();

                DeeperFilteredEntries.Clear();
                foreach (var vm in sorted) DeeperFilteredEntries.Add(vm);

                UpdateDeeperFilterPillCounts();
                UpdateDeeperEmptyState(sorted.Count, _deeperAllEntries.Count);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyDeeperFilterAndSort error: {Error}", ex.Message); }
        }

        // -------------------------------------------------------------------
        // Row VM construction
        // -------------------------------------------------------------------

        private DeeperLibraryRowVm BuildRowVm(EnhancementLibraryEntry e)
        {
            var (mediaLabel, mediaGlyph, mediaBrushKey) = ResolveMediaSourceDisplay(e.MediaSource);

            var typeBadgeBgKey = e.MediaType == MediaTypes.Audio
                ? "DeeperHubAudioBadgeBgBrush"
                : "DeeperHubVideoBadgeBgBrush";

            var tags = new List<DeeperAutoTagVm>();
            if (e.AutoTags != null)
            {
                foreach (var tag in e.AutoTags)
                {
                    var (glyph, key, bgKey) = tag switch
                    {
                        EnhancementAutoTagger.TagHaptics => ("📳", "deeper_library_autotag_haptics", "DeeperHubHapticsChipBgBrush"),
                        EnhancementAutoTagger.TagWebcam  => ("📷", "deeper_library_autotag_webcam",  "DeeperHubWebcamChipBgBrush"),
                        _                                => ("●",  "",                                "DeeperAccentTransparent20Brush"),
                    };
                    var label = string.IsNullOrEmpty(key) ? tag : Loc.Get(key);
                    tags.Add(new DeeperAutoTagVm
                    {
                        Glyph = glyph,
                        Label = label,
                        Background = (Brush)FindResource(bgKey),
                        Foreground = (Brush)FindResource("TextLightBrush"),
                    });
                }
            }

            bool eligible = IsCatalogueEligible(e);
            bool hasAuth = !string.IsNullOrEmpty(App.Settings?.Current?.AuthToken);

            return new DeeperLibraryRowVm
            {
                Entry = e,
                MediaTypeBadgeBg = (Brush)FindResource(typeBadgeBgKey),
                MediaTypeBadgeFg = (Brush)FindResource("TextLightBrush"),

                CreatorDisplay   = string.IsNullOrEmpty(e.Creator) ? "" : e.Creator,
                ShowCreator      = string.IsNullOrEmpty(e.Creator) ? Visibility.Collapsed : Visibility.Visible,

                MediaSourceLabel = mediaLabel,
                MediaSourceGlyph = mediaGlyph,
                MediaSourceBrush = (Brush)FindResource(mediaBrushKey),
                ShowMediaSource  = string.IsNullOrEmpty(mediaLabel) ? Visibility.Collapsed : Visibility.Visible,

                TimestampDisplay = FormatRelativeTime(e.LastModified),
                ShowTimestamp    = e.LastModified == default ? Visibility.Collapsed : Visibility.Visible,

                Tags     = tags,
                ShowTags = tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible,

                ShowSubmitButton = eligible ? Visibility.Visible : Visibility.Collapsed,
                SubmitEnabled    = eligible && hasAuth,
                SubmitTooltip    = Loc.Get(hasAuth
                    ? "deeper_library_submit_tooltip"
                    : "deeper_library_submit_button_disabled_tooltip"),
            };
        }

        // Mirrors the cases in the old BuildDeeperMediaLine — local-exists vs
        // local-missing vs URL vs none. Returns ("", "", "TextDimBrush") for
        // empty source so the row collapses the meta segment.
        private static (string label, string glyph, string brushKey) ResolveMediaSourceDisplay(string mediaSource)
        {
            if (string.IsNullOrEmpty(mediaSource))
                return ("", "", "TextDimBrush");

            if (mediaSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                mediaSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string host;
                try { host = new Uri(mediaSource).Host; }
                catch { host = mediaSource; }
                return (host, "🌐", "DeeperAccentBrush");
            }

            bool exists = false;
            try { exists = System.IO.File.Exists(mediaSource); } catch { }
            var name = System.IO.Path.GetFileName(mediaSource);
            if (string.IsNullOrEmpty(name)) name = mediaSource;
            return (name,
                    exists ? "✓" : "⚠",
                    exists ? "DeeperAccentBrush" : "TextMutedBrush");
        }

        private static string FormatRelativeTime(DateTime when)
        {
            if (when == default) return "";
            var diff = DateTime.Now - when;
            if (diff.TotalMinutes < 1) return Loc.Get("deeper_hub_time_just_now");
            if (diff.TotalMinutes < 60) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_minutes_ago"), (int)diff.TotalMinutes);
            if (diff.TotalHours   < 24) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_hours_ago"),   (int)diff.TotalHours);
            if (diff.TotalDays    < 7)  return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_days_ago"),    (int)diff.TotalDays);
            if (diff.TotalDays    < 31) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_weeks_ago"),   (int)(diff.TotalDays / 7));
            if (diff.TotalDays    < 365) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_months_ago"), (int)(diff.TotalDays / 30));
            return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_years_ago"), (int)(diff.TotalDays / 365));
        }

        // -------------------------------------------------------------------
        // Pill counts + empty state
        // -------------------------------------------------------------------

        private void UpdateDeeperFilterPillCounts()
        {
            try
            {
                var needle = (_deeperSearchText ?? "").Trim();
                var searched = _deeperAllEntries.Where(e => DeeperEntryMatchesSearch(e, needle)).ToList();

                int all = searched.Count;
                int video   = searched.Count(e => DeeperEntryMatchesMediaType(e, DeeperMediaTypeFilter.Video));
                int audio   = searched.Count(e => DeeperEntryMatchesMediaType(e, DeeperMediaTypeFilter.Audio));
                int haptics = searched.Count(e => DeeperEntryHasTag(e, EnhancementAutoTagger.TagHaptics));
                int webcam  = searched.Count(e => DeeperEntryHasTag(e, EnhancementAutoTagger.TagWebcam));

                if (TxtDeeperPillAllCount     != null) TxtDeeperPillAllCount.Text     = all.ToString(CultureInfo.InvariantCulture);
                if (TxtDeeperPillVideoCount   != null) TxtDeeperPillVideoCount.Text   = video.ToString(CultureInfo.InvariantCulture);
                if (TxtDeeperPillAudioCount   != null) TxtDeeperPillAudioCount.Text   = audio.ToString(CultureInfo.InvariantCulture);
                if (TxtDeeperPillHapticsCount != null) TxtDeeperPillHapticsCount.Text = haptics.ToString(CultureInfo.InvariantCulture);
                if (TxtDeeperPillWebcamCount  != null) TxtDeeperPillWebcamCount.Text  = webcam.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
        }

        private void UpdateDeeperEmptyState(int filteredCount, int totalCount)
        {
            if (TxtDeeperLibraryEmpty == null) return;
            if (totalCount == 0)
            {
                TxtDeeperLibraryEmpty.Text = Loc.Get("deeper_library_empty");
                TxtDeeperLibraryEmpty.Visibility = Visibility.Visible;
                return;
            }
            if (filteredCount == 0)
            {
                TxtDeeperLibraryEmpty.Text = Loc.Get("deeper_hub_empty_filtered");
                TxtDeeperLibraryEmpty.Visibility = Visibility.Visible;
                return;
            }
            TxtDeeperLibraryEmpty.Visibility = Visibility.Collapsed;
        }

        // -------------------------------------------------------------------
        // UI event handlers (wired from XAML)
        // -------------------------------------------------------------------

        private void DeeperSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_deeperHubInitDone) return;
            _deeperSearchText = TxtDeeperSearch?.Text ?? "";
            if (_deeperSearchDebounceTimer == null)
            {
                _deeperSearchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DeeperSearchDebounceMs) };
                _deeperSearchDebounceTimer.Tick += (_, _) =>
                {
                    _deeperSearchDebounceTimer?.Stop();
                    ApplyDeeperFilterAndSort();
                };
            }
            _deeperSearchDebounceTimer.Stop();
            _deeperSearchDebounceTimer.Start();
        }

        // Media-type pills are mutually exclusive (All / Video / Audio).
        // Haptics / Webcam toggle independently and stack on top.
        private void DeeperPillAll_Click(object sender, RoutedEventArgs e)   => SetDeeperMediaTypeFilter(DeeperMediaTypeFilter.All);
        private void DeeperPillVideo_Click(object sender, RoutedEventArgs e) => SetDeeperMediaTypeFilter(DeeperMediaTypeFilter.Video);
        private void DeeperPillAudio_Click(object sender, RoutedEventArgs e) => SetDeeperMediaTypeFilter(DeeperMediaTypeFilter.Audio);

        private void DeeperPillHaptics_Click(object sender, RoutedEventArgs e)
        {
            _deeperFilterHaptics = !_deeperFilterHaptics;
            RefreshDeeperPillVisuals();
            ApplyDeeperFilterAndSort();
        }
        private void DeeperPillWebcam_Click(object sender, RoutedEventArgs e)
        {
            _deeperFilterWebcam = !_deeperFilterWebcam;
            RefreshDeeperPillVisuals();
            ApplyDeeperFilterAndSort();
        }

        private void SetDeeperMediaTypeFilter(DeeperMediaTypeFilter f)
        {
            _deeperMediaTypeFilter = f;
            RefreshDeeperPillVisuals();
            ApplyDeeperFilterAndSort();
        }

        private void RefreshDeeperPillVisuals()
        {
            if (BtnDeeperPillAll     != null) BtnDeeperPillAll.IsChecked     = _deeperMediaTypeFilter == DeeperMediaTypeFilter.All;
            if (BtnDeeperPillVideo   != null) BtnDeeperPillVideo.IsChecked   = _deeperMediaTypeFilter == DeeperMediaTypeFilter.Video;
            if (BtnDeeperPillAudio   != null) BtnDeeperPillAudio.IsChecked   = _deeperMediaTypeFilter == DeeperMediaTypeFilter.Audio;
            if (BtnDeeperPillHaptics != null) BtnDeeperPillHaptics.IsChecked = _deeperFilterHaptics;
            if (BtnDeeperPillWebcam  != null) BtnDeeperPillWebcam.IsChecked  = _deeperFilterWebcam;
        }

        private void DeeperSort_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_deeperHubInitDone || CmbDeeperSort?.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
            _deeperSortMode = (item.Tag as string) switch
            {
                "name"    => DeeperSortMode.Name,
                "creator" => DeeperSortMode.Creator,
                _         => DeeperSortMode.Recent,
            };
            ApplyDeeperFilterAndSort();
        }

        // -------------------------------------------------------------------
        // Per-row action handlers (DataTemplate buttons → DataContext is a VM)
        // -------------------------------------------------------------------

        private static EnhancementLibraryEntry? EntryFromDataContext(object sender)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DeeperLibraryRowVm vm) return vm.Entry;
            return null;
        }

        private void DeeperRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry != null) OpenDeeperFile(entry.FilePath);
        }

        private void DeeperRowPlay_Click(object sender, RoutedEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry == null) return;
            e.Handled = true;
            // Bug fix: OpenInDeeperPlayer takes a MEDIA path (mp4/mp3/etc.).
            // entry.FilePath is the .ccpenh.json path — passing it to
            // OpenInDeeperPlayer made the player try to play the JSON as
            // audio and fail with "Couldn't open that audio file."
            // OpenDeeperEnhancementInPlayer routes the JSON through the host,
            // which knows how to load the bound media (URL or local file).
            OpenDeeperEnhancementInPlayer(entry.FilePath);
        }

        private void DeeperRowDelete_Click(object sender, RoutedEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry == null) return;
            e.Handled = true;
            DeleteDeeperLibraryEntry(entry);
        }

        private void DeeperRowSubmit_Click(object sender, RoutedEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry == null) return;
            e.Handled = true;
            _ = SubmitDeeperLibraryEntryAsync(entry);
        }

        // -------------------------------------------------------------------
        // Init + reload-from-disk
        // -------------------------------------------------------------------

        private void InitializeDeeperHub()
        {
            if (_deeperHubInitDone) return;
            _deeperHubInitDone = true;
            RefreshDeeperPillVisuals();
            ReloadDeeperLibraryFromDisk();
        }

        private void ReloadDeeperLibraryFromDisk()
        {
            var lib = App.EnhancementLibrary;
            if (lib == null) return;
            _deeperAllEntries.Clear();
            foreach (var entry in lib.ScanLibrary()) _deeperAllEntries.Add(entry);
            ApplyDeeperFilterAndSort();
            if (TxtDeeperLibraryCount != null)
                TxtDeeperLibraryCount.Text = string.Format(Loc.Get("deeper_library_count_fmt"), _deeperAllEntries.Count);
        }
    }
}
