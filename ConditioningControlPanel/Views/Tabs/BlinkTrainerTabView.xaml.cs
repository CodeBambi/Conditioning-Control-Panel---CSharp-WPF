using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class BlinkTrainerTabView : UserControl
    {
        public BlinkTrainerTabView()
        {
            InitializeComponent();
        }

        private void BlinkTrainerMixOptionMix_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerMixOptionMix_Click(sender, e);
        }
        private void BlinkTrainerMixOptionSame_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerMixOptionSame_Click(sender, e);
        }
        private void BlinkTrainerSlider_DragEnd(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerSlider_DragEnd(sender, e);
        }
        private void BlinkTrainerSlider_DragStart(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerSlider_DragStart(sender, e);
        }
        private void BlinkTrainerSlider_LostCapture(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerSlider_LostCapture(sender, e);
        }
        private void BlinkTrainerStageMedia_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerStageMedia_MediaEnded(sender, e);
        }
        private void BtnBlinkTrainerAddFolderCard_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerAddFolderCard_Click(sender, e);
        }
        private void BtnBlinkTrainerCalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerCalibrate_Click(sender, e);
        }
        private void BtnBlinkTrainerGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerGateUnlock_Click(sender, e);
        }
        private void BtnBlinkTrainerManageConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerManageConsent_Click(sender, e);
        }
        private void BtnBlinkTrainerQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerQuickRecal_Click(sender, e);
        }
        private void BtnBlinkTrainerRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerRevokeConsent_Click(sender, e);
        }
        private void BtnBlinkTrainerStartSession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerStartSession_Click(sender, e);
        }
        private void BtnBlinkTrainerStartStopTracker_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerStartStopTracker_Click(sender, e);
        }
        // Phase 2: the camera/monitor pickers, the blink-recal toggle and the restrict-gaze
        // checkbox moved to Settings → Devices (they were duplicate editors of one setting each),
        // so their shims went with them. This is the read-only chip's link back.
        private void BtnOpenDeviceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OpenDeviceSettings();
        }
        private void SliderBlinkTrainerDurationNew_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBlinkTrainerDurationNew_Changed(sender, e);
        }
        private void SliderBlinkTrainerOpacityNew_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBlinkTrainerOpacityNew_Changed(sender, e);
        }
        private void SliderBlinkTrainerOpacityNew_Loaded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBlinkTrainerOpacityNew_Loaded(sender, e);
        }
        private void ToggleBlinkTrainerIncludeVideos_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ToggleBlinkTrainerIncludeVideos_Changed(sender, e);
        }
    }
}
