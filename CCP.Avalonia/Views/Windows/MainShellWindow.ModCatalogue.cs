// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.ModCatalogue.cs (191 lines).
//
// Sorted member by member: GENUINELY 100% head-side. The file is two round trips - upload a mod to
// the web catalogue, install one dropped on the window - and every step of both is a service this
// head does not have. Nothing here is layout or settings, so there is no half to restore.
//
// What each member needs, exactly:
//   ShareModToCatalogueAsync - App.Catalogue.SubmitCatalogueAssetAsync
//                              (ConditioningControlPanel/Services/CatalogueService.cs), the auth
//                              token from AppSettings.AuthToken (reachable) plus App.UserDisplayName
//                              (not), App.Notifications.Show
//                              (…/Services/Notifications/NotificationService.cs) for all four
//                              outcome toasts, and RecordCatalogueSubmission /
//                              ShowCatalogueSubmissionResultToast, which are in
//                              MainShellWindow.CatalogueSubmissions.cs - still a stub, and not a
//                              file this layer owns. The AssetSubmitDialog it opens IS ported
//                              (Views/Dialogs/AssetSubmitDialog).
//   HandleModDropAsync       - App.Mods.ReadManifest / InstallModAsync
//                              (ConditioningControlPanel/Services/ModService.cs). CoreMods carries
//                              the READ side of the mod seam (Affirmation, InstalledMods, the
//                              colours); installing an archive is not on it. The MessageBox confirm
//                              maps to Views/Dialogs/MessageDialog.ConfirmAsync, which this head
//                              ships.
//   BuildModCatalogueAsset   - Models.ModPackage / ModManifest
//   SafeDirectorySize          (ConditioningControlPanel/Models/, not in Core). SafeDirectorySize is
//                              pure BCL and would compile here, but a helper with no asset to size
//                              is not a restoration.
//   TryBuildPreviewThumb     - BitmapImage + JpegBitmapEncoder. The Avalonia equivalent is
//                              Avalonia.Media.Imaging.Bitmap.CreateScaledBitmap + Save(stream), so
//                              this one is a real port rather than a seam - it just has no caller
//                              until BuildModCatalogueAsset above has its models.
//   CatalogueSchemaMod       - "ccp-mod/v1", and the two preview caps. Constants with no reader.
//   ModPreviewMaxPixels
//   ModPreviewMaxBytes

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
