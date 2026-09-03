// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperSubmissions.cs (183 lines).
//
// The Deeper-specific twin of MainShellWindow.CatalogueSubmissions.cs, whose read half IS restored.
// This one is not, and the reason is not "the models are missing" - AppSettings.DeeperSubmissions
// (AppSettings.cs:2337) and DeeperSubmissionRecord are both in Core. It is that this file has no
// read half worth extracting:
//
//   IsAcceptedStatus, CanonicalSubmissionKey
//       Byte-identical bodies to IsCatalogueAcceptedStatus / CanonicalCataloguePathKey, which are
//       now live one file over. Nothing outside this file calls the Deeper copies on WPF either
//       (grepped), so restoring them would add a second spelling of a test this head already has.
//       Whoever wires the Deeper library badge should call the catalogue pair.
//   RecordDeeperSubmission(filePath, SubmissionResult)
//       Blocked on its PARAMETER TYPE, not its body: SubmissionResult is
//       ConditioningControlPanel/Services/CatalogueService.cs:518, head-only. It also ends by
//       calling ApplyDeeperFilterAndSort, which is a stub in MainShellWindow.DeeperHub.cs.
//   CheckDeeperSubmissionStatusesAsync(force)
//       App.Catalogue.FetchMySubmissionsAsync - a network round trip with no seam. Its three
//       pacing members (_lastSubmissionCheckUtc, SubmissionCheckThrottle, _submissionCheckInFlight)
//       exist only to throttle that call and go with it.
//   NotifyDeeperSubmissionAccepted(catalogueId, canonicalPath)
//       App.Notifications.ShowSticky, which this head does not ship, plus _deeperAllEntries and
//       SwitchToDeeperLibraryTab (both stubs, MainShellWindow.DeeperHub.cs / DeeperTab.cs). Its
//       loc keys are real and portable: deeper_submission_accepted_toast_fmt via Loc.GetF, and
//       deeper_submission_accepted_action_view for the View action.
//
// Checked and NOT the blocker: CoreReleaseContent. Pack ids, install stamps and pack info are not
// read anywhere in this flow.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
