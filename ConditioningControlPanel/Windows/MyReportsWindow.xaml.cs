using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// #769: small read-only list of the report numbers (BUG-XXXXXXXXXX) this user has been
    /// given for bug reports and suggestions, newest first, each with a Copy button. The
    /// numbers are persisted by BugReportService into AppSettings.RecentBugReports so a user
    /// can quote one in Discord long after the success dialog is gone.
    /// </summary>
    public partial class MyReportsWindow : Window
    {
        /// <summary>Row view-model — pre-formatted so the DataTemplate stays binding-only.</summary>
        public class Row
        {
            public string Token { get; set; } = string.Empty;
            public string SubtitleText { get; set; } = string.Empty;
        }

        public MyReportsWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadRows();
        }

        private void LoadRows()
        {
            try
            {
                var stored = App.Settings?.Current?.RecentBugReports;
                var parsed = BugReportService.ParseRecentReports(stored); // newest first

                var rows = new List<Row>();
                foreach (var r in parsed)
                {
                    var kindText = Loc.Get(r.Kind == BugReportService.ReportKind.Suggestion
                        ? "my_reports_kind_suggestion"
                        : "my_reports_kind_bug");

                    // Stamps are stored in UTC; show them in the user's local time.
                    var dateText = r.TimestampUtc.HasValue
                        ? r.TimestampUtc.Value.ToLocalTime().ToString("g")
                        : string.Empty;

                    rows.Add(new Row
                    {
                        Token = r.Token,
                        SubtitleText = string.IsNullOrEmpty(dateText) ? kindText : $"{dateText}  •  {kindText}",
                    });
                }

                ReportsList.ItemsSource = rows;
                TxtEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[BugReport] My Reports list failed to load");
                ReportsList.ItemsSource = null;
                TxtEmpty.Visibility = Visibility.Visible;
            }
        }

        private void BtnCopyRow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn) return;
                var token = btn.Tag as string;
                if (string.IsNullOrWhiteSpace(token)) return;
                Clipboard.SetText(token);
                btn.Content = Loc.Get("btn_copied");
            }
            catch (Exception ex)
            {
                // Clipboard can be locked by another process — never crash the dialog over it.
                App.Logger?.Warning(ex, "[BugReport] clipboard copy failed");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
