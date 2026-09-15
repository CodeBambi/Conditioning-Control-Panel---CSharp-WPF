using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Modal bug report dialog. Collects description + steps from the user, shows the exact
    /// outgoing payload in a read-only preview, and submits when the user clicks Send.
    /// Send is disabled for 2 seconds after the window opens to force the user to look at the
    /// preview before submitting.
    /// </summary>
    public partial class BugReportWindow : Window
    {
        private readonly BugReportService _service;
        private readonly DispatcherTimer _enableTimer;
        private readonly ReportKind _kind;

        private readonly TextBox _txtDescription, _txtSteps, _txtPreview, _txtSuccessToken;
        private readonly TextBlock _txtMetadataSummary, _txtScrubberCounts, _txtStatus, _txtSuccessHeadline,
            _lblSuccessTokenLabel, _txtSuccessHint, _txtCopyToken;
        private readonly CheckBox _chkIncludeAppLog;
        private readonly Button _btnSend, _btnCancel, _btnSuccessDone;
        private readonly Border _successPanel;
        private readonly Grid _successTokenRow;

        private BugReportService.BugReportDraft? _draft;
        private bool _submitted;
        private bool _submitting;
        private bool _closed;

        public BugReportWindow() : this(ReportKind.Bug, null) { }

        public BugReportWindow(ReportKind kind) : this(kind, null) { }

        /// <summary>
        /// The service overload is also used by offline UI tests; production callers use the
        /// parameterless overload and therefore retain the real service and endpoint.
        /// </summary>
        public BugReportWindow(ReportKind kind, BugReportService? service)
        {
            InitializeComponent();
            _kind = kind;
            _service = service ?? new BugReportService();

            _txtDescription = this.FindControl<TextBox>("TxtDescription")!;
            _txtSteps = this.FindControl<TextBox>("TxtSteps")!;
            _txtPreview = this.FindControl<TextBox>("TxtPreview")!;
            _txtSuccessToken = this.FindControl<TextBox>("TxtSuccessToken")!;
            _txtMetadataSummary = this.FindControl<TextBlock>("TxtMetadataSummary")!;
            _txtScrubberCounts = this.FindControl<TextBlock>("TxtScrubberCounts")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _txtSuccessHeadline = this.FindControl<TextBlock>("TxtSuccessHeadline")!;
            _lblSuccessTokenLabel = this.FindControl<TextBlock>("LblSuccessTokenLabel")!;
            _txtSuccessHint = this.FindControl<TextBlock>("TxtSuccessHint")!;
            _txtCopyToken = this.FindControl<TextBlock>("TxtCopyToken")!;
            _chkIncludeAppLog = this.FindControl<CheckBox>("ChkIncludeAppLog")!;
            _btnSend = this.FindControl<Button>("BtnSend")!;
            _btnCancel = this.FindControl<Button>("BtnCancel")!;
            _btnSuccessDone = this.FindControl<Button>("BtnSuccessDone")!;
            _successPanel = this.FindControl<Border>("SuccessPanel")!;
            _successTokenRow = this.FindControl<Grid>("SuccessTokenRow")!;

            ApplyKind();

            _enableTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _enableTimer.Tick += EnableTimer_Tick;
            _txtDescription.TextChanged += OnFieldChanged;
            _txtSteps.TextChanged += OnFieldChanged;
            _chkIncludeAppLog.IsCheckedChanged += OnFieldChanged;
            _btnSend.Click += BtnSend_Click;
            _btnCancel.Click += BtnCancel_Click;
            this.FindControl<Button>("BtnCopyToken")!.Click += BtnCopyToken_Click;
            _btnSuccessDone.Click += BtnSuccessDone_Click;
            Opened += OnOpened;

            // Fill the preview before Show() as well as on Opened so --render-view draws the real
            // payload. Submit uses this same displayed draft: diagnostics cannot change invisibly
            // between consent in the preview and the request.
            RefreshPreview();
        }

        /// <summary>
        /// Word the dialog for its kind. These controls carry no localization binding because a
        /// local Text set does not clear an Avalonia binding; a later language change must not turn
        /// a suggestion form back into bug wording.
        /// </summary>
        private void ApplyKind()
        {
            var suggestion = _kind == ReportKind.Suggestion;
            Title = Loc.Get(suggestion ? "suggestion_title" : "bug_report_title");
            this.FindControl<TextBlock>("TxtHeaderTitle")!.Text = Title;
            this.FindControl<TextBlock>("TxtPrivacyNotice")!.Text = Loc.Get(suggestion
                ? "suggestion_privacy_notice" : "bug_report_privacy_notice");
            this.FindControl<TextBlock>("LblDescription")!.Text = Loc.Get(suggestion
                ? "suggestion_description_label" : "bug_report_description_label");

            this.FindControl<TextBlock>("LblSteps")!.IsVisible = !suggestion;
            _txtSteps.IsVisible = !suggestion;
            _chkIncludeAppLog.IsVisible = !suggestion;
            _txtScrubberCounts.IsVisible = !suggestion;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            if (_closed) return;
            _enableTimer.Start();
            _txtDescription.Focus();
        }

        private void EnableTimer_Tick(object? sender, EventArgs e)
        {
            _enableTimer.Stop();
            if (!_closed && !_submitting && !_submitted && _draft is not null)
                _btnSend.IsEnabled = true;
        }

        private void OnFieldChanged(object? sender, EventArgs e) => RefreshPreview();

        private void RefreshPreview()
        {
            if (_closed) return;
            _draft = null;
            try
            {
                var draft = _service.CreateDraft(
                    _txtDescription.Text,
                    _txtSteps.Text,
                    _chkIncludeAppLog.IsChecked == true,
                    _kind);
                var preview = _service.RenderPreview(draft);

                // Publish the draft only with the preview that represents it. This is deliberately
                // a cached snapshot rather than a second CreateDraft in BtnSend_Click.
                _draft = draft;
                var m = draft.Metadata;
                _txtMetadataSummary.Text =
                    $"app_version : {m.AppVersion}\n" +
                    $"os          : {m.Os}\n" +
                    $".NET        : {m.Dotnet}\n" +
                    $"language    : {m.Language}\n" +
                    $"active_mod  : {m.ActiveModId}";
                _txtScrubberCounts.Text = Loc.GetF(
                    "bug_report_scrubber_count",
                    draft.Counts.Paths,
                    draft.Counts.Emails,
                    draft.Counts.Tokens,
                    draft.Counts.AppData);
                _txtPreview.Text = preview;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BugReport] preview render failed");
                _txtStatus.Text = Loc.Get("bug_report_error_toast");
            }
        }

        private async void BtnSend_Click(object? sender, RoutedEventArgs e)
        {
            if (_closed || _submitting || _submitted || !_btnSend.IsEnabled || _draft is null) return;

            _submitting = true;
            _btnSend.IsEnabled = false;
            _btnCancel.IsEnabled = false;
            _txtDescription.IsEnabled = false;
            _txtSteps.IsEnabled = false;
            _chkIncludeAppLog.IsEnabled = false;
            _txtStatus.Text = "…";

            // Keep the exact snapshot shown in the preview while the request is in flight. The
            // input controls are disabled too, so a visible edit cannot diverge from the payload.
            var draft = _draft;
            try
            {
                var result = await _service.SubmitAsync(draft);
                if (_closed) return;

                _submitted = result.Outcome is BugReportService.SubmitOutcome.Success
                    or BugReportService.SubmitOutcome.SavedPending;
                switch (result.Outcome)
                {
                    case BugReportService.SubmitOutcome.Success:
                        ShowSuccessPanel(
                            Loc.GetF(_kind == ReportKind.Suggestion
                                ? "suggestion_success_toast" : "bug_report_success_toast",
                                result.Token ?? "(no token)"),
                            result.Token);
                        break;

                    case BugReportService.SubmitOutcome.SavedPending:
                        var headline = string.IsNullOrWhiteSpace(result.Token)
                            ? BugReportService.TidyEmptyTokenPlaceholder(
                                Loc.GetF("bug_report_saved_pending_toast", string.Empty))
                            : Loc.GetF("bug_report_saved_pending_toast", result.Token);
                        ShowSuccessPanel(headline, result.Token);
                        break;

                    case BugReportService.SubmitOutcome.ValidationFailed:
                    case BugReportService.SubmitOutcome.NetworkError:
                    default:
                        await ShowErrorAndResetAsync(result.ErrorMessage);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BugReport] submit failed");
                if (!_closed) await ShowErrorAndResetAsync(ex.Message);
            }
        }

        private async Task ShowErrorAndResetAsync(string? detail)
        {
            if (_closed) return;
            var caption = Loc.Get(_kind == ReportKind.Suggestion ? "suggestion_title" : "bug_report_title");
            var message = Loc.Get("bug_report_error_toast") +
                (string.IsNullOrWhiteSpace(detail) ? string.Empty : "\n\n" + detail);
            try
            {
                await Views.Dialogs.MessageDialog.ShowAsync(this, caption, message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BugReport] error dialog failed");
            }

            if (_closed) return;
            _submitting = false;
            _submitted = false;
            _btnSend.IsEnabled = true;
            _btnCancel.IsEnabled = true;
            _txtDescription.IsEnabled = true;
            _txtSteps.IsEnabled = true;
            _chkIncludeAppLog.IsEnabled = true;
            _txtStatus.Text = string.Empty;
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            if (_closed || _submitting) return;
            Close();
        }

        /// <summary>
        /// Swap the form for the success panel (#769). The report number stays on screen in a
        /// read-only, selectable box with a Copy button until the user clicks Done. A 202 without
        /// a token still shows the headline, minus the box.
        /// </summary>
        private void ShowSuccessPanel(string headline, string? token)
        {
            if (_closed) return;
            _txtSuccessHeadline.Text = headline;

            var hasToken = !string.IsNullOrWhiteSpace(token);
            _txtSuccessToken.Text = token ?? string.Empty;
            _lblSuccessTokenLabel.IsVisible = hasToken;
            _successTokenRow.IsVisible = hasToken;
            _txtSuccessHint.IsVisible = hasToken;
            _successPanel.IsVisible = true;
            _btnSuccessDone.Focus();
        }

        private async void BtnCopyToken_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_closed || string.IsNullOrWhiteSpace(_txtSuccessToken.Text) || Clipboard is null) return;
                await Clipboard.SetTextAsync(_txtSuccessToken.Text);
                if (!_closed) _txtCopyToken.Text = Loc.Get("btn_copied");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BugReport] clipboard copy failed");
            }
        }

        private void BtnSuccessDone_Click(object? sender, RoutedEventArgs e)
        {
            if (!_closed) Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _enableTimer.Stop();
            _draft = null;
            base.OnClosed(e);
        }
    }
}
