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

        /// <summary>
        /// Keeps the session bar's clip a rounded rect at its live size. Border.ClipToBounds clips
        /// to the layout RECTANGLE, not the corner radius, so without this the sweeping sheen would
        /// poke square corners past the bar's rounded ends. Pure view concern, so it lives here.
        /// </summary>
        private void SessionBarHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.Border host && host.ActualWidth > 0 && host.ActualHeight > 0)
            {
                host.Clip = new System.Windows.Media.RectangleGeometry(
                    new Rect(0, 0, host.ActualWidth, host.ActualHeight), 8, 8);
            }
        }
    }
}
