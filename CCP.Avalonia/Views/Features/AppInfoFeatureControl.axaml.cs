using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// About + the three support-form buttons, ported from the WPF head. Each support button opens
    /// its real owned modal; report submission itself is handled by the shared Core service.
    /// </summary>
    public partial class AppInfoFeatureControl : UserControl
    {
        public AppInfoFeatureControl()
        {
            InitializeComponent();
            // Both literals in WPF too (OnLoaded), not loc keys.
            this.FindControl<TextBlock>("TxtVersion")!.Text = $"v{CoreReleaseContent.AppVersion}";
            this.FindControl<TextBlock>("TxtProduct")!.Text = "Conditioning Control Panel";

            this.FindControl<Button>("BtnReportBug")!.Click += BtnReportBug_Click;
            this.FindControl<Button>("BtnSuggestion")!.Click += BtnSuggestion_Click;
            this.FindControl<Button>("BtnMyReports")!.Click += BtnMyReports_Click;
        }

        private async void BtnReportBug_Click(object? sender, RoutedEventArgs e) =>
            await OpenSupportWindowAsync(() => new BugReportWindow(ReportKind.Bug),
                "AppInfo: failed to open BugReportWindow", "Failed to open bug report.");

        private async void BtnSuggestion_Click(object? sender, RoutedEventArgs e) =>
            await OpenSupportWindowAsync(() => new BugReportWindow(ReportKind.Suggestion),
                "AppInfo: failed to open suggestion window", "Failed to open suggestion form.");

        private async void BtnMyReports_Click(object? sender, RoutedEventArgs e) =>
            await OpenSupportWindowAsync(() => new MyReportsWindow(),
                "AppInfo: failed to open MyReportsWindow", "Failed to open your reports.");

        private async Task OpenSupportWindowAsync(Func<Window> create, string logMessage, string failureMessage)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner || !owner.IsVisible) return;
            Window? dialog = null;
            try
            {
                dialog = create();
                await dialog.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Log.Error(ex, logMessage);
                if (owner.IsVisible)
                    await MessageDialog.ShowAsync(owner, dialog?.Title ?? "Support", failureMessage + "\n\n" + ex.Message);
            }
        }
    }
}
