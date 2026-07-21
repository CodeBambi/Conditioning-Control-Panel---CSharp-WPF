using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// "Graded Intake" — the banded-descent intake, promoted out of the Lab into its own
    /// Exclusives page (t1 gated). Nothing about the feature changed in the move: the same
    /// controls, handlers and settings are simply hosted here instead of on the Lab card.
    /// Code-behind is a pure forwarder to the MainWindow handlers in MainWindow.Lab.cs.
    /// Pop Quiz rode along because it lived inside the same card, but it is NOT premium and
    /// deliberately sits outside <c>GradedIntakeGate</c>.
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
        private void BtnGI_GateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnGateUnlock_Click(sender, e);
        }
    }
}
