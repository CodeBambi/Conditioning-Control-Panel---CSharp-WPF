using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Forwarding shim: every handler hands straight to the MainWindow partial
    /// (MainWindow/MainWindow.Haptics.cs), which owns the state. Handlers declared
    /// inside a DataTemplate resolve against THIS class, so the toy-card Test button
    /// has to be here too.
    ///
    /// <para>PHASE 4 (UX restructure): this view is no longer mounted in MainWindow.xaml. It is
    /// the "haptics" module of the Studio rack (Views/Tabs/StudioTabView.xaml), and MainWindow
    /// reaches it through the single <c>MainWindow.HapticsTab</c> passthrough. Nothing in this
    /// file had to change: <see cref="Owner"/> walks to the containing WINDOW, which is still
    /// MainWindow - the rack is a UserControl inside it, not a separate window - so all 34
    /// forwards below resolve exactly as before.</para>
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

        private void TxtHapticIntifaceUrl_TextChanged(object sender, TextChangedEventArgs e)
            => Owner?.TxtHapticIntifaceUrl_TextChanged(sender, e);

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

        // ---- Phase F: temperament, toy input, FunScript, luminance, audio advanced ----

        private void RbHapticTemperament_Checked(object sender, RoutedEventArgs e)
            => Owner?.RbHapticTemperament_Checked(sender, e);

        private void ChkHapticToyInput_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticToyInput_Changed(sender, e);

        private void ChkHapticToyAttentionCheck_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticToyAttentionCheck_Changed(sender, e);

        private void SliderHapticOverrideCooldown_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderHapticOverrideCooldown_Changed(sender, e);

        private void ChkHapticFunScript_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticFunScript_Changed(sender, e);

        private void ChkHapticFunScriptVibe_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticFunScriptVibe_Changed(sender, e);

        private void ChkHapticLuminance_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticLuminance_Changed(sender, e);

        private void SliderHapticLuminance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderHapticLuminance_Changed(sender, e);

        private void ChkHapticBandSplit_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkHapticBandSplit_Changed(sender, e);

        private void SliderDspSensitivity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderDspSensitivity_Changed(sender, e);

        private void SliderDspSmoothing_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderDspSmoothing_Changed(sender, e);

        private void SliderDspBass_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderDspBass_Changed(sender, e);

        private void SliderDspRms_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderDspRms_Changed(sender, e);

        private void SliderDspOnset_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderDspOnset_Changed(sender, e);

        private void SliderDspMax_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => Owner?.SliderDspMax_Changed(sender, e);

        private void BtnDspReset_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnDspReset_Click(sender, e);
    }
}
