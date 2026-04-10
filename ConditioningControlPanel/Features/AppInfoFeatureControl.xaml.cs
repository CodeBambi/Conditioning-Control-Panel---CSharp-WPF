using System;
using System.Reflection;
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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TxtVersion.Text = $"v{UpdateService.AppVersion}";
            TxtProduct.Text = "Conditioning Control Panel";
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
                await App.CheckForUpdatesManuallyAsync(owner);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AppInfo: check updates failed");
                MessageBox.Show(
                    $"Failed to check for updates: {ex.Message}",
                    "Update Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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
    }
}
