using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// About + the three support-form buttons, ported from the WPF head.
    ///
    /// The three Click handlers each open a head-only dialog (BugReportWindow, MyReportsWindow)
    /// and are stubs here; the buttons render and do nothing. The version string comes from
    /// UpdateService.AppVersion in the WPF head, a const that has not moved to Core.
    /// </summary>
    public partial class AppInfoFeatureControl : UserControl
    {
        public AppInfoFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);
            // Both literals in WPF too (OnLoaded), not loc keys.
            // ponytail: needs UpdateService.AppVersion, wired when it moves to Core
            this.FindControl<TextBlock>("TxtVersion")!.Text = "v0.0.0";
            this.FindControl<TextBlock>("TxtProduct")!.Text = "Conditioning Control Panel";

            // ponytail: needs BugReportWindow / MyReportsWindow, wired when those dialogs are ported
            this.FindControl<Button>("BtnReportBug")!.Click += (_, _) => { };
            this.FindControl<Button>("BtnSuggestion")!.Click += (_, _) => { };
            this.FindControl<Button>("BtnMyReports")!.Click += (_, _) => { };
        }
    }
}
