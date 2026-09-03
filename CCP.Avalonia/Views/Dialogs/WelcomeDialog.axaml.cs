using System.Threading.Tasks;
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
    ///  - <c>App.Mods.GetAffirmation() ?? "Subject"</c> becomes <see cref="CoreMods.Affirmation"/>,
    ///    which carries the same fallback.
    ///  - <see cref="ShowIfNeeded"/> takes an owner and is async: Avalonia's <c>ShowDialog</c> needs
    ///    a non-null owner Window and returns a Task, so the parameterless synchronous WPF static
    ///    cannot be ported one for one.
    /// </summary>
    public partial class WelcomeDialog : Window
    {
        public WelcomeDialog()
        {
            AvaloniaXamlLoader.Load(this);

            this.FindControl<TextBlock>("TxtWelcomeHeading")!.Text =
                Loc.GetF("label_welcome", CoreMods.Affirmation);

            this.FindControl<Button>("BtnBegin")!.Click += (_, _) => Close(true);
        }

        /// <summary>
        /// Show welcome dialog if user hasn't been welcomed yet.
        /// </summary>
        /// <returns>True if welcome was shown (first launch), false otherwise</returns>
        public static async Task<bool> ShowIfNeeded(Window owner)
        {
            if (CoreSettings.Current.Welcomed) return false;

            await new WelcomeDialog().ShowDialog(owner);

            // WPF latched Welcomed whatever the dialog returned — dismissing it still counts as
            // having seen it, otherwise the dialog reappears on every launch.
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Save();
            return true;
        }
    }
}
