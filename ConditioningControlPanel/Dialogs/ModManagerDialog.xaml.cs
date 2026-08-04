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

        /// <summary>Pack ids currently being downloaded from THIS dialog (per-mod button guard).</summary>
        private readonly System.Collections.Generic.HashSet<string> _packDownloads =
            new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Pack ids that finished WHILE this dialog was open. The pack row would otherwise just vanish
        /// the moment the install lands (it is keyed on "not installed"), which reads as the button
        /// having done nothing — these keep a confirmation on screen for the rest of the session.
        /// </summary>
        private readonly System.Collections.Generic.HashSet<string> _packsJustInstalled =
            new(System.StringComparer.OrdinalIgnoreCase);

        public ModManagerDialog()
        {
            InitializeComponent();
            RefreshModList();
            SubscribeToPackEvents();
            Closed += (_, _) => UnsubscribeFromPackEvents();
        }

        // ------------------------------------------------------------------ content packs
        //
        // Built-in mods ship without their media on a modular install (docs/CONTENT_PACKS_PLAN.md §4):
        // the manifests, theme and text all work, but the voice lines / portraits / DTRH barks come
        // down as a release-hosted pack. This section adds the per-mod Download control for that.
        // The mod id -> pack id mapping lives in ONE place: ModPackCatalog.

        private void SubscribeToPackEvents()
        {
            try
            {
                var svc = App.ReleaseContent;
                if (svc == null) return;

                svc.PackProgressChanged += OnPackProgressChanged;
                // Two signals, deliberately. PackInstalled = the bytes are on disk (ends the progress
                // bar); ModAvailabilityChanged = the .ccpmod has been extracted into the built-in slot
                // and the resolver cache dropped, i.e. the mod is genuinely usable. Both handlers are
                // idempotent, so it does not matter which arrives first.
                svc.PackInstalled += OnPackInstalled;

                if (App.Mods != null) App.Mods.ModAvailabilityChanged += OnModAvailabilityChanged;
            }
            catch (System.Exception ex)
            {
                App.Logger?.Warning(ex, "[ModManager] Could not subscribe to release-content events");
            }
        }

        private void UnsubscribeFromPackEvents()
        {
            try
            {
                if (App.Mods != null) App.Mods.ModAvailabilityChanged -= OnModAvailabilityChanged;

                var svc = App.ReleaseContent;
                if (svc == null) return;
                svc.PackProgressChanged -= OnPackProgressChanged;
                svc.PackInstalled -= OnPackInstalled;
            }
            catch { }
        }

        /// <summary>
        /// A downloaded pack finished extracting into its built-in slot — the mod is usable now, so the
        /// list markers and the pack row are stale. Argument is a mod id (pack id as a fallback).
        /// </summary>
        private void OnModAvailabilityChanged(object? sender, string modOrPackId)
        {
            MarshalToUi(() =>
            {
                var packId = ModPackCatalog.PackIdForMod(modOrPackId) ?? modOrPackId;
                _packDownloads.Remove(packId);
                _packsJustInstalled.Add(packId);
                RefreshListKeepingSelection();
            });
        }

        /// <summary>True when the pack row currently on screen belongs to <paramref name="packId"/>.</summary>
        private bool IsPackRowShowing(string packId)
        {
            if (_selectedMod == null) return false;
            return string.Equals(ModPackCatalog.PackIdForMod(_selectedMod.Id), packId,
                System.StringComparison.OrdinalIgnoreCase);
        }

        private void OnPackProgressChanged(object? sender, PackProgressEventArgs e)
        {
            // Raised on the download loop's background thread.
            MarshalToUi(() =>
            {
                if (!IsPackRowShowing(e.PackId)) return;

                PackPanel.Visibility = Visibility.Visible;
                PackProgress.Visibility = Visibility.Visible;
                PackProgress.Value = e.Percent;
                TxtPackState.Text = e.Percent >= 100
                    ? Loc.Get("modmgr_pack_installing")
                    : Loc.GetF("modmgr_pack_downloading", (int)System.Math.Round(e.Percent));
            });
        }

        private void OnPackInstalled(object? sender, string packId)
        {
            MarshalToUi(() =>
            {
                _packDownloads.Remove(packId);
                _packsJustInstalled.Add(packId);
                RefreshListKeepingSelection();
            });
        }

        /// <summary>
        /// Repaints the list (the "media missing" markers move) without yanking the user back to the
        /// active mod — <see cref="RefreshModList"/> auto-selects the active one, which would be a
        /// jarring jump when a download merely finished in the background.
        /// </summary>
        private void RefreshListKeepingSelection()
        {
            var keep = _selectedMod?.Id;
            RefreshModList();

            if (string.IsNullOrEmpty(keep)) return;
            foreach (var obj in ModList.Items)
            {
                if (obj is ListBoxItem item && item.Tag as string == keep)
                {
                    // Assigning re-raises SelectionChanged -> ShowModDetails -> UpdatePackPanel.
                    ModList.SelectedItem = item;
                    return;
                }
            }
        }

        private static void MarshalToUi(System.Action action)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                if (dispatcher.CheckAccess()) { action(); return; }

                // Normal, never Loaded — Loaded-priority work is starved in this app.
                dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch { }
        }

        /// <summary>
        /// Paints the pack row for the selected mod. Collapsed unless this is a built-in whose pack is
        /// mapped, the app is a modular install, and the pack is not stamped yet.
        /// </summary>
        private void UpdatePackPanel(ModPackage mod)
        {
            try
            {
                var entry = ModPackCatalog.ForMod(mod.Id);
                var packId = entry?.PackId;
                var svc = App.ReleaseContent;

                if (entry == null || string.IsNullOrEmpty(packId) || svc == null || svc.IsFullInstall)
                {
                    PackPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                if (svc.IsInstalled(packId!))
                {
                    // Already on disk: show a confirmation only if it landed during this session,
                    // otherwise the row has no reason to exist.
                    if (!_packsJustInstalled.Contains(packId!))
                    {
                        PackPanel.Visibility = Visibility.Collapsed;
                        return;
                    }

                    PackPanel.Visibility = Visibility.Visible;
                    PackProgress.Visibility = Visibility.Visible;
                    PackProgress.Value = 100;
                    TxtPackState.Text = Loc.Get("modmgr_pack_ready");
                    BtnDownloadPack.IsEnabled = false;
                    BtnDownloadPack.Content = Loc.GetF("modmgr_btn_download_pack",
                        ModPackCatalog.FormatSize(ModPackCatalog.SizeBytesFor(entry)));
                    return;
                }

                PackPanel.Visibility = Visibility.Visible;

                var inFlight = _packDownloads.Contains(packId!);
                PackProgress.Visibility = inFlight ? Visibility.Visible : Visibility.Collapsed;
                if (!inFlight) PackProgress.Value = 0;

                TxtPackState.Text = inFlight
                    ? Loc.GetF("modmgr_pack_downloading", (int)System.Math.Round(PackProgress.Value))
                    : Loc.Get("modmgr_pack_not_downloaded");

                BtnDownloadPack.Content = Loc.GetF("modmgr_btn_download_pack",
                    ModPackCatalog.FormatSize(ModPackCatalog.SizeBytesFor(entry)));
                BtnDownloadPack.IsEnabled = !inFlight;
            }
            catch (System.Exception ex)
            {
                App.Logger?.Warning(ex, "[ModManager] Pack panel refresh failed");
                PackPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnDownloadPack_Click(object sender, RoutedEventArgs e)
        {
            var mod = _selectedMod;
            if (mod == null) return;

            var entry = ModPackCatalog.ForMod(mod.Id);
            var packId = entry?.PackId;
            var svc = App.ReleaseContent;
            if (entry == null || string.IsNullOrEmpty(packId) || svc == null) return;
            if (!_packDownloads.Add(packId!)) return; // already running from this dialog

            BtnDownloadPack.IsEnabled = false;
            PackProgress.Visibility = Visibility.Visible;
            PackProgress.Value = 0;
            TxtPackState.Text = Loc.GetF("modmgr_pack_downloading", 0);

            var ok = false;
            try
            {
                // No cancellation token: closing the Mod Manager must not kill the download, and
                // RequestPackAsync de-dupes so the first-run picker and this button share one task.
                // Progress<T> posts back to the UI thread it was created on. The selection can move
                // while a 331 MB pack comes down, so every write re-checks that the row on screen is
                // still this pack's.
                ok = await svc.RequestPackAsync(packId!, new System.Progress<double>(p =>
                {
                    if (!IsPackRowShowing(packId!)) return;
                    PackProgress.Value = p;
                    TxtPackState.Text = p >= 100
                        ? Loc.Get("modmgr_pack_installing")
                        : Loc.GetF("modmgr_pack_downloading", (int)System.Math.Round(p));
                }));
            }
            catch (System.Exception ex)
            {
                App.Logger?.Warning(ex, "[ModManager] Pack {Pack} download threw", packId);
            }
            finally
            {
                _packDownloads.Remove(packId!);
            }

            if (ok)
            {
                _packsJustInstalled.Add(packId!);
                RefreshListKeepingSelection();
            }
            else if (IsPackRowShowing(packId!))
            {
                PackProgress.Visibility = Visibility.Collapsed;
                TxtPackState.Text = svc.ManifestUnavailable
                    ? Loc.Get("modmgr_pack_unavailable")
                    : Loc.Get("modmgr_pack_failed");
                BtnDownloadPack.IsEnabled = true;
            }
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

        // The web catalogue is where shared mods "end up": community-made mods
        // listed with their creator-hosted MEGA download links. Download the
        // .ccpmod there, then drag it onto the main window (or use Install).
        private void BtnBrowseCatalogue_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://app.cclabs.app/catalogue/mods",
                    UseShellExecute = true,
                });
            }
            catch (System.Exception ex)
            {
                App.Logger?.Warning(ex, "[ModManager] Failed to open mod catalogue URL");
            }
        }

        private void RefreshModList()
        {
            ModList.Items.Clear();
            if (App.Mods == null) return;

            foreach (var mod in App.Mods.InstalledMods.Values.OrderBy(m => !m.IsBuiltIn).ThenBy(m => m.Name))
            {
                var prefix = mod.Id == App.Mods.ActiveModId ? "\u2605 " : "  "; // star for active
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock
                {
                    Text = prefix + mod.Name,
                    VerticalAlignment = VerticalAlignment.Center
                });
                // Catalogue share status pill (user mods the owner has shared).
                var badge = MainWindow.CreateCatalogueStatusBadge(
                    MainWindow.GetCatalogueRecord(MainWindow.CatalogueKindMods, mod.Id));
                if (badge != null) row.Children.Add(badge);

                // "media not downloaded yet" marker for built-ins on a modular install.
                if (ModPackCatalog.NeedsDownload(mod.Id))
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = Loc.Get("modmgr_badge_needs_download"),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47)),
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0)
                    });
                }

                var item = new ListBoxItem
                {
                    Content = row,
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
            TxtModAuthor.Text = Loc.GetF("label_by_author", mod.Manifest.Author);
            TxtModVersion.Text = Loc.GetF("label_version_prefix", mod.Manifest.Version);
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

            // Tube Fit edits the avatar's fit inside the tube for the mod that's actually rendering,
            // so it only makes sense (and only previews correctly) for the active mod.
            BtnTubeFit.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

            // Can't uninstall built-in mods or active mod
            BtnUninstall.Visibility = (!mod.IsBuiltIn && !isActive) ? Visibility.Visible : Visibility.Collapsed;

            // Only user-installed mods can be shared to the catalogue.
            BtnShare.Visibility = (!mod.IsBuiltIn && !string.IsNullOrEmpty(mod.InstalledPath))
                ? Visibility.Visible : Visibility.Collapsed;

            // Built-in mods whose media still has to come down off the release.
            UpdatePackPanel(mod);
        }

        private async void BtnShare_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMod == null || Owner is not MainWindow mw) return;

            BtnShare.IsEnabled = false;
            try
            {
                await mw.ShareModToCatalogueAsync(_selectedMod, this);
                RefreshModList(); // pick up the new status badge
            }
            finally
            {
                BtnShare.IsEnabled = true;
            }
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
                Loc.GetF("msg_confirm_uninstall_mod", _selectedMod.Name),
                Loc.Get("title_confirm_uninstall"),
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
                Title = Loc.Get("title_install_mod"),
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
                        MessageBox.Show(Loc.Get("msg_mod_installed_successfully"), Loc.Get("title_success"),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(installResult.ErrorMessage ?? Loc.Get("msg_failed_to_install_mod"), Loc.Get("title_install_failed"),
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
                Title = Loc.Get("title_export_config_as_mod"),
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

                    MessageBox.Show(Loc.GetF("msg_mod_exported_to", sfd.FileName), Loc.Get("title_export_complete"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(Loc.GetF("msg_export_failed", ex.Message), Loc.Get("title_export_error"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    BtnExport.IsEnabled = true;
                }
            }
        }

        // Live WYSIWYG editor for the avatar's scale/offsets inside the tube. Saves a per-mod user
        // override in settings (never into the mod), so no ModWasChanged refresh is needed.
        private void BtnTubeFit_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TubeFitDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            var creator = new ModCreatorWindow { Owner = this };
            creator.ShowDialog();
        }

    }
}
