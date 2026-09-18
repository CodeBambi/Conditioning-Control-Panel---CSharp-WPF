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
using DeeperMediaTypeFilter = ConditioningControlPanel.Services.Deeper.EnhancementLibraryFilter.MediaTypeFilter;
using DeeperSortMode = ConditioningControlPanel.Services.Deeper.EnhancementLibraryFilter.SortMode;

namespace ConditioningControlPanel
{
    // Mission 2 (Deeper Hub redesign) — filter / sort / search plumbing and
    // row view-model projection. The XAML in MainWindow.xaml binds
    // DeeperTab.DeeperLibraryList.ItemsSource to DeeperFilteredEntries; this partial
    // owns the source-of-truth list, the filter/sort state, the per-row
    // VM projection, and the wire-up for the search box, filter pills,
    // sort dropdown, and per-row action buttons.
    public partial class MainWindow
    {
        // Filter / sort / count rules live in EnhancementLibraryFilter (pure,
        // unit-tested); this partial only holds the UI state and the wiring.

        // -------------------------------------------------------------------
        // Per-row view model. Pre-computed strings + brushes + visibilities so
        // the DataTemplate can stay pure-bind (no converters). Holds the
        // original Entry so action handlers can recover FilePath.
        // -------------------------------------------------------------------
        public sealed class DeeperLibraryRowVm : System.ComponentModel.INotifyPropertyChanged
        {
            public EnhancementLibraryEntry Entry { get; init; } = new();

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

            // Single-click selection (keyboard + context menu target). Restored by
            // path across list rebuilds in ApplyDeeperFilterAndSort.
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            // Brief accent flash after an import (or when an import turned out to
            // be a file already in the library) so the eye lands on the row.
            private bool _isHighlighted;
            public bool IsHighlighted
            {
                get => _isHighlighted;
                set
                {
                    if (_isHighlighted == value) return;
                    _isHighlighted = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsHighlighted)));
                }
            }

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
            // Explains the glyph: the missing-media warning used to be a bare "⚠".
            public string MediaSourceTooltip { get; init; } = "";
            public Brush MediaSourceBrush { get; init; } = Brushes.Gray;
            public Visibility ShowMediaSource { get; init; } = Visibility.Collapsed;

            public string TimestampDisplay { get; init; } = "";
            public Visibility ShowTimestamp { get; init; } = Visibility.Collapsed;

            // Media length in the mini timeline's m:ss / h:mm:ss shape.
            // Collapsed (not "0:00") while nobody knows.
            public string DurationDisplay { get; init; } = "";
            public Visibility ShowDuration { get; init; } = Visibility.Collapsed;

            // Tag chips (small visual list)
            public List<DeeperAutoTagVm> Tags { get; init; } = new();
            public Visibility ShowTags { get; init; } = Visibility.Collapsed;

            // Catalogue submission status badge — shown once the user has
            // submitted this file (pending / published / rejected). Always
            // visible (unlike the hover-only action buttons) so acceptance is
            // discoverable at a glance.
            public Visibility ShowSubmissionBadge { get; init; } = Visibility.Collapsed;
            public string SubmissionBadgeGlyph { get; init; } = "";
            public string SubmissionBadgeLabel { get; init; } = "";
            public Brush SubmissionBadgeBg { get; init; } = Brushes.Transparent;
            public Brush SubmissionBadgeFg { get; init; } = Brushes.White;
            public string SubmissionBadgeTooltip { get; init; } = "";

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

        // Full path of the selected row (null = none). Kept as a path, not a VM,
        // because every filter/sort pass rebuilds the VM list.
        private string? _deeperSelectedPath;
        // Path of the row currently flashing after an import/reveal. Held here (not
        // only on the row VM) because the library watcher's debounced LibraryChanged
        // lands mid-flash and ApplyDeeperFilterAndSort rebuilds every VM; without it
        // the highlight died at ~300 ms instead of the intended 1800 ms.
        private string? _deeperHighlightedPath;

        private string _deeperSearchText = "";
        private DeeperMediaTypeFilter _deeperMediaTypeFilter = DeeperMediaTypeFilter.All;
        private bool _deeperFilterHaptics;
        private bool _deeperFilterWebcam;
        private DeeperSortMode _deeperSortMode = DeeperSortMode.Recent;
        private bool _deeperSortDescending = EnhancementLibraryFilter.DefaultDescending(DeeperSortMode.Recent);

        private EnhancementLibraryFilter.Criteria CurrentDeeperCriteria()
            => new((_deeperSearchText ?? "").Trim(), _deeperMediaTypeFilter, _deeperFilterHaptics, _deeperFilterWebcam);

        private DispatcherTimer? _deeperSearchDebounceTimer;
        private const int DeeperSearchDebounceMs = 150;
        private bool _deeperHubInitDone;

        // -------------------------------------------------------------------
        // Filter + sort
        // -------------------------------------------------------------------

        private void ApplyDeeperFilterAndSort()
        {
            if (!_deeperHubInitDone) return;
            try
            {
                var criteria = CurrentDeeperCriteria();
                var pass = _deeperAllEntries.Where(e => EnhancementLibraryFilter.Matches(e, criteria));
                var sorted = EnhancementLibraryFilter.Sort(pass, _deeperSortMode, _deeperSortDescending)
                    .Select(BuildRowVm).ToList();

                DeeperFilteredEntries.Clear();
                foreach (var vm in sorted)
                {
                    vm.IsSelected = DeeperPathsEqual(vm.Entry.FilePath, _deeperSelectedPath);
                    vm.IsHighlighted = _deeperHighlightedPath != null && DeeperPathsEqual(vm.Entry.FilePath, _deeperHighlightedPath);
                    DeeperFilteredEntries.Add(vm);
                }

                UpdateDeeperFilterPillCounts();
                UpdateDeeperEmptyState(sorted.Count, _deeperAllEntries.Count);
                UpdateDeeperHeaderCount(sorted.Count, _deeperAllEntries.Count);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyDeeperFilterAndSort error: {Error}", ex.Message); }
        }

        // -------------------------------------------------------------------
        // Row VM construction
        // -------------------------------------------------------------------

        private DeeperLibraryRowVm BuildRowVm(EnhancementLibraryEntry e)
        {
            var (mediaLabel, mediaGlyph, mediaBrushKey) = ResolveMediaSourceDisplay(e.MediaSource);
            var mediaTooltip = mediaGlyph == "⚠" ? Loc.Get("deeper_hub_tip_media_missing") : (mediaLabel ?? "");

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

            var badge = ResolveSubmissionBadge(e);

            return new DeeperLibraryRowVm
            {
                Entry = e,

                ShowSubmissionBadge   = badge.Show,
                SubmissionBadgeGlyph  = badge.Glyph,
                SubmissionBadgeLabel  = badge.Label,
                SubmissionBadgeBg     = badge.Bg,
                SubmissionBadgeFg     = badge.Fg,
                SubmissionBadgeTooltip = badge.Tooltip,

                MediaTypeBadgeBg = (Brush)FindResource(typeBadgeBgKey),
                MediaTypeBadgeFg = (Brush)FindResource("TextLightBrush"),

                CreatorDisplay   = string.IsNullOrEmpty(e.Creator) ? "" : e.Creator,
                ShowCreator      = string.IsNullOrEmpty(e.Creator) ? Visibility.Collapsed : Visibility.Visible,

                MediaSourceLabel = mediaLabel,
                MediaSourceGlyph = mediaGlyph,
                MediaSourceTooltip = mediaTooltip,
                MediaSourceBrush = (Brush)FindResource(mediaBrushKey),
                ShowMediaSource  = string.IsNullOrEmpty(mediaLabel) ? Visibility.Collapsed : Visibility.Visible,

                TimestampDisplay = FormatRelativeTime(e.LastModified),
                ShowTimestamp    = e.LastModified == default ? Visibility.Collapsed : Visibility.Visible,

                DurationDisplay  = e.DurationSeconds > 0 ? MediaDurationCache.Format(e.DurationSeconds) : "",
                ShowDuration     = e.DurationSeconds > 0 ? Visibility.Visible : Visibility.Collapsed,

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
                catch (UriFormatException) { host = mediaSource; } // swallow: show the raw source
                return (host, "🌐", "DeeperAccentBrush");
            }

            bool exists = false;
            try { exists = System.IO.File.Exists(mediaSource); } catch (Exception ex) { Diag.Swallowed(ex); }
            var name = System.IO.Path.GetFileName(mediaSource);
            if (string.IsNullOrEmpty(name)) name = mediaSource;
            return (name,
                    exists ? "✓" : "⚠",
                    exists ? "DeeperAccentBrush" : "TextMutedBrush");
        }

        // Translucent backgrounds + solid accent foregrounds matching the toast
        // palette (#4CAF50 green / #FFB347 amber / #FF6B6B red). Frozen statics
        // so every row reuses one brush instead of allocating per build.
        private static readonly Brush SubBadgePublishedBg = FrozenBrush("#334CAF50");
        private static readonly Brush SubBadgePendingBg   = FrozenBrush("#33FFB347");
        private static readonly Brush SubBadgeRejectedBg  = FrozenBrush("#33FF6B6B");
        private static readonly Brush SubBadgePublishedFg = FrozenBrush("#7BE08A");
        private static readonly Brush SubBadgePendingFg   = FrozenBrush("#FFC97A");
        private static readonly Brush SubBadgeRejectedFg  = FrozenBrush("#FF9B9B");

        private static SolidColorBrush FrozenBrush(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            b.Freeze();
            return b;
        }

        // Maps a tracked catalogue submission (AppSettings.DeeperSubmissions,
        // keyed by canonical path) to the row's status pill. Collapsed when the
        // user has never submitted this file.
        private (Visibility Show, string Glyph, string Label, Brush Bg, Brush Fg, string Tooltip)
            ResolveSubmissionBadge(EnhancementLibraryEntry e)
        {
            try
            {
                var subs = App.Settings?.Current?.DeeperSubmissions;
                if (subs == null || e == null) return (Visibility.Collapsed, "", "", Brushes.Transparent, Brushes.White, "");

                string key;
                try { key = System.IO.Path.GetFullPath(e.FilePath); } catch { key = e.FilePath; }
                if (!subs.TryGetValue(key, out var rec) || rec == null)
                    return (Visibility.Collapsed, "", "", Brushes.Transparent, Brushes.White, "");

                return (rec.Status ?? "").ToLowerInvariant() switch
                {
                    "approved" or "published" => (Visibility.Visible, "✅",
                        Loc.Get("deeper_submission_badge_published"), SubBadgePublishedBg, SubBadgePublishedFg,
                        Loc.Get("deeper_submission_badge_published_tip")),
                    "rejected" => (Visibility.Visible, "⚠",
                        Loc.Get("deeper_submission_badge_rejected"), SubBadgeRejectedBg, SubBadgeRejectedFg,
                        Loc.Get("deeper_submission_badge_rejected_tip")),
                    _ => (Visibility.Visible, "⏳",
                        Loc.Get("deeper_submission_badge_pending"), SubBadgePendingBg, SubBadgePendingFg,
                        Loc.Get("deeper_submission_badge_pending_tip")),
                };
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex);
                return (Visibility.Collapsed, "", "", Brushes.Transparent, Brushes.White, "");
            }
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
                // Each pill = rows that would show with THAT pill on and every other
                // active filter kept, so the numbers always agree with the list.
                var (all, video, audio, haptics, webcam) = EnhancementLibraryFilter.CountPills(_deeperAllEntries, CurrentDeeperCriteria());

                if (DeeperTab.TxtDeeperPillAllCount     != null) DeeperTab.TxtDeeperPillAllCount.Text     = all.ToString(CultureInfo.InvariantCulture);
                if (DeeperTab.TxtDeeperPillVideoCount   != null) DeeperTab.TxtDeeperPillVideoCount.Text   = video.ToString(CultureInfo.InvariantCulture);
                if (DeeperTab.TxtDeeperPillAudioCount   != null) DeeperTab.TxtDeeperPillAudioCount.Text   = audio.ToString(CultureInfo.InvariantCulture);
                if (DeeperTab.TxtDeeperPillHapticsCount != null) DeeperTab.TxtDeeperPillHapticsCount.Text = haptics.ToString(CultureInfo.InvariantCulture);
                if (DeeperTab.TxtDeeperPillWebcamCount  != null) DeeperTab.TxtDeeperPillWebcamCount.Text  = webcam.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void UpdateDeeperEmptyState(int filteredCount, int totalCount)
        {
            if (DeeperTab.TxtDeeperLibraryEmpty == null) return;
            var actions = DeeperTab.DeeperLibraryEmptyActions;
            if (totalCount == 0)
            {
                DeeperTab.TxtDeeperLibraryEmpty.Text = Loc.Get("deeper_library_empty");
                DeeperTab.TxtDeeperLibraryEmpty.Visibility = Visibility.Visible;
                if (actions != null) actions.Visibility = Visibility.Visible;
                return;
            }
            if (actions != null) actions.Visibility = Visibility.Collapsed;
            if (filteredCount == 0)
            {
                DeeperTab.TxtDeeperLibraryEmpty.Text = Loc.Get("deeper_hub_empty_filtered");
                DeeperTab.TxtDeeperLibraryEmpty.Visibility = Visibility.Visible;
                return;
            }
            DeeperTab.TxtDeeperLibraryEmpty.Visibility = Visibility.Collapsed;
        }

        // "{total} file(s)", plus "({n} shown)" while a filter hides some.
        private void UpdateDeeperHeaderCount(int shownCount, int totalCount)
        {
            if (DeeperTab.TxtDeeperLibraryCount == null) return;
            DeeperTab.TxtDeeperLibraryCount.Text = shownCount == totalCount
                ? string.Format(Loc.Get("deeper_library_count_fmt"), totalCount)
                : string.Format(Loc.Get("deeper_library_count_shown_fmt"), totalCount, shownCount);
        }

        // -------------------------------------------------------------------
        // UI event handlers (wired from XAML)
        // -------------------------------------------------------------------

        internal void DeeperSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_deeperHubInitDone) return;
            _deeperSearchText = DeeperTab.TxtDeeperSearch?.Text ?? "";
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
        internal void DeeperPillAll_Click(object sender, RoutedEventArgs e)   => SetDeeperMediaTypeFilter(DeeperMediaTypeFilter.All);
        internal void DeeperPillVideo_Click(object sender, RoutedEventArgs e) => SetDeeperMediaTypeFilter(DeeperMediaTypeFilter.Video);
        internal void DeeperPillAudio_Click(object sender, RoutedEventArgs e) => SetDeeperMediaTypeFilter(DeeperMediaTypeFilter.Audio);

        internal void DeeperPillHaptics_Click(object sender, RoutedEventArgs e)
        {
            _deeperFilterHaptics = !_deeperFilterHaptics;
            RefreshDeeperPillVisuals();
            ApplyDeeperFilterAndSort();
        }
        internal void DeeperPillWebcam_Click(object sender, RoutedEventArgs e)
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
            if (DeeperTab.BtnDeeperPillAll     != null) DeeperTab.BtnDeeperPillAll.IsChecked     = _deeperMediaTypeFilter == DeeperMediaTypeFilter.All;
            if (DeeperTab.BtnDeeperPillVideo   != null) DeeperTab.BtnDeeperPillVideo.IsChecked   = _deeperMediaTypeFilter == DeeperMediaTypeFilter.Video;
            if (DeeperTab.BtnDeeperPillAudio   != null) DeeperTab.BtnDeeperPillAudio.IsChecked   = _deeperMediaTypeFilter == DeeperMediaTypeFilter.Audio;
            if (DeeperTab.BtnDeeperPillHaptics != null) DeeperTab.BtnDeeperPillHaptics.IsChecked = _deeperFilterHaptics;
            if (DeeperTab.BtnDeeperPillWebcam  != null) DeeperTab.BtnDeeperPillWebcam.IsChecked  = _deeperFilterWebcam;
        }

        // Opens the web catalogue in the user's default browser. The URL is a
        // constant pointing at the public CCP web frontend (cclabs-web,
        // Next.js + Supabase). Process.Start with UseShellExecute=true is the
        // standard "open externally" pattern — bypasses any embedded browser
        // and respects the user's OS default. Errors are non-fatal: the user
        // can always type the URL by hand if shell-execute is locked down.
        internal void BtnDeeperCatalogue_Click(object sender, RoutedEventArgs e)
        {
            const string url = "https://app.cclabs.app/catalogue";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper catalogue URL");
            }
        }

        internal void DeeperSort_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_deeperHubInitDone || DeeperTab.CmbDeeperSort?.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
            _deeperSortMode = (item.Tag as string) switch
            {
                "name"     => DeeperSortMode.Name,
                "creator"  => DeeperSortMode.Creator,
                "duration" => DeeperSortMode.Duration,
                _          => DeeperSortMode.Recent,
            };
            _deeperSortDescending = EnhancementLibraryFilter.DefaultDescending(_deeperSortMode);
            RefreshDeeperSortDirGlyph();
            ApplyDeeperFilterAndSort();
        }

        internal void DeeperSortDir_Click(object sender, RoutedEventArgs e)
        {
            _deeperSortDescending = !_deeperSortDescending;
            RefreshDeeperSortDirGlyph();
            ApplyDeeperFilterAndSort();
        }

        private void RefreshDeeperSortDirGlyph()
        {
            if (DeeperTab.BtnDeeperSortDir != null)
                DeeperTab.BtnDeeperSortDir.Content = _deeperSortDescending ? "▼" : "▲";
        }

        // -------------------------------------------------------------------
        // Per-row action handlers (DataTemplate buttons → DataContext is a VM)
        // -------------------------------------------------------------------

        private static EnhancementLibraryEntry? EntryFromDataContext(object sender)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DeeperLibraryRowVm vm) return vm.Entry;
            return null;
        }

        // Single click selects; double click (MouseLeftButtonDown, ClickCount 2)
        // opens; Enter/Delete/Up/Down work on the selection; right-click menu
        // targets the row under the cursor.
        internal void DeeperRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry == null) return;
            SelectDeeperRow(entry.FilePath, scrollIntoView: false);
            try { DeeperTab.DeeperLibraryList?.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        internal void DeeperRow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            var entry = EntryFromDataContext(sender);
            if (entry == null) return;
            e.Handled = true;
            SelectDeeperRow(entry.FilePath, scrollIntoView: false);
            OpenDeeperFile(entry.FilePath);
        }

        internal void DeeperRowMenuOpen_Click(object sender, RoutedEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry != null) OpenDeeperFile(entry.FilePath);
        }

        internal void DeeperRowMenuReveal_Click(object sender, RoutedEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry == null) return;
            try
            {
                if (!System.IO.File.Exists(entry.FilePath)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + System.IO.Path.GetFullPath(entry.FilePath) + "\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Deeper: reveal in folder failed"); }
        }

        internal void DeeperRowMenuCopyPath_Click(object sender, RoutedEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry == null) return;
            try { Clipboard.SetText(System.IO.Path.GetFullPath(entry.FilePath)); }
            catch (Exception ex) { App.Logger?.Debug("Deeper: copy path failed: {Error}", ex.Message); }
        }

        internal void DeeperRowPlay_Click(object sender, RoutedEventArgs e)
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
            //
            // A player that already has THIS file loaded is just brought to the
            // front without reloading: the host would otherwise restart playback
            // from the top. ShowOrActivate owns the single-window bookkeeping.
            if (Views.Deeper.EnhancementPlayerWindow.Current != null
                && DeeperPathsEqual(App.DeeperHost?.LoadedFilePath, entry.FilePath))
            {
                try { Views.Deeper.EnhancementPlayerWindow.ShowOrActivate(this); return; }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
            OpenDeeperEnhancementInPlayer(entry.FilePath);
        }

        internal void DeeperLibraryList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    case System.Windows.Input.Key.Enter:
                    {
                        var sel = SelectedDeeperEntry();
                        if (sel != null) { OpenDeeperFile(sel.FilePath); e.Handled = true; }
                        break;
                    }
                    case System.Windows.Input.Key.Delete:
                    {
                        var sel = SelectedDeeperEntry();
                        if (sel != null) { DeleteDeeperLibraryEntry(sel); e.Handled = true; }
                        break;
                    }
                    case System.Windows.Input.Key.Up:
                        MoveDeeperSelection(-1);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Down:
                        MoveDeeperSelection(+1);
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Deeper list key error: {Error}", ex.Message); }
        }

        // Ctrl+F anywhere on the tab focuses the search box.
        internal void DeeperTab_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.F
                || !System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) return;
            var box = DeeperTab.TxtDeeperSearch;
            if (box == null) return;
            box.Focus();
            box.SelectAll();
            e.Handled = true;
        }

        private static bool DeeperPathsEqual(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) { Diag.Swallowed(ex); return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private int SelectedDeeperIndex()
        {
            for (int i = 0; i < DeeperFilteredEntries.Count; i++)
                if (DeeperFilteredEntries[i].IsSelected) return i;
            return -1;
        }

        private EnhancementLibraryEntry? SelectedDeeperEntry()
        {
            var i = SelectedDeeperIndex();
            return i < 0 ? null : DeeperFilteredEntries[i].Entry;
        }

        private void MoveDeeperSelection(int delta)
        {
            if (DeeperFilteredEntries.Count == 0) return;
            var cur = SelectedDeeperIndex();
            var next = cur < 0 ? (delta > 0 ? 0 : DeeperFilteredEntries.Count - 1)
                               : Math.Clamp(cur + delta, 0, DeeperFilteredEntries.Count - 1);
            SelectDeeperRow(DeeperFilteredEntries[next].Entry.FilePath, scrollIntoView: true);
        }

        private void SelectDeeperRow(string? filePath, bool scrollIntoView)
        {
            _deeperSelectedPath = filePath;
            int index = -1;
            for (int i = 0; i < DeeperFilteredEntries.Count; i++)
            {
                var vm = DeeperFilteredEntries[i];
                vm.IsSelected = DeeperPathsEqual(vm.Entry.FilePath, filePath);
                if (vm.IsSelected) index = i;
            }
            if (!scrollIntoView || index < 0) return;
            try
            {
                var scroller = FindDescendantScrollViewer(DeeperTab.DeeperLibraryList);
                if (scroller == null) return;
                // CanContentScroll + VirtualizingStackPanel: offsets are in items.
                if (index < scroller.VerticalOffset) scroller.ScrollToVerticalOffset(index);
                else if (index >= scroller.VerticalOffset + scroller.ViewportHeight)
                    scroller.ScrollToVerticalOffset(index - Math.Max(1, scroller.ViewportHeight) + 1);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        internal void DeeperRowDelete_Click(object sender, RoutedEventArgs e)
        {
            var entry = EntryFromDataContext(sender);
            if (entry == null) return;
            e.Handled = true;
            DeleteDeeperLibraryEntry(entry);
        }

        internal void DeeperRowSubmit_Click(object sender, RoutedEventArgs e)
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
            foreach (var entry in lib.ScanLibrary())
            {
                // A row whose delete is in its undo grace period stays hidden even
                // though the file is still on disk.
                if (IsDeeperDeletePending(entry.FilePath)) continue;
                _deeperAllEntries.Add(entry);
            }
            ApplyDeeperFilterAndSort();
            ProbeDeeperDurations();
        }

        // Scroll the list to the row for <paramref name="filePath"/> and flash it.
        // No-op when the row is filtered out.
        private void RevealDeeperLibraryRow(string filePath)
        {
            try
            {
                string key;
                try { key = System.IO.Path.GetFullPath(filePath); } catch (Exception ex) { Diag.Swallowed(ex); key = filePath; }
                int index = -1;
                for (int i = 0; i < DeeperFilteredEntries.Count; i++)
                {
                    var p = DeeperFilteredEntries[i].Entry.FilePath;
                    string full;
                    try { full = System.IO.Path.GetFullPath(p); } catch (Exception ex) { Diag.Swallowed(ex); full = p; }
                    if (string.Equals(full, key, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
                }
                if (index < 0) return;
                var vm = DeeperFilteredEntries[index];

                // CanContentScroll + VirtualizingStackPanel: the vertical offset is in items.
                var scroller = FindDescendantScrollViewer(DeeperTab.DeeperLibraryList);
                scroller?.ScrollToVerticalOffset(Math.Max(0, index - 1));

                // Remember the path so a watcher-driven rebuild re-applies the flash to
                // the fresh VM; the Tick clears both the field and whichever VM is live.
                _deeperHighlightedPath = key;
                vm.IsHighlighted = true;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    if (DeeperPathsEqual(_deeperHighlightedPath, key)) _deeperHighlightedPath = null;
                    vm.IsHighlighted = false;
                    try
                    {
                        foreach (var row in DeeperFilteredEntries)
                            if (row.IsHighlighted && DeeperPathsEqual(row.Entry.FilePath, key)) row.IsHighlighted = false;
                    }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                };
                t.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("RevealDeeperLibraryRow error: {Error}", ex.Message); }
        }

        private static System.Windows.Controls.ScrollViewer? FindDescendantScrollViewer(DependencyObject? root)
        {
            if (root == null) return null;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.ScrollViewer sv) return sv;
                var deeper = FindDescendantScrollViewer(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        // -------------------------------------------------------------------
        // Duration back-fill. The list renders first; then rows whose file
        // carries no media_duration and whose media is a local file get
        // probed one at a time on a worker thread. Each answer lands in
        // MediaDurationCache (so the next scan reads it for free) and on the
        // matching entries; the list is rebuilt in small batches so a long
        // library fills in as it goes. Failures are remembered for the
        // session so a broken file is never opened twice. Remote media is
        // never touched.
        // -------------------------------------------------------------------

        private bool _deeperDurationProbeRunning;
        private readonly HashSet<string> _deeperDurationProbeTried = new(StringComparer.OrdinalIgnoreCase);
        private const int DeeperDurationRefreshBatch = 5;

        private List<string> PendingDeeperDurationProbes() => _deeperAllEntries
            .Where(e => e.DurationSeconds <= 0
                        && !string.IsNullOrEmpty(e.MediaSource)
                        && !_deeperDurationProbeTried.Contains(e.MediaSource)
                        && MediaDurationCache.IsProbeable(e.MediaSource))
            .Select(e => e.MediaSource)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        private async void ProbeDeeperDurations()
        {
            if (_deeperDurationProbeRunning) return;
            var pending = PendingDeeperDurationProbes();
            if (pending.Count == 0) return;
            _deeperDurationProbeRunning = true;
            try
            {
                while (pending.Count > 0)
                {
                    int found = 0;
                    foreach (var source in pending)
                    {
                        _deeperDurationProbeTried.Add(source);
                        var seconds = await MediaDurationCache.ProbeAsync(source);
                        if (seconds is not > 0) continue;
                        MediaDurationCache.Remember(source, seconds.Value);
                        foreach (var entry in _deeperAllEntries)
                        {
                            if (entry.DurationSeconds <= 0
                                && string.Equals(entry.MediaSource, source, StringComparison.OrdinalIgnoreCase))
                                entry.DurationSeconds = seconds.Value;
                        }
                        found++;
                        if (found % DeeperDurationRefreshBatch == 0) ApplyDeeperFilterAndSort();
                    }
                    if (found > 0 && found % DeeperDurationRefreshBatch != 0) ApplyDeeperFilterAndSort();
                    // A reload while probing can bring in fresh rows; sweep again.
                    pending = PendingDeeperDurationProbes();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ProbeDeeperDurations error: {Error}", ex.Message); }
            finally { _deeperDurationProbeRunning = false; }
        }
    }
}
