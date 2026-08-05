using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Views.Tabs
{
    // ---------------------------------------------------------------------------------------
    // Presentation rows for the Programs tab and the enrollment ceremony.
    //
    // The app has no ViewModel layer, so these are plain public carriers built once in
    // MainWindow.ProgramsTab.cs and handed to an ItemsControl's ItemsSource. They hold only
    // already-formatted strings and already-resolved brushes: no service reads, no logic, so a
    // template can never reach back into the runtime while it renders.
    // ---------------------------------------------------------------------------------------

    /// <summary>One program on the browse list (nothing enrolled).</summary>
    public class ProgramBrowseItem
    {
        public string ProgramId { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Pitch { get; set; } = "";
        public string LengthLabel { get; set; } = "";

        public string TierLabel { get; set; } = "";
        public Brush TierBrush { get; set; } = Brushes.Gray;
        public Brush TierBackground { get; set; } = Brushes.Transparent;

        public Brush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>
        /// The program's banner strip for the card's header band - the same art the Dashboard
        /// Today card wears, resolved with no generic fallback (see ProgramArt.Banner). Null
        /// collapses the band.
        /// </summary>
        public ImageSource? BannerArt { get; set; }
        public Visibility BannerVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>
        /// The program's art, as the OPACITY MASK for the crest's accent-filled Rectangle - never as
        /// an Image source. Program art ships as white RGB with its luminance in the ALPHA channel
        /// (see Services/Program/ProgramArt.cs), so accent Fill + this as the mask is what makes
        /// bright source read as full accent and dark source as transparent. Null when nothing
        /// resolves, in which case <see cref="ArtVisibility"/> collapses the crest.
        /// </summary>
        public Brush? ArtMask { get; set; }

        /// <summary>Accent halo behind the crest. Frozen radial, built in code from the accent.</summary>
        public Brush ArtGlowBrush { get; set; } = Brushes.Transparent;

        /// <summary>Shows the art crest. Collapsed is the normal no-art state, not a failure.</summary>
        public Visibility ArtVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>
        /// Inverse of <see cref="ArtVisibility"/>: the bare 44px glyph the card showed before the
        /// crest existed. Exactly one of the two is ever visible, so missing art costs the card its
        /// crest and nothing else - same contract as the run header's sigil-or-accent-bar slot.
        /// </summary>
        public Visibility IconOnlyVisibility { get; set; } = Visibility.Visible;

        /// <summary>Premium program the user cannot currently take - the ✨ locked treatment.</summary>
        public bool IsLocked { get; set; }

        public string ActionText { get; set; } = "";
        public bool IsActionEnabled { get; set; } = true;

        public string ReasonText { get; set; } = "";
        public Visibility ReasonVisibility { get; set; } = Visibility.Collapsed;

        public double CardOpacity { get; set; } = 1.0;
    }

    /// <summary>One node on the whole-program reward track.</summary>
    public class ProgramDayPip
    {
        public int DayIndex { get; set; }
        public string Label { get; set; } = "";
        public string Tip { get; set; } = "";

        public Brush Fill { get; set; } = Brushes.Transparent;
        public Brush Stroke { get; set; } = Brushes.Gray;
        public Thickness PipBorderThickness { get; set; } = new Thickness(1);
        public Brush LabelBrush { get; set; } = Brushes.Gray;
        public double PipOpacity { get; set; } = 1.0;
        public FontWeight LabelWeight { get; set; } = FontWeights.Normal;

        /// <summary>Node diameter. Today is the largest, done days middle, future days smallest.</summary>
        public double NodeSize { get; set; } = 30;
        public double LabelSize { get; set; } = 11;

        /// <summary>
        /// Today's node only. Read once by the template's DataTrigger to start the breathing-glow
        /// storyboard - the carrier never changes after build, so the trigger can never re-fire.
        /// </summary>
        public bool IsCurrent { get; set; }
        public Brush GlowBrush { get; set; } = Brushes.Transparent;
        public Visibility GlowVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>Milestone treatment: boss crown / reward gift under the node.</summary>
        public string RewardGlyph { get; set; } = "";
        public Visibility RewardVisibility { get; set; } = Visibility.Collapsed;
        public string RewardTip { get; set; } = "";
    }

    /// <summary>One task row inside today's panel.</summary>
    public class ProgramTaskItem
    {
        public string TaskId { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>
        /// The plain "how do I actually do this" line under the flavour text: the exact feature
        /// the verifier draws credit from and what has to happen for the task to tick. Authored
        /// descriptions carry the fiction; this line carries the mechanics (#805-era support
        /// reports showed users guessing wrong). Collapsed once the task is complete.
        /// </summary>
        public string HowTo { get; set; } = "";
        public Visibility HowToVisibility { get; set; } = Visibility.Collapsed;

        public string StatusGlyph { get; set; } = "";
        public Brush StatusBrush { get; set; } = Brushes.Gray;

        /// <summary>
        /// The app's own product icon for whatever this task is verified by (Resources/features/*).
        /// Resolved in code-behind like every other value here, so the template never touches the
        /// runtime. Deliberately full-colour: this is the same iconography the Dashboard shows
        /// under every mod. Null for tasks with no feature behind them - rituals, ambient work -
        /// in which case <see cref="IconVisibility"/> collapses it and the status glyph carries the
        /// row on its own.
        /// </summary>
        public ImageSource? Icon { get; set; }
        public Visibility IconVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>Inverse of <see cref="IconVisibility"/>: the glyph carries the icon slot alone.</summary>
        public Visibility GlyphVisibility { get; set; } = Visibility.Visible;

        public string ProgressText { get; set; } = "";

        /// <summary>Counted tasks (TargetValue > 1) show the mini progress bar; others collapse it.</summary>
        public Visibility BarVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>
        /// Star widths for the mini bar's filled/remaining columns. Pre-computed GridLengths like
        /// every other value here - the template binds ColumnDefinition.Width straight to them.
        /// </summary>
        public GridLength ProgressStar { get; set; } = new GridLength(0, GridUnitType.Star);
        public GridLength RemainderStar { get; set; } = new GridLength(1, GridUnitType.Star);

        /// <summary>Mini bar fill - the program accent, resolved in code.</summary>
        public Brush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>Accent when the task is done, the plain glass border otherwise.</summary>
        public Brush CardBorderBrush { get; set; } = Brushes.Transparent;

        /// <summary>The ✓ chip in the card's top-right corner.</summary>
        public Visibility DoneChipVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>
        /// True only on the rebuild immediately after the task flipped to complete (the code-behind
        /// diffs against a seen-incomplete set), so the template's pop storyboard fires exactly once.
        /// </summary>
        public bool JustCompleted { get; set; }

        public string BadgeText { get; set; } = "";
        public Visibility BadgeVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>Ritual tasks get the photo picker; auto-verified ones never do.</summary>
        public Visibility SubmitVisibility { get; set; } = Visibility.Collapsed;

        public double RowOpacity { get; set; } = 1.0;
        public Brush TextBrush { get; set; } = Brushes.White;
    }

    /// <summary>
    /// One feature layer today's session turns on ("Bubbles", "Pink filter"), shown under the day
    /// blurb. Built from the day's session TEMPLATE, not from the live engine - the run view says
    /// what today will do before it has done any of it.
    /// </summary>
    public class ProgramLayerChip
    {
        public string Label { get; set; } = "";

        /// <summary>Full text on hover, including the "new today" note when the layer is new.</summary>
        public string Tip { get; set; } = "";

        /// <summary>
        /// The app's own product icon for the feature (Resources/features/*), resolved in code like
        /// every other value here. Full-colour on purpose - the task cards and the Dashboard show
        /// this exact set full-colour, so a tinted copy would read as a different, lesser set.
        /// Collapsed when the PNG does not resolve; the chip is then a plain labelled pill.
        /// </summary>
        public ImageSource? Icon { get; set; }
        public Visibility IconVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>Accent when the layer is new today, the plain glass border otherwise.</summary>
        public Brush BorderBrush { get; set; } = Brushes.Transparent;
        public Brush LabelBrush { get; set; } = Brushes.White;

        /// <summary>Fill of the NEW pill. The program accent, resolved in code.</summary>
        public Brush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>The NEW pill: this layer was not in the previous day's session.</summary>
        public Visibility NewVisibility { get; set; } = Visibility.Collapsed;
    }

    /// <summary>One upcoming day in the run view's "up next" column.</summary>
    public class ProgramUpNextItem
    {
        public string DayLabel { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Pre-joined "45 minutes · Complete 3 lock cards · Boss day".</summary>
        public string Meta { get; set; } = "";

        /// <summary>Accent on a boss day, muted otherwise.</summary>
        public Brush DayBrush { get; set; } = Brushes.Gray;

        public string Glyph { get; set; } = "";
        public string GlyphTip { get; set; } = "";
        public Visibility GlyphVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>Further-out days sit back a little, so the list reads as a horizon.</summary>
        public double RowOpacity { get; set; } = 1.0;
    }

    /// <summary>One chapter of the arc, shown up front in the enrollment ceremony.</summary>
    public class ProgramChapterItem
    {
        public string Name { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string DaysLabel { get; set; } = "";
        public string RewardText { get; set; } = "";
        public Visibility RewardVisibility { get; set; } = Visibility.Collapsed;
        public Brush AccentBrush { get; set; } = Brushes.Gray;
    }
}
