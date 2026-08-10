using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// "She's Listening" — the voice-control Exclusive. A purpose-built surface for the offline
    /// mic features (spoken mantras + the "Hey Bambi" voice commands), with a command cheat-sheet.
    /// The microphone hardware and the voice input modes (device, wake word, push-to-talk,
    /// headphone barge-in) are owned by Settings > Devices since Phase 2 of the UX restructure and
    /// appear here only as read-only chips; what stays live is what belongs to this page - the
    /// master switch, sensitivity, spoken mantras, wake calibration, the test and mic consent.
    /// Every handler still delegates to a MainWindow partial, so no settings logic lives here.
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
        private void BtnSL_Calibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_Calibrate_Click(sender, e);
        }
        // Phase 2: the mic device picker, the wake-word and push-to-talk toggles and the
        // headphone switch are read-only chips here now - Settings > Devices owns them, so
        // seven shims went with them. This is the link to that page.
        private void BtnSL_OpenDeviceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.OpenDeviceSettings();
        }
        private void BtnSL_MicMaster_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleVoiceMic();
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
