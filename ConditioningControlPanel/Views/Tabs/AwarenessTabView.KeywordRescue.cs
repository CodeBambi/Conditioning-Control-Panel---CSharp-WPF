using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// PHASE 5 (G3) passthrough seam - same pattern as <c>CompanionTabView</c> and
    /// <c>AppSettingsTabView.Devices</c>. The keyword-trigger / Screen OCR editors physically
    /// live in <see cref="Controls.Companion.KeywordTriggersPanel"/> now, but MainWindow's
    /// handlers keep reading them under their original names off <c>AwarenessTab</c>, so
    /// <c>MainWindow.KeywordTriggers.cs</c> stays a one-word re-point rather than a rewrite.
    /// </summary>
    public partial class AwarenessTabView
    {
        internal Slider SliderKeywordBufferTimeout => KeywordPanel.SliderKeywordBufferTimeout;
        internal TextBlock TxtKeywordBufferTimeout => KeywordPanel.TxtKeywordBufferTimeout;

        internal Slider SliderKeywordSessionMultiplier => KeywordPanel.SliderKeywordSessionMultiplier;
        internal TextBlock TxtKeywordSessionMultiplier => KeywordPanel.TxtKeywordSessionMultiplier;

        internal TextBlock TxtScreenOcrOffHint => KeywordPanel.TxtScreenOcrOffHint;
        internal StackPanel ScreenOcrIntervalPanel => KeywordPanel.ScreenOcrIntervalPanel;
        internal Slider SliderScreenOcrInterval => KeywordPanel.SliderScreenOcrInterval;
        internal TextBlock TxtScreenOcrInterval => KeywordPanel.TxtScreenOcrInterval;
        internal ComboBox CmbOcrConfirmation => KeywordPanel.CmbOcrConfirmation;

        internal TextBlock TxtHighlightOffHint => KeywordPanel.TxtHighlightOffHint;
        internal StackPanel HighlightDurationPanel => KeywordPanel.HighlightDurationPanel;
        internal ComboBox CmbOcrHighlightMode => KeywordPanel.CmbOcrHighlightMode;
        internal Slider SliderKeywordHighlightDuration => KeywordPanel.SliderKeywordHighlightDuration;
        internal TextBlock TxtKeywordHighlightDuration => KeywordPanel.TxtKeywordHighlightDuration;

        internal StackPanel KeywordTriggerListPanel => KeywordPanel.KeywordTriggerListPanel;

        internal Button HelpBtnKeywordTriggers => KeywordPanel.HelpBtnKeywordTriggers;
        internal Button HelpBtnScreenOcr => KeywordPanel.HelpBtnScreenOcr;
    }
}
