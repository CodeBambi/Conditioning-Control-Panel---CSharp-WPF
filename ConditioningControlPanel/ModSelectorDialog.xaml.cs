using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Models;
using Microsoft.Win32;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// First-run dialog for selecting a mod (replaces ContentModeDialog).
    /// Shows built-in mods as prominent cards with option to install custom mods.
    /// </summary>
    public partial class ModSelectorDialog : Window
    {
        public string SelectedModId { get; private set; } = BuiltInMods.BambiSleepId;

        public ModSelectorDialog()
        {
            InitializeComponent();
        }

        private void CardBambi_Click(object sender, MouseButtonEventArgs e)
        {
            SelectedModId = BuiltInMods.BambiSleepId;
            DialogResult = true;
            Close();
        }

        private void CardSissy_Click(object sender, MouseButtonEventArgs e)
        {
            SelectedModId = BuiltInMods.SissyHypnoId;
            DialogResult = true;
            Close();
        }

        private async void InstallModLink_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Install Mod",
                Filter = "CCP Mod Files (*.ccpmod)|*.ccpmod|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true && App.Mods != null)
            {
                var result = await App.Mods.InstallModAsync(ofd.FileName);
                if (result.Success && result.ModId != null)
                {
                    SelectedModId = result.ModId;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(result.ErrorMessage ?? "Failed to install mod.", "Install Failed",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// Show mod selection dialog if user hasn't chosen yet.
        /// Sets the active mod and initializes defaults.
        /// </summary>
        public static string ShowIfNeeded()
        {
            if (!App.Settings.Current.ContentModeChosen)
            {
                var dialog = new ModSelectorDialog();
                dialog.ShowDialog();

                var modId = dialog.SelectedModId;

                // Activate the selected mod
                App.Mods?.ActivateMod(modId);
                App.Settings.Current.ActiveModId = modId;
                App.Settings.Current.ContentModeChosen = true;

                // Initialize default triggers for chosen mod
                if (App.Mods != null)
                {
                    App.Settings.Current.SubliminalPool = App.Mods.GetDefaultSubliminalPool();
                    App.Settings.Current.LockCardPhrases = App.Mods.GetDefaultLockCardPhrases();
                    App.Settings.Current.CustomTriggers = App.Mods.GetDefaultCustomTriggers();
                }

                App.Settings.Save();
                return modId;
            }
            return App.Settings.Current.ActiveModId;
        }
    }
}
