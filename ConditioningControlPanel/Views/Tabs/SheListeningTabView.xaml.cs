using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// "She's Listening" — the voice-control Exclusive. A purpose-built surface for the offline
    /// mic features (spoken mantras + the "Hey Bambi" voice commands), with a command cheat-sheet.
    /// Its toggles bind the SAME AppSettings as the Takeover tab; the code-behind delegates to the
    /// MainWindow handlers (which keep both tabs in sync), so there is no duplicated consent/settings
    /// logic here.
    /// </summary>
    public partial class SheListeningTabView : UserControl
    {
        public SheListeningTabView()
        {
            InitializeComponent();
        }

        private void ChkSL_Mantras_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_Mantras_Changed(sender, e);
        }
        private void ChkSL_WakeWord_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_WakeWord_Changed(sender, e);
        }
        private void TxtSL_WakeWords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_WakeWords_LostFocus(sender, e);
        }
        private void BtnSL_Calibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_Calibrate_Click(sender, e);
        }
        private void ChkSL_PushToTalk_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_PushToTalk_Changed(sender, e);
        }
        private void BtnSL_SetPttKey_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_SetPttKey_Click(sender, e);
        }
        private void ChkSL_Headphones_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_Headphones_Changed(sender, e);
        }
        private void BtnSL_MicMaster_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleVoiceMic();
        }
        private void CmbSL_MicDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_MicDevice_SelectionChanged(sender, e);
        }
        private void BtnSL_MicRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_MicRefresh_Click(sender, e);
        }
        private void SldSL_MicSensitivity_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_MicSensitivity_Changed(sender, e);
        }
        private void BtnSL_TestMantra_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnTestVoice_Click(sender, e);
        }
        private void BtnSL_GateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnGateUnlock_Click(sender, e);
        }
        private void BtnSL_RevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_RevokeMicConsent_Click(sender, e);
        }
    }
}
