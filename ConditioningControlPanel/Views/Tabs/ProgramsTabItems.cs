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
