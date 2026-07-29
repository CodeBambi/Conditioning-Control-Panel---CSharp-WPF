using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// "Graded Intake" — the banded-descent intake, promoted out of the Lab into its own
    /// Exclusives page. Nothing about the feature changed in the move: the same
    /// controls, handlers and settings are simply hosted here instead of on the Lab card.
    /// Code-behind is a pure forwarder to the MainWindow handlers in MainWindow.Lab.cs.
    /// Pop Quiz rode along because it lived inside the same card, but it is NOT premium and
    /// deliberately sits outside <c>GradedIntakeGate</c>.
    ///
    /// The gate is no longer a plain t1 lock: free accounts get one run a week, so
    /// <c>GradedIntakeGate</c> (with its swappable copy) and <c>GradedIntakePassBanner</c> are
    /// painted together from <c>MainWindow.RefreshGradedIntakeGate</c>, which is the only thing
    /// that should ever touch their visibility.
    /// </summary>
    public partial class GradedIntakeTabView : UserControl
    {
        public GradedIntakeTabView()
        {
            InitializeComponent();
        }

        private void BtnStartQuiz_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnStartQuiz_Click(sender, e);
        }
        private void BtnStartIntake_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnStartIntake_Click(sender, e);
        }
        private void BtnTestPopQuiz_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnTestPopQuiz_Click(sender, e);
        }
        private void ChkPopQuizEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkPopQuizEnabled_Changed(sender, e);
        }
        private void SliderPopQuizFrequency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderPopQuizFrequency_ValueChanged(sender, e);
        }
        /// <summary>Gate CTA. Serves both closed states: the shared App Info &amp; Data popup is
        /// where signing in lives as well as where the tiers are, so NeedsLogin and Spent can share
        /// one destination even though their button labels differ.</summary>
        private void BtnGI_GateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnGateUnlock_Click(sender, e);
        }
    }
}
