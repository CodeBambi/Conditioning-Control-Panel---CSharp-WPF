using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class LabTabView : UserControl
    {
        public LabTabView()
        {
            InitializeComponent();

            // The Chaos play-mode pick lives here now (moved from the in-game hub). Story stays
            // disabled until there's story content — ChaosModeService.StoryModeEnabled is the
            // single reversible switch. Reflect the current selection so the radios stay in sync.
            RbChaosStory.IsEnabled = Services.Chaos.ChaosModeService.StoryModeEnabled;
            ChaosStorySoonBadge.Visibility = Services.Chaos.ChaosModeService.StoryModeEnabled
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
            if (Services.Chaos.ChaosModeService.StoryModeEnabled
                && Services.Chaos.ChaosModeService.SelectedPlayMode == Services.Chaos.ChaosPlayMode.Story)
                RbChaosStory.IsChecked = true;
            else
                RbChaosFreeDesktop.IsChecked = true;
        }

        // Records the Lab-card play-mode pick (read by ChaosModeService.StartRun for every launch
        // path). While Story is disabled this can only ever be Free Desktop. Null-guarded because
        // a Checked event can fire mid-InitializeComponent before the sibling radio exists.
        private void ChaosPlayMode_Changed(object sender, RoutedEventArgs e)
        {
            if (RbChaosStory == null || RbChaosFreeDesktop == null) return;
            Services.Chaos.ChaosModeService.SelectedPlayMode =
                (RbChaosStory.IsChecked == true && Services.Chaos.ChaosModeService.StoryModeEnabled)
                    ? Services.Chaos.ChaosPlayMode.Story
                    : Services.Chaos.ChaosPlayMode.FreeDesktop;
        }

        private void BtnClearChatMemory_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnClearChatMemory_Click(sender, e);
        }
        private void BtnGazeMinigame_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGazeMinigame_Click(sender, e);
        }
        private void BtnLabBlinkTrainerOpenNew_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLabBlinkTrainerOpenNew_Click(sender, e);
        }
        private void BtnLabEffectsSetupLocal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLabEffectsSetupLocal_Click(sender, e);
        }
        private void BtnQuickStartChaos_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnQuickStartChaos_Click(sender, e);
        }
        private void BtnStartChaos_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStartChaos_Click(sender, e);
        }
        // BtnStartQuiz / BtnStartIntake / BtnTestPopQuiz moved to GradedIntakeTabView
        // when the Graded Intake graduated to its own Exclusives page.
        private void BtnStartBureau_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStartBureau_Click(sender, e);
        }
        private void BtnWebcamDebugCalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamDebugCalibrate_Click(sender, e);
        }
        private void BtnWebcamDebugQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamDebugQuickRecal_Click(sender, e);
        }
        private void BtnWebcamDebugStart_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamDebugStart_Click(sender, e);
        }
        private void BtnWebcamDebugTrackerTest_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamDebugTrackerTest_Click(sender, e);
        }
        private void BtnWebcamDeviceRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamDeviceRefresh_Click(sender, e);
        }
        private void BtnWebcamReviewPrivacy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamReviewPrivacy_Click(sender, e);
        }
        private void BtnWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamRevokeConsent_Click(sender, e);
        }
        private void ChkAllowEffect_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAllowEffect_Changed(sender, e);
        }
        private void ChkBlinkRecalShortcut_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBlinkRecalShortcut_Changed(sender, e);
        }
        private void ChkCapEffects_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkCapEffects_Changed(sender, e);
        }
        private void ChkChatMemoryEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkChatMemoryEnabled_Changed(sender, e);
        }
        private void ChkFocusGaze_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkFocusGaze_Changed(sender, e);
        }
        private void ChkRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkRestrictGazeToCalScreen_Changed(sender, e);
        }
        private void ChkWebcamDebugCursor_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkWebcamDebugCursor_Changed(sender, e);
        }
        private void ChkWebcamDriftCorrection_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkWebcamDriftCorrection_Changed(sender, e);
        }
        private void CmbWebcamDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbWebcamDevice_SelectionChanged(sender, e);
        }
        private void CmbWebcamMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbWebcamMonitor_SelectionChanged(sender, e);
        }
        private void SliderMaxHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderMaxHapticIntensity_ValueChanged(sender, e);
        }
    }
}
