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
//       Was blocked on its parameter type; SubmissionResult is Core's since catalogue U1
//       (CCP.Core/Services/Catalogue/CatalogueClient.cs). It also ends by
//       calling ApplyDeeperFilterAndSort, which is a stub in MainShellWindow.DeeperHub.cs.
//   CheckDeeperSubmissionStatusesAsync / NotifyDeeperSubmissionAccepted
//       Ported into MainShellWindow.CatalogueStatus.cs (/api/enhancements/mine poll, one-time
//       accepted toast with a View action; not sticky - this head has no ShowSticky).
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
