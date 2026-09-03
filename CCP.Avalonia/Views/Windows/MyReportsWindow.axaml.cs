using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// #769: small read-only list of the report numbers (BUG-XXXXXXXXXX) this user has been given
    /// for bug reports and suggestions, newest first, each with a Copy button.
    ///
    /// PORTED from ConditioningControlPanel/Windows/MyReportsWindow.xaml.cs. Deviations:
    ///  - The rows come from AppSettings.RecentBugReports via BugReportService.ParseRecentReports,
    ///    both still in the WPF head, so LoadRows has no rows and the empty state shows.
    ///  - The per-row Copy click is one handler on the ItemsControl; the row Button carries the
    ///    token in Tag exactly as before.
    /// </summary>
    public partial class MyReportsWindow : Window
    {
        /// <summary>Row view-model — pre-formatted so the DataTemplate stays binding-only.</summary>
        public class Row
        {
            public string Token { get; set; } = string.Empty;
            public string SubtitleText { get; set; } = string.Empty;
        }

        private readonly ItemsControl _reportsList;
        private readonly TextBlock _txtEmpty;

        public MyReportsWindow()
        {
            AvaloniaXamlLoader.Load(this);
            _reportsList = this.FindControl<ItemsControl>("ReportsList")!;
            _txtEmpty = this.FindControl<TextBlock>("TxtEmpty")!;

            _reportsList.AddHandler(Button.ClickEvent, BtnCopyRow_Click);
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            LoadRows();
        }

        private void LoadRows()
        {
            // ponytail: needs AppSettings.RecentBugReports + BugReportService.ParseRecentReports,
            // wired when they move to Core. No source means no rows, so the empty state shows;
            // the row subtitle is "{local date}  •  {kind}" as in the WPF code-behind.
            var rows = new List<Row>();

            _reportsList.ItemsSource = rows;
            _txtEmpty.IsVisible = rows.Count == 0;
        }

        private async void BtnCopyRow_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (e.Source is not Button btn || btn.Tag is not string token) return;
                if (string.IsNullOrWhiteSpace(token) || Clipboard is null) return;
                await Clipboard.SetTextAsync(token);
                if (btn.Content is TextBlock label) label.Text = Loc.Get("btn_copied");
            }
            catch
            {
                // Clipboard can be locked by another process — never crash the dialog over it.
            }
        }
    }
}
