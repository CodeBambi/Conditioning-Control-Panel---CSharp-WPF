using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class DeeperTabView : UserControl
    {
        public DeeperTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-4b): the header glyph's drift starts when the tab appears and is
            // parked the moment it is collapsed again.
            IsVisibleChanged += DeeperTabView_IsVisibleChanged;
        }

        private void DeeperTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnDeeperTabVisibilityChanged(IsVisible);
        }

        // Row hover lift. Composed with (not a replacement for) the row's existing IsMouseOver
        // border-brush reveal, which stays in the DataTemplate's own style.
        private void DeeperRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnDeeperRowHover(sender as FrameworkElement, true);
        }

        private void DeeperRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnDeeperRowHover(sender as FrameworkElement, false);
        }

        private void BtnDeeperCatalogue_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperCatalogue_Click(sender, e);
        }
        private void BtnDeeperImport_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperImport_Click(sender, e);
        }
        private void BtnDeeperNewEnhancement_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperNewEnhancement_Click(sender, e);
        }
        private void BtnDeeperOpenLibraryFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperOpenLibraryFolder_Click(sender, e);
        }
        private void BtnDeeperOpenPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperOpenPlayer_Click(sender, e);
        }
        private void BtnDeeperTutorial_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperTutorial_Click(sender, e);
        }
        private void BtnDeeperWebcamCalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamCalibrate_Click(sender, e);
        }
        private void BtnDeeperWebcamManageConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamManageConsent_Click(sender, e);
        }
        private void BtnDeeperWebcamQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamQuickRecal_Click(sender, e);
        }
        private void BtnDeeperWebcamRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamRefresh_Click(sender, e);
        }
        private void BtnDeeperWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamRevokeConsent_Click(sender, e);
        }
        private void BtnDeeperWebcamStartStopTracker_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamStartStopTracker_Click(sender, e);
        }
        private void BtnDeeperWelcomeDemo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWelcomeDemo_Click(sender, e);
        }
        private void BtnDeeperWelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWelcomeDismiss_Click(sender, e);
        }
        private void BtnDeeperWelcomeTour_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWelcomeTour_Click(sender, e);
        }
        private void ChkBlinkRecalShortcut_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBlinkRecalShortcut_Changed(sender, e);
        }
        private void ChkDeeperWebcamRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkDeeperWebcamRestrictGazeToCalScreen_Changed(sender, e);
        }
        private void CmbDeeperWebcamDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbDeeperWebcamDevice_SelectionChanged(sender, e);
        }
        private void CmbDeeperWebcamMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbDeeperWebcamMonitor_SelectionChanged(sender, e);
        }
        private void DeeperPillAll_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillAll_Click(sender, e);
        }
        private void DeeperPillAudio_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillAudio_Click(sender, e);
        }
        private void DeeperPillHaptics_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillHaptics_Click(sender, e);
        }
        private void DeeperPillVideo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillVideo_Click(sender, e);
        }
        private void DeeperPillWebcam_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillWebcam_Click(sender, e);
        }
        private void DeeperRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperRow_Click(sender, e);
        }

        private void DeeperRowDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperRowDelete_Click(sender, e);
        }
        private void DeeperRowPlay_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperRowPlay_Click(sender, e);
        }
        private void DeeperRowSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperRowSubmit_Click(sender, e);
        }
        private void DeeperSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperSearch_TextChanged(sender, e);
        }
        private void DeeperSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperSort_SelectionChanged(sender, e);
        }
    }
}
