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
//                              MainShellWindow.CatalogueSubmissions.cs. That file's READ half is
//                              live now (CatalogueKindMods, GetCatalogueRecord,
//                              IsCatalogueAcceptedStatus), but the record-WRITE half is still out
//                              because its parameter type SubmissionResult is head-only. The
//                              AssetSubmitDialog it opens IS ported (Views/Dialogs/AssetSubmitDialog).
//   HandleModDropAsync       - App.Mods.ReadManifest / InstallModAsync
//                              (ConditioningControlPanel/Services/ModService.cs). CoreMods carries
//                              the READ side of the mod seam (Affirmation, InstalledMods, the
//                              colours); installing an archive is not on it. The MessageBox confirm
//                              maps to Views/Dialogs/MessageDialog.ConfirmAsync, which this head
//                              ships.
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

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
