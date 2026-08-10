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

        // TOMBSTONE (UX restructure, Phase 5). Six shims left with the "AI Companion Effects &
        // Memory" card: BtnClearChatMemory_Click, BtnLabEffectsSetupLocal_Click,
        // ChkAllowEffect_Changed, ChkCapEffects_Changed, ChkChatMemoryEnabled_Changed and
        // SliderMaxHapticIntensity_ValueChanged. The card is Z7b of the Companion room now
        // (Views\Controls\Companion\AiPermissionsGrid), and its shims went with it — the real
        // handlers never moved at all; they are still MainWindow.Patreon.cs's.
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
        private void BtnStartGoon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStartGoon_Click(sender, e);
        }
        // BtnStartQuiz / BtnStartIntake / BtnTestPopQuiz moved to GradedIntakeTabView
        // when the Graded Intake graduated to its own Exclusives page.
        private void BtnStartBureau_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStartBureau_Click(sender, e);
        }
        // The webcam engine bar (camera/monitor pickers, calibrate, tracker start/stop,
        // diagnostics, blink-recal + restrict-gaze toggles) moved to Settings → Devices in
        // Phase 2, so its thirteen shims went with it. All that is left on this tab is the
        // read-only status chip and the way back to the page that owns the camera.
        private void BtnOpenDeviceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OpenDeviceSettings();
        }
        private void ChkFocusGaze_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkFocusGaze_Changed(sender, e);
        }
    }
}
