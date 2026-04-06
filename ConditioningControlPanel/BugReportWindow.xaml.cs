using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Modal bug report dialog. Collects description + steps from the user,
    /// shows the exact outgoing payload in a read-only preview, and submits
    /// through BugReportService when the user clicks Send.
    ///
    /// Send is disabled for 2 seconds after the window opens to force the user
    /// to look at the preview before submitting.
    /// </summary>
    public partial class BugReportWindow : Window
    {
        private readonly BugReportService _service;
        private readonly DispatcherTimer _enableTimer;
        private bool _submitted;
        private bool _submitting;

        public BugReportWindow()
        {
            InitializeComponent();

            _service = App.BugReport ?? new BugReportService();

            _enableTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _enableTimer.Tick += (_, _) =>
            {
                _enableTimer.Stop();
                if (!_submitting && !_submitted)
                {
                    BtnSend.IsEnabled = true;
                }
            };

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshPreview();
            _enableTimer.Start();
            TxtDescription.Focus();
        }

        private void OnFieldChanged(object sender, RoutedEventArgs e) => RefreshPreview();

        private void RefreshPreview()
        {
            try
            {
                var draft = _service.CreateDraft(
                    TxtDescription.Text,
                    TxtSteps.Text,
                    ChkIncludeAppLog.IsChecked == true);

                // Metadata summary line
                var m = draft.Metadata;
                TxtMetadataSummary.Text =
                    $"app_version : {m.AppVersion}\n" +
                    $"os          : {m.Os}\n" +
                    $".NET        : {m.Dotnet}\n" +
                    $"language    : {m.Language}\n" +
                    $"active_mod  : {m.ActiveModId}";

                // Scrubber counts line
                TxtScrubberCounts.Text = Loc.GetF(
                    "bug_report_scrubber_count",
                    draft.Counts.Paths,
                    draft.Counts.Emails,
                    draft.Counts.Tokens,
                    draft.Counts.AppData);

                // Exact outgoing payload — no hidden fields ever.
                TxtPreview.Text = _service.RenderPreview(draft);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[BugReport] preview render failed");
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_submitting || _submitted) return;
            _submitting = true;
            BtnSend.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            TxtStatus.Text = "…";

            try
            {
                var draft = _service.CreateDraft(
                    TxtDescription.Text,
                    TxtSteps.Text,
                    ChkIncludeAppLog.IsChecked == true);

                var result = await _service.SubmitAsync(draft).ConfigureAwait(true);
                _submitted = true;

                switch (result.Outcome)
                {
                    case BugReportService.SubmitOutcome.Success:
                        MessageBox.Show(
                            this,
                            Loc.GetF("bug_report_success_toast", result.Token ?? "(no token)"),
                            Loc.Get("bug_report_title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        Close();
                        break;

                    case BugReportService.SubmitOutcome.SavedPending:
                        MessageBox.Show(
                            this,
                            Loc.GetF("bug_report_saved_pending_toast", result.Token ?? ""),
                            Loc.Get("bug_report_title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        Close();
                        break;

                    case BugReportService.SubmitOutcome.ValidationFailed:
                    case BugReportService.SubmitOutcome.NetworkError:
                    default:
                        MessageBox.Show(
                            this,
                            Loc.Get("bug_report_error_toast") + "\n\n" + (result.ErrorMessage ?? ""),
                            Loc.Get("bug_report_title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        // Allow retry
                        _submitted = false;
                        _submitting = false;
                        BtnSend.IsEnabled = true;
                        BtnCancel.IsEnabled = true;
                        TxtStatus.Text = string.Empty;
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[BugReport] submit failed");
                MessageBox.Show(
                    this,
                    Loc.Get("bug_report_error_toast") + "\n\n" + ex.Message,
                    Loc.Get("bug_report_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                _submitted = false;
                _submitting = false;
                BtnSend.IsEnabled = true;
                BtnCancel.IsEnabled = true;
                TxtStatus.Text = string.Empty;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_submitting) return;
            Close();
        }
    }
}
