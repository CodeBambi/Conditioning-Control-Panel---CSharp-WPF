// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.Presets.cs (2316 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (87):
//   private void SetupHelpButtons(…)
//   private void SetHelpContent(…)
//   private void HelpVideoButton_Click(…)
//   private Models.Preset? _selectedPreset
//   private List<Models.Preset> _allPresets
//   private void InitializePresets(…)
//   private void RefreshPresetsDropdown(…)
//   private void CmbPresets_SelectionChanged(…)
//   private void RefreshPresetsList(…)
//   private void RefreshPresetsModVisuals(…)
//   private Border CreatePresetCard(…)
//   private Style? TryFindTabStyle(…)
//   private void AddStatIcon(…)
//   private string GetPresetQuickStats(…)
//   private void SelectPreset(…)
//   internal void SessionCard_Click(…)
//   private Models.Session? _selectedSession
//   internal void ChkCornerGifEnabled_Changed(…)
//   internal void BtnSelectCornerGif_Click(…)
//   private System.Windows.Threading.DispatcherTimer? _cornerGifSizeDebounce
//   internal void SliderCornerGifSize_ValueChanged(…)
//   internal void RbCornerPos_Checked(…)
//   internal void SliderCornerGifOpacity_ValueChanged(…)
//   private string _selectedCornerGifPath
//   private Models.CornerPosition GetSelectedCornerPosition(…)
//   internal void BtnRevealSpoilers_Click(…)
//   private bool ShowStyledDialog(…)
//   private MediaDropChoice ShowMediaDropChoiceDialog(…)
//   private Features.FeaturePopupWindow? _activeFeaturePopup
//   private void ShowFeaturePopup(…)
//   internal void CardFlash_Click(…)
//   internal void CardSubliminal_Click(…)
//   internal void CardBouncingText_Click(…)
//   internal void CardBubblePop_Click(…)
//   internal void CardLockCard_Click(…)
//   internal void CardMystery_Click(…)
//   internal void CardVault_Click(…)
//   internal void CardJustDrop_Click(…)
//   internal void RefreshMosaicTierBadges(…)
//   internal void RefreshMysteryTile(…)
//   private static string? MysteryFeatureName(…)
//   private static int MysteryFeatureTier(…)
//   private static string MysteryFeatureArtPath(…)
//   internal void ToggleWallFeature(…)
//   internal static bool IsWallFeatureOn(…)
//   internal void SetWallFeature(…)
//   private void OnSettingsPropertyChangedForWall(…)
//   internal void RefreshWallActiveStates(…)
//   private static void SetTierBadge(…)
//   internal void CardSystem_Click(…)
//   internal void VelvetBtnWebcam_Click(…)
//   internal void VelvetBtnAppInfo_Click(…)
//   internal void VelvetBtnSchedulerRamp_Click(…)
//   internal void BtnCatalogue_Click(…)
//   internal void BtnSessionHistory_Click(…)
//   internal void BtnStartSession_Click(…)
//   private async void StartSession(…)
//   private void OnSessionCompleted(…)
//   private DateTime _suppressSessionSummaryUntil
//   internal void SuppressNextSessionSummary(…)
//   private void OnSessionLogReady(…)
//   private SessionCompleteWindow? _liveSessionRecap
//   private bool _liveSessionRecapTeardownHooked
//   private void CloseLiveSessionRecap(…)
//   private void ShowSessionSummaryWhenClear(…)
//   private void OnSessionProgressUpdated(…)
//   private void OnSessionPhaseChanged(…)
//   private void OnSessionStarted(…)
//   private void OnSessionStopped(…)
//   private void BtnStopSession_Click(…)
//   private void BtnPauseSession_Click(…)
//   public void ApplySessionSettings(…)
//   public void UpdateSpiralOpacity(…)
//   public void EnablePinkFilter(…)
//   public void EnableSpiral(…)
//   public void UpdatePinkFilterOpacity(…)
//   public void EnableBrainDrain(…)
//   public void UpdateBrainDrainIntensity(…)
//   public void SetBubblesActive(…)
//   private void HandleHyperlinkClick(…)
//   private void LoadPreset(…)
//   private void ReconcileRunningServices(…)
//   internal void BtnLoadPreset_Click(…)
//   internal void BtnNewPreset_Click(…)
//   private void PromptSaveNewPreset(…)
//   internal void BtnSaveOverPreset_Click(…)
//   internal void BtnDeletePreset_Click(…)

// ONE member of that list is NOT blocked and is restored below: OpenStudioModule. It is
// ShowTab("studio") + StudioTab.FocusRackEntry(key), and both halves are on this head - ShowTab is
// real (MainShellWindow.TabNavigation.cs) and StudioTabView.FocusRackEntry is a full port. It is
// the single editor entry every mosaic tile and the Play door's Loom card route through, so
// leaving it stubbed meant every one of them landed on whichever module the rack had selected last.

using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Selects one module of the Studio rack and shows the Studio tab.
        ///
        /// <para><b>Haptics is the one rack key that must NOT come through the rack row.</b> It is
        /// the only module that is also a ShowTab key, and everything it owns hangs off that key
        /// rather than off the row - the bark (<c>NotifyTabNavigated("haptics")</c>) and the
        /// first-visit intro card both fire from the top of ShowTab with the INCOMING key. Routing
        /// it as a plain module would land on the right panel while silently saying nothing.
        /// ShowTab("haptics") selects the same row itself, so the landing is identical.</para>
        ///
        /// <para>ponytail: WPF opens with <c>Services.EmiDesk.EmiTargets.NoteRackOpened(rackKey)</c>,
        /// so opening Flashes scores Flashes rather than the Studio. EmiTargets is not in Core
        /// (CCP.Core/Services/EmiDesk holds only the chrome/layout half), so the scoring is the one
        /// thing missing here.</para>
        /// </summary>
        internal void OpenStudioModule(string rackKey)
        {
            if (string.Equals(rackKey, "haptics", StringComparison.OrdinalIgnoreCase))
            {
                ShowTab("haptics");
                return;
            }
            try { StudioRack?.FocusRackEntry(rackKey); }
            catch (Exception ex) { Log.Debug("OpenStudioModule({Key}): {E}", rackKey, ex.Message); }
            ShowTab("studio");
        }

        // ponytail: needs the services in MainWindow.Presets.cs; wired when they move to Core.
        private void BtnCatalogue_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.Presets.cs; wired when they move to Core.
        private void BtnPauseSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.Presets.cs; wired when they move to Core.
        private void CmbPresets_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e) { }

    }
}
