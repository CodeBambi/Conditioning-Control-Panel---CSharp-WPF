using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Multi-step privacy/consent flow for the offline microphone ("repeat after me").
    /// Steps: 1) what it enables, 2) privacy contract, 3) explicit consent.
    /// Mirrors WebcamConsentDialog so the two consent flows feel identical.
    /// ShowDialog&lt;bool&gt; yields true only when every gate passed and Enable was clicked.
    /// </summary>
    public partial class MicConsentDialog : Window
    {
        private enum Step { Intro = 1, Privacy = 2, Consent = 3 }
        private Step _step = Step.Intro;

        /// <summary>True when the user completed all consent gates and clicked Enable.</summary>
        public bool ConsentGiven { get; private set; }

        private readonly ScrollViewer _panel1, _panel2, _panel3;
        private readonly Ellipse _dot1, _dot2, _dot3;
        private readonly Button _btnBack, _btnNext, _btnEnable;
        private readonly CheckBox _chk1, _chk2, _chk3;
        private readonly TextBox _txtConfirm;
        private readonly TextBlock _txtConfirmHint;

        public MicConsentDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _panel1 = this.FindControl<ScrollViewer>("PanelStep1")!;
            _panel2 = this.FindControl<ScrollViewer>("PanelStep2")!;
            _panel3 = this.FindControl<ScrollViewer>("PanelStep3")!;
            _dot1 = this.FindControl<Ellipse>("DotStep1")!;
            _dot2 = this.FindControl<Ellipse>("DotStep2")!;
            _dot3 = this.FindControl<Ellipse>("DotStep3")!;
            _btnBack = this.FindControl<Button>("BtnBack")!;
            _btnNext = this.FindControl<Button>("BtnNext")!;
            _btnEnable = this.FindControl<Button>("BtnEnable")!;
            _chk1 = this.FindControl<CheckBox>("ChkConsent1")!;
            _chk2 = this.FindControl<CheckBox>("ChkConsent2")!;
            _chk3 = this.FindControl<CheckBox>("ChkConsent3")!;
            _txtConfirm = this.FindControl<TextBox>("TxtConfirm")!;
            _txtConfirmHint = this.FindControl<TextBlock>("TxtConfirmHint")!;

            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => { ConsentGiven = false; Close(false); };
            _btnBack.Click += (_, _) =>
            {
                if (_step == Step.Privacy) _step = Step.Intro;
                else if (_step == Step.Consent) _step = Step.Privacy;
                UpdateUiForStep();
            };
            _btnNext.Click += (_, _) =>
            {
                _step = _step == Step.Intro ? Step.Privacy : Step.Consent;
                UpdateUiForStep();
            };
            _btnEnable.Click += (_, _) => Enable();
            _chk1.IsCheckedChanged += (_, _) => UpdateEnableButtonState();
            _chk2.IsCheckedChanged += (_, _) => UpdateEnableButtonState();
            _chk3.IsCheckedChanged += (_, _) => UpdateEnableButtonState();
            _txtConfirm.TextChanged += (_, _) => UpdateEnableButtonState();

            UpdateUiForStep();
        }

        private void UpdateUiForStep()
        {
            _panel1.IsVisible = _step == Step.Intro;
            _panel2.IsVisible = _step == Step.Privacy;
            _panel3.IsVisible = _step == Step.Consent;

            _dot1.Fill = StepDotBrush(Step.Intro);
            _dot2.Fill = StepDotBrush(Step.Privacy);
            _dot3.Fill = StepDotBrush(Step.Consent);

            _btnBack.IsVisible = _step != Step.Intro;

            switch (_step)
            {
                case Step.Intro:
                    _btnNext.IsVisible = true;
                    _btnEnable.IsVisible = false;
                    _btnNext.Content = "I want to know more →";
                    break;
                case Step.Privacy:
                    _btnNext.IsVisible = true;
                    _btnEnable.IsVisible = false;
                    _btnNext.Content = "Continue →";
                    break;
                case Step.Consent:
                    _btnNext.IsVisible = false;
                    _btnEnable.IsVisible = true;
                    UpdateEnableButtonState();
                    break;
            }
        }

        private IBrush StepDotBrush(Step s)
        {
            if (_step == s) return (IBrush)this.FindResource("PinkBrush")!;
            return (int)_step > (int)s ? new SolidColorBrush(Color.FromRgb(0x8A, 0x4A, 0x6F))
                                       : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x52));
        }

        private void UpdateEnableButtonState()
        {
            var allChecked = _chk1.IsChecked == true && _chk2.IsChecked == true && _chk3.IsChecked == true;
            var typed = _txtConfirm.Text?.Trim() == "ENABLE";
            _btnEnable.IsEnabled = allChecked && typed;

            if (allChecked && typed)
            {
                _txtConfirmHint.Text = "All gates passed. You can enable now.";
                _txtConfirmHint.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xE0, 0xA0));
            }
            else
            {
                var missing = "";
                if (!allChecked) missing += "all 3 checkboxes";
                if (!allChecked && !typed) missing += " + ";
                if (!typed) missing += "ENABLE typed";
                _txtConfirmHint.Text = "Waiting for: " + missing + ".";
                _txtConfirmHint.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xA0));
            }
        }

        private void Enable()
        {
            // ponytail: needs AppSettings (MicConsentGiven) + SettingsService.Save, wired when they move to Core.
            // The mic stays closed - it only opens during an explicit listen window while Takeover runs.
            Log.Information("Mic consent granted at {Time}", System.DateTime.UtcNow);

            ConsentGiven = true;
            Close(true);
        }
    }
}
