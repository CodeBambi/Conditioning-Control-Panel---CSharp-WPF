using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class HapticsTabView : UserControl
    {
        public HapticsTabView()
        {
            InitializeComponent();
        }

        private void AlgorithmCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.AlgorithmCard_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void BtnHapticConnect_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnHapticConnect_Click(sender, e);
        }
        private void BtnHapticTest_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnHapticTest_Click(sender, e);
        }
        private void BtnHapticsHelp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnHapticsHelp_Click(sender, e);
        }
        private void ChkHapticAudioSync_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkHapticAudioSync_Changed(sender, e);
        }
        private void ChkHapticAutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkHapticAutoConnect_Changed(sender, e);
        }
        private void ChkHapticFeature_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkHapticFeature_Changed(sender, e);
        }
        private void ChkHapticsEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkHapticsEnabled_Changed(sender, e);
        }
        private void CmbHapticDtrhDensity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbHapticDtrhDensity_SelectionChanged(sender, e);
        }
        private void CmbHapticMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbHapticMode_SelectionChanged(sender, e);
        }
        private void CmbHapticProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbHapticProvider_SelectionChanged(sender, e);
        }
        private void SliderHapticFeature_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderHapticFeature_Changed(sender, e);
        }
        private void SliderHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderHapticIntensity_ValueChanged(sender, e);
        }
        private void SliderVideoHapticDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderVideoHapticDelay_Changed(sender, e);
        }
        private void SliderVideoHapticPower_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderVideoHapticPower_Changed(sender, e);
        }
        private void TxtHapticUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtHapticUrl_TextChanged(sender, e);
        }
    }
}
