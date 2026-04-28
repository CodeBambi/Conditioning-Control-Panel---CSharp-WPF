using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Multi-step privacy/consent flow for the webcam tracking feature.
    /// Steps: 1) what it does, 2) privacy contract, 3) explicit consent, 4) calibration prompt.
    /// On approval flips AppSettings.WebcamConsent* fields. Does NOT start the camera.
    /// </summary>
    public partial class WebcamConsentDialog : Window
    {
        private const string SourceUrl = "https://github.com/CC-Labs-llc/Conditioning-Control-Panel---CSharp-WPF/blob/main/ConditioningControlPanel/Services/WebcamTrackingService.cs";

        private enum Step { Intro = 1, Privacy = 2, Consent = 3, Calibrate = 4 }
        private Step _step = Step.Intro;

        /// <summary>True when the user completed all consent gates and clicked Enable.</summary>
        public bool ConsentGiven { get; private set; }

        /// <summary>True when the user clicked "Calibrate now" on step 4. Caller opens calibration window.</summary>
        public bool WantsCalibrationNow { get; private set; }

        public WebcamConsentDialog()
        {
            InitializeComponent();
            UpdateUiForStep();
        }

        private void UpdateUiForStep()
        {
            PanelStep1.Visibility = _step == Step.Intro ? Visibility.Visible : Visibility.Collapsed;
            PanelStep2.Visibility = _step == Step.Privacy ? Visibility.Visible : Visibility.Collapsed;
            PanelStep3.Visibility = _step == Step.Consent ? Visibility.Visible : Visibility.Collapsed;
            PanelStep4.Visibility = _step == Step.Calibrate ? Visibility.Visible : Visibility.Collapsed;

            DotStep1.Fill = StepDotBrush(Step.Intro);
            DotStep2.Fill = StepDotBrush(Step.Privacy);
            DotStep3.Fill = StepDotBrush(Step.Consent);
            DotStep4.Fill = StepDotBrush(Step.Calibrate);

            BtnBack.Visibility = _step == Step.Intro ? Visibility.Collapsed : Visibility.Visible;
            BtnCancel.Visibility = _step == Step.Calibrate ? Visibility.Collapsed : Visibility.Visible;

            switch (_step)
            {
                case Step.Intro:
                    BtnNext.Visibility = Visibility.Visible;
                    BtnEnable.Visibility = Visibility.Collapsed;
                    BtnSkipCal.Visibility = Visibility.Collapsed;
                    BtnCalNow.Visibility = Visibility.Collapsed;
                    BtnNext.Content = "I want to know more →";
                    break;
                case Step.Privacy:
                    BtnNext.Visibility = Visibility.Visible;
                    BtnEnable.Visibility = Visibility.Collapsed;
                    BtnSkipCal.Visibility = Visibility.Collapsed;
                    BtnCalNow.Visibility = Visibility.Collapsed;
                    BtnNext.Content = "Continue →";
                    break;
                case Step.Consent:
                    BtnNext.Visibility = Visibility.Collapsed;
                    BtnEnable.Visibility = Visibility.Visible;
                    BtnSkipCal.Visibility = Visibility.Collapsed;
                    BtnCalNow.Visibility = Visibility.Collapsed;
                    UpdateEnableButtonState();
                    break;
                case Step.Calibrate:
                    BtnNext.Visibility = Visibility.Collapsed;
                    BtnEnable.Visibility = Visibility.Collapsed;
                    BtnBack.Visibility = Visibility.Collapsed;
                    BtnSkipCal.Visibility = Visibility.Visible;
                    BtnCalNow.Visibility = Visibility.Visible;
                    break;
            }
        }

        private Brush StepDotBrush(Step s)
        {
            if (_step == s) return (Brush)FindResource("PinkBrush");
            return (int)_step > (int)s ? new SolidColorBrush(Color.FromRgb(0x8A, 0x4A, 0x6F))
                                       : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x52));
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _step = _step == Step.Intro ? Step.Privacy : Step.Consent;
            UpdateUiForStep();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_step == Step.Privacy) _step = Step.Intro;
            else if (_step == Step.Consent) _step = Step.Privacy;
            UpdateUiForStep();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ConsentGiven = false;
            DialogResult = false;
            Close();
        }

        private void ConsentCheckChanged(object sender, RoutedEventArgs e) => UpdateEnableButtonState();

        private void TxtConfirm_TextChanged(object sender, TextChangedEventArgs e) => UpdateEnableButtonState();

        private void UpdateEnableButtonState()
        {
            var allChecked = ChkConsent1.IsChecked == true
                          && ChkConsent2.IsChecked == true
                          && ChkConsent3.IsChecked == true;
            var typed = TxtConfirm?.Text?.Trim() == "ENABLE";
            BtnEnable.IsEnabled = allChecked && typed;

            if (TxtConfirmHint != null)
            {
                if (allChecked && typed)
                {
                    TxtConfirmHint.Text = "All gates passed. You can enable now.";
                    TxtConfirmHint.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xE0, 0xA0));
                }
                else
                {
                    var missing = "";
                    if (!allChecked) missing += "all 3 checkboxes";
                    if (!allChecked && !typed) missing += " + ";
                    if (!typed) missing += "ENABLE typed";
                    TxtConfirmHint.Text = "Waiting for: " + missing + ".";
                    TxtConfirmHint.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xA0));
                }
            }
        }

        private void BtnEnable_Click(object sender, RoutedEventArgs e)
        {
            // Persist consent. Camera stays closed — user must explicitly enable a feature
            // toggle in the Lab card to actually start tracking.
            var s = App.Settings?.Current;
            if (s != null)
            {
                s.WebcamConsentGiven = true;
                s.WebcamConsentVersion = WebcamTrackingService.ConsentVersion;
                s.WebcamConsentDate = DateTime.UtcNow;
                App.Settings?.Save();
            }

            App.Logger?.Information("Webcam consent granted at {Time}, version {Version}",
                DateTime.UtcNow, WebcamTrackingService.ConsentVersion);

            ConsentGiven = true;
            _step = Step.Calibrate;
            UpdateUiForStep();
        }

        private void BtnSkipCal_Click(object sender, RoutedEventArgs e)
        {
            WantsCalibrationNow = false;
            DialogResult = true;
            Close();
        }

        private void BtnCalNow_Click(object sender, RoutedEventArgs e)
        {
            WantsCalibrationNow = true;
            DialogResult = true;
            Close();
        }

        private void LnkSource_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = SourceUrl, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamConsentDialog: failed to open source URL");
            }
        }
    }
}
