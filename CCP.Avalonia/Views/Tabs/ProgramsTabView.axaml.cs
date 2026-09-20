using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// Read-only Programs catalogue. It reads the Core catalogue directly and deliberately does not
    /// construct ProgramService: this slice has no enrollment, timers, ledger or session execution.
    /// </summary>
    public partial class ProgramsTabView : UserControl
    {
        private IReadOnlyList<ProgramDefinition> _library = BuiltInPrograms.All();
        private string? _selectedProgramId;

        public ProgramsTabView()
        {
            InitializeComponent();
            Find<ListBox>("ProgramLibraryList").SelectionChanged += ProgramLibraryList_SelectionChanged;
            RefreshBrowse();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            RefreshBrowse();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (VisualRoot is not null) RefreshBrowse();
            });

        /// <summary>Uses a supplied read-only catalogue without creating a runtime service.</summary>
        internal void UseProgramLibrary(IReadOnlyList<ProgramDefinition> library)
        {
            ArgumentNullException.ThrowIfNull(library);
            _library = library;
            _selectedProgramId = null;
            RefreshBrowse();
        }

        private void RefreshBrowse()
        {
            var list = Find<ListBox>("ProgramLibraryList");
            // Snapshot first: replacing ItemsSource raises SelectionChanged with an empty
            // selection, which would null out _selectedProgramId before we can restore it.
            var wantedId = _selectedProgramId;
            var items = MainShellWindow.BuildProgramBrowseItems(_library);
            list.ItemsSource = items;

            Find<StackPanel>("ProgramsBrowsePanel").IsVisible = true;
            Find<StackPanel>("ProgramsRunPanel").IsVisible = false;
            Find<StackPanel>("ProgramsLapsedPanel").IsVisible = false;
            Find<StackPanel>("ProgramsGraduatedPanel").IsVisible = false;
            Find<TextBlock>("TxtProgramsBrowseEmpty").IsVisible = items.Count == 0;

            var selected = items.FirstOrDefault(item => item.ProgramId == wantedId)
                           ?? items.FirstOrDefault();
            list.SelectedItem = selected;
            _selectedProgramId = selected?.ProgramId;
            ShowProgramDetails(selected);
        }

        private void ProgramLibraryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var selected = Find<ListBox>("ProgramLibraryList").SelectedItem as ProgramBrowseItem;
            _selectedProgramId = selected?.ProgramId;
            ShowProgramDetails(selected);
        }

        private void ShowProgramDetails(ProgramBrowseItem? item)
        {
            var panel = Find<Border>("ProgramDetailsPanel");
            panel.IsVisible = item is not null;
            var chapters = Find<ItemsControl>("ProgramDetailsChapterList");
            chapters.ItemsSource = item?.Definition.Chapters.Select(chapter => new ProgramChapterBrowseItem
            {
                Name = chapter.Name,
                Subtitle = chapter.Subtitle,
                DaysLabel = Loc.GetF("programs_length_days", chapter.Days.Count),
                RewardText = chapter.RewardDescription ?? ""
            }).ToList();

            if (item is null)
            {
                Find<TextBlock>("TxtProgramDetailsIcon").Text = "";
                Find<TextBlock>("TxtProgramDetailsTitle").Text = "";
                Find<TextBlock>("TxtProgramDetailsSubtitle").Text = "";
                Find<TextBlock>("TxtProgramDetailsPitch").Text = "";
                Find<TextBlock>("TxtProgramDetailsLength").Text = "";
                Find<TextBlock>("TxtProgramDetailsTier").Text = "";
                return;
            }

            Find<TextBlock>("TxtProgramDetailsIcon").Text = item.Icon;
            Find<TextBlock>("TxtProgramDetailsTitle").Text = item.Title;
            Find<TextBlock>("TxtProgramDetailsSubtitle").Text = item.Subtitle;
            Find<TextBlock>("TxtProgramDetailsPitch").Text = item.Pitch;
            Find<TextBlock>("TxtProgramDetailsLength").Text = item.LengthLabel;
            Find<TextBlock>("TxtProgramDetailsTier").Text = item.TierLabel;
        }

        // ---- HANDLERS -------------------------------------------------------------

        // Execution belongs to the later enrollment/session layer. These handlers intentionally do
        // nothing while their buttons are disabled by the browse-only carrier; no progress state is
        // implied by opening or rendering this view.
        private void BtnProgramEnroll_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramPauseResume_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramWithdraw_Click(object? sender, RoutedEventArgs e) { }
        private void BtnStartTodaySession_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramSubmitRitual_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramRestart_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramDismissGraduated_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>
        /// Keeps the session bar's clip a rounded rect at its live size. A Border's ClipToBounds
        /// clips to the layout RECTANGLE, not the corner radius, so without this the sweeping sheen
        /// would poke square corners past the bar's rounded ends. Pure view concern, so it lives
        /// here - ported as-is from the WPF original.
        /// </summary>
        private void SessionBarHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is Border host && host.Bounds.Width > 0 && host.Bounds.Height > 0)
            {
                host.Clip = new RectangleGeometry(new Rect(0, 0, host.Bounds.Width, host.Bounds.Height))
                {
                    RadiusX = 8,
                    RadiusY = 8,
                };
            }
        }

        // ---- BROWSE DATA ----------------------------------------------------------

        private T Find<T>(string name) where T : Control => this.FindControl<T>(name)!;
    }

    // -------------------------------------------------------------------------------------------
    // Presentation rows, copied from ConditioningControlPanel/Views/Tabs/ProgramsTabItems.cs.
    //
    // Same names, same order, same members. Two mechanical changes throughout: every
    // System.Windows.Visibility becomes a bool named <X>Visible, because Avalonia binds IsVisible
    // to a bool directly; and System.Windows.Media.Brush / ImageSource become Avalonia's IBrush /
    // IImage. ponytail: the WPF file stays the source of truth until Views/Tabs moves to Core, at
    // which point one of these two copies is deleted.
    // -------------------------------------------------------------------------------------------

    /// <summary>One program on the browse list (nothing enrolled).</summary>
    public class ProgramBrowseItem
    {
        /// <summary>The Core definition behind this row; no catalogue copy is made in the head.</summary>
        public ProgramDefinition Definition { get; set; } = null!;
        public string ProgramId { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Pitch { get; set; } = "";
        public string LengthLabel { get; set; } = "";

        public string TierLabel { get; set; } = "";
        public IBrush TierBrush { get; set; } = Brushes.Gray;
        public IBrush TierBackground { get; set; } = Brushes.Transparent;

        public IBrush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>The program's banner strip for the card's header band. Null hides the band.</summary>
        public IImageBrushSource? BannerArt { get; set; }
        public bool BannerVisible { get; set; }

        /// <summary>
        /// The program's art, as the OPACITY MASK for the crest's accent-filled Rectangle - never as
        /// an Image source. Program art ships as white RGB with its luminance in the ALPHA channel,
        /// so accent Fill + this as the mask is what makes bright source read as full accent.
        /// </summary>
        public IBrush? ArtMask { get; set; }

        /// <summary>Accent halo behind the crest. Radial, built in code from the accent.</summary>
        public IBrush ArtGlowBrush { get; set; } = Brushes.Transparent;

        /// <summary>Shows the art crest. Hidden is the normal no-art state, not a failure.</summary>
        public bool ArtVisible { get; set; }

        /// <summary>Inverse of <see cref="ArtVisible"/>: the bare 44px glyph the card showed before
        /// the crest existed. Exactly one of the two is ever visible.</summary>
        public bool IconOnlyVisible { get; set; } = true;

        /// <summary>Premium program the user cannot currently take - the ✨ locked treatment.</summary>
        public bool IsLocked { get; set; }

        public string ActionText { get; set; } = "";
        public bool IsActionEnabled { get; set; } = true;
        public double ActionOpacity { get; set; } = 0.5;

        public string ReasonText { get; set; } = "";
        public bool ReasonVisible { get; set; }

        public double CardOpacity { get; set; } = 1.0;
    }

    /// <summary>One chapter in the selected read-only program details.</summary>
    public class ProgramChapterBrowseItem
    {
        public string Name { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string DaysLabel { get; set; } = "";
        public string RewardText { get; set; } = "";
        public bool RewardVisible => !string.IsNullOrWhiteSpace(RewardText);
    }

    /// <summary>One node on the whole-program reward track.</summary>
    public class ProgramDayPip
    {
        public int DayIndex { get; set; }
        public string Label { get; set; } = "";
        public string Tip { get; set; } = "";

        public IBrush Fill { get; set; } = Brushes.Transparent;
        public IBrush Stroke { get; set; } = Brushes.Gray;
        public Thickness PipBorderThickness { get; set; } = new Thickness(1);
        public IBrush LabelBrush { get; set; } = Brushes.Gray;
        public double PipOpacity { get; set; } = 1.0;
        public FontWeight LabelWeight { get; set; } = FontWeight.Normal;

        /// <summary>Node diameter. Today is the largest, done days middle, future days smallest.</summary>
        public double NodeSize { get; set; } = 30;
        public double LabelSize { get; set; } = 11;

        /// <summary>Today's node. Drives the node's size/colour treatment only.</summary>
        public bool IsCurrent { get; set; }

        /// <summary>
        /// Today's node AND motion allowed. On WPF a DataTrigger read this once to start the
        /// breathing-glow storyboard; the storyboards are dropped on this head, so nothing reads it
        /// yet - kept so the carrier still matches the original one for one.
        /// </summary>
        public bool Breathe { get; set; }
        public IBrush GlowBrush { get; set; } = Brushes.Transparent;
        public bool GlowVisible { get; set; }

        /// <summary>
        /// One-shot: this day just flipped to complete, so its node flares. Same story as
        /// <see cref="Breathe"/> - the flare Ellipse is still in the template, unanimated.
        /// </summary>
        public bool Ignite { get; set; }

        /// <summary>Fill of the ignite flare. The program accent as a radial, built in code.</summary>
        public IBrush IgniteBrush { get; set; } = Brushes.Transparent;

        /// <summary>Milestone treatment: boss crown / reward gift under the node.</summary>
        public string RewardGlyph { get; set; } = "";
        public bool RewardVisible { get; set; }
        public string RewardTip { get; set; } = "";
    }

    /// <summary>One task row inside today's panel.</summary>
    public class ProgramTaskItem
    {
        public string TaskId { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>
        /// The plain "how do I actually do this" line under the flavour text: the exact feature the
        /// verifier draws credit from and what has to happen. Hidden once the task is complete.
        /// </summary>
        public string HowTo { get; set; } = "";
        public bool HowToVisible { get; set; }

        public string StatusGlyph { get; set; } = "";
        public IBrush StatusBrush { get; set; } = Brushes.Gray;

        /// <summary>
        /// The app's own product icon for whatever this task is verified by (Resources/features/*).
        /// Null for tasks with no feature behind them - rituals, ambient work - in which case
        /// <see cref="IconVisible"/> is false and the status glyph carries the row on its own.
        /// </summary>
        public IImage? Icon { get; set; }
        public bool IconVisible { get; set; }

        /// <summary>Inverse of <see cref="IconVisible"/>: the glyph carries the icon slot alone.</summary>
        public bool GlyphVisible { get; set; } = true;

        public string ProgressText { get; set; } = "";

        /// <summary>Counted tasks (TargetValue &gt; 1) show the mini progress bar; others hide it.</summary>
        public bool BarVisible { get; set; }

        /// <summary>
        /// Star widths for the mini bar's filled/remaining columns. Pre-computed GridLengths like
        /// every other value here - the template binds ColumnDefinition.Width straight to them.
        /// </summary>
        public GridLength ProgressStar { get; set; } = new GridLength(0, GridUnitType.Star);
        public GridLength RemainderStar { get; set; } = new GridLength(1, GridUnitType.Star);

        /// <summary>Mini bar fill - the program accent, resolved in code.</summary>
        public IBrush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>Accent when the task is done, the plain glass border otherwise.</summary>
        public IBrush CardBorderBrush { get; set; } = Brushes.Transparent;

        /// <summary>The ✓ chip in the card's top-right corner.</summary>
        public bool DoneChipVisible { get; set; }

        /// <summary>
        /// Ink for that ✓, picked from the chip's own accent fill rather than fixed near-white - a
        /// pale program or mod accent rendered the tick white on white.
        /// </summary>
        public IBrush DoneChipForeground { get; set; } = Brushes.White;

        /// <summary>
        /// True only on the rebuild immediately after the task flipped to complete. On WPF this
        /// drove the card's pop storyboard, which is dropped here.
        /// </summary>
        public bool JustCompleted { get; set; }

        public string BadgeText { get; set; } = "";
        public bool BadgeVisible { get; set; }

        /// <summary>Ritual tasks get the photo picker; auto-verified ones never do.</summary>
        public bool SubmitVisible { get; set; }

        public double RowOpacity { get; set; } = 1.0;
        public IBrush TextBrush { get; set; } = Brushes.White;
    }

    /// <summary>
    /// One feature layer today's session turns on ("Bubbles", "Pink filter"), shown under the day
    /// blurb. Built from the day's session TEMPLATE, not from the live engine.
    /// </summary>
    public class ProgramLayerChip
    {
        public string Label { get; set; } = "";

        /// <summary>Full text on hover, including the "new today" note when the layer is new.</summary>
        public string Tip { get; set; } = "";

        /// <summary>
        /// The app's own product icon for the feature (Resources/features/*). Hidden when the PNG
        /// does not resolve; the chip is then a plain labelled pill.
        /// </summary>
        public IImage? Icon { get; set; }
        public bool IconVisible { get; set; }

        /// <summary>Accent when the layer is new today, the plain glass border otherwise.</summary>
        public IBrush BorderBrush { get; set; } = Brushes.Transparent;
        public IBrush LabelBrush { get; set; } = Brushes.White;

        /// <summary>Fill of the NEW pill. The program accent, resolved in code.</summary>
        public IBrush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>
        /// Ink for the NEW pill's label, picked from the accent behind it rather than fixed
        /// near-white - a pale accent rendered the word white on white.
        /// </summary>
        public IBrush NewForeground { get; set; } = Brushes.White;

        /// <summary>The NEW pill: this layer was not in the previous day's session.</summary>
        public bool NewVisible { get; set; }
    }

    /// <summary>One upcoming day in the run view's "up next" column.</summary>
    public class ProgramUpNextItem
    {
        public string DayLabel { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Pre-joined "45 minutes · Complete 3 lock cards · Boss day".</summary>
        public string Meta { get; set; } = "";

        /// <summary>Accent on a boss day, muted otherwise.</summary>
        public IBrush DayBrush { get; set; } = Brushes.Gray;

        public string Glyph { get; set; } = "";
        public string GlyphTip { get; set; } = "";
        public bool GlyphVisible { get; set; }

        /// <summary>Further-out days sit back a little, so the list reads as a horizon.</summary>
        public double RowOpacity { get; set; } = 1.0;
    }
}
