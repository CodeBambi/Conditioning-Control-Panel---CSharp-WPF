using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// Settings door · DEVICES. See the XAML header for what moved here and what died with it.
    ///
    /// <para>Two kinds of handler live in this file, and the difference is deliberate:</para>
    /// <list type="bullet">
    ///   <item><b>Hops.</b> Everything the webcam engine bar, the voice-mode toggles and the panic
    ///   key do already had a home in a MainWindow partial (<c>MainWindow.LabTab.cs</c>,
    ///   <c>MainWindow.Autonomy.cs</c>, <c>MainWindow.UiUpdates.cs</c>). Those handlers were not
    ///   rewritten - they still read the control by <c>x:Name</c>, only through
    ///   <c>AppSettingsTab.&lt;Name&gt;</c> instead of <c>LabTab.</c> / <c>BambiTakeoverTab.</c> /
    ///   <c>SettingsTab.</c>. So the shims here are the usual
    ///   <c>Window.GetWindow(this) is MainWindow mw</c> forwarders.</item>
    ///   <item><b>Ported.</b> The microphone device picker and the two precision sliders had no
    ///   MainWindow handler at all - their only implementation lived inside the retired
    ///   <c>WebcamFeatureControl</c> popup. That logic is carried over verbatim (same guards, same
    ///   live re-arm, same name-matched device restore) rather than invented.</item>
    /// </list>
    ///
    /// <para><b>_loading starts true on purpose.</b> A Slider raises ValueChanged during
    /// <c>InitializeComponent</c> - <c>Minimum="0.3"</c> coerces the default 0 up to 0.3 - before
    /// its sibling TextBlock exists and long before settings are read. Without the guard, opening
    /// the app would silently write 0.3 over the user's wake threshold. Same trap the popup had.</para>
    /// </summary>
    public partial class DevicesSettingsSection : UserControl, IAppSettingsSection
    {
        private bool _loading = true;
        private bool _micPopulating;   // guards the device combo while we rebuild it

        public DevicesSettingsSection()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadMicSection();
        }

        private static MainWindow? Main => Application.Current?.MainWindow as MainWindow;

        // =====================================================================================
        //  section lifecycle
        // =====================================================================================

        /// <summary>
        /// Called by <see cref="Views.Tabs.AppSettingsTabView.RefreshSections"/> every time the
        /// Settings door opens. Device lists go stale the moment someone plugs a camera or a headset
        /// in, so both are re-enumerated here rather than once at startup - this is the seam that
        /// replaces "the Lab tab was shown" / "the Webcam popup was opened".
        /// </summary>
        public void OnSectionShown()
        {
            LoadMicSection();
            var mw = Main;
            if (mw == null) return;
            try { mw.RefreshDeviceSettingsLists(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Settings/Devices: device list refresh failed"); }
        }

        // =====================================================================================
        //  webcam - hops into MainWindow.LabTab.cs (handlers unchanged, only the host moved)
        // =====================================================================================

        private void ChkBlinkRecalShortcut_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkBlinkRecalShortcut_Changed(sender, e);
        }

        private void CmbWebcamDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CmbWebcamDevice_SelectionChanged(sender, e);
        }

        private void BtnWebcamDeviceRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnWebcamDeviceRefresh_Click(sender, e);
        }

        private void CmbWebcamMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CmbWebcamMonitor_SelectionChanged(sender, e);
        }

        private void BtnWebcamReviewPrivacy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnWebcamReviewPrivacy_Click(sender, e);
        }

        private void BtnWebcamDebugCalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnWebcamDebugCalibrate_Click(sender, e);
        }

        private void BtnWebcamDebugQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnWebcamDebugQuickRecal_Click(sender, e);
        }

        private void BtnWebcamDebugStart_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnWebcamDebugStart_Click(sender, e);
        }

        private void BtnWebcamDebugTrackerTest_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnWebcamDebugTrackerTest_Click(sender, e);
        }

        private void BtnWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnWebcamRevokeConsent_Click(sender, e);
        }

        private void ChkWebcamDebugCursor_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkWebcamDebugCursor_Changed(sender, e);
        }

        private void ChkWebcamDriftCorrection_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkWebcamDriftCorrection_Changed(sender, e);
        }

        private void ChkRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkRestrictGazeToCalScreen_Changed(sender, e);
        }

        // =====================================================================================
        //  voice modes - hops into MainWindow.Autonomy.cs (the handlers that used to read the
        //  Collapsed BambiTakeoverTab.TakeoverVoiceInputLegacy block; they read this one now)
        // =====================================================================================

        private void ChkSpeechWakeWord_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkSpeechWakeWord_Changed(sender, e);
        }

        private void TxtSpeechWakeWords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.TxtSpeechWakeWords_LostFocus(sender, e);
        }

        private void ChkSpeechPushToTalk_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkSpeechPushToTalk_Changed(sender, e);
        }

        private void BtnSetPttKey_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnSetPttKey_Click(sender, e);
        }

        // =====================================================================================
        //  panic key - hops into MainWindow.UiUpdates.cs / MainWindow.xaml.cs
        // =====================================================================================

        private void ChkNoPanic_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkNoPanic_Changed(sender, e);
        }

        private void BtnPanicKey_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnPanicKey_Click(sender, e);
        }

        // =====================================================================================
        //  global hotkeys - second launcher for ChatShortcutCaptureDialog (the real editor)
        // =====================================================================================

        private void BtnChatShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnChatShortcut_Click(sender, e);
        }

        private void BtnCameraShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnCameraShortcut_Click(sender, e);
        }

        // =====================================================================================
        //  microphone - ported wholesale from the retired WebcamFeatureControl popup
        // =====================================================================================

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
            catch (Exception ex) { App.Logger?.Warning(ex, "Settings/Devices: mic section load failed"); }
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

            // The She's Listening chip quotes the device name - keep it honest.
            try { Main?.RefreshSheListeningDeviceChips(); } catch { }
        }

        private void BtnMicRefresh_Click(object sender, RoutedEventArgs e) => PopulateMicDevices();

        private void SliderWakePrecision_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null || TxtWakeVal == null) return;
            s.SpeechWakeMatchThreshold = e.NewValue;        // clamped in the setter
            TxtWakeVal.Text = s.SpeechWakeMatchThreshold.ToString("0.00");
            App.Settings?.Save();
        }

        private void SliderCmdPrecision_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null || TxtCmdVal == null) return;
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
            try { Main?.RefreshSheListeningDeviceChips(); } catch { }
        }
    }
}
