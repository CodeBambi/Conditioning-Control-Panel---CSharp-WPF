using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// PHASE 5 (G3): the custom keyword-trigger + Screen OCR editors, rescued from the
    /// permanently-Collapsed <c>PatreonTabView</c> and mounted on the Awareness tab.
    /// <para>
    /// Pure re-hosting: every handler forwards to the same <see cref="MainWindow"/> methods the
    /// dead tab called, and the services behind them (<c>KeywordTriggerService</c>,
    /// <c>App.ScreenOcr</c>, <c>App.KeywordHighlight</c>) are untouched.
    /// </para>
    /// </summary>
    public partial class KeywordTriggersPanel : UserControl
    {
        public KeywordTriggersPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Opens the drawer and scrolls it into view. Used by the Awareness tab's
        /// "advanced editor" hyperlink when no preset is installed - before Phase 5 that
        /// branch dead-ended in the App Info popup because this editor had no home.
        /// <para>
        /// The BringIntoView is deferred at <see cref="DispatcherPriority.Normal"/> so the
        /// Expander has finished expanding first. Never <c>DispatcherPriority.Loaded</c> -
        /// that priority is starved on this app's dispatcher.
        /// </para>
        /// </summary>
        internal void RevealTriggerEditor()
        {
            KeywordTriggersExpander.IsExpanded = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new System.Action(() =>
            {
                try { BringIntoView(); }
                catch { /* layout torn down mid-navigation - nothing to scroll to */ }
            }));
        }

        private void BtnAddKeywordTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAddKeywordTrigger_Click(sender, e);
        }

        private void BtnImportFromCustomTriggers_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnImportFromCustomTriggers_Click(sender, e);
        }

        private void CmbOcrConfirmation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbOcrConfirmation_SelectionChanged(sender, e);
        }

        private void CmbOcrHighlightMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbOcrHighlightMode_SelectionChanged(sender, e);
        }

        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.InnerScrollViewer_PreviewMouseWheel(sender, e);
        }

        private void SliderKeywordBufferTimeout_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordBufferTimeout_ValueChanged(sender, e);
        }

        private void SliderKeywordHighlightDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordHighlightDuration_ValueChanged(sender, e);
        }

        private void SliderKeywordSessionMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordSessionMultiplier_ValueChanged(sender, e);
        }

        private void SliderScreenOcrInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderScreenOcrInterval_ValueChanged(sender, e);
        }
    }
}
