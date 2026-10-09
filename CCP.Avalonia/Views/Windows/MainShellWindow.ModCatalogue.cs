// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.ModCatalogue.cs (191 lines).
//
// Sorted member by member: GENUINELY 100% head-side. The file is two round trips - upload a mod to
// the web catalogue, install one dropped on the window - and every step of both is a service this
// head does not have. Nothing here is layout or settings, so there is no half to restore.
//
// What each member needs, exactly:
//   ShareModToCatalogueAsync - App.Catalogue.SubmitCatalogueAssetAsync
//                              (Core CatalogueClient since catalogue U1), the auth
//                              token from AppSettings.AuthToken (reachable) plus App.UserDisplayName
//                              (not), App.Notifications.Show
//                              (…/Services/Notifications/NotificationService.cs) for all four
//                              outcome toasts, and RecordCatalogueSubmission /
//                              ShowCatalogueSubmissionResultToast, which are in
//                              MainShellWindow.CatalogueSubmissions.cs. That file's READ half is
//                              live now (CatalogueKindMods, GetCatalogueRecord,
//                              IsCatalogueAcceptedStatus), but the record-WRITE half is still out
//                              because its parameter type SubmissionResult is head-only. The
//                              AssetSubmitDialog it opens IS ported (Views/Dialogs/AssetSubmitDialog).
//   HandleModDropAsync       - PORTED below (window-wide drop, MainShellWindow.SessionIO.cs):
//                              Core ModService.PeekManifestAsync / App.Mods.InstallModAsync.
//   BuildModCatalogueAsset   - CORRECTION: an earlier revision of this header said ModPackage and
//   SafeDirectorySize          ModManifest are "not in Core". They ARE - CCP.Core/Models/
//                              ModPackage.cs and ModManifest.cs - and CCP.Avalonia gets
//                              Newtonsoft.Json transitively through CCP.Core, so the JObject
//                              envelope compiles here too. What actually blocks these two is
//                              simply that their only caller is ShareModToCatalogueAsync above.
//                              SafeDirectorySize is pure BCL; a helper with no asset to size is
//                              not a restoration.
//   TryBuildPreviewThumb     - BitmapImage + JpegBitmapEncoder, and this one is NOT a straight
//                              rewrite: Avalonia's Bitmap.Save writes PNG only, so the 64 KB JPEG
//                              cap below needs SkiaSharp's encoder reached directly. Avalonia.Skia
//                              is referenced but SkiaSharp is not a direct package reference, and
//                              a .csproj edit is out of this layer's scope.
//   CatalogueSchemaMod       - "ccp-mod/v1", and the two preview caps. Constants with no reader.
//   ModPreviewMaxPixels
//   ModPreviewMaxBytes

using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF HandleModDropAsync (ModCatalogue.cs:84): read the manifest, confirm with
        /// name and author, install, toast the outcome.</summary>
        internal async Task HandleModDropAsync(string ccpmodPath)
        {
            if (App.Mods == null) return;

            var manifest = await ModService.PeekManifestAsync(ccpmodPath);
            if (manifest == null)
            {
                App.Notifications.Show(Loc.GetF("toast_mod_install_failed_fmt", Loc.Get("msg_failed_to_install_mod")),
                    Helpers.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            if (!await Dialogs.MessageDialog.ConfirmAsync(this, Loc.Get("title_install_mod"),
                    Loc.GetF("msg_confirm_install_mod_fmt", manifest.Name, manifest.Author))) return;

            var result = await App.Mods.InstallModAsync(ccpmodPath);
            if (result.Success)
                App.Notifications.Show(Loc.GetF("toast_mod_installed_fmt", manifest.Name),
                    Helpers.NotificationType.Success, TimeSpan.FromSeconds(8));
            else
                App.Notifications.Show(Loc.GetF("toast_mod_install_failed_fmt", result.ErrorMessage ?? Loc.Get("msg_failed_to_install_mod")),
                    Helpers.NotificationType.Error, TimeSpan.FromSeconds(10));
        }
    }
}
