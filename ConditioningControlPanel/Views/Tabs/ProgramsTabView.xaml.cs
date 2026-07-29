using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Training Programs tab. Pure view: every handler forwards to the MainWindow partial
    /// (MainWindow.ProgramsTab.cs), which owns the service reads and the refresh.
    /// </summary>
    public partial class ProgramsTabView : UserControl
    {
        public ProgramsTabView()
        {
            InitializeComponent();
        }

        private void BtnProgramEnroll_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnProgramEnroll_Click(sender, e);
        }

        private void BtnProgramPauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnProgramPauseResume_Click(sender, e);
        }

        private void BtnProgramWithdraw_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnProgramWithdraw_Click(sender, e);
        }

        private void BtnStartTodaySession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStartTodaySession_Click(sender, e);
        }

        private void BtnProgramSubmitRitual_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnProgramSubmitRitual_Click(sender, e);
        }

        private void BtnProgramRestart_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnProgramRestart_Click(sender, e);
        }

        private void BtnProgramDismissGraduated_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnProgramDismissGraduated_Click(sender, e);
        }
    }
}
