using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/DeeperTabView.xaml.cs.
    ///
    /// The WPF code-behind is a pure relay: every handler is
    /// <c>if (Window.GetWindow(this) is MainWindow mw) mw.&lt;same name&gt;(...)</c>, plus the
    /// mod-aware feature art and the FX lifecycle hook. None of that can move yet, so the
    /// handlers are stubs with identical names so the eventual wiring diffs cleanly.
    /// </summary>
    public partial class DeeperTabView : UserControl
    {
        public DeeperTabView()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new DeeperTabViewModel();
        }

        // ponytail: needs MainWindow.OnDeeperTabVisibilityChanged (the header glyph's drift clock)
        // and MainWindow.DeeperFx, wired when the FX layer moves to Core. On WPF this rode
        // IsVisibleChanged; the Avalonia equivalent would be an IsVisibleProperty observer.

        // ponytail: needs ModService + ModResourceResolver for the mod-aware deeper.png plates,
        // wired when they move to Core. Both plates are art-less on this head anyway (see the
        // .axaml header), so there is nothing to repaint yet.

        // ponytail: every handler below routes to MainWindow on WPF
        // (Window.GetWindow(this) is MainWindow mw -> mw.<same name>). Needs the
        // MainWindow.DeeperHub / MainWindow.BlinkTrainer partials, wired when EnhancementLibrary,
        // WebcamService and the tutorial overlay move to Core.
        private void DeeperRow_MouseEnter(object? sender, PointerEventArgs e) { }
        private void DeeperRow_MouseLeave(object? sender, PointerEventArgs e) { }
        private void DeeperRow_Click(object? sender, PointerReleasedEventArgs e) { }

        private void BtnDeeperCatalogue_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperImport_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeeperNewEnhancement_Click(object? sender, RoutedEventArgs e) { }
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
        // Phase 2: blink-recal, the camera/monitor pickers and restrict-gaze moved to
        // Settings -> Devices (one editor per setting). Only the chip's link back is left.
        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperPillAll_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperPillAudio_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperPillHaptics_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperPillVideo_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperPillWebcam_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperRowDelete_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperRowPlay_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperRowSubmit_Click(object? sender, RoutedEventArgs e) { }
        private void DeeperSearch_TextChanged(object? sender, TextChangedEventArgs e) { }
        private void DeeperSort_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
    }

    /// <summary>
    /// Supplies the rows and the FORMATTED / state-chosen strings the view binds to. Static
    /// strings come straight from {loc:Str key} in the XAML; only the ones the WPF head writes
    /// from code need to live here, with the same keys the WPF code-behind uses:
    ///   MainWindow.DeeperHub.cs:524   deeper_library_count_fmt
    ///   MainWindow.DeeperHub.cs:354   deeper_library_empty / deeper_hub_empty_filtered
    ///   MainWindow.BlinkTrainer.cs:1497 blink_trainer_consent_granted / _required
    ///   MainWindow.BlinkTrainer.cs:1502 blink_trainer_consent_manage / _grant
    ///   MainWindow.BlinkTrainer.cs:1515 blink_trainer_calibration_none / _calibrated_format
    /// The data is placeholder: EnhancementLibrary and WebcamService are still in the WPF head.
    /// </summary>
    public sealed class DeeperTabViewModel
    {
        // ---- library ------------------------------------------------------------------
        // Three sample rows, deliberately different from each other so the row template's
        // optional branches (tag chips, submission badge, submit button, media source) all
        // actually draw in --render-all instead of being proved by an empty list.
        public ObservableCollection<DeeperLibraryRowVm> FilteredEntries { get; } = new()
        {
            new DeeperLibraryRowVm
            {
                Name = "Sink & Drift - long form",
                MediaTypeIcon = "🎬",
                MediaTypeBadgeBg = new SolidColorBrush(Color.FromArgb(0x33, 0x7B, 0x5C, 0xFF)),
                CreatorDisplay = "bambi",
                ShowCreator = true,
                MediaSourceGlyph = "●",
                MediaSourceLabel = "local file",
                MediaSourceBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
                ShowMediaSource = true,
                TimestampDisplay = "2 days ago",
                ShowTimestamp = true,
                ShowTags = true,
                Tags =
                {
                    new DeeperAutoTagVm
                    {
                        Glyph = "📳", Label = "haptics",
                        Background = new SolidColorBrush(Color.FromArgb(0x33, 0x7B, 0x5C, 0xFF)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xA6, 0xFF)),
                    },
                    new DeeperAutoTagVm
                    {
                        Glyph = "📷", Label = "webcam",
                        Background = new SolidColorBrush(Color.FromArgb(0x33, 0x7B, 0x5C, 0xFF)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xA6, 0xFF)),
                    },
                },
                ShowSubmitButton = true,
                SubmitEnabled = true,
                SubmitTooltip = "Submit this enhancement to the catalogue",
            },
            new DeeperLibraryRowVm
            {
                Name = "Whisper loop (audio only)",
                MediaTypeIcon = "🎵",
                MediaTypeBadgeBg = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4)),
                CreatorDisplay = "cc labs",
                ShowCreator = true,
                MediaSourceGlyph = "●",
                MediaSourceLabel = "catalogue",
                MediaSourceBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0x5C, 0xFF)),
                ShowMediaSource = true,
                TimestampDisplay = "last week",
                ShowTimestamp = true,
                ShowSubmissionBadge = true,
                SubmissionBadgeGlyph = "✓",
                SubmissionBadgeLabel = "published",
                SubmissionBadgeBg = new SolidColorBrush(Color.FromArgb(0x33, 0x4A, 0xDE, 0x80)),
                SubmissionBadgeFg = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
                SubmissionBadgeTooltip = "Accepted into the catalogue",
            },
            new DeeperLibraryRowVm
            {
                Name = "Untitled import",
                MediaTypeIcon = "🎬",
                MediaTypeBadgeBg = new SolidColorBrush(Color.FromArgb(0x33, 0x7B, 0x5C, 0xFF)),
                TimestampDisplay = "just now",
                ShowTimestamp = true,
            },
        };

        public string LibraryCountText => Loc.GetF("deeper_library_count_fmt", FilteredEntries.Count);

        /// <summary>WPF picks between two keys in UpdateDeeperEmptyState; the hint only shows when
        /// the list is empty, which the placeholder list is not.</summary>
        public bool ShowLibraryEmpty => FilteredEntries.Count == 0;
        public string LibraryEmptyText => Loc.Get(AllEntriesCount == 0 ? "deeper_library_empty" : "deeper_hub_empty_filtered");
        private int AllEntriesCount => FilteredEntries.Count;

        // Pill counts. Plain numbers on WPF too (UpdateDeeperFilterPillCounts writes ints).
        public int PillAllCount => FilteredEntries.Count;
        public int PillVideoCount => 2;
        public int PillAudioCount => 1;
        public int PillHapticsCount => 1;
        public int PillWebcamCount => 1;

        /// <summary>WPF authors the card Collapsed and MainWindow reveals it on first run. Shown
        /// here so --render-all proves the card draws rather than hiding a raw key.</summary>
        public bool ShowWelcomeCard => true;

        // ---- webcam column ------------------------------------------------------------
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
