using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Forwarding shim: every handler hands straight to the MainWindow partial
    /// (MainWindow/MainWindow.Haptics.cs), which owns the state. Handlers declared
    /// inside a DataTemplate resolve against THIS class, so the toy-card Test button
    /// has to be here too.
    /// </summary>
    public partial class HapticsTabView : UserControl
    {
        public HapticsTabView()
        {
            InitializeComponent();
        }

        private MainWindow? Owner => Window.GetWindow(this) as MainWindow;

        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnGateUnlock_Click(sender, e);

        private void ChkHapticsEnabled_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticsEnabled_Changed(sender, e);

        private void BtnHapticConnect_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnHapticConnect_Click(sender, e);

        private void BtnHapticPanic_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnHapticPanic_Click(sender, e);

        private void BtnHapticTest_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnHapticTest_Click(sender, e);

        private void BtnHapticToyTest_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnHapticToyTest_Click(sender, e);

        private void BtnHapticsHelp_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnHapticsHelp_Click(sender, e);

        private void ChkHapticProvider_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticProvider_Changed(sender, e);

        private void ChkHapticAutoConnect_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticAutoConnect_Changed(sender, e);

        private void TxtHapticUrl_TextChanged(object sender, TextChangedEventArgs e)
            => Owner?.TxtHapticUrl_TextChanged(sender, e);

        private void SliderHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderHapticIntensity_ValueChanged(sender, e);

        private void SliderHapticMaxPower_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderHapticMaxPower_ValueChanged(sender, e);

        private void SliderHapticDtrhAmbient_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderHapticDtrhAmbient_Changed(sender, e);

        private void CmbHapticDtrhDensity_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => Owner?.CmbHapticDtrhDensity_SelectionChanged(sender, e);

        private void CmbPatternMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => Owner?.CmbPatternMode_SelectionChanged(sender, e);

        private void SliderPatternIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderPatternIntensity_Changed(sender, e);

        private void BtnPatternPlay_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnPatternPlay_Click(sender, e);

        private void PatternPreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
            => Owner?.PatternPreviewCanvas_SizeChanged(sender, e);

        private void SliderVideoHapticDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderVideoHapticDelay_Changed(sender, e);

        private void SliderVideoHapticPower_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderVideoHapticPower_Changed(sender, e);
    }
}
