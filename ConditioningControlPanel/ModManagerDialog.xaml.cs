using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Microsoft.Win32;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Mod browser/manager dialog — list, details, install/uninstall/activate.
    /// </summary>
    public partial class ModManagerDialog : Window
    {
        /// <summary>
        /// True if the user activated a different mod during this session (caller should refresh UI).
        /// </summary>
        public bool ModWasChanged { get; private set; }

        private ModPackage? _selectedMod;

        public ModManagerDialog()
        {
            InitializeComponent();
            RefreshModList();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshModList()
        {
            ModList.Items.Clear();
            if (App.Mods == null) return;

            foreach (var mod in App.Mods.InstalledMods.Values.OrderBy(m => !m.IsBuiltIn).ThenBy(m => m.Name))
            {
                var prefix = mod.Id == App.Mods.ActiveModId ? "\u2605 " : "  "; // star for active
                var item = new ListBoxItem
                {
                    Content = prefix + mod.Name,
                    Tag = mod.Id,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                ModList.Items.Add(item);

                // Auto-select active mod
                if (mod.Id == App.Mods.ActiveModId)
                    ModList.SelectedItem = item;
            }
        }

        private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModList.SelectedItem is ListBoxItem item && item.Tag is string modId)
            {
                if (App.Mods?.InstalledMods.TryGetValue(modId, out var mod) == true)
                {
                    ShowModDetails(mod);
                    return;
                }
            }
            DetailsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowModDetails(ModPackage mod)
        {
            _selectedMod = mod;
            DetailsPanel.Visibility = Visibility.Visible;

            TxtModName.Text = mod.Name;
            TxtModAuthor.Text = $"by {mod.Manifest.Author}";
            TxtModVersion.Text = $"v{mod.Manifest.Version}";
            TxtModDescription.Text = mod.Manifest.Description ?? "";

            // Theme color
            var colorHex = mod.Manifest.Theme?.AccentColor ?? "#FF69B4";
            TxtThemeColor.Text = colorHex;
            try
            {
                ThemeColorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            }
            catch
            {
                ThemeColorPreview.Background = new SolidColorBrush(Colors.HotPink);
            }

            // Companion
            TxtCompanion.Text = mod.Manifest.Identity?.CompanionName ?? "BambiSprite";

            // Active state
            var isActive = mod.Id == App.Mods?.ActiveModId;
            TxtActiveIndicator.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            BtnActivate.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;

            // Can't uninstall built-in mods or active mod
            BtnUninstall.Visibility = (!mod.IsBuiltIn && !isActive) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMod == null || App.Mods == null) return;

            App.Mods.ActivateMod(_selectedMod.Id);
            App.Settings.Current.ActiveModId = _selectedMod.Id;
            App.Settings.Save();

            ModWasChanged = true;
            RefreshModList();

            // Re-show details for the newly active mod
            ShowModDetails(_selectedMod);
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMod == null || App.Mods == null) return;
            if (_selectedMod.IsBuiltIn) return;

            var result = MessageBox.Show(
                $"Uninstall \"{_selectedMod.Name}\"? This cannot be undone.",
                "Confirm Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var wasActive = _selectedMod.Id == App.Mods.ActiveModId;
                App.Mods.UninstallMod(_selectedMod.Id);

                if (wasActive)
                {
                    App.Settings.Current.ActiveModId = App.Mods.ActiveModId;
                    App.Settings.Save();
                    ModWasChanged = true;
                }

                _selectedMod = null;
                DetailsPanel.Visibility = Visibility.Collapsed;
                RefreshModList();
            }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Install Mod",
                Filter = "CCP Mod Files (*.ccpmod)|*.ccpmod|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true && App.Mods != null)
            {
                BtnInstall.IsEnabled = false;
                try
                {
                    var installResult = await App.Mods.InstallModAsync(ofd.FileName);
                    if (installResult.Success)
                    {
                        RefreshModList();
                        MessageBox.Show($"Mod installed successfully!", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(installResult.ErrorMessage ?? "Failed to install mod.", "Install Failed",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally
                {
                    BtnInstall.IsEnabled = true;
                }
            }
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (App.Mods == null) return;

            var sfd = new SaveFileDialog
            {
                Title = "Export Current Configuration as Mod",
                Filter = "CCP Mod Files (*.ccpmod)|*.ccpmod",
                FileName = $"{App.Mods.ActiveMod.Name.Replace(" ", "-").ToLowerInvariant()}-export.ccpmod"
            };

            if (sfd.ShowDialog() == true)
            {
                BtnExport.IsEnabled = false;
                try
                {
                    await App.Mods.ExportCurrentAsModAsync(
                        sfd.FileName,
                        App.Mods.ActiveMod.Name + " Export",
                        App.Mods.ActiveMod.Manifest.Author);

                    MessageBox.Show($"Mod exported to:\n{sfd.FileName}", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    BtnExport.IsEnabled = true;
                }
            }
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            var creator = new ModCreatorWindow { Owner = this };
            creator.ShowDialog();
        }

    }
}
