using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Share an installed mod to the web catalogue. Unlike presets/sessions the
    // binary is NOT uploaded — mods are 100+ MB, so the creator hosts the .ccpmod
    // on MEGA and the catalogue stores metadata + the download link (ccp-mod/v1).
    public partial class MainWindow
    {
        private const string CatalogueSchemaMod = "ccp-mod/v1";

        // Preview thumb limits: keep the JSON envelope small.
        private const int ModPreviewMaxPixels = 160;
        private const int ModPreviewMaxBytes = 64 * 1024;

        internal async Task ShareModToCatalogueAsync(ModPackage mod, Window? owner = null)
        {
            if (mod == null || mod.IsBuiltIn || string.IsNullOrEmpty(mod.InstalledPath)) return;

            if (string.IsNullOrEmpty(App.Settings?.Current?.AuthToken) || App.Catalogue == null)
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                    Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                return;
            }

            var dialog = new AssetSubmitDialog(
                mod.Name, App.UserDisplayName,
                requireDownloadUrl: true,
                defaultTags: mod.Manifest.Tags != null ? string.Join(", ", mod.Manifest.Tags) : null)
            { Owner = owner ?? this };
            if (dialog.ShowDialog() != true || !dialog.Confirmed) return;

            JObject asset;
            try { asset = BuildModCatalogueAsset(mod, dialog.DownloadUrl); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Mod asset build failed");
                App.Notifications?.Show(Loc.Get("catalogue_toast_unknown_error"),
                    Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitCatalogueAssetAsync(
                    CatalogueKindMods, asset, CatalogueSchemaMod,
                    dialog.Creator, dialog.Tags, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Mod share threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            RecordCatalogueSubmission(CatalogueKindMods, mod.Id, result);

            // Mods get a dedicated success toast that says where the mod ends
            // up (the web catalogue's Mods tab) and reminds the creator the
            // .ccpmod stays hosted on THEIR MEGA - we only list the link.
            if (result is SubmissionResult.Success)
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_mod_success"),
                    Services.NotificationType.Success, TimeSpan.FromSeconds(10));
            }
            else
            {
                ShowCatalogueSubmissionResultToast(result);
            }
        }

        // Drag-drop install: a .ccpmod downloaded from the web catalogue (MEGA)
        // dropped onto the main window. Installing a mod is a trust decision, so
        // peek the manifest and confirm name/author before touching anything.
        private async Task HandleModDropAsync(string ccpmodPath)
        {
            if (App.Mods == null) return;

            var manifest = await ModService.PeekManifestAsync(ccpmodPath);
            if (manifest == null)
            {
                App.Notifications?.Show(Loc.GetF("toast_mod_install_failed_fmt",
                    Loc.Get("msg_failed_to_install_mod")),
                    Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            var confirm = MessageBox.Show(this,
                Loc.GetF("msg_confirm_install_mod_fmt", manifest.Name, manifest.Author),
                Loc.Get("title_install_mod"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var result = await App.Mods.InstallModAsync(ccpmodPath);
            if (result.Success)
            {
                App.Notifications?.Show(Loc.GetF("toast_mod_installed_fmt", manifest.Name),
                    Services.NotificationType.Success, TimeSpan.FromSeconds(8));
            }
            else
            {
                App.Notifications?.Show(Loc.GetF("toast_mod_install_failed_fmt",
                    result.ErrorMessage ?? Loc.Get("msg_failed_to_install_mod")),
                    Services.NotificationType.Error, TimeSpan.FromSeconds(10));
            }
        }

        // ccp-mod/v1: metadata from mod.json + the creator's download link. No
        // sha256 in v1 — the installed mod is an extracted folder, so a locally
        // re-zipped hash wouldn't match the bytes actually hosted on MEGA.
        private static JObject BuildModCatalogueAsset(ModPackage mod, string downloadUrl)
        {
            var m = mod.Manifest;
            var asset = new JObject
            {
                ["mod_id"] = m.Id,
                ["name"] = m.Name,
                ["version"] = m.Version,
                ["author"] = m.Author,
                ["download"] = new JObject
                {
                    ["provider"] = "mega",
                    ["url"] = downloadUrl,
                    ["installed_size_bytes"] = SafeDirectorySize(mod.InstalledPath!),
                },
            };
            if (!string.IsNullOrWhiteSpace(m.Description)) asset["description"] = m.Description;
            if (!string.IsNullOrWhiteSpace(m.MinAppVersion)) asset["min_app_version"] = m.MinAppVersion;

            var thumb = TryBuildPreviewThumb(mod);
            if (thumb != null) asset["preview_thumb_b64"] = thumb;

            return asset;
        }

        private static long SafeDirectorySize(string dir)
        {
            try
            {
                return new DirectoryInfo(dir)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch { return 0; }
        }

        // Downscaled JPEG of the mod's previewImage (if it ships one), base64.
        // Returns null when there's no usable preview or it won't fit the cap.
        private static string? TryBuildPreviewThumb(ModPackage mod)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mod.Manifest.PreviewImage) || mod.InstalledPath == null)
                    return null;
                var path = Path.Combine(mod.InstalledPath, "resources", mod.Manifest.PreviewImage);
                if (!File.Exists(path)) return null;

                var src = new System.Windows.Media.Imaging.BitmapImage();
                src.BeginInit();
                src.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                src.UriSource = new Uri(path, UriKind.Absolute);
                // Decode straight to thumb size: cheap and memory-safe for big art.
                src.DecodePixelWidth = ModPreviewMaxPixels;
                src.EndInit();
                src.Freeze();

                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 80 };
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                if (ms.Length > ModPreviewMaxBytes) return null;
                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Catalogue] Mod preview thumb failed: {Error}", ex.Message);
                return null;
            }
        }
    }
}
