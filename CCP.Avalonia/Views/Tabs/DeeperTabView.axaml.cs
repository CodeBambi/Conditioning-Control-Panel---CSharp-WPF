using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/DeeperTabView.xaml.cs.
    ///
    /// The WPF code-behind is a pure relay: every handler is
    /// <c>if (Window.GetWindow(this) is MainWindow mw) mw.&lt;same name&gt;(...)</c>, plus the
    /// mod-aware feature art and the FX lifecycle hook. The relay targets have not moved, so the
    /// handlers are stubs with identical names so the eventual wiring diffs cleanly - but the
    /// FEATURE ART is real again: Helpers.ModArt.TryLoad plus CoreMods.ModChanged is the same
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
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged += OnModChanged;
            ApplyFeatureArt();
            if (Owner is { } shell) shell.InitializeDeeperHub();
            else ViewModel?.ReloadLibrary();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
            base.OnDetachedFromVisualTree(e);
        }

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

        // ponytail: needs MainWindow.OnDeeperTabVisibilityChanged (the header glyph's drift clock)
        // and MainWindow.DeeperFx, wired when the FX layer moves to Core. On WPF this rode
        // IsVisibleChanged; the Avalonia equivalent would be an IsVisibleProperty observer.

        /// <summary>WPF's <c>Window.GetWindow(this) as MainWindow</c>, written the way every other
        /// ported tab writes it (PlayTabView.axaml.cs:56).</summary>
        private MainShellWindow? Owner => TopLevel.GetTopLevel(this) as MainShellWindow;

        // The index/filter slice is local and read-only. Editor, player, import, delete,
        // catalogue and webcam actions stay explicit stubs until their head services move.
        private DeeperTabViewModel? ViewModel => DataContext as DeeperTabViewModel;

        private void DeeperRow_MouseEnter(object? sender, PointerEventArgs e) { }
        private void DeeperRow_MouseLeave(object? sender, PointerEventArgs e) { }
        private void DeeperRow_Click(object? sender, PointerReleasedEventArgs e) { }

        private void BtnDeeperCatalogue_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperImport_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperNewEnhancement_Click(object? sender, RoutedEventArgs e)
            => Owner?.BtnDeeperNewEnhancement_Click(sender, e);
        private void BtnDeeperOpenLibraryFolder_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperOpenPlayer_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperTutorial_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperWebcamCalibrate_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperWebcamManageConsent_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperWebcamQuickRecal_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperWebcamRevokeConsent_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperWebcamStartStopTracker_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperWelcomeDemo_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperWelcomeDismiss_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperWelcomeTour_Click(object? sender, RoutedEventArgs e) { }
        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) { }

        private void DeeperPillAll_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.SetMediaType(DeeperLocalLibrary.MediaTypeFilter.All);
            RefreshPills();
        }

        private void DeeperPillAudio_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.SetMediaType(DeeperLocalLibrary.MediaTypeFilter.Audio);
            RefreshPills();
        }

        private void DeeperPillVideo_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel?.SetMediaType(DeeperLocalLibrary.MediaTypeFilter.Video);
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
            BtnDeeperPillAll.IsChecked = model.MediaType == DeeperLocalLibrary.MediaTypeFilter.All;
            BtnDeeperPillVideo.IsChecked = model.MediaType == DeeperLocalLibrary.MediaTypeFilter.Video;
            BtnDeeperPillAudio.IsChecked = model.MediaType == DeeperLocalLibrary.MediaTypeFilter.Audio;
            BtnDeeperPillHaptics.IsChecked = model.Haptics;
            BtnDeeperPillWebcam.IsChecked = model.Webcam;
        }

        private void DeeperRowDelete_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperRowPlay_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperRowSubmit_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            var text = sender is TextBox box ? box.Text : TxtDeeperSearch?.Text;
            ViewModel?.SetSearch(text);
        }

        private void DeeperSort_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                ViewModel?.SetSort(tag);
        }
    }

    /// <summary>Read-only local library state for the hub. The shell asks this model to reload;
    /// all file parsing and filtering is shared in <see cref="DeeperLocalLibrary"/>.</summary>
    public sealed class DeeperTabViewModel : INotifyPropertyChanged
    {
        private readonly List<DeeperLocalLibrary.Entry> _allEntries = new();
        private string _search = "";
        private DeeperLocalLibrary.MediaTypeFilter _mediaType;
        private bool _haptics;
        private bool _webcam;
        private DeeperLocalLibrary.SortMode _sort = DeeperLocalLibrary.SortMode.Recent;
        private bool _descending = true;
        private bool _libraryError;

        public DeeperTabViewModel() => ReloadLibrary();

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<DeeperLibraryRowVm> FilteredEntries { get; } = new();
        public DeeperLocalLibrary.MediaTypeFilter MediaType => _mediaType;
        public bool Haptics => _haptics;
        public bool Webcam => _webcam;
        public bool ShowLibraryError => _libraryError;
        public string LibraryErrorText => Loc.Get("deeper_import_library_not_ready");
        public bool ShowLibraryEmpty => !_libraryError && FilteredEntries.Count == 0;
        public string LibraryEmptyText => Loc.Get(_allEntries.Count == 0 ? "deeper_library_empty" : "deeper_hub_empty_filtered");
        public string LibraryCountText => FilteredEntries.Count == _allEntries.Count
            ? Loc.GetF("deeper_library_count_fmt", _allEntries.Count)
            : Loc.GetF("deeper_library_count_shown_fmt", _allEntries.Count, FilteredEntries.Count);
        public int PillAllCount => Count(DeeperLocalLibrary.MediaTypeFilter.All);
        public int PillVideoCount => Count(DeeperLocalLibrary.MediaTypeFilter.Video);
        public int PillAudioCount => Count(DeeperLocalLibrary.MediaTypeFilter.Audio);
        public int PillHapticsCount => Count(new DeeperLocalLibrary.FilterCriteria(_search, _mediaType, true, _webcam));
        public int PillWebcamCount => Count(new DeeperLocalLibrary.FilterCriteria(_search, _mediaType, _haptics, true));
        public bool ShowWelcomeCard => true;

        private int Count(DeeperLocalLibrary.MediaTypeFilter type)
            => Count(new DeeperLocalLibrary.FilterCriteria(_search, type, _haptics, _webcam));

        private int Count(DeeperLocalLibrary.FilterCriteria criteria)
            => _allEntries.Count(entry => DeeperLocalLibrary.Matches(entry, criteria));

        public void ReloadLibrary()
        {
            var result = DeeperLocalLibrary.Scan();
            _allEntries.Clear();
            _allEntries.AddRange(result.Entries);
            _libraryError = result.HasError;
            ApplyFilter();
        }

        public void SetSearch(string? search)
        {
            _search = search ?? "";
            ApplyFilter();
        }

        public void SetMediaType(DeeperLocalLibrary.MediaTypeFilter type)
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
                "name" => DeeperLocalLibrary.SortMode.Name,
                "creator" => DeeperLocalLibrary.SortMode.Creator,
                _ => DeeperLocalLibrary.SortMode.Recent,
            };
            _descending = _sort == DeeperLocalLibrary.SortMode.Recent;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var criteria = new DeeperLocalLibrary.FilterCriteria(_search, _mediaType, _haptics, _webcam, _sort, _descending);
            var rows = DeeperLocalLibrary.Filter(_allEntries, criteria);
            FilteredEntries.Clear();
            foreach (var entry in rows) FilteredEntries.Add(BuildRow(entry));
            Notify(nameof(LibraryCountText), nameof(ShowLibraryEmpty), nameof(LibraryEmptyText),
                nameof(PillAllCount), nameof(PillVideoCount), nameof(PillAudioCount),
                nameof(PillHapticsCount), nameof(PillWebcamCount), nameof(ShowLibraryError));
        }

        private static DeeperLibraryRowVm BuildRow(DeeperLocalLibrary.Entry entry)
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
            return new DeeperLibraryRowVm
            {
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
    /// <c>Run</c> takes a literal rather than a binding. The real Entry is not carried: it is a
    /// WPF-head model, and no handler on this head reads it yet.
    /// </summary>
    public sealed class DeeperLibraryRowVm
    {
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
