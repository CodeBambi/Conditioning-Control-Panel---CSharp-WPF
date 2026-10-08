using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Onboarding wizard for the local-AI (Ollama) provider. Drives users through
    /// detect → consent → install Ollama → pull model → smoke test → done.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/LocalAiSetupWizard.xaml.cs. The page switching,
    /// footer wording, advanced-model toggle and progress-bar maths are the original's, and the two
    /// settings touches are wired through <see cref="CoreSettings"/>: the wizard opens on whatever
    /// model <c>CompanionPrompt.AiModel</c> already names, and finishing flips the provider to Local
    /// and saves. Every step drives OllamaSetupService (moved to Core unchanged) exactly as WPF does;
    /// off Windows there is no installer download - the user installs Ollama and Continue re-detects
    /// (see docs/avalonia-decisions.md). WPF's DialogResult becomes Close(bool).
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
            foreach (var link in new[] { C<TextBlock>("LinkManualInstall"), C<TextBlock>("LinkManualInstallError") })
            {
                // WPF Hyperlink: click and keyboard (Tab + Enter/Space) both open the page.
                link.PointerPressed += (_, _) => LinkManualInstall_Click();
                link.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; LinkManualInstall_Click(); } };
            }
            if (!CanAutoInstall)
            {
                // WPF's lines name a Windows installer, the tray and Add/Remove Programs; say what is true here.
                C<TextBlock>("TxtConsentLine1").Text = Loc.Get("label_local_ai_consent_install_ollama_manual");
                C<TextBlock>("TxtConsentLine3").Text = Loc.Get("label_local_ai_consent_runs_local_linux");
                C<TextBlock>("TxtConsentDiskNote").Text = Loc.Get("label_local_ai_consent_disk_note_linux");
            }

            _targetModel = ResolveStartingModel();
            _txtAdvancedModel.Text = _targetModel;
            UpdateConsentDiskNote();

            Opened += async (_, _) => await StartDetectAsync();
        }

        private static string ResolveStartingModel()
        {
            var saved = CoreSettings.Current?.CompanionPrompt?.AiModel;
            return string.IsNullOrWhiteSpace(saved) ? DefaultModel : saved!.Trim();
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

        // Test seams over OllamaSetupService (Core): tests swap these for fakes so no real ollama,
        // pull or network is ever touched. Production is exactly the WPF calls.
        internal static Func<string, CancellationToken, Task<OllamaSetupService.StatusSnapshot>> Detect =
            (model, ct) => OllamaSetupService.DetectAsync(targetModel: model, ct: ct);
        internal static Func<CancellationToken, Task<bool>> StartService = ct => OllamaSetupService.StartServiceAsync(ct: ct);
        internal static Func<IProgress<OllamaSetupService.DownloadProgress>, CancellationToken, Task<string>> DownloadInstaller =
            (p, ct) => OllamaSetupService.DownloadInstallerAsync(p, ct);
        internal static Func<string, CancellationToken, Task<bool>> RunInstaller =
            (path, ct) => OllamaSetupService.RunInstallerSilentAsync(path, ct: ct);
        internal static Func<string, IProgress<OllamaSetupService.PullProgress>, CancellationToken, Task> PullModel =
            (model, p, ct) => OllamaSetupService.PullModelAsync(model, progress: p, ct: ct);
        internal static Func<string, CancellationToken, Task<(bool ok, TimeSpan elapsed, string reply)>> SmokeTest =
            (model, ct) => OllamaSetupService.SmokeTestAsync(model, ct: ct);
        /// <summary>Decision (docs/avalonia-decisions.md): off Windows the wizard never downloads or runs an
        /// installer; the user installs Ollama, Continue re-runs detection.</summary>
        internal static bool CanAutoInstall = OperatingSystem.IsWindows();

        private CancellationTokenSource? _cts;

        /// <param name="afterManualInstall">Linux Continue: still nothing found is an error, not a silent return to Consent.</param>
        private async Task StartDetectAsync(bool afterManualInstall = false)
        {
            Show(Step.Detecting);
            _cts = new CancellationTokenSource();
            try
            {
                var snap = await Detect(_targetModel, _cts.Token);

                switch (snap.Status)
                {
                    case OllamaSetupService.InstallStatus.Ready:
                        await StartSmokeTestAsync();
                        return;

                    case OllamaSetupService.InstallStatus.RunningNoModel:
                        await StartPullAsync();
                        return;

                    case OllamaSetupService.InstallStatus.InstalledNotRunning:
                        var started = await StartService(_cts.Token);
                        if (!started)
                        {
                            ShowError(Loc.Get(CanAutoInstall ? "error_local_ai_start_service_failed" : "error_local_ai_start_service_failed_linux"));
                            return;
                        }
                        var snap2 = await Detect(_targetModel, _cts.Token);
                        if (snap2.TargetModelInstalled) await StartSmokeTestAsync();
                        else await StartPullAsync();
                        return;

                    case OllamaSetupService.InstallStatus.NotInstalled:
                    default:
                        if (afterManualInstall) ShowError(Loc.Get("error_local_ai_not_found_linux"));
                        else Show(Step.Consent);
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LocalAiSetupWizard: detect failed");
                Show(Step.Consent);
            }
        }

        // -------- Step 2: Consent → Download --------

        private async Task StartDownloadInstallerAsync()
        {
            Show(Step.DownloadInstaller);
            SetDownloadProgressBar(0);
            _txtDownloadProgress.Text = "";
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var progress = new Progress<OllamaSetupService.DownloadProgress>(p =>
            {
                if (p.PercentComplete.HasValue) SetDownloadProgressBar(p.PercentComplete.Value);

                var rate = OllamaSetupService.FormatRate(p.BytesPerSecond);
                var bytes = OllamaSetupService.FormatBytes(p.BytesReceived);
                if (p.TotalBytes.HasValue)
                {
                    var total = OllamaSetupService.FormatBytes(p.TotalBytes.Value);
                    var pct = p.PercentComplete.HasValue ? $" ({p.PercentComplete.Value:0}%)" : "";
                    _txtDownloadProgress.Text = string.IsNullOrEmpty(rate)
                        ? $"{bytes} / {total}{pct}"
                        : $"{bytes} / {total}{pct} • {rate}";
                }
                else
                {
                    _txtDownloadProgress.Text = string.IsNullOrEmpty(rate) ? bytes : $"{bytes} • {rate}";
                }
            });

            try
            {
                var path = await DownloadInstaller(progress, _cts.Token);
                await StartInstallAsync(path);
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LocalAiSetupWizard: installer download failed");
                ShowError(Loc.GetF("error_local_ai_download_failed", ex.Message));
            }
        }

        private void SetDownloadProgressBar(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            double max = _downloadProgressTrack.Bounds.Width - 6;
            if (max > 0) _downloadProgressFill.Width = max * percent / 100.0;
        }

        // -------- Step 3: Install --------

        private async Task StartInstallAsync(string installerPath)
        {
            Show(Step.Installing);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var ok = await RunInstaller(installerPath, _cts.Token);
                if (!ok)
                {
                    // Leave the installer in %TEMP% on failure so a re-run can retry without a fresh download.
                    ShowError(Loc.Get("error_local_ai_install_failed"));
                    return;
                }
                try { if (File.Exists(installerPath)) File.Delete(installerPath); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to delete OllamaSetup.exe after install"); }
                await StartPullAsync();
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LocalAiSetupWizard: silent install failed");
                ShowError(Loc.GetF("error_local_ai_install_failed_detail", ex.Message));
            }
        }

        // -------- Step 4: Pull model --------

        private async Task StartPullAsync()
        {
            Show(Step.PullModel);
            _txtPullHeader.Text = Loc.GetF("label_local_ai_pulling_model_named", _targetModel);
            _txtPullStatus.Text = "";
            _txtPullDetail.Text = "";
            SetPullProgressBar(0);

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var progress = new Progress<OllamaSetupService.PullProgress>(p =>
            {
                _txtPullStatus.Text = p.Status;
                if (p.PercentComplete.HasValue)
                {
                    SetPullProgressBar(p.PercentComplete.Value);
                    var bytes = p.Completed.HasValue ? OllamaSetupService.FormatBytes(p.Completed.Value) : "";
                    var total = p.Total.HasValue ? OllamaSetupService.FormatBytes(p.Total.Value) : "";
                    _txtPullDetail.Text = string.IsNullOrEmpty(bytes)
                        ? $"{p.PercentComplete.Value:0}%"
                        : $"{bytes} / {total} ({p.PercentComplete.Value:0}%)";
                }
                else
                {
                    _txtPullDetail.Text = "";
                }
            });

            try
            {
                await PullModel(_targetModel, progress, _cts.Token);
                SetPullProgressBar(100);
                await StartSmokeTestAsync();
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LocalAiSetupWizard: pull failed (model={Model})", _targetModel);
                ShowError(Loc.GetF("error_local_ai_pull_failed", _targetModel, ex.Message));
            }
        }

        private void SetPullProgressBar(double percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            double max = _pullProgressTrack.Bounds.Width - 6;
            if (max > 0) _pullProgressFill.Width = max * percent / 100.0;
        }

        // -------- Step 5: Smoke test --------

        private async Task StartSmokeTestAsync()
        {
            Show(Step.SmokeTest);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var (ok, elapsed, _) = await SmokeTest(_targetModel, _cts.Token);
                if (!ok)
                {
                    ShowError(Loc.Get("error_local_ai_smoke_failed"));
                    return;
                }
                Finish(elapsed);
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LocalAiSetupWizard: smoke test threw");
                ShowError(Loc.GetF("error_local_ai_smoke_threw", ex.Message));
            }
        }

        // -------- Step 6: Done --------

        private void Finish(TimeSpan smokeElapsed)
        {
            try
            {
                var prompt = CoreSettings.Current?.CompanionPrompt;
                if (prompt != null)
                {
                    prompt.AiProvider = AiProviderType.Local;
                    prompt.AiModel = _targetModel;
                    CoreSettings.Save();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LocalAiSetupWizard: failed to save settings on Finish");
            }

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

        private async void BtnPrimary_Click()
        {
            switch (_step)
            {
                case Step.Consent:
                    if (_chkAdvanced.IsChecked == true)
                    {
                        var typed = (_txtAdvancedModel.Text ?? "").Trim();
                        if (!string.IsNullOrEmpty(typed)) _targetModel = typed;
                    }
                    // Off Windows Continue re-runs detection: the user installed Ollama themselves.
                    if (CanAutoInstall) await StartDownloadInstallerAsync();
                    else await StartDetectAsync(afterManualInstall: true);
                    break;
                case Step.Done:
                    Close(true);
                    break;
                case Step.Error:
                    // Retry from detect — the right next step depends on what's now true.
                    await StartDetectAsync();
                    break;
            }
        }

        private void BtnSecondary_Click()
        {
            // Cancel current step and bail out. The Done state hides this button entirely.
            _cts?.Cancel();
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
                Platform.ExternalOpener.Open(ManualInstallUrl);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LocalAiSetupWizard: failed to open manual install URL");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}
