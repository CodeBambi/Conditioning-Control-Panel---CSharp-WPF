using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// #769: small read-only list of the report numbers (BUG-XXXXXXXXXX) this user has been given
    /// for bug reports and suggestions, newest first, each with a Copy button.
    ///
    /// PORTED from ConditioningControlPanel/Windows/MyReportsWindow.xaml.cs. Deviations:
    ///  - The parser is <c>RecentReports.Parse</c> in Core; the WPF original reached it through
    ///    <c>BugReportService.ParseRecentReports</c>, which now forwards to the same code.
    ///  - The per-row Copy click is one handler on the ItemsControl; the row Button carries the
    ///    token in Tag exactly as before.
    ///  - The Copy label is swapped by rebinding, not by assigning Text: the TextBlock carries a
    ///    <c>{loc:Str}</c> binding and a local value would be undone on the next language change
    ///    (CLAUDE.md, "setting text from code").
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
            var rows = new List<Row>();
            try
            {
                foreach (var r in RecentReports.Parse(CoreSettings.Current.RecentBugReports))
                {
                    var kindText = Loc.Get(r.Kind == ReportKind.Suggestion
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
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BugReport] My Reports list failed to load");
                rows.Clear();
            }

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
                if (btn.Content is TextBlock label) SetLocKey(label, "btn_copied");
            }
            catch (Exception ex)
            {
                // Clipboard can be locked by another process — never crash the dialog over it.
                Log.Warning(ex, "[BugReport] clipboard copy failed");
            }
        }

        /// <summary>
        /// Rebinds a label to another loc key. Assigning .Text instead would sit under the
        /// {loc:Str} binding the DataTemplate installed and be undone on the next language change.
        /// </summary>
        private static void SetLocKey(TextBlock label, string key) =>
            label.Bind(TextBlock.TextProperty, new Binding($"[{key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
            });
    }
}
