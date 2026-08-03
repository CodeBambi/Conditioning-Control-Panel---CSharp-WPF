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
        private readonly BugReportService.ReportKind _kind;
        private bool _submitted;
        private bool _submitting;

        public BugReportWindow(BugReportService.ReportKind kind = BugReportService.ReportKind.Bug)
        {
            InitializeComponent();

            _kind = kind;
            _service = App.BugReport ?? new BugReportService();

            if (_kind == BugReportService.ReportKind.Suggestion)
                ApplySuggestionMode();

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

        /// <summary>
        /// Re-skin the shared report dialog as a lightweight suggestion form: swap
        /// the wording and hide the defect-only fields (repro steps, activity-log
        /// opt-in, scrubber counts). Everything else — metadata block, preview,
        /// transport — is identical to a bug report.
        /// </summary>
        private void ApplySuggestionMode()
        {
            Title = Loc.Get("suggestion_title");
            TxtHeaderTitle.Text = Loc.Get("suggestion_title");
            TxtPrivacyNotice.Text = Loc.Get("suggestion_privacy_notice");
            LblDescription.Text = Loc.Get("suggestion_description_label");

            LblSteps.Visibility = Visibility.Collapsed;
            TxtSteps.Visibility = Visibility.Collapsed;
            ChkIncludeAppLog.Visibility = Visibility.Collapsed;
            TxtScrubberCounts.Visibility = Visibility.Collapsed;
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
                    ChkIncludeAppLog.IsChecked == true,
                    _kind);

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
                    ChkIncludeAppLog.IsChecked == true,
                    _kind);

                var result = await _service.SubmitAsync(draft).ConfigureAwait(true);
                _submitted = true;

                bool isSuggestion = _kind == BugReportService.ReportKind.Suggestion;
                string caption = Loc.Get(isSuggestion ? "suggestion_title" : "bug_report_title");

                switch (result.Outcome)
                {
                    // #769: the report number used to flash past in a MessageBox — not selectable,
                    // gone on OK. Show it in-window instead, selectable + copyable, and persisted
                    // by the service so "My Reports" can hand it back later.
                    case BugReportService.SubmitOutcome.Success:
                        ShowSuccessPanel(
                            Loc.GetF(isSuggestion ? "suggestion_success_toast" : "bug_report_success_toast",
                                     result.Token ?? "(no token)"),
                            result.Token);
                        break;

                    case BugReportService.SubmitOutcome.SavedPending:
                        ShowSuccessPanel(
                            Loc.GetF("bug_report_saved_pending_toast", result.Token ?? ""),
                            result.Token);
                        break;

                    case BugReportService.SubmitOutcome.ValidationFailed:
                    case BugReportService.SubmitOutcome.NetworkError:
                    default:
                        MessageBox.Show(
                            this,
                            Loc.Get("bug_report_error_toast") + "\n\n" + (result.ErrorMessage ?? ""),
                            caption,
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
                    Loc.Get(_kind == BugReportService.ReportKind.Suggestion ? "suggestion_title" : "bug_report_title"),
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

        /// <summary>
        /// Swap the form for the success panel (#769). The report number stays on screen in a
        /// read-only, selectable box with a Copy button until the user clicks Done, so it can be
        /// quoted in Discord. A 202 without a token still shows the headline, minus the box.
        /// </summary>
        private void ShowSuccessPanel(string headline, string? token)
        {
            TxtSuccessHeadline.Text = headline;

            bool hasToken = !string.IsNullOrWhiteSpace(token);
            TxtSuccessToken.Text = token ?? string.Empty;

            var tokenVis = hasToken ? Visibility.Visible : Visibility.Collapsed;
            LblSuccessTokenLabel.Visibility = tokenVis;
            SuccessTokenRow.Visibility = tokenVis;
            TxtSuccessHint.Visibility = tokenVis;

            SuccessPanel.Visibility = Visibility.Visible;
            BtnSuccessDone.Focus();
        }

        private void BtnCopyToken_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var token = TxtSuccessToken.Text;
                if (string.IsNullOrWhiteSpace(token)) return;
                Clipboard.SetText(token);
                BtnCopyToken.Content = Loc.Get("btn_copied");
            }
            catch (Exception ex)
            {
                // Clipboard can be locked by another process — never crash the dialog over it.
                App.Logger?.Warning(ex, "[BugReport] clipboard copy failed");
            }
        }

        private void BtnSuccessDone_Click(object sender, RoutedEventArgs e) => Close();
    }
}
