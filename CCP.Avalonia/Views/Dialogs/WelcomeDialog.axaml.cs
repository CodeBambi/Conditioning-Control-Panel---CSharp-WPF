using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Welcome dialog shown on first launch.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/WelcomeDialog.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> becomes <c>Close(true)</c>.
    ///  - <c>App.Mods.GetAffirmation()</c> and <c>App.Settings</c> are head services; both are
    ///    ponytail stubs below.
    /// </summary>
    public partial class WelcomeDialog : Window
    {
        public WelcomeDialog()
        {
            AvaloniaXamlLoader.Load(this);

            // ponytail: needs App.Mods.GetAffirmation(), wired when it moves to Core
            this.FindControl<TextBlock>("TxtWelcomeHeading")!.Text = Loc.GetF("label_welcome", "Subject");

            this.FindControl<Button>("BtnBegin")!.Click += (_, _) => Close(true);
        }

        /// <summary>
        /// Show welcome dialog if user hasn't been welcomed yet.
        /// </summary>
        /// <returns>True if welcome was shown (first launch), false otherwise</returns>
        public static bool ShowIfNeeded()
        {
            // ponytail: needs App.Settings (Welcomed flag + Save), wired when it moves to Core.
            // Until then this never fires, so first launch shows nothing rather than showing it
            // on every launch.
            return false;
        }
    }
}
