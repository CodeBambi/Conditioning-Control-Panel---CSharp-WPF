// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperTab.cs (1241 lines).
// Sorted member by member against the fifteen Core seams; the blanket "wired when the services
// move to Core" claim was wrong for the tab's own entry path, which is restored below.
//
// WHAT IS REAL HERE: opening the Deeper tab. BtnDeeper_Click navigates, retires the rail pulse
// flag (HasSeenDeeperTab, CCP.Core/Models/AppSettings.cs:7929) through CoreSettings, and shows or
// hides the welcome card from HasSeenDeeperWelcome (:7935). DismissDeeperWelcomeCard writes the
// other flag and folds the card. NOTHING CALLS DismissDeeperWelcomeCard YET - its three WPF
// callers (BtnDeeperWelcomeTour / Demo / Dismiss) relay from DeeperTabView.axaml.cs, which this
// layer does not own; it is internal so that relay needs no Core change when it lands.
//
// THE x:NAME HAZARD APPLIES TWICE HERE, and both hops go through FindControl for that reason.
// This window loads with AvaloniaXamlLoader.Load(this), so "DeeperTab" is reached with Named<T>.
// DeeperTabView loads the same way (DeeperTabView.axaml.cs:24), so its DeeperWelcomeCard field is
// permanently null even though the .axaml marks it x:FieldModifier="internal" - that modifier
// invites exactly the write that would silently do nothing. FindControl on the view reads its own
// name scope and is the only form that works.
//
// NOT BLOCKED, MOVED: ChkEnableDeeper_Changed. The Deeper master switch is now owned by
// Views/Controls/AppSettings/GeneralSettingsSection.axaml.cs:142, which persists EnableDeeper and
// guards its own programmatic sets. It is not this partial's job any more; a second copy here
// would be a second writer for one setting.
//
// STILL OUT, and why:
//   The library, the player and the editor - OpenDeeperFile, OpenInDeeperPlayer,
//   OpenDeeperEditorFromPlayer, OpenInDeeperEditorForMedia, RefreshDeeperLibraryUI,
//   OnDeeperLibraryChanged, BtnDeeperImport_Click, ImportEnhancementFiles, DeleteDeeperLibraryEntry,
//   OpenDeeperBundledDemo, HandlePendingFileOpen. All of them go through App.EnhancementLibrary
//   (ConditioningControlPanel/Services/Deeper/EnhancementLibrary.cs). The Deeper windows on this
//   head reach a library of their own; the SHELL has no reference to one and there is no seam.
//   InitializeDeeperHub / ReloadDeeperLibraryFromDisk are stubs in MainShellWindow.DeeperHub.cs,
//   which this layer does not own, so BtnDeeper_Click leaves that pair out rather than half-scan.
//   The browser half - OnDeeperBrowserBound/Unbound, RefreshBrowserWebcamButton,
//   BtnWebcamTracking_Click, MaybePromptBrowserWebcamForEnhancement, OnBrowserEnhanceMatchChanged,
//   ChkForceShowBambiCloud_Changed, ToggleEnhanceIfPossible_Changed. WebView2 plus WebcamTrackingState.
//   BtnWebcamTracking_Click is also a REFUSAL and not just a wait: its portable half flips a caption,
//   its unportable half opens or closes the camera, and the same judgement recorded for the two
//   status pills in MainShellWindow.LabTab.cs holds - a control that reports tracking with no
//   camera behind it is worse than one that does nothing.
//   The tutorial - StartDeeperTabTutorial, BtnDeeperTutorial_Click, BtnDeeperWelcomeTour_Click.
//   StartTutorial(TutorialType.Deeper) is not on this head.
//   The rail pulse - StartDeeperTabPulse / StopDeeperTabPulse, a WPF storyboard on BtnDeeper.
//   The catalogue - TriggerCatalogueLookupForNavigation, RunCatalogueLookupAsync,
//   OpenCataloguePickerDialog, DownloadAndOpenCatalogueEntryAsync, SubmitDeeperLibraryEntryAsync,
//   ShowCatalogueLookupToast, ShowCatalogueSubmissionResultToast, IsCatalogueEligible. App.Catalogue
//   plus App.Notifications, the same pair MainShellWindow.CatalogueSubmissions.cs left out.
//   IsImportableEnhancementPath is pure and would compile, and is held back with
//   ImportEnhancementFiles, its only caller.
//   SwitchToDeeperLibraryTab, MaybePromptMandatoryVideoEnhancement, BtnDeeperOpenPlayer_Click,
//   BtnDeeperNewEnhancement_Click, BtnDeeperOpenLibraryFolder_Click - all relays into the above.

using System;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The rail's Deeper entry. Navigates, then retires the "you have not opened this yet"
        /// pulse flag exactly where WPF does - on the first open, not on install - so the rail
        /// stops nagging even though the pulse animation itself is not on this head.
        /// </summary>
        private void BtnDeeper_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            ShowTab("deeper");

            var s = CoreSettings.Current;          // never null; the seam hands over a real instance
            if (!s.HasSeenDeeperTab)
            {
                s.HasSeenDeeperTab = true;
                CoreSettings.Save();
            }

            UpdateDeeperWelcomeCardVisibility();
            // ponytail: WPF also calls InitializeDeeperHub() + ReloadDeeperLibraryFromDisk() here.
            // Both are stubs in MainShellWindow.DeeperHub.cs (App.EnhancementLibrary), not owned
            // by this layer - a half-scan that filled the list from nothing would read as an empty
            // library rather than an unported one.
        }

        /// <summary>
        /// Shows the first-run welcome card until it has been dismissed once. Reached through
        /// FindControl on the view, NOT through its x:FieldModifier="internal" field: DeeperTabView
        /// loads with AvaloniaXamlLoader.Load(this), so that field is permanently null and a write
        /// to it would compile, review clean and do nothing forever.
        ///
        /// The card's IsVisible ALSO carries {Binding ShowWelcomeCard} (DeeperTabView.axaml:444).
        /// That binding is one-shot today - ShowWelcomeCard is a get-only `=> true` on a placeholder
        /// view model with no change notification (DeeperTabView.axaml.cs:172), so it evaluates once
        /// at load and never pushes again, and this local set wins and stays won. When that view
        /// model becomes real and starts raising PropertyChanged, this write must move INTO it:
        /// Avalonia keeps a binding alive under a local value, so the next push would silently undo
        /// the line below (CLAUDE.md, "Porting a WPF view to Avalonia").
        /// </summary>
        private void UpdateDeeperWelcomeCardVisibility()
        {
            try
            {
                var card = Named<Tabs.DeeperTabView>("DeeperTab")?.FindControl<Border>("DeeperWelcomeCard");
                if (card is null) return;
                card.IsVisible = !CoreSettings.Current.HasSeenDeeperWelcome;
            }
            catch (Exception ex)
            {
                Log.Debug("UpdateDeeperWelcomeCardVisibility failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Folds the welcome card and remembers it. Internal because its three callers are the
        /// tour / demo / dismiss buttons on DeeperTabView, which relay into the shell on WPF and
        /// are stubs on this head - nothing calls this yet.
        /// </summary>
        internal void DismissDeeperWelcomeCard()
        {
            var s = CoreSettings.Current;
            if (!s.HasSeenDeeperWelcome)
            {
                s.HasSeenDeeperWelcome = true;
                CoreSettings.Save();
            }
            UpdateDeeperWelcomeCardVisibility();
        }
    }
}
