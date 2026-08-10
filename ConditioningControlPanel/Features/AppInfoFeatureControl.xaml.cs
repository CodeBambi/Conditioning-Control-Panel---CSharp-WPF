using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Features
{
    public partial class AppInfoFeatureControl : UserControl
    {
        public AppInfoFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // Phase 2: AccountSectionsHost / ExternalSectionsHost and BtnCheckUpdates are gone.
        // The account cards are a real page now (Settings · Account) instead of Borders borrowed
        // out of PatreonTab at popup-open time, and the update check lives in Settings · Updates
        // beside the patch notes. What remains here is About + the three support forms.

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TxtVersion.Text = $"v{UpdateService.AppVersion}";
            TxtProduct.Text = "Conditioning Control Panel";
        }

        private void BtnReportBug_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new BugReportWindow
                {
                    Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "AppInfo: failed to open BugReportWindow");
                MessageBox.Show(
                    "Failed to open bug report.\n\n" + ex.Message,
                    "Bug Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnSuggestion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new BugReportWindow(BugReportService.ReportKind.Suggestion)
                {
                    Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "AppInfo: failed to open suggestion window");
                MessageBox.Show(
                    "Failed to open suggestion form.\n\n" + ex.Message,
                    "Suggestion",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>#769: list the report numbers this user has been given, newest first.</summary>
        private void BtnMyReports_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new MyReportsWindow
                {
                    Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "AppInfo: failed to open MyReportsWindow");
                MessageBox.Show(
                    "Failed to open your reports.\n\n" + ex.Message,
                    "My Reports",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
