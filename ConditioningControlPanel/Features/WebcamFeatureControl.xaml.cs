using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Popup host for the Lab webcam tracking controls plus a self-contained Microphone section. The
    /// webcam controls (LabWebcamEngineBar) are borrowed from the Lab tab by MainWindow and parented
    /// into <see cref="SettingsHost"/> while the popup is open, then returned on close. The mic section
    /// reads/writes the SAME AppSettings the She's Listening tab binds (device, voice precision,
    /// headphones/barge-in), and re-arms the live mic when the device changes — one source of truth.
    /// </summary>
    public partial class WebcamFeatureControl : UserControl
    {
        // Starts true: a Slider raises ValueChanged DURING InitializeComponent (Minimum=0.3 coerces the
        // default 0 → 0.3) before its sibling TextBlocks exist and before LoadMicSection runs. The guard
        // makes every change handler no-op until the control is loaded; LoadMicSection clears it.
        private bool _loading = true;
        private bool _micPopulating;  // guards the device combo while we rebuild it

        public WebcamFeatureControl()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadMicSection();
        }

        /// <summary>Host panel that receives the borrowed Lab webcam engine bar.</summary>
        public Panel WebcamSettingsHost => SettingsHost;

        private void LoadMicSection()
        {
            _loading = true;
            try
            {
                PopulateMicDevices();
                var s = App.Settings?.Current;
                if (s != null)
                {
                    SliderWakePrecision.Value = s.SpeechWakeMatchThreshold;
                    SliderCmdPrecision.Value = s.SpeechMatchThreshold;
                    TxtWakeVal.Text = s.SpeechWakeMatchThreshold.ToString("0.00");
                    TxtCmdVal.Text = s.SpeechMatchThreshold.ToString("0.00");
                    ChkHeadphones.IsChecked = s.SpeechHeadphonesMode;
                }
            }
            finally { _loading = false; }
        }

        private void PopulateMicDevices()
        {
            int saved = App.Settings?.Current?.SpeechInputDeviceIndex ?? -1;
            _micPopulating = true;
            try
            {
                CmbMicDevice.Items.Clear();
                ComboBoxItem? toSelect = null;
                foreach (var dev in Services.Speech.SpeechService.EnumerateInputDevices())
                {
                    var item = new ComboBoxItem { Content = dev.Name, Tag = dev.Index };
                    CmbMicDevice.Items.Add(item);
                    if (dev.Index == saved) toSelect = item;
                }
                // Fall back to "System default" (first entry) if the saved device is gone.
                CmbMicDevice.SelectedItem = toSelect ?? (CmbMicDevice.Items.Count > 0 ? CmbMicDevice.Items[0] : null);
            }
            finally { _micPopulating = false; }
        }

        private void CmbMicDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_micPopulating || _loading) return;
            if (CmbMicDevice.SelectedItem is not ComboBoxItem item || item.Tag is not int idx) return;

            var s = App.Settings?.Current;
            if (s == null) return;
            var name = idx < 0 ? "" : (item.Content?.ToString() ?? "");
            if (s.SpeechInputDeviceIndex == idx && s.SpeechInputDeviceName == name) return;
            s.SpeechInputDeviceIndex = idx;
            s.SpeechInputDeviceName = name; // matched by name on reopen — robust to ordinal reshuffle (#441b)
            App.Settings?.Save();

            // Apply live if the mic is armed: cut the current capture so the wake loop reopens on the new device.
            if (s.MicConsentGiven && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled))
            {
                try { App.Speech?.StopListening(); } catch { }
                try { App.Autonomy?.RefreshVoiceInputModes(); } catch { }
            }
        }

        private void BtnMicRefresh_Click(object sender, RoutedEventArgs e) => PopulateMicDevices();

        private void SliderWakePrecision_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SpeechWakeMatchThreshold = e.NewValue;        // clamped in the setter
            TxtWakeVal.Text = s.SpeechWakeMatchThreshold.ToString("0.00");
            App.Settings?.Save();
        }

        private void SliderCmdPrecision_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SpeechMatchThreshold = e.NewValue;            // clamped in the setter
            TxtCmdVal.Text = s.SpeechMatchThreshold.ToString("0.00");
            App.Settings?.Save();
        }

        private void ChkHeadphones_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SpeechHeadphonesMode = ChkHeadphones.IsChecked == true;
            App.Settings?.Save();
        }
    }
}
