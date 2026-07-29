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

    /// <summary>One square on the whole-program day strip.</summary>
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
    }

    /// <summary>One task row inside today's panel.</summary>
    public class ProgramTaskItem
    {
        public string TaskId { get; set; } = "";
        public string Description { get; set; } = "";

        public string StatusGlyph { get; set; } = "";
        public Brush StatusBrush { get; set; } = Brushes.Gray;

        public string ProgressText { get; set; } = "";
        public Visibility ProgressVisibility { get; set; } = Visibility.Collapsed;

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
