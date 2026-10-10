using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using DeeperFilter = ConditioningControlPanel.Services.Deeper.EnhancementLibraryFilter;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/DeeperTabView.xaml.cs.
    ///
    /// The WPF code-behind is a pure relay: every handler is
    /// <c>if (Window.GetWindow(this) is MainWindow mw) mw.&lt;same name&gt;(...)</c>, plus the
    /// mod-aware feature art and the FX lifecycle hook. Library rows, the welcome card, player,
    /// folder and catalogue relay to MainShellWindow.DeeperHub.cs; import, tutorial and the webcam
    /// card are still stubs (see docs/avalonia-parity.md). The FEATURE ART is real: Helpers.ModArt.TryLoad plus CoreMods.ModChanged is the same
    /// answer WPF's ModResourceResolver gives, and Assets/features/deeper.png is linked here.
    /// </summary>
    public partial class DeeperTabView : UserControl
    {
        public DeeperTabView()
        {
            // InitializeComponent, NOT AvaloniaXamlLoader.Load: the loader does not assign the
            // generated x:Name fields, so DeeperHeroArt/DeeperSideArt would be permanently null
            // and ApplyFeatureArt would be a silent no-op (CLAUDE.md porting trap 7).
            InitializeComponent();
            DataContext = new DeeperTabViewModel();
            ApplyFeatureArt();
            AddHandler(KeyDownEvent, DeeperTab_KeyDown, RoutingStrategies.Tunnel);   // WPF PreviewKeyDown
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged += OnModChanged;
            Platform.WebcamTracker.Instance.StateChanged += RefreshTrackerButton;   // X15
            RefreshTrackerButton();
            ApplyFeatureArt();
            if (Owner is { } shell) shell.InitializeDeeperHub();
            else ViewModel?.ReloadLibrary();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
            Platform.WebcamTracker.Instance.StateChanged -= RefreshTrackerButton;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>WPF MainWindow.BlinkTrainer.cs:375 RefreshBlinkTrainerTrackerButton: the Deeper hub's
        /// Start / Stop button follows the tracker, whoever started or stopped it (WPF hardcodes the label).</summary>
        internal void RefreshTrackerButton()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshTrackerButton); return; }
            BtnDeeperWebcamStartStopTracker.Content = TrackerLabel(TrackerRunning());
        }

        internal static Func<bool> TrackerRunning = () => Platform.WebcamTracker.Instance.IsRunning;
        internal static string TrackerLabel(bool running) => running ? "Stop tracker" : "Start tracker";

        /// <summary>ModChanged can be raised off the UI thread, so the repaint is marshalled.</summary>
        private void OnModChanged(object? sender, ModPackage mod) =>
            Dispatcher.UIThread.Post(ApplyFeatureArt);

        /// <summary>
        /// The two deeper.png plates, mod override first - the split of WPF's ModResourceResolver
        /// repaint across the seam. A null answer leaves the authored wash, glyph and scrim, which
        /// is what the resolver's own null path does.
        /// </summary>
        private void ApplyFeatureArt()
        {
            var art = Helpers.ModArt.TryLoad("features/deeper.png");
            if (art == null) return;

            DeeperHeroArt.Background = new ImageBrush(art)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Right,
            };
            DeeperSideArt.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        }

        // WPF's IsVisibleChanged -> OnDeeperTabVisibilityChanged is the shell's SwitchTabFx here.

        /// <summary>WPF's <c>Window.GetWindow(this) as MainWindow</c>, written the way every other
        /// ported tab writes it (PlayTabView.axaml.cs:56).</summary>
        private MainShellWindow? Owner => TopLevel.GetTopLevel(this) as MainShellWindow;

        private DeeperTabViewModel? ViewModel => DataContext as DeeperTabViewModel;

        private static EnhancementLibraryEntry? EntryOf(object? sender) =>
            ((sender as Control)?.DataContext as DeeperLibraryRowVm)?.Entry;

        private void DeeperRow_MouseEnter(object? sender, PointerEventArgs e) => Owner?.OnDeeperRowHover(sender as Control, true);
        private void DeeperRow_MouseLeave(object? sender, PointerEventArgs e) => Owner?.OnDeeperRowHover(sender as Control, false);

        /// <summary>WPF DeeperRow_Click (MainWindow.DeeperHub.cs:537): one left click selects the row
        /// AND opens it in the editor; the right button belongs to the context menu.</summary>
        private void DeeperRow_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left || EntryOf(sender) is not { } entry) return;
            ViewModel?.Select(entry.FilePath);
            this.FindControl<ItemsControl>("DeeperLibraryList")?.Focus();
            Owner?.OpenDeeperFile(entry.FilePath);
        }

        private void DeeperRowMenuOpen_Click(object? sender, RoutedEventArgs e)
        {
            if (EntryOf(sender) is { } entry) Owner?.OpenDeeperFile(entry.FilePath);
        }

        private void DeeperRowMenuReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (EntryOf(sender) is { } entry) MainShellWindow.RevealDeeperFile(entry.FilePath);
        }

        private async void DeeperRowMenuCopyPath_Click(object? sender, RoutedEventArgs e)
        {
            if (EntryOf(sender) is not { } entry || TopLevel.GetTopLevel(this)?.Clipboard is not { } clip) return;
            try { await clip.SetTextAsync(DeeperTabViewModel.FullPath(entry.FilePath)); }
            catch (Exception ex) { Serilog.Log.Debug("Deeper: copy path failed: {Error}", ex.Message); }
        }

        /// <summary>WPF DeeperLibraryList_PreviewKeyDown: Enter opens, Delete deletes, Up/Down move.</summary>
        private void DeeperLibraryList_KeyDown(object? sender, KeyEventArgs e)
        {
            if (ViewModel is not { } model) return;
            switch (e.Key)
            {
                case Key.Enter when model.SelectedEntry is { } open:
                    Owner?.OpenDeeperFile(open.FilePath); e.Handled = true; break;
                case Key.Delete when model.SelectedEntry is { } gone:
                    Owner?.DeleteDeeperLibraryEntry(gone); e.Handled = true; break;
                case Key.Up: ScrollToRow(model.MoveSelection(-1)); e.Handled = true; break;
                case Key.Down: ScrollToRow(model.MoveSelection(+1)); e.Handled = true; break;
            }
        }

        private void ScrollToRow(int index)
        {
            if (index >= 0) this.FindControl<ItemsControl>("DeeperLibraryList")?.ContainerFromIndex(index)?.BringIntoView();
        }

        /// <summary>WPF DeeperTab_PreviewKeyDown: Ctrl+F anywhere on the tab focuses the search box.</summary>
        private void DeeperTab_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.F || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || TxtDeeperSearch is not { } box) return;
            box.Focus();
            box.SelectAll();
            e.Handled = true;
        }

        private void BtnDeeperCatalogue_Click(object? sender, RoutedEventArgs e)
            => _ = Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), MainShellWindow.DeeperCatalogueUrl);
        /// <summary>The import picker (tests answer it without a dialog). Null or empty = cancelled.</summary>
        internal Func<System.Threading.Tasks.Task<IReadOnlyList<string>?>>? PickImportFiles;

        /// <summary>WPF BtnDeeperImport_Click (MainWindow.DeeperTab.cs:743): pick one or more
        /// .ccpenh.json files, import them (Views/Deeper/DeeperImport), reload the rows.
        /// ponytail: WPF also scrolled to and flashed the imported row (RevealDeeperLibraryRow, a
        /// shell method that is not on this head yet); the row appears, unflashed.</summary>
        private async void BtnDeeperImport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                CoreBark.NotifyUiAction("deeper_import");
                var paths = await (PickImportFiles ?? PickImportFilesAsync)();
                if (paths == null || paths.Count == 0) return;
                Views.Deeper.DeeperImport.ImportFiles(paths);
                // Force-refresh now: a watcher's debounce can lag a fast manual import (WPF).
                Owner?.InitializeDeeperHub();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Deeper import failed"); }
        }

        private async System.Threading.Tasks.Task<IReadOnlyList<string>?> PickImportFilesAsync()
        {
            if (TopLevel.GetTopLevel(this) is not { } top) return null;
            global::Avalonia.Platform.Storage.IStorageFolder? start = null;
            try
            {
                var last = CoreSettings.Current?.DeeperLastDirectory;
                if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
                    start = await top.StorageProvider.TryGetFolderFromPathAsync(last);
            }
            catch { /* the picker opens wherever the platform likes */ }
            var files = await top.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = Loc.Get("deeper_import_dialog_title"),
                AllowMultiple = true,
                SuggestedStartLocation = start,
                FileTypeFilter = new List<global::Avalonia.Platform.Storage.FilePickerFileType>
                {
                    new("Deeper enhancements (*.ccpenh.json)") { Patterns = new[] { "*.ccpenh.json" } },
                    new("JSON files (*.json)") { Patterns = new[] { "*.json" } },
                    new("All files (*.*)") { Patterns = new[] { "*" } },
                },
            });
            return files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        }
        private void BtnDeeperNewEnhancement_Click(object? sender, RoutedEventArgs e)
            => Owner?.BtnDeeperNewEnhancement_Click(sender, e);
        private void BtnDeeperOpenLibraryFolder_Click(object? sender, RoutedEventArgs e) => Owner?.OpenDeeperLibraryFolder();
        private void BtnDeeperOpenPlayer_Click(object? sender, RoutedEventArgs e) => Owner?.BtnDeeperOpenPlayer_Click();
        // WPF DeeperTabView.xaml.cs:89: StartTutorial(TutorialType.Deeper).
        private void BtnDeeperTutorial_Click(object? sender, RoutedEventArgs e) => CoreTutorial.Start("Deeper");

        /// <summary>Settings > Devices owns the camera on this head: the Deeper webcam block's buttons
        /// run the same handlers its buttons do (consent, calibrate, quick recal, tracker start/stop).</summary>
        private Controls.AppSettings.DevicesSettingsSection? Devices =>
            Owner?.AppSettingsPage?.FindControl<Controls.AppSettings.DevicesSettingsSection>("SectionDevices");
        private void BtnDeeperWebcamCalibrate_Click(object? sender, RoutedEventArgs e) => Devices?.BtnWebcamDebugCalibrate_Click(sender, e);
        private void BtnDeeperWebcamManageConsent_Click(object? sender, RoutedEventArgs e) => Devices?.BtnWebcamReviewPrivacy_Click(sender, e);
        private void BtnDeeperWebcamQuickRecal_Click(object? sender, RoutedEventArgs e) => Devices?.BtnWebcamDebugQuickRecal_Click(sender, e);
        private void BtnDeeperWebcamRevokeConsent_Click(object? sender, RoutedEventArgs e) => Devices?.BtnWebcamRevokeConsent_Click(sender, e);
        private void BtnDeeperWebcamStartStopTracker_Click(object? sender, RoutedEventArgs e) => Devices?.BtnWebcamDebugStart_Click(sender, e);
        private void BtnDeeperWelcomeDismiss_Click(object? sender, RoutedEventArgs e) => Owner?.DismissDeeperWelcomeCard();
        private void BtnDeeperWelcomeDemo_Click(object? sender, RoutedEventArgs e) => Owner?.BtnDeeperWelcomeDemo_Click();
        private void BtnDeeperWelcomeTour_Click(object? sender, RoutedEventArgs e) => Owner?.BtnDeeperWelcomeTour_Click();
        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) => Owner?.OpenDeviceSettings();

        private void DeeperPillAll_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.SetMediaType(DeeperFilter.MediaTypeFilter.All);
            RefreshPills();
        }

        private void DeeperPillAudio_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.SetMediaType(DeeperFilter.MediaTypeFilter.Audio);
            RefreshPills();
        }

        private void DeeperPillVideo_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.SetMediaType(DeeperFilter.MediaTypeFilter.Video);
            RefreshPills();
        }

        private void DeeperPillHaptics_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.ToggleHaptics();
            RefreshPills();
        }

        private void DeeperPillWebcam_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.ToggleWebcam();
            RefreshPills();
        }

        private void RefreshPills()
        {
            if (ViewModel is not { } model) return;
            BtnDeeperPillAll.IsChecked = model.MediaType == DeeperFilter.MediaTypeFilter.All;
            BtnDeeperPillVideo.IsChecked = model.MediaType == DeeperFilter.MediaTypeFilter.Video;
            BtnDeeperPillAudio.IsChecked = model.MediaType == DeeperFilter.MediaTypeFilter.Audio;
            BtnDeeperPillHaptics.IsChecked = model.Haptics;
            BtnDeeperPillWebcam.IsChecked = model.Webcam;
        }

        private void DeeperRowDelete_Click(object? sender, RoutedEventArgs e)
        {
            if (EntryOf(sender) is not { } entry) return;
            e.Handled = true;
            Owner?.DeleteDeeperLibraryEntry(entry);
        }

        private void DeeperRowPlay_Click(object? sender, RoutedEventArgs e)
        {
            if (EntryOf(sender) is not { } entry) return;
            e.Handled = true;
            Owner?.PlayDeeperLibraryEntry(entry.FilePath);
        }
        // WPF MainWindow.DeeperHub.cs:704.
        private void DeeperRowSubmit_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not DeeperLibraryRowVm row || Owner is not { } shell) return;
            e.Handled = true;
            _ = shell.SubmitDeeperLibraryEntryAsync(row.Entry);
        }
        private void DeeperSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            var text = sender is TextBox box ? box.Text : TxtDeeperSearch?.Text;
            ViewModel?.SetSearch(text);
        }

        private void DeeperSort_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel?.SetSort(tag);
                RefreshSortDirection();
            }
        }

        private void DeeperSortDir_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.ToggleSortDirection();
            RefreshSortDirection();
        }

        private void RefreshSortDirection()
        {
            if (ViewModel is { } model && BtnDeeperSortDir is { } button)
                button.Content = model.SortDirectionGlyph;
        }
    }

    /// <summary>Read-only local library state for the hub. The shell asks this model to reload;
    /// all file parsing and filtering is shared in <see cref="DeeperLocalLibrary"/>.</summary>
    public sealed class DeeperTabViewModel : INotifyPropertyChanged
    {
        private readonly List<EnhancementLibraryEntry> _allEntries = new();
        private string _search = "";
        private DeeperFilter.MediaTypeFilter _mediaType;
        private bool _haptics;
        private bool _webcam;
        private DeeperFilter.SortMode _sort = DeeperFilter.SortMode.Recent;
        private bool _descending = DeeperFilter.DefaultDescending(DeeperFilter.SortMode.Recent);
        private bool _libraryError;

        // The shell/visual-tree attach owns the first scan. Keeping construction cheap prevents
        // the first show from enumerating the folder twice (the standalone renderer still scans
        // from OnAttachedToVisualTree when it has no shell owner).
        public DeeperTabViewModel() { }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<DeeperLibraryRowVm> FilteredEntries { get; } = new();
        public DeeperFilter.MediaTypeFilter MediaType => _mediaType;
        public bool Haptics => _haptics;
        public bool Webcam => _webcam;
        public bool ShowLibraryError => _libraryError;
        public string LibraryErrorText => Loc.Get("deeper_import_library_not_ready");
        public bool ShowLibraryEmpty => !_libraryError && FilteredEntries.Count == 0;
        public string LibraryEmptyText => Loc.Get(_allEntries.Count == 0 ? "deeper_library_empty" : "deeper_hub_empty_filtered");
        public string LibraryCountText => FilteredEntries.Count == _allEntries.Count
            ? Loc.GetF("deeper_library_count_fmt", _allEntries.Count)
            : Loc.GetF("deeper_library_count_shown_fmt", _allEntries.Count, FilteredEntries.Count);
        public int PillAllCount => CurrentPillCounts.All;
        public int PillVideoCount => CurrentPillCounts.Video;
        public int PillAudioCount => CurrentPillCounts.Audio;
        public int PillHapticsCount => CurrentPillCounts.Haptics;
        public int PillWebcamCount => CurrentPillCounts.Webcam;
        public bool ShowWelcomeCard => true;
        public string SortDirectionGlyph => _descending ? "▼" : "▲";

        private DeeperFilter.Criteria CurrentCriteria()
            => new((_search ?? "").Trim(), _mediaType, _haptics, _webcam);

        private DeeperFilter.PillCounts CurrentPillCounts
            => DeeperFilter.CountPills(_allEntries, CurrentCriteria());

        public void ReloadLibrary()
        {
            var result = DeeperLocalLibrary.Scan();
            _allEntries.Clear();
            // A row in its undo grace period stays hidden although the file is still on disk
            // (WPF MainWindow.DeeperHub.cs:733).
            _allEntries.AddRange(result.Entries.Where(e => !PendingDeletes.ContainsKey(FullPath(e.FilePath))));
            _libraryError = result.HasError;
            ApplyFilter();
        }

        /// <summary>Full path -> start timestamp of a delete waiting out its undo grace (the shell
        /// owns the clock and the commit).</summary>
        internal Dictionary<string, long> PendingDeletes { get; } = new(StringComparer.OrdinalIgnoreCase);

        private string? _selectedPath;

        /// <summary>Rebuilds the rows without a rescan - a submission badge changed.</summary>
        public void Refresh() => ApplyFilter();

        internal EnhancementLibraryEntry? SelectedEntry => FilteredEntries.FirstOrDefault(r => r.IsSelected)?.Entry;

        /// <summary>WPF SelectDeeperRow (MainWindow.DeeperHub.cs:673). Returns the row index or -1.</summary>
        internal int Select(string? path)
        {
            _selectedPath = path;
            var index = -1;
            for (var i = 0; i < FilteredEntries.Count; i++)
            {
                FilteredEntries[i].IsSelected = PathsEqual(FilteredEntries[i].Entry.FilePath, path);
                if (FilteredEntries[i].IsSelected) index = i;
            }
            return index;
        }

        /// <summary>WPF MoveDeeperSelection: Up/Down from nothing lands on the first/last row.</summary>
        internal int MoveSelection(int delta)
        {
            if (FilteredEntries.Count == 0) return -1;
            var cur = -1;
            for (var i = 0; i < FilteredEntries.Count; i++) if (FilteredEntries[i].IsSelected) cur = i;
            var next = cur < 0 ? (delta > 0 ? 0 : FilteredEntries.Count - 1)
                               : Math.Clamp(cur + delta, 0, FilteredEntries.Count - 1);
            return Select(FilteredEntries[next].Entry.FilePath);
        }

        internal static string FullPath(string path)
        {
            try { return Path.GetFullPath(path); } catch { return path; }
        }

        internal static bool PathsEqual(string? a, string? b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
            && string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);

        public void SetSearch(string? search)
        {
            _search = search ?? "";
            ApplyFilter();
        }

        public void SetMediaType(DeeperFilter.MediaTypeFilter type)
        {
            _mediaType = type;
            ApplyFilter();
        }

        public void ToggleHaptics()
        {
            _haptics = !_haptics;
            ApplyFilter();
        }

        public void ToggleWebcam()
        {
            _webcam = !_webcam;
            ApplyFilter();
        }

        public void SetSort(string tag)
        {
            _sort = tag switch
            {
                "name" => DeeperFilter.SortMode.Name,
                "creator" => DeeperFilter.SortMode.Creator,
                "duration" => DeeperFilter.SortMode.Duration,
                _ => DeeperFilter.SortMode.Recent,
            };
            _descending = DeeperFilter.DefaultDescending(_sort);
            ApplyFilter();
        }

        public void ToggleSortDirection()
        {
            _descending = !_descending;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var criteria = CurrentCriteria();
            var rows = DeeperFilter.Sort(
                _allEntries.Where(entry => DeeperFilter.Matches(entry, criteria)), _sort, _descending);
            FilteredEntries.Clear();
            foreach (var entry in rows)
            {
                var row = BuildRow(entry);
                row.IsSelected = PathsEqual(entry.FilePath, _selectedPath);
                FilteredEntries.Add(row);
            }
            Notify(nameof(LibraryCountText), nameof(ShowLibraryEmpty), nameof(LibraryEmptyText),
                nameof(PillAllCount), nameof(PillVideoCount), nameof(PillAudioCount),
                nameof(PillHapticsCount), nameof(PillWebcamCount), nameof(ShowLibraryError),
                nameof(SortDirectionGlyph));
        }

        private static DeeperLibraryRowVm BuildRow(EnhancementLibraryEntry entry)
        {
            var isAudio = string.Equals(entry.MediaType, MediaTypes.Audio, StringComparison.OrdinalIgnoreCase);
            var tags = entry.AutoTags.Select(tag => new DeeperAutoTagVm
            {
                Glyph = tag.Equals(EnhancementAutoTagger.TagHaptics, StringComparison.OrdinalIgnoreCase) ? "📳" : "📷",
                Label = tag,
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0x7B, 0x5C, 0xFF)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xA6, 0xFF)),
            }).ToList();
            var source = DescribeSource(entry.MediaSource);
            // WPF MainWindow.DeeperHub.cs:225,262-266.
            var eligible = MainShellWindow.IsCatalogueEligible(entry);
            var hasAuth = !string.IsNullOrEmpty(CoreSettings.Current.AuthToken);
            var badge = ResolveSubmissionBadge(entry);
            return new DeeperLibraryRowVm
            {
                ShowSubmissionBadge = badge.Show,
                SubmissionBadgeGlyph = badge.Glyph,
                SubmissionBadgeLabel = badge.Label,
                SubmissionBadgeBg = badge.Bg,
                SubmissionBadgeFg = badge.Fg,
                SubmissionBadgeTooltip = badge.Tip,
                ShowSubmitButton = eligible,
                SubmitEnabled = eligible && hasAuth,
                SubmitTooltip = Loc.Get(hasAuth
                    ? "deeper_library_submit_tooltip"
                    : "deeper_library_submit_button_disabled_tooltip"),
                Entry = entry,
                Name = entry.Name,
                MediaTypeIcon = isAudio ? "🎵" : "🎬",
                MediaTypeBadgeBg = new SolidColorBrush(Color.FromArgb(0x33,
                    isAudio ? (byte)0xFF : (byte)0x7B,
                    isAudio ? (byte)0x69 : (byte)0x5C,
                    isAudio ? (byte)0xB4 : (byte)0xFF)),
                CreatorDisplay = entry.Creator,
                ShowCreator = !string.IsNullOrEmpty(entry.Creator),
                MediaSourceGlyph = source.Glyph,
                MediaSourceLabel = source.Label,
                MediaSourceBrush = source.Exists ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)) : Brushes.Gray,
                ShowMediaSource = !string.IsNullOrEmpty(source.Label),
                TimestampDisplay = FormatRelativeTime(entry.LastModified),
                ShowTimestamp = entry.LastModified != default,
                Tags = tags,
                ShowTags = tags.Count > 0,
            };
        }

        // WPF MainWindow.DeeperHub.cs:299-347: the toast palette, translucent pill + solid text.
        private static readonly IBrush SubPublishedBg = new ImmutableSolidColorBrush(Color.Parse("#334CAF50"));
        private static readonly IBrush SubPendingBg = new ImmutableSolidColorBrush(Color.Parse("#33FFB347"));
        private static readonly IBrush SubRejectedBg = new ImmutableSolidColorBrush(Color.Parse("#33FF6B6B"));
        private static readonly IBrush SubPublishedFg = new ImmutableSolidColorBrush(Color.Parse("#7BE08A"));
        private static readonly IBrush SubPendingFg = new ImmutableSolidColorBrush(Color.Parse("#FFC97A"));
        private static readonly IBrush SubRejectedFg = new ImmutableSolidColorBrush(Color.Parse("#FF9B9B"));

        /// <summary>The row's catalogue-submission pill from AppSettings.DeeperSubmissions, keyed by
        /// canonical path; hidden when this file was never submitted.</summary>
        internal static (bool Show, string Glyph, string Label, IBrush Bg, IBrush Fg, string Tip)
            ResolveSubmissionBadge(EnhancementLibraryEntry entry)
        {
            var subs = CoreSettings.Current.DeeperSubmissions;
            if (subs == null || !subs.TryGetValue(MainShellWindow.CanonicalCataloguePathKey(entry.FilePath), out var rec) || rec == null)
                return (false, "", "", Brushes.Transparent, Brushes.White, "");
            return (rec.Status ?? "").ToLowerInvariant() switch
            {
                "approved" or "published" => (true, "✅", Loc.Get("deeper_submission_badge_published"),
                    SubPublishedBg, SubPublishedFg, Loc.Get("deeper_submission_badge_published_tip")),
                "rejected" => (true, "⚠", Loc.Get("deeper_submission_badge_rejected"),
                    SubRejectedBg, SubRejectedFg, Loc.Get("deeper_submission_badge_rejected_tip")),
                _ => (true, "⏳", Loc.Get("deeper_submission_badge_pending"),
                    SubPendingBg, SubPendingFg, Loc.Get("deeper_submission_badge_pending_tip")),
            };
        }

        private static (string Label, string Glyph, bool Exists) DescribeSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return ("", "", false);
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return (uri.Host, "🌐", true);
            var name = Path.GetFileName(source);
            if (string.IsNullOrEmpty(name)) name = source;
            var exists = false;
            try { exists = File.Exists(source); } catch { }
            return (name, exists ? "✓" : "⚠", exists);
        }

        private static string FormatRelativeTime(DateTime when)
        {
            if (when == default) return "";
            var diff = DateTime.Now - when;
            if (diff.TotalMinutes < 1) return Loc.Get("deeper_hub_time_just_now");
            if (diff.TotalHours < 1) return Loc.GetF("deeper_hub_time_minutes_ago", (int)diff.TotalMinutes);
            if (diff.TotalDays < 1) return Loc.GetF("deeper_hub_time_hours_ago", (int)diff.TotalHours);
            if (diff.TotalDays < 7) return Loc.GetF("deeper_hub_time_days_ago", (int)diff.TotalDays);
            if (diff.TotalDays < 31) return Loc.GetF("deeper_hub_time_weeks_ago", (int)(diff.TotalDays / 7));
            if (diff.TotalDays < 365) return Loc.GetF("deeper_hub_time_months_ago", (int)(diff.TotalDays / 30));
            return Loc.GetF("deeper_hub_time_years_ago", (int)(diff.TotalDays / 365));
        }

        private void Notify(params string[] names)
        {
            foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private bool Consented => true;
        public string ConsentStatusText => Loc.Get(Consented ? "blink_trainer_consent_granted" : "blink_trainer_consent_required");
        public string ConsentButtonText => Loc.Get(Consented ? "blink_trainer_consent_manage" : "blink_trainer_consent_grant");
        public bool ShowRevokeConsent => Consented;
        public string CalibrationStatusText => Loc.Get("blink_trainer_calibration_none");
    }

    /// <summary>
    /// The Avalonia twin of MainWindow.DeeperHub.cs's DeeperLibraryRowVm. Pre-computed strings +
    /// brushes so the DataTemplate stays pure-bind. Two shape changes, both forced: WPF's
    /// <c>Visibility</c> becomes <c>bool</c> (Avalonia binds IsVisible directly), and the two
    /// two-<c>Run</c> TextBlocks become one pre-joined string, since an Avalonia
    /// <c>Run</c> takes a literal rather than a binding. The shared Core Entry is carried on the
    /// row VM so future head action handlers can consume the same parsed record.
    /// </summary>
    public sealed class DeeperLibraryRowVm : INotifyPropertyChanged
    {
        private bool _isSelected;
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Keyboard/click selection; the row template paints it (WPF DataTrigger IsSelected).</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public EnhancementLibraryEntry Entry { get; init; } = new();
        public string Name { get; init; } = "";
        public string MediaTypeIcon { get; init; } = "🎬";
        public IBrush MediaTypeBadgeBg { get; init; } = Brushes.Transparent;
        public IBrush MediaTypeBadgeFg { get; init; } = Brushes.White;

        public string CreatorDisplay { get; init; } = "";
        public bool ShowCreator { get; init; }

        public string MediaSourceLabel { get; init; } = "";
        public string MediaSourceGlyph { get; init; } = "";
        public IBrush MediaSourceBrush { get; init; } = Brushes.Gray;
        public bool ShowMediaSource { get; init; }

        public string TimestampDisplay { get; init; } = "";
        public bool ShowTimestamp { get; init; }

        public List<DeeperAutoTagVm> Tags { get; init; } = new();
        public bool ShowTags { get; init; }

        public bool ShowSubmissionBadge { get; init; }
        public string SubmissionBadgeGlyph { get; init; } = "";
        public string SubmissionBadgeLabel { get; init; } = "";
        public string SubmissionBadgeText => $"{SubmissionBadgeGlyph} {SubmissionBadgeLabel}";
        public IBrush SubmissionBadgeBg { get; init; } = Brushes.Transparent;
        public IBrush SubmissionBadgeFg { get; init; } = Brushes.White;
        public string SubmissionBadgeTooltip { get; init; } = "";

        public bool ShowSubmitButton { get; init; }
        public bool SubmitEnabled { get; init; }
        public string SubmitTooltip { get; init; } = "";
    }

    public sealed class DeeperAutoTagVm
    {
        public string Glyph { get; init; } = "";
        public string Label { get; init; } = "";
        public string Display => $"{Glyph} {Label}";
        public IBrush Background { get; init; } = Brushes.Transparent;
        public IBrush Foreground { get; init; } = Brushes.White;
    }
}
