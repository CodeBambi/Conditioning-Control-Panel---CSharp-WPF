using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Onboarding wizard for the local-AI (Ollama) provider. Drives users through
    /// detect → consent → install Ollama → pull model → smoke test → done.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/LocalAiSetupWizard.xaml.cs. The page switching,
    /// footer wording, advanced-model toggle and progress-bar maths are the original's. Everything
    /// that reached OllamaSetupService, App.Settings or App.Logger is a stub that lands on the page
    /// the real call would have shown and stops there - the service is Windows-head code and has
    /// not moved to Core yet. WPF's DialogResult becomes Close(bool).
    /// </summary>
    public partial class LocalAiSetupWizard : Window
    {
        private const string DefaultModel = "qwen3.5:latest";
        private const string ManualInstallUrl = "https://ollama.com/download";

        private enum Step
        {
            Detecting,
            Consent,
            DownloadInstaller,
            Installing,
            PullModel,
            SmokeTest,
            Done,
            Error
        }

        private Step _step = Step.Detecting;
        private string _targetModel = DefaultModel;
        private bool _wizardComplete;

        public bool LocalAiReady { get; private set; }
        public string SelectedModel => _targetModel;

        private readonly StackPanel _panelDetecting, _panelConsent, _panelDownloadInstaller, _panelInstalling,
            _panelPullModel, _panelSmokeTest, _panelDone, _panelError;
        private readonly TextBlock _txtStepTitle, _txtStepSubtitle, _txtConsentLine2, _txtDownloadProgress,
            _txtPullHeader, _txtPullStatus, _txtPullDetail, _txtDoneDetail, _txtPrimary, _txtSecondary;
        private readonly Button _btnPrimary, _btnSecondary;
        private readonly CheckBox _chkAdvanced;
        private readonly Grid _advancedPanel;
        private readonly TextBox _txtAdvancedModel, _txtErrorDetail;
        private readonly Border _downloadProgressTrack, _downloadProgressFill, _pullProgressTrack, _pullProgressFill;

        public LocalAiSetupWizard()
        {
            AvaloniaXamlLoader.Load(this);

            T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
            _panelDetecting = C<StackPanel>("PanelDetecting");
            _panelConsent = C<StackPanel>("PanelConsent");
            _panelDownloadInstaller = C<StackPanel>("PanelDownloadInstaller");
            _panelInstalling = C<StackPanel>("PanelInstalling");
            _panelPullModel = C<StackPanel>("PanelPullModel");
            _panelSmokeTest = C<StackPanel>("PanelSmokeTest");
            _panelDone = C<StackPanel>("PanelDone");
            _panelError = C<StackPanel>("PanelError");
            _txtStepTitle = C<TextBlock>("TxtStepTitle");
            _txtStepSubtitle = C<TextBlock>("TxtStepSubtitle");
            _txtConsentLine2 = C<TextBlock>("TxtConsentLine2");
            _txtDownloadProgress = C<TextBlock>("TxtDownloadProgress");
            _txtPullHeader = C<TextBlock>("TxtPullHeader");
            _txtPullStatus = C<TextBlock>("TxtPullStatus");
            _txtPullDetail = C<TextBlock>("TxtPullDetail");
            _txtDoneDetail = C<TextBlock>("TxtDoneDetail");
            _txtPrimary = C<TextBlock>("TxtPrimary");
            _txtSecondary = C<TextBlock>("TxtSecondary");
            _btnPrimary = C<Button>("BtnPrimary");
            _btnSecondary = C<Button>("BtnSecondary");
            _chkAdvanced = C<CheckBox>("ChkAdvanced");
            _advancedPanel = C<Grid>("AdvancedPanel");
            _txtAdvancedModel = C<TextBox>("TxtAdvancedModel");
            _txtErrorDetail = C<TextBox>("TxtErrorDetail");
            _downloadProgressTrack = C<Border>("DownloadProgressTrack");
            _downloadProgressFill = C<Border>("DownloadProgressFill");
            _pullProgressTrack = C<Border>("PullProgressTrack");
            _pullProgressFill = C<Border>("PullProgressFill");

            _btnPrimary.Click += (_, _) => BtnPrimary_Click();
            _btnSecondary.Click += (_, _) => BtnSecondary_Click();
            _chkAdvanced.IsCheckedChanged += (_, _) => ChkAdvanced_Changed();
            C<TextBlock>("LinkManualInstall").PointerPressed += (_, _) => LinkManualInstall_Click();
            C<TextBlock>("LinkManualInstallError").PointerPressed += (_, _) => LinkManualInstall_Click();

            _targetModel = ResolveStartingModel();
            _txtAdvancedModel.Text = _targetModel;
            UpdateConsentDiskNote();

            // WPF ran this from Loaded; here it is synchronous because the stub cannot await
            // anything, and running it now is what puts the Consent page in the render PNG.
            StartDetect();
        }

        private static string ResolveStartingModel()
        {
            // ponytail: needs App.Settings.Current.CompanionPrompt.AiModel, wired when settings move to Core
            return DefaultModel;
        }

        private void UpdateConsentDiskNote()
        {
            // The default model is ~6.6 GB; for unknown custom models we just say "varies."
            // Both lines stay readable in any locale.
            _txtConsentLine2.Text = string.Equals(_targetModel, DefaultModel, StringComparison.OrdinalIgnoreCase)
                ? Loc.GetF("label_local_ai_consent_pull_model_known", _targetModel, "~6.6 GB")
                : Loc.GetF("label_local_ai_consent_pull_model_custom", _targetModel);
        }

        // -------- Step transitions --------

        private void Show(Step s)
        {
            _step = s;
            _panelDetecting.IsVisible = s == Step.Detecting;
            _panelConsent.IsVisible = s == Step.Consent;
            _panelDownloadInstaller.IsVisible = s == Step.DownloadInstaller;
            _panelInstalling.IsVisible = s == Step.Installing;
            _panelPullModel.IsVisible = s == Step.PullModel;
            _panelSmokeTest.IsVisible = s == Step.SmokeTest;
            _panelDone.IsVisible = s == Step.Done;
            _panelError.IsVisible = s == Step.Error;

            switch (s)
            {
                case Step.Detecting:
                    _txtStepTitle.Text = Loc.Get("label_local_ai_step_detecting");
                    _txtStepSubtitle.Text = Loc.Get("label_local_ai_step_detecting_sub");
                    _btnPrimary.IsVisible = false;
                    _txtSecondary.Text = Loc.Get("btn_cancel");
                    _btnSecondary.IsEnabled = true;
                    break;
                case Step.Consent:
                    _txtStepTitle.Text = Loc.Get("label_local_ai_step_consent");
                    _txtStepSubtitle.Text = Loc.Get("label_local_ai_step_consent_sub");
                    _btnPrimary.IsVisible = true;
                    _txtPrimary.Text = Loc.Get("btn_continue");
                    _txtSecondary.Text = Loc.Get("btn_cancel");
                    _btnPrimary.IsEnabled = true;
                    _btnSecondary.IsEnabled = true;
                    break;
                case Step.DownloadInstaller:
                    _txtStepTitle.Text = Loc.Get("label_local_ai_step_download");
                    _txtStepSubtitle.Text = Loc.Get("label_local_ai_step_download_sub");
                    _btnPrimary.IsVisible = false;
                    _txtSecondary.Text = Loc.Get("btn_cancel");
                    _btnSecondary.IsEnabled = true;
                    break;
                case Step.Installing:
                    _txtStepTitle.Text = Loc.Get("label_local_ai_step_install");
                    _txtStepSubtitle.Text = Loc.Get("label_local_ai_step_install_sub");
                    _btnPrimary.IsVisible = false;
                    // Don't allow cancel during silent install — Ollama's NSIS installer
                    // doesn't roll back gracefully and a half-finished install is worse
                    // than a finished one the user can uninstall.
                    _btnSecondary.IsEnabled = false;
                    break;
                case Step.PullModel:
                    _txtStepTitle.Text = Loc.Get("label_local_ai_step_pull");
                    _txtStepSubtitle.Text = Loc.Get("label_local_ai_step_pull_sub");
                    _btnPrimary.IsVisible = false;
                    _txtSecondary.Text = Loc.Get("btn_cancel");
                    _btnSecondary.IsEnabled = true;
                    break;
                case Step.SmokeTest:
                    _txtStepTitle.Text = Loc.Get("label_local_ai_step_smoke");
                    _txtStepSubtitle.Text = Loc.Get("label_local_ai_step_smoke_sub");
                    _btnPrimary.IsVisible = false;
                    _btnSecondary.IsEnabled = false;
                    break;
                case Step.Done:
                    _txtStepTitle.Text = Loc.Get("label_local_ai_step_done");
                    _txtStepSubtitle.Text = Loc.Get("label_local_ai_step_done_sub");
                    _btnPrimary.IsVisible = true;
                    _txtPrimary.Text = Loc.Get("btn_close");
                    _btnPrimary.IsEnabled = true;
                    _btnSecondary.IsVisible = false;
                    break;
                case Step.Error:
                    _txtStepTitle.Text = Loc.Get("label_local_ai_step_error");
                    _txtStepSubtitle.Text = Loc.Get("label_local_ai_step_error_sub");
                    _btnPrimary.IsVisible = true;
                    _txtPrimary.Text = Loc.Get("btn_retry");
                    _txtSecondary.Text = Loc.Get("btn_close");
                    _btnPrimary.IsEnabled = true;
                    _btnSecondary.IsEnabled = true;
                    break;
            }
        }

        // -------- Step 1: Detect --------

        private void StartDetect()
        {
            Show(Step.Detecting);
            // ponytail: needs OllamaSetupService.DetectAsync, wired when it moves to Core.
            // The WPF original lands on Consent when detection throws; the stub takes that branch.
            Show(Step.Consent);
        }

        // -------- Step 2: Consent → Download --------

        private void StartDownloadInstaller()
        {
            Show(Step.DownloadInstaller);
            SetDownloadProgressBar(0);
            _txtDownloadProgress.Text = "";
            // ponytail: needs OllamaSetupService.DownloadInstallerAsync + StartInstall/StartPull/
            // StartSmokeTest chain, wired when it moves to Core. The page shows; nothing drives it.
        }

        private void SetDownloadProgressBar(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            double max = _downloadProgressTrack.Bounds.Width - 6;
            if (max > 0) _downloadProgressFill.Width = max * percent / 100.0;
        }

        private void SetPullProgressBar(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            double max = _pullProgressTrack.Bounds.Width - 6;
            if (max > 0) _pullProgressFill.Width = max * percent / 100.0;
        }

        // -------- Step 6: Done --------

        private void Finish(TimeSpan smokeElapsed)
        {
            // ponytail: needs App.Settings (AiProvider/AiModel + Save), wired when settings move to Core
            LocalAiReady = true;
            _wizardComplete = true;

            var seconds = Math.Max(0, (int)Math.Round(smokeElapsed.TotalSeconds));
            _txtDoneDetail.Text = Loc.GetF("label_local_ai_done_detail", _targetModel, seconds);
            Show(Step.Done);
        }

        private void ShowError(string detail)
        {
            _txtErrorDetail.Text = detail;
            Show(Step.Error);
        }

        // -------- Footer button handlers --------

        private void BtnPrimary_Click()
        {
            switch (_step)
            {
                case Step.Consent:
                    if (_chkAdvanced.IsChecked == true)
                    {
                        var typed = (_txtAdvancedModel.Text ?? "").Trim();
                        if (!string.IsNullOrEmpty(typed)) _targetModel = typed;
                    }
                    StartDownloadInstaller();
                    break;
                case Step.Done:
                    Close(true);
                    break;
                case Step.Error:
                    // Retry from detect — the right next step depends on what's now true.
                    StartDetect();
                    break;
            }
        }

        private void BtnSecondary_Click()
        {
            // Cancel current step and bail out. The Done state hides this button entirely.
            Close(_wizardComplete);
        }

        private void ChkAdvanced_Changed()
        {
            _advancedPanel.IsVisible = _chkAdvanced.IsChecked == true;
        }

        private void LinkManualInstall_Click()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ManualInstallUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LocalAiSetupWizard: failed to open manual install URL");
            }
        }
    }
}
