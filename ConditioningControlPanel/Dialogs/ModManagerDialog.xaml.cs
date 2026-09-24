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
    /// The Customise window: pick a mod, then its companion (look + personality) in one card.
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
        /// Offline mode blocks every content-pack fetch (ReleaseContentService refuses the manifest
        /// GET and the download separately). Read live, not cached: Settings -> Data can flip it while
        /// this dialog is open.
        /// </summary>
        private static bool IsOfflineMode => App.Settings?.Current?.OfflineMode == true;

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

                BtnDownloadPack.Content = Loc.GetF("modmgr_btn_download_pack",
                    ModPackCatalog.FormatSize(ModPackCatalog.SizeBytesFor(entry)));

                // Offline mode is a hard stop one rung below this button: the service refuses both the
                // manifest fetch and the download. Say so BEFORE the press. An armed button that can
                // only ever fail, then blames the user's connection, is what sent a reporter through a
                // reinstall and a bug report for a setting they had turned on themselves.
                if (!inFlight && IsOfflineMode)
                {
                    TxtPackState.Text = Loc.Get("modmgr_pack_offline");
                    BtnDownloadPack.IsEnabled = false;
                    return;
                }

                TxtPackState.Text = inFlight
                    ? Loc.GetF("modmgr_pack_downloading", (int)System.Math.Round(PackProgress.Value))
                    : Loc.Get("modmgr_pack_not_downloaded");

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
                // Offline mode first: the service short-circuits before it ever touches the network, so
                // it never sets ManifestUnavailable and the old order fell through to "check your
                // connection" — advice for a problem the user does not have.
                TxtPackState.Text = IsOfflineMode
                    ? Loc.Get("modmgr_pack_offline")
                    : svc.ManifestUnavailable
                        ? Loc.Get("modmgr_pack_unavailable")
                        : Loc.Get("modmgr_pack_failed");
                BtnDownloadPack.IsEnabled = !IsOfflineMode;
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
                var inUse = mod.Id == App.Mods.ActiveModId;
                var line = new DockPanel { LastChildFill = true };
                if (inUse)
                {
                    var tag = new TextBlock
                    {
                        Text = Loc.Get("modmgr_in_use"),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xF0, 0xCB)),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0)
                    };
                    DockPanel.SetDock(tag, Dock.Right);
                    line.Children.Add(tag);
                }
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(row);
                row.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(AccentOf(mod)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                });
                row.Children.Add(new TextBlock
                {
                    Text = mod.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
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
                    Content = line,
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
            TxtModAuthor.Text = Loc.GetF("modmgr_by_version", mod.Manifest.Author, mod.Manifest.Version);
            TxtModDescription.Text = mod.Manifest.Description ?? "";
            TxtModDescription.ToolTip = string.IsNullOrWhiteSpace(mod.Manifest.Description) ? null : mod.Manifest.Description;

            // Banner across the top; what the mod overrides on disk rides its tooltip.
            ShowArtSummary(mod);

            // One primary action: "Use this mod", or IN USE.
            var isActive = mod.Id == App.Mods?.ActiveModId;
            TxtActiveIndicator.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            BtnActivate.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;

            // The card edits the LIVE companion, which is the active mod's. Any other mod gets a
            // read-only preview of its companion, read off its manifest without activating it.
            if (isActive) CompanionCard.Refresh();
            else CompanionCard.ShowPreview(mod, AccentOf(mod));

            // Overflow verbs for this mod: uninstall (never a built-in or the active one) and
            // share (user-installed mods only).
            BtnUninstall.Visibility = (!mod.IsBuiltIn && !isActive) ? Visibility.Visible : Visibility.Collapsed;
            BtnShare.Visibility = (!mod.IsBuiltIn && !string.IsNullOrEmpty(mod.InstalledPath))
                ? Visibility.Visible : Visibility.Collapsed;
            SepModActions.Visibility = BtnUninstall.Visibility == Visibility.Visible || BtnShare.Visibility == Visibility.Visible
                ? Visibility.Visible : Visibility.Collapsed;

            ShowDefaults(mod);

            // Built-in mods whose media still has to come down off the release.
            UpdatePackPanel(mod);
        }

        /// <summary>The mod's theme accent, for its list dot and its preview glyph.</summary>
        private static Color AccentOf(ModPackage mod)
        {
            try { return (Color)ColorConverter.ConvertFromString(mod.Manifest.Theme?.AccentColor ?? "#FF69B4"); }
            catch { return Colors.HotPink; }
        }

        // ------------------------------------------------------------------ per-mod defaults
        //
        // Two dropdowns: the settings preset and the asset preset to load each time this mod is
        // switched to. Stored per mod id in AppSettings (ModPresetDefaults), applied by
        // MainWindow.ApplyModDefaultPresets on every activation. A mod's suggestion pre-selects
        // its item and reads "(recommended)"; the user's choice, or pressing "Use this mod" with it
        // showing, is what stores it. Nothing is applied behind the user's back.

        private bool _fillingDefaults;

        private static System.Collections.Generic.List<Preset> AllSettingsPresets()
        {
            var list = Preset.GetDefaultPresets();
            var user = App.Settings?.Current?.UserPresets;
            if (user != null) list.AddRange(user);
            return list;
        }

        private void ShowDefaults(ModPackage mod)
        {
            var s = App.Settings?.Current;
            var none = new System.Collections.Generic.Dictionary<string, string>();
            _fillingDefaults = true;
            try
            {
                var settings = AllSettingsPresets();
                FillDefaults(CmbDefaultSettings, settings.Select(p => (p.Id, p.Name)),
                    ModPresetDefaults.ResolveSettings(settings, mod.Manifest.SuggestedSettingsPreset)?.Id,
                    s?.ModDefaultSettingsPreset ?? none, mod.Id);

                var assets = s?.AssetPresets ?? new System.Collections.Generic.List<AssetPreset>();
                FillDefaults(CmbDefaultAssets, assets.Select(p => (p.Id, p.Name)),
                    ModPresetDefaults.ResolveAssets(assets, mod.Manifest.SuggestedAssetPreset)?.Id,
                    s?.ModDefaultAssetPreset ?? none, mod.Id);
            }
            finally
            {
                _fillingDefaults = false;
            }
        }

        private static void FillDefaults(ComboBox combo, System.Collections.Generic.IEnumerable<(string Id, string Name)> presets,
            string? suggestedId, System.Collections.Generic.IReadOnlyDictionary<string, string> stored, string modId)
        {
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = Loc.Get("modmgr_keep_current"), Tag = ModPresetDefaults.KeepCurrent });
            foreach (var (id, name) in presets)
            {
                var label = id == suggestedId ? Loc.GetF("modmgr_recommended_fmt", name) : name;
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = id });
            }

            var initial = ModPresetDefaults.Initial(stored, modId, suggestedId);
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == initial)
                                 ?? combo.Items[0];
        }

        private void CmbDefaultSettings_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            StoreDefault(CmbDefaultSettings, App.Settings?.Current?.ModDefaultSettingsPreset);

        private void CmbDefaultAssets_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            StoreDefault(CmbDefaultAssets, App.Settings?.Current?.ModDefaultAssetPreset);

        /// <summary>An explicit pick is the consent: store it for the selected mod.</summary>
        private void StoreDefault(ComboBox combo, System.Collections.Generic.Dictionary<string, string>? map)
        {
            if (_fillingDefaults || map == null || _selectedMod == null) return;
            if (combo.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            ModPresetDefaults.Store(map, _selectedMod.Id, id);
            App.Settings!.Save();
        }

        /// <summary>"Use this mod" with the dropdowns showing is a choice too: store what they show,
        /// so a pre-selected recommendation the user saw and accepted applies on the switch.</summary>
        private void CommitShownDefaults()
        {
            var s = App.Settings?.Current;
            if (s == null || _selectedMod == null) return;
            if (CmbDefaultSettings.SelectedItem is ComboBoxItem { Tag: string sid })
                ModPresetDefaults.Store(s.ModDefaultSettingsPreset, _selectedMod.Id, sid);
            if (CmbDefaultAssets.SelectedItem is ComboBoxItem { Tag: string aid })
                ModPresetDefaults.Store(s.ModDefaultAssetPreset, _selectedMod.Id, aid);
            App.Settings!.Save();
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
            ImgModPreview.Source = null;
            BannerPanel.ToolTip = null;

            var installed = mod.InstalledPath;
            var hasFolder = !string.IsNullOrEmpty(installed) && Directory.Exists(installed);

            // Banner: the mod's own bannerImage, else its previewImage, else the built-in art.
            var banner = hasFolder
                ? LoadPreviewImage(installed!, mod.Manifest.BannerImage) ?? LoadPreviewImage(installed!, mod.Manifest.PreviewImage)
                : null;
            banner ??= LoadBuiltInBanner(BuiltInBannerFor(mod.Id));
            if (banner != null)
            {
                ImgModPreview.Source = banner;
            }

            if (!hasFolder) return;

            var summary = SummarizeOverrides(Path.Combine(installed!, "resources"));
            if (summary != null)
            {
                BannerPanel.ToolTip = summary;
            }
        }

        /// <summary>Stock banner art for the built-in mods (Resources/features), or null.</summary>
        internal static string? BuiltInBannerFor(string? modId) => modId switch
        {
            BuiltInMods.CCPDefaultId => "ccp_banner.png",
            BuiltInMods.BambiSleepId => "vault_bambi.png",
            BuiltInMods.SissyHypnoId => "vault_sissy.png",
            BuiltInMods.DronificationId => "vault_drone.png",
            BuiltInMods.LockedId => "vault_locked.png",
            _ => null
        };

        private static ImageSource? LoadBuiltInBanner(string? file)
        {
            if (file == null) return null;
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new System.Uri($"pack://application:,,,/Resources/features/{file}", System.UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 400;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The manifest's previewImage decoded from inside the mod folder, or null when it is
        /// unset/missing/unreadable. The path comes out of author-supplied JSON, so it is held
        /// to the same rules as any other mod resource path: relative, and no climbing out.
        /// </summary>
        private static ImageSource? LoadPreviewImage(string installedPath, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            if (relativePath!.Contains("..") || Path.IsPathRooted(relativePath)) return null;

            try
            {
                var full = Path.Combine(installedPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) return null;

                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new System.Uri(full, System.UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 400;   // the banner is 190px wide; a full-size decode here costs MBs
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
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

            System.Collections.Generic.Dictionary<string, int> groups;
            int total;
            try
            {
                groups = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
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
                .ThenBy(kv => kv.Key, System.StringComparer.OrdinalIgnoreCase)
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
            var mod = _selectedMod;
            CommitShownDefaults();

            // The one switching path the top-bar combo and the launcher use (ActivateMod +
            // ApplyActiveModChange), run NOW so the companion card below reads the new mod.
            if ((Owner as MainWindow ?? App.MainWindowRef) is { } mw)
            {
                mw.SwitchActiveModFromLauncher(mod.Id);
            }
            else
            {
                App.Mods.ActivateMod(mod.Id);
                App.Settings.Current.ActiveModId = mod.Id;
                App.Settings.Save();
                ModWasChanged = true;
            }

            RefreshListKeepingSelection();
        }

        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            if (BtnMore.ContextMenu == null) return;
            BtnMore.ContextMenu.PlacementTarget = BtnMore;
            BtnMore.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            BtnMore.ContextMenu.IsOpen = true;
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

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            var creator = new ModCreatorWindow { Owner = this };
            creator.ShowDialog();
        }

    }
}
