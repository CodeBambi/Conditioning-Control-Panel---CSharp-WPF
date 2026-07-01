using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class BambiTakeoverTabView : UserControl
    {
        public BambiTakeoverTabView()
        {
            InitializeComponent();
        }

        private void BtnAutonomyStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAutonomyStartStop_Click(sender, e);
        }
        private void BtnForceStartAutonomy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnForceStartAutonomy_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void BtnTestAutonomy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestAutonomy_Click(sender, e);
        }
        private void BtnTestVoice_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestVoice_Click(sender, e);
        }
        private void ChkAutonomyVoice_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyVoice_Changed(sender, e);
        }
        private void ChkAutonomyResume_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyResume_Changed(sender, e);
        }
        private void ChkShowTakeoverCountdown_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkShowTakeoverCountdown_Changed(sender, e);
        }
        private void ChkSpeechWakeWord_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkSpeechWakeWord_Changed(sender, e);
        }
        private void TxtSpeechWakeWords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtSpeechWakeWords_LostFocus(sender, e);
        }
        private void ChkSpeechPushToTalk_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkSpeechPushToTalk_Changed(sender, e);
        }
        private void BtnSetPttKey_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSetPttKey_Click(sender, e);
        }
        private void ChkAutonomyBehavior_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyBehavior_Changed(sender, e);
        }
        private void ChkAutonomyEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyEnabled_Changed(sender, e);
        }
        private void ChkAutonomyIdle_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyIdle_Changed(sender, e);
        }
        private void ChkAutonomyRandom_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyRandom_Changed(sender, e);
        }
        private void ChkAutonomyTimeAware_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyTimeAware_Changed(sender, e);
        }
        private void SliderAutonomyAnnounce_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyAnnounce_Changed(sender, e);
        }
        private void SliderAutonomyCooldown_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyCooldown_Changed(sender, e);
        }
        private void SliderAutonomyIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyIntensity_Changed(sender, e);
        }
        private void SliderAutonomyInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyInterval_Changed(sender, e);
        }
    }
}
