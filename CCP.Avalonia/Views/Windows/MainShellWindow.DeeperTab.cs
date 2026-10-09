// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperTab.cs (1241 lines).
// Sorted member by member against the fifteen Core seams; the blanket "wired when the services
// move to Core" claim was wrong for the tab's own entry path, which is restored below.
//
// WHAT IS REAL HERE: opening the Deeper tab. BtnDeeper_Click navigates, retires the rail pulse
// flag (HasSeenDeeperTab, CCP.Core/Models/AppSettings.cs:7929) through CoreSettings, and shows or
// hides the welcome card from HasSeenDeeperWelcome (:7935). DismissDeeperWelcomeCard writes the
// other flag and folds the card; the card's tour / demo / dismiss buttons relay to it from
// DeeperTabView.axaml.cs through MainShellWindow.DeeperHub.cs.
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
// LIBRARY ACTIONS (open, play, delete, folder, demo) are in MainShellWindow.DeeperHub.cs.
// STILL OUT, and why:
//   OpenInDeeperPlayer, OpenDeeperEditorFromPlayer, OpenInDeeperEditorForMedia,
//   HandlePendingFileOpen (file-association entry points), OnDeeperLibraryChanged (no watcher),
//   BtnDeeperImport_Click / ImportEnhancementFiles (EnhancementLibrary.FindDuplicateOf /
//   PromoteToLibrary are WPF-only).
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
//   (Not out any more: the catalogue. The submit half is in MainShellWindow.DeeperSubmissions.cs
//   (U3); the lookup -> toast -> picker -> download -> player chain is at the bottom of this file.)
//   IsImportableEnhancementPath is pure and would compile, and is held back with
//   ImportEnhancementFiles, its only caller.
//   MaybePromptMandatoryVideoEnhancement (with its "Don't ask again" fold).
//
// NOT BLOCKED, RESTORED: BtnDeeperNewEnhancement_Click. It used to be listed on the line above with
// the other library relays and that was wrong - the only thing it takes from EnhancementLibrary is
// CreateBlank, which is a three-line construction of Core types
// (ConditioningControlPanel/Services/Deeper/EnhancementLibrary.cs:396), and both windows it needs
// are already on this head (Views/Deeper/NewEnhancementDialog, Views/Deeper/DeeperEditorWindow).

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The Deeper master switch's shell half (WPF MainWindow.Settings.cs:119 on load,
        /// MainWindow.DeeperTab.cs:132 on toggle): the rail entry follows EnableDeeper, and turning
        /// it off while Deeper is the open tab falls back to Settings.
        /// </summary>
        internal void ApplyEnableDeeper()
        {
            var enabled = CoreSettings.Current.EnableDeeper;
            if (Named<Button>("BtnDeeper") is { } door) door.IsVisible = enabled;
            if (!enabled && Named<Control>("DeeperTab") is { IsVisible: true }) ShowTab("settings");
        }

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
            // The rescan rides ShowTab's "deeper" case (SwitchTabFx), as on WPF.
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
        /// "New enhancement": ask for a media type and a source, then open the editor on a blank
        /// project. WPF (MainWindow.DeeperTab.cs:194) sends the dialog's answer through
        /// <c>App.EnhancementLibrary.CreateBlank</c>; that method builds an <c>Enhancement</c> and
        /// nothing else (Services/Deeper/EnhancementLibrary.cs:396), so it is inlined here rather
        /// than holding the door shut on a service move. Everything the editor then does with the
        /// project - the timeline, validation, save, the recent-files write - is already ported
        /// (Views/Deeper/DeeperEditorWindow.axaml.cs and its inlined file-ops region).
        ///
        /// <para>The "deeper_new" bark fires first, where WPF fires it, through
        /// <see cref="CoreBark"/>. The editor goes through OpenDeeperEditor, whose Closed handler
        /// refreshes the hub list as WPF's does.</para>
        /// </summary>
        internal async void BtnDeeperNewEnhancement_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                CoreBark.NotifyUiAction("deeper_new");

                // ShowDialog throws on an owner that is not VISIBLE, and a shell minimised to the
                // tray is loaded but not visible.
                if (!IsVisible) return;

                var dialog = new Views.Deeper.NewEnhancementDialog();
                if (!await dialog.ShowDialogSafe<bool>(this)) return;

                // CreateBlank, inlined. Name is deliberately left empty: the editor's header falls
                // back to the localized "Untitled" until the user (or HT auto-fill) sets one.
                var enhancement = new Models.Deeper.Enhancement
                {
                    MediaType = dialog.SelectedMediaType,
                    MediaSource = dialog.SelectedSource,
                };
                OpenDeeperEditor(enhancement, null);   // closing it refreshes the hub list
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Deeper: opening the editor for a new enhancement failed");
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

        // ============ the catalogue lookup (WPF MainWindow.DeeperTab.cs:1017-1204) ============
        // Fired from OnBrowserNavigationCompleted with the WebHost's live URL, as WPF fires on
        // WebView2's NavigationCompleted (MainWindow.Browser.cs:158).
        private System.Threading.CancellationTokenSource? _catalogueLookupCts;
        private string? _currentCatalogueHtVideoId;

        internal void TriggerCatalogueLookupForNavigation(string url)
        {
            try
            {
                if (!HtUrlHelper.IsEligibleHtUrl(url)) return;
                try { _catalogueLookupCts?.Cancel(); } catch { /* idempotent */ }
                _catalogueLookupCts?.Dispose();
                var cts = new System.Threading.CancellationTokenSource();
                _catalogueLookupCts = cts;
                _ = RunCatalogueLookupAsync(url, cts.Token);
            }
            catch (Exception ex) { Log.Warning(ex, "[Catalogue] TriggerCatalogueLookupForNavigation threw"); }
        }

        internal async System.Threading.Tasks.Task RunCatalogueLookupAsync(string url, System.Threading.CancellationToken ct)
        {
            LookupResult result;
            try { result = await App.CatalogueLookup.LookupForUrlAsync(url, ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Log.Warning(ex, "[Catalogue] Lookup threw unexpectedly"); return; }
            if (ct.IsCancellationRequested) return;
            // None / InvalidUrl / NetworkError: silent by design.
            if (result is LookupResult.Success s) ShowCatalogueLookupToast(url, s.Entries);
        }

        private void ShowCatalogueLookupToast(string url, System.Collections.Generic.List<CatalogueEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;
            var videoId = HtUrlHelper.TryExtractHtVideoId(url);
            _currentCatalogueHtVideoId = videoId;
            var one = entries.Count == 1;
            App.Notifications.Show(
                one ? Loc.Get("catalogue_lookup_toast_one") : string.Format(Loc.Get("catalogue_lookup_toast_many_fmt"), entries.Count),
                Helpers.NotificationType.Info, TimeSpan.FromSeconds(10),
                Loc.Get(one ? "catalogue_lookup_action_use_one" : "catalogue_lookup_action_pick_one"),
                () =>
                {
                    // Stale-toast guard: the user navigated away before clicking.
                    if (!string.Equals(_currentCatalogueHtVideoId, videoId, StringComparison.Ordinal))
                    {
                        Log.Information("[Catalogue] Toast action ignored (user navigated away)");
                        return;
                    }
                    if (one) _ = DownloadAndOpenCatalogueEntryAsync(entries[0]);
                    else OpenCataloguePickerDialog(entries, videoId);
                });
        }

        private async void OpenCataloguePickerDialog(System.Collections.Generic.List<CatalogueEntry> entries, string? videoId)
        {
            try
            {
                var dlg = new Dialogs.CataloguePickerDialog(entries, videoId);
                await dlg.ShowDialogSafe<bool>(this);
                if (dlg.SelectedEntry != null) await DownloadAndOpenCatalogueEntryAsync(dlg.SelectedEntry);
            }
            catch (Exception ex) { Log.Warning(ex, "[Catalogue] Picker dialog threw"); }
        }

        internal async System.Threading.Tasks.Task DownloadAndOpenCatalogueEntryAsync(CatalogueEntry entry)
        {
            DownloadResult result;
            try { result = await App.CatalogueLookup.DownloadAndOpenAsync(entry, default); }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Download flow threw");
                result = new DownloadResult.NetworkError();
            }
            var n = App.Notifications;
            switch (result)
            {
                case DownloadResult.Success:
                    n.Show(string.Format(Loc.Get("catalogue_lookup_toast_loaded_fmt"), entry.Title), Helpers.NotificationType.Info, TimeSpan.FromSeconds(6));
                    break;
                case DownloadResult.NetworkError:
                    n.Show(Loc.Get("catalogue_lookup_toast_download_failed"), Helpers.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.InvalidFile:
                    n.Show(Loc.Get("catalogue_lookup_toast_invalid_file"), Helpers.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.SaveError:
                    n.Show(Loc.Get("catalogue_lookup_toast_save_failed"), Helpers.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.OpenError oe:
                    n.Show(string.Format(Loc.Get("catalogue_lookup_toast_open_failed_fmt"), oe.LocalFilename),
                        Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10),
                        Loc.Get("catalogue_lookup_action_open_library"), () => ShowTab("deeper"));
                    break;
            }
        }

        /// <summary>WPF's opener (MainWindow.xaml.cs:674): a downloaded enhancement auto-plays in
        /// the player, tagged "catalogue". False on a parse failure -> the OpenError toast.</summary>
        private bool OpenCatalogueEnhancement(string path)
        {
            try
            {
                var enhancement = ConditioningControlPanel.Services.Deeper.EnhancementSerializer.LoadFromFile(path);
                ShowOrActivateDeeperPlayer().LoadEnhancementFromMemory(enhancement, "catalogue");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Player open failed for {Path}", path);
                return false;
            }
        }
    }
}
