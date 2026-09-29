using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Mod browser/manager dialog — list, details, install/uninstall/activate.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/ModManagerDialog.xaml.cs. Deviations:
    ///  - The listing reads the real thing: <c>CoreMods.InstalledMods</c> and
    ///    <c>CoreMods.ActiveModId</c>, over the real <c>ModPackage</c>/<c>ModManifest</c> models in
    ///    Core. With no mod service seeded (this head today) that is the one built-in CCP Default,
    ///    which is exactly what <c>App.Mods</c> answered before its service came up.
    ///  - The mod-id → pack-id mapping comes from Core <c>ModService.PackIdForMod</c>; pack sizes and install state come from
    ///    <see cref="ModPickerCatalog"/> on this head.
    ///  - Install, uninstall, activate and export go through the head's <c>App.Mods</c>
    ///    (Core ModService), exactly as WPF. Catalogue sharing is still a stub (no catalogue client).
    ///  - <c>Visibility</c> -> <c>IsVisible</c>; <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>;
    ///    <c>ColorConverter.ConvertFromString</c> -> <c>Color.Parse</c>;
    ///    <c>BitmapImage</c> -> <c>Bitmap</c> (<c>DecodePixelWidth</c> becomes
    ///    <c>DecodeToWidth</c>); <c>OpenFileDialog</c>/<c>SaveFileDialog</c> -> the
    ///    <c>StorageProvider</c> pickers.
    ///  - <c>MessageBox.Show</c> becomes <c>MessageDialog.ConfirmAsync</c>, this head's message box.
    ///  - The share-status pill comes from <c>MainShellWindow.CreateCatalogueStatusBadge</c>.
    ///  - Handlers are wired in the constructor rather than in markup, per the porting convention.
    /// </summary>
    public partial class ModManagerDialog : Window
    {
        /// <summary>
        /// True if the user activated a different mod during this session (caller should refresh UI).
        /// </summary>
        public bool ModWasChanged { get; private set; }

        private ModPackage? _selectedMod;

        /// <summary>Pack ids currently being downloaded from THIS dialog (per-mod button guard).</summary>
        private readonly HashSet<string> _packDownloads = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Pack ids that finished WHILE this dialog was open. The pack row would otherwise just vanish
        /// the moment the install lands (it is keyed on "not installed"), which reads as the button
        /// having done nothing — these keep a confirmation on screen for the rest of the session.
        /// </summary>
        private readonly HashSet<string> _packsJustInstalled = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Which mod the star, the active indicator and the button rules read as active. Seeded from
        /// <c>CoreMods.ActiveModId</c>; re-read from <c>App.Mods</c> after each activate/uninstall.
        /// </summary>
        private string _activeModId = CoreMods.ActiveModId;

        private readonly ListBox _modList;
        private readonly StackPanel _detailsPanel;
        private readonly TextBlock _txtModName, _txtModAuthor, _txtModVersion, _txtModDescription;
        private readonly TextBlock _txtArtOverrides, _txtPackState, _txtThemeColor, _txtCompanion, _txtActiveIndicator;
        private readonly Border _previewImagePanel, _packPanel, _themeColorPreview;
        private readonly Image _imgModPreview;
        private readonly ProgressBar _packProgress;
        private readonly Button _btnDownloadPack, _btnActivate, _btnShare, _btnTubeFit, _btnUninstall;
        private readonly Button _btnInstall, _btnExport;

        public ModManagerDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _modList = this.FindControl<ListBox>("ModList")!;
            _detailsPanel = this.FindControl<StackPanel>("DetailsPanel")!;
            _txtModName = this.FindControl<TextBlock>("TxtModName")!;
            _txtModAuthor = this.FindControl<TextBlock>("TxtModAuthor")!;
            _txtModVersion = this.FindControl<TextBlock>("TxtModVersion")!;
            _txtModDescription = this.FindControl<TextBlock>("TxtModDescription")!;
            _txtArtOverrides = this.FindControl<TextBlock>("TxtArtOverrides")!;
            _txtPackState = this.FindControl<TextBlock>("TxtPackState")!;
            _txtThemeColor = this.FindControl<TextBlock>("TxtThemeColor")!;
            _txtCompanion = this.FindControl<TextBlock>("TxtCompanion")!;
            _txtActiveIndicator = this.FindControl<TextBlock>("TxtActiveIndicator")!;
            _previewImagePanel = this.FindControl<Border>("PreviewImagePanel")!;
            _packPanel = this.FindControl<Border>("PackPanel")!;
            _themeColorPreview = this.FindControl<Border>("ThemeColorPreview")!;
            _imgModPreview = this.FindControl<Image>("ImgModPreview")!;
            _packProgress = this.FindControl<ProgressBar>("PackProgress")!;
            _btnDownloadPack = this.FindControl<Button>("BtnDownloadPack")!;
            _btnActivate = this.FindControl<Button>("BtnActivate")!;
            _btnShare = this.FindControl<Button>("BtnShare")!;
            _btnTubeFit = this.FindControl<Button>("BtnTubeFit")!;
            _btnUninstall = this.FindControl<Button>("BtnUninstall")!;
            _btnInstall = this.FindControl<Button>("BtnInstall")!;
            _btnExport = this.FindControl<Button>("BtnExport")!;

            _modList.SelectionChanged += ModList_SelectionChanged;

            this.FindControl<Border>("TitleBar")!.PointerPressed += TitleBar_PointerPressed;
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnBrowseCatalogue")!.Click += (_, _) => BtnBrowseCatalogue_Click();
            this.FindControl<Button>("BtnCreate")!.Click += (_, _) => BtnCreate_Click();
            _btnDownloadPack.Click += (_, _) => _ = BtnDownloadPack_Click();
            _btnActivate.Click += (_, _) => BtnActivate_Click();
            _btnShare.Click += (_, _) => BtnShare_Click();
            _btnTubeFit.Click += (_, _) => BtnTubeFit_Click();
            _btnUninstall.Click += (_, _) => BtnUninstall_Click();
            _btnInstall.Click += (_, _) => BtnInstall_Click();
            _btnExport.Click += (_, _) => BtnExport_Click();

            RefreshModList();

            // WPF SubscribeToPackEvents. Two signals, deliberately. PackInstalled = the bytes are on
            // disk (ends the progress bar); ModAvailabilityChanged = the .ccpmod has been extracted
            // into the built-in slot, i.e. the mod is genuinely usable. Both handlers are idempotent.
            var svc = App.ReleaseContent;
            var mods = App.Mods;
            if (svc != null)
            {
                svc.PackProgressChanged += OnPackProgressChanged;
                svc.PackInstalled += OnPackInstalled;
            }
            if (mods != null) mods.ModAvailabilityChanged += OnModAvailabilityChanged;
            Closed += (_, _) =>
            {
                if (mods != null) mods.ModAvailabilityChanged -= OnModAvailabilityChanged;
                if (svc == null) return;
                svc.PackProgressChanged -= OnPackProgressChanged;
                svc.PackInstalled -= OnPackInstalled;
            };
        }

        private static void MarshalToUi(Action action) => global::Avalonia.Threading.Dispatcher.UIThread.Post(action);

        /// <summary>A downloaded pack finished extracting into its built-in slot (argument is a mod id,
        /// pack id as a fallback): the list markers and the pack row are stale.</summary>
        private void OnModAvailabilityChanged(object? sender, string modOrPackId) => MarshalToUi(() =>
            OnPackInstalled(sender, ModService.PackIdForMod(modOrPackId) ?? modOrPackId));

        private void OnPackInstalled(object? sender, string packId) => MarshalToUi(() =>
        {
            _packDownloads.Remove(packId);
            _packsJustInstalled.Add(packId);
            RefreshListKeepingSelection();
        });

        /// <summary>Raised on the download loop's background thread.</summary>
        private void OnPackProgressChanged(object? sender, PackProgressEventArgs e) => MarshalToUi(() => ShowPackProgress(e.PackId, e.Percent));

        private void ShowPackProgress(string packId, double percent)
        {
            if (!IsPackRowShowing(packId)) return;
            _packPanel.IsVisible = true;
            _packProgress.IsVisible = true;
            _packProgress.Value = percent;
            _txtPackState.Text = percent >= 100
                ? Loc.Get("modmgr_pack_installing")
                : Loc.GetF("modmgr_pack_downloading", (int)Math.Round(percent));
        }

        /// <summary>
        /// Repaints the list without yanking the user back to the active mod (RefreshModList
        /// auto-selects the active one).
        /// </summary>
        private void RefreshListKeepingSelection()
        {
            var keep = _selectedMod?.Id;
            RefreshModList();
            if (string.IsNullOrEmpty(keep)) return;
            var item = _modList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag as string == keep);
            if (item == null) return;
            // Re-select even when it is already selected: ShowModDetails repaints the pack row.
            if (_modList.SelectedItem == item) ShowModDetails(CoreMods.InstalledMods[keep!]);
            else _modList.SelectedItem = item;
        }

        /// <summary>Offline mode blocks every content-pack fetch; read live, as WPF does.</summary>
        private static bool IsOfflineMode => CoreSettings.Service?.Current?.OfflineMode == true;

        // ------------------------------------------------------------------ content packs
        //
        // Built-in mods ship without their media on a modular install (docs/CONTENT_PACKS_PLAN.md §4):
        // the manifests, theme and text all work, but the voice lines / portraits / DTRH barks come
        // down as a release-hosted pack. This section adds the per-mod Download control for that.
        // The mod id -> pack id mapping lives in ONE place: ModService.PackIdForMod.

        /// <summary>True when the pack row currently on screen belongs to <paramref name="packId"/>.</summary>
        private bool IsPackRowShowing(string packId)
        {
            if (_selectedMod == null) return false;
            return string.Equals(ModService.PackIdForMod(_selectedMod.Id), packId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Paints the pack row for the selected mod (WPF UpdatePackPanel). Collapsed unless this is a
        /// built-in whose pack is mapped, the app is a modular install, and the pack is not stamped yet.
        /// </summary>
        private void UpdatePackPanel(ModPackage mod)
        {
            var entry = ModPickerCatalog.ForMod(mod.Id);
            var packId = entry?.PackId;
            var svc = App.ReleaseContent;

            if (string.IsNullOrEmpty(packId) || svc == null || svc.IsFullInstall)
            {
                _packPanel.IsVisible = false;
                return;
            }

            if (svc.IsInstalled(packId!))
            {
                // Already on disk: show a confirmation only if it landed during this session,
                // otherwise the row has no reason to exist.
                if (!_packsJustInstalled.Contains(packId!))
                {
                    _packPanel.IsVisible = false;
                    return;
                }

                _packPanel.IsVisible = true;
                _packProgress.IsVisible = true;
                _packProgress.Value = 100;
                _txtPackState.Text = Loc.Get("modmgr_pack_ready");
                _btnDownloadPack.IsEnabled = false;
                SetDownloadPackLabel(entry!);
                return;
            }

            _packPanel.IsVisible = true;

            var inFlight = _packDownloads.Contains(packId!);
            _packProgress.IsVisible = inFlight;
            if (!inFlight) _packProgress.Value = 0;
            SetDownloadPackLabel(entry!);

            // Offline mode is a hard stop one rung below this button: say so BEFORE the press.
            if (!inFlight && IsOfflineMode)
            {
                _txtPackState.Text = Loc.Get("modmgr_pack_offline");
                _btnDownloadPack.IsEnabled = false;
                return;
            }

            _txtPackState.Text = inFlight
                ? Loc.GetF("modmgr_pack_downloading", (int)Math.Round(_packProgress.Value))
                : Loc.Get("modmgr_pack_not_downloaded");

            _btnDownloadPack.IsEnabled = !inFlight;
        }

        /// <summary>
        /// The download button's caption. A TextBlock child rather than Content, because Avalonia
        /// parses "_" in Content as an access key (CLAUDE.md trap 1) and this string is formatted.
        /// </summary>
        private void SetDownloadPackLabel(ModPickerCatalogEntry entry) =>
            _btnDownloadPack.Content = new TextBlock
            {
                Text = Loc.GetF("modmgr_btn_download_pack", ModPickerCatalog.FormatSize(ModPickerCatalog.SizeBytesFor(entry)))
            };

        /// <summary>WPF BtnDownloadPack_Click. Internal so a test can await the whole download.</summary>
        internal async Task BtnDownloadPack_Click()
        {
            var mod = _selectedMod;
            if (mod == null) return;

            var packId = ModPickerCatalog.ForMod(mod.Id)?.PackId;
            var svc = App.ReleaseContent;
            if (string.IsNullOrEmpty(packId) || svc == null) return;
            if (!_packDownloads.Add(packId!)) return; // already running from this dialog

            _btnDownloadPack.IsEnabled = false;
            ShowPackProgress(packId!, 0);

            var ok = false;
            try
            {
                // No cancellation token, as WPF: closing the Mod Manager must not kill the download,
                // and RequestPackAsync de-dupes so every caller shares one task. Progress<T> posts
                // back to this (UI) thread; each write re-checks the row on screen is still this pack's.
                ok = await svc.RequestPackAsync(packId!, new Progress<double>(p => ShowPackProgress(packId!, p)));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModManager] Pack {Pack} download threw", packId);
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
                _packProgress.IsVisible = false;
                _txtPackState.Text = IsOfflineMode
                    ? Loc.Get("modmgr_pack_offline")
                    : svc.ManifestUnavailable
                        ? Loc.Get("modmgr_pack_unavailable")
                        : Loc.Get("modmgr_pack_failed");
                _btnDownloadPack.IsEnabled = !IsOfflineMode;
            }
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                try { BeginMoveDrag(e); } catch { /* dragging can throw if the press already ended */ }
            }
        }

        // The web catalogue is where shared mods "end up": community-made mods
        // listed with their creator-hosted MEGA download links. Download the
        // .ccpmod there, then drag it onto the main window (or use Install).
        private static void BtnBrowseCatalogue_Click()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://app.cclabs.app/catalogue/mods",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                // The button just does nothing when no handler is registered for an https URI.
                Log.Warning(ex, "[ModManager] Could not open the web catalogue");
            }
        }

        private void RefreshModList()
        {
            _modList.Items.Clear();

            foreach (var mod in CoreMods.InstalledMods.Values.OrderBy(m => !m.IsBuiltIn).ThenBy(m => m.Name))
            {
                var prefix = mod.Id == _activeModId ? "★ " : "  "; // star for active
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock
                {
                    Text = prefix + mod.Name,
                    VerticalAlignment = VerticalAlignment.Center
                });

                // Catalogue share status pill (user mods the owner has shared).
                var badge = Windows.MainShellWindow.CreateCatalogueStatusBadge(
                    Windows.MainShellWindow.GetCatalogueRecord(Windows.MainShellWindow.CatalogueKindMods, mod.Id));
                if (badge != null) row.Children.Add(badge);

                // "media not downloaded yet" marker for built-ins on a modular install.
                if (ModPickerCatalog.NeedsDownload(mod.Id))
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
                    Foreground = Brushes.White
                };
                _modList.Items.Add(item);

                // Auto-select active mod
                if (mod.Id == _activeModId)
                    _modList.SelectedItem = item;
            }
        }

        private void ModList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_modList.SelectedItem is ListBoxItem item && item.Tag is string modId)
            {
                if (CoreMods.InstalledMods.TryGetValue(modId, out var mod))
                {
                    ShowModDetails(mod);
                    return;
                }
            }
            _detailsPanel.IsVisible = false;
        }

        private void ShowModDetails(ModPackage mod)
        {
            _selectedMod = mod;
            _detailsPanel.IsVisible = true;

            _txtModName.Text = mod.Name;
            _txtModAuthor.Text = Loc.GetF("label_by_author", mod.Manifest.Author);
            _txtModVersion.Text = Loc.GetF("label_version_prefix", mod.Manifest.Version);
            _txtModDescription.Text = mod.Manifest.Description ?? "";

            // Preview image + what the mod actually overrides on disk.
            ShowArtSummary(mod);

            // Theme color
            var colorHex = mod.Manifest.Theme?.AccentColor ?? "#FF69B4";
            _txtThemeColor.Text = colorHex;
            try
            {
                _themeColorPreview.Background = new SolidColorBrush(Color.Parse(colorHex));
            }
            catch
            {
                _themeColorPreview.Background = new SolidColorBrush(Colors.HotPink);
            }

            // Companion
            _txtCompanion.Text = mod.Manifest.Identity?.CompanionName ?? "BambiSprite";

            // Active state
            var isActive = mod.Id == _activeModId;
            _txtActiveIndicator.IsVisible = isActive;
            _btnActivate.IsVisible = !isActive;

            // Tube Fit edits the avatar's fit inside the tube for the mod that's actually rendering,
            // so it only makes sense (and only previews correctly) for the active mod.
            _btnTubeFit.IsVisible = isActive;

            // Can't uninstall built-in mods or active mod
            _btnUninstall.IsVisible = !mod.IsBuiltIn && !isActive;

            // ponytail: Share needs the catalogue client (not ported); hidden rather than a dead button.
            _btnShare.IsVisible = false;

            // Built-in mods whose media still has to come down off the release.
            UpdatePackPanel(mod);
        }

        // ------------------------------------------------------------------ art summary
        //
        // Two read-only rows under the description: the manifest's own previewImage (declared
        // by every mod exported from the creator, and never rendered anywhere in the app until
        // now) and a count of what the mod shadows. Mod art is pure path shadowing -- a file at
        // resources/<path> replaces the app's Resources/<same path> -- so a plain file count
        // per top-level folder is an honest picture of how much of the app the mod repaints.
        // Both rows stay hidden for built-in mods, which have no InstalledPath to read.

        private void ShowArtSummary(ModPackage mod)
        {
            _imgModPreview.Source = null;
            _previewImagePanel.IsVisible = false;
            _txtArtOverrides.IsVisible = false;

            var installed = mod.InstalledPath;
            if (string.IsNullOrEmpty(installed) || !Directory.Exists(installed)) return;

            var preview = LoadPreviewImage(installed!, mod.Manifest.PreviewImage);
            if (preview != null)
            {
                _imgModPreview.Source = preview;
                _previewImagePanel.IsVisible = true;
            }

            var summary = SummarizeOverrides(Path.Combine(installed!, "resources"));
            if (summary != null)
            {
                _txtArtOverrides.Text = summary;
                _txtArtOverrides.IsVisible = true;
            }
        }

        /// <summary>
        /// The manifest's previewImage decoded from inside the mod folder, or null when it is
        /// unset/missing/unreadable. The path comes out of author-supplied JSON, so it is held
        /// to the same rules as any other mod resource path: relative, and no climbing out.
        /// </summary>
        private static Bitmap? LoadPreviewImage(string installedPath, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            if (relativePath!.Contains("..") || Path.IsPathRooted(relativePath)) return null;

            try
            {
                var full = Path.Combine(installedPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) return null;

                using var stream = File.OpenRead(full);
                // The row is 260px wide; a full-size decode here costs MBs. WPF spelled this
                // DecodePixelWidth on BitmapImage.
                return Bitmap.DecodeToWidth(stream, 320);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// "Overrides: 19 files (features 12, nav 7)" for a mod's resources tree, or null when
        /// the folder is missing or empty. Grouped by top-level folder, biggest first; files
        /// sitting loose at the root of resources/ group as "root".
        /// </summary>
        internal static string? SummarizeOverrides(string resourcesDir)
        {
            if (string.IsNullOrEmpty(resourcesDir) || !Directory.Exists(resourcesDir)) return null;

            Dictionary<string, int> groups;
            int total;
            try
            {
                groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                total = 0;
                foreach (var file in Directory.EnumerateFiles(resourcesDir, "*.*", SearchOption.AllDirectories))
                {
                    total++;
                    var group = TopLevelFolder(resourcesDir, file);
                    groups[group] = groups.TryGetValue(group, out var n) ? n + 1 : 1;
                }
            }
            catch
            {
                return null;   // unreadable tree is not worth a broken details panel
            }

            if (total == 0) return null;

            var breakdown = string.Join(", ", groups
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key} {kv.Value}"));

            var noun = total == 1 ? "file" : "files";
            return $"Overrides: {total} {noun} ({breakdown})";
        }

        /// <summary>First path segment below <paramref name="root"/>, or "root" for a loose file.</summary>
        private static string TopLevelFolder(string root, string filePath)
        {
            var relative = Path.GetRelativePath(root, filePath);
            var cut = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            return cut > 0 ? relative[..cut] : "root";
        }

        private void BtnShare_Click()
        {
            if (_selectedMod == null) return;

            // ponytail: needs MainWindow.ShareModToCatalogueAsync (catalogue client), wired when it
            // moves to Core. WPF disabled the button for the upload and refreshed the status badge.
        }

        private void BtnActivate_Click()
        {
            if (_selectedMod == null) return;

            if (App.Mods == null) return;

            // WPF ModManagerDialog.xaml.cs:578-582
            App.Mods.ActivateMod(_selectedMod.Id);
            CoreSettings.Current.ActiveModId = _selectedMod.Id;
            CoreSettings.Save();
            _activeModId = App.Mods.ActiveModId;

            ModWasChanged = true;
            RefreshModList();

            // Re-show details for the newly active mod
            ShowModDetails(_selectedMod);
        }

        private async void BtnUninstall_Click()
        {
            if (_selectedMod == null) return;
            if (_selectedMod.IsBuiltIn || App.Mods == null) return;

            var confirmed = await MessageDialog.ConfirmAsync(
                this,
                Loc.Get("title_confirm_uninstall"),
                Loc.GetF("msg_confirm_uninstall_mod", _selectedMod.Name));

            if (!confirmed) return;

            // WPF ModManagerDialog.xaml.cs:605-614
            var wasActive = _selectedMod.Id == App.Mods.ActiveModId;
            App.Mods.UninstallMod(_selectedMod.Id);
            if (wasActive)
            {
                CoreSettings.Current.ActiveModId = App.Mods.ActiveModId;
                CoreSettings.Save();
                ModWasChanged = true;
            }
            _activeModId = App.Mods.ActiveModId;

            _selectedMod = null;
            _detailsPanel.IsVisible = false;
            RefreshModList();
        }

        private async void BtnInstall_Click()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get("title_install_mod"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("CCP Mod Files") { Patterns = new[] { "*.ccpmod" } },
                    FilePickerFileTypes.All,
                },
            });

            if (files.Count == 0 || App.Mods == null || files[0].TryGetLocalPath() is not { } path) return;

            _btnInstall.IsEnabled = false;
            try
            {
                var result = await App.Mods.InstallModAsync(path);
                if (result.Success)
                {
                    RefreshModList();
                    await MessageDialog.ShowAsync(this, Loc.Get("title_success"), Loc.Get("msg_mod_installed_successfully"));
                }
                else
                {
                    await MessageDialog.ShowAsync(this, Loc.Get("title_install_failed"),
                        result.ErrorMessage ?? Loc.Get("msg_failed_to_install_mod"));
                }
            }
            finally
            {
                _btnInstall.IsEnabled = true;
            }
        }

        private async void BtnExport_Click()
        {
            var mods = CoreMods.InstalledMods;
            var active = (mods.TryGetValue(_activeModId, out var byId) ? byId : null)
                         ?? mods.Values.FirstOrDefault();
            if (active == null) return;

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get("title_export_config_as_mod"),
                SuggestedFileName = $"{active.Name.Replace(" ", "-").ToLowerInvariant()}-export.ccpmod",
                DefaultExtension = "ccpmod",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CCP Mod Files") { Patterns = new[] { "*.ccpmod" } },
                },
            });

            if (file == null || App.Mods == null || file.TryGetLocalPath() is not { } path) return;

            _btnExport.IsEnabled = false;
            try
            {
                await App.Mods.ExportCurrentAsModAsync(path, active.Name + " Export", active.Manifest.Author);
                await MessageDialog.ShowAsync(this, Loc.Get("title_export_complete"), Loc.GetF("msg_mod_exported_to", path));
            }
            catch (Exception ex)
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_export_error"), Loc.GetF("msg_export_failed", ex.Message));
            }
            finally
            {
                _btnExport.IsEnabled = true;
            }
        }

        // Live WYSIWYG editor for the avatar's scale/offsets inside the tube. Saves a per-mod user
        // override in settings (never into the mod), so no ModWasChanged refresh is needed.
        private void BtnTubeFit_Click()
        {
            var dialog = new TubeFitDialog();
            dialog.ShowDialog(this);
        }

        /// <summary>
        /// WPF's <c>new ModCreatorWindow { Owner = this }.ShowDialog()</c>. The window IS on this
        /// head (<c>Views/Windows/ModCreatorWindow.axaml</c>) and this is its first call site here.
        /// Avalonia's ShowDialog is async and throws on an owner that is not on screen, so the
        /// visibility guard is the twin of WPF's implicit "Owner is showing".
        ///
        /// <para>That window still carries its own stubs (several panel partials, audio preview,
        /// art framing) and says so in its own notes. Opening it is still the right call: every
        /// section it cannot edit is visibly absent rather than misreported, and its save path
        /// writes a real mod.json.</para>
        /// </summary>
        private async void BtnCreate_Click()
        {
            try
            {
                if (!IsVisible) return;
                await new ModCreatorWindow().ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModManager] Mod creator failed to open");
            }
        }
    }
}
