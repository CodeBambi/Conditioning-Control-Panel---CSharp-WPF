// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Presets.cs (2316 lines).
//
// The old blanket header claimed every member reaches App.*, a service, a device, WebView2 or
// Win32. Sorted member by member that is wrong three ways, and the three matter:
//
//   1. THE PRESET DROPDOWN IS DEAD ON BOTH HEADS. CmbPresets carries Visibility="Collapsed" in
//      MainWindow.xaml:1830 and IsVisible="False" here, and NOTHING in either head ever shows it -
//      grep for CmbPresets.Visibility returns no writes at all. So InitializePresets,
//      RefreshPresetsDropdown and the load-confirm inside CmbPresets_SelectionChanged would
//      populate a control no user can open. Every dependency of those three is available
//      (Models.Preset with GetDefaultPresets/ApplyTo and AppSettings.UserPresets are in Core,
//      CoreMods.MakeModAware and AccentColorHex answer) - they are skipped because there is
//      nothing to show, not because anything is missing. The LIVE preset surface is the chip rail
//      on PresetsTabView (RefreshPresetsList / CreatePresetCard / SelectPreset), which paints into
//      PresetsTab.PresetCardsPanel and PresetsTab's detail pane; that is a view this layer does
//      not own, and restoring the rail here would be a second copy no click can reach.
//
//   2. The eleven Dashboard wall toggles resolve completely and are restored below.
//
//   3. BtnCatalogue_Click is a browser launch, not a service call, and is restored below.
//
// The rest of the list is genuinely blocked, for the reasons the old header gave: the session
// engine (start/stop/pause and every OnSession* callback), the overlay services the feature cards
// drive, FeaturePopupWindow, the corner-GIF picker, and the styled/media-drop dialogs.
//
// The handlers named by MainShellWindow.axaml are real methods, because a missing one is a XAML
// compile error, not a runtime gap.
//
// Members dropped (87 - the four now restored are marked RESTORED):
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
//   internal void ToggleWallFeature(…)                 RESTORED
//   internal static bool IsWallFeatureOn(…)            RESTORED
//   internal void SetWallFeature(…)                    RESTORED
//   private void OnSettingsPropertyChangedForWall(…)
//   internal void RefreshWallActiveStates(…)
//   private static void SetTierBadge(…)
//   internal void CardSystem_Click(…)
//   internal void VelvetBtnWebcam_Click(…)
//   internal void VelvetBtnAppInfo_Click(…)
//   internal void VelvetBtnSchedulerRamp_Click(…)
//   internal void BtnCatalogue_Click(…)                RESTORED
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

// OpenStudioModule was the first member restored here, and is not blocked either: it is
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

        /// <summary>
        /// The Catalogue entry on the Library door. WPF opens the web catalogue with
        /// <c>Process.Start(UseShellExecute)</c>; Avalonia's <c>TopLevel.Launcher</c> is this
        /// head's equivalent and works on Linux, so this is a port rather than a seam - the same
        /// swap Views/Dialogs/UpdateFailedDialog and LoginDialog already make.
        /// </summary>
        private async void BtnCatalogue_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try { await Launcher.LaunchUriAsync(new Uri("https://app.cclabs.app/catalogue")); }
            catch (Exception ex) { Log.Warning(ex, "Failed to open CCP Catalogue URL"); }
        }

        // =====================================================================================
        //  the Dashboard wall toggles
        // =====================================================================================
        // The eleven mosaic card toggles. Restored because every dependency answers: the flags are
        // AppSettings fields (Core), the save is CoreSettings.Save, the refusal is
        // MainShellWindow.SessionFeatureLock.cs, and the "also start or stop the live service"
        // half is gated on App.IsEngineRunning - which is CoreSession.IsEngineRunning here, and
        // FALSE on this head. So that branch is not dropped or faked: it is taken correctly, and
        // with no engine running the persisted flag IS the whole truth of the feature's state.
        // Nothing here can lie about a service that is running, because none can be.
        // Two of the eleven are not gated that way: WPF's spiral and pinkfilter cases call
        // App.Overlay.RefreshOverlays() UNCONDITIONALLY (MainWindow.Presets.cs:1329-1330), because
        // the overlays repaint from settings rather than being started and stopped. There is no
        // overlay service on this head either, so the outcome is the same - write the flag, save -
        // but the reason differs, and it is the line to re-read when the overlays land.
        //
        // ponytail: SettingsTabView's thirteen Card*_Toggle / Combo*_Toggle stubs are what call
        // these on WPF, and they are still stubs (Views/Tabs/SettingsTabView.axaml.cs:270-295,
        // each already carrying in a comment the mw.ToggleWallFeature("<key>") it wants). That
        // view is not this layer's, so these three have no caller yet - one line per stub.
        //
        // Still missing from this group, and all for the same reason: RefreshWallActiveStates,
        // RefreshMosaicTierBadges, RefreshMysteryTile, SetTierBadge and
        // OnSettingsPropertyChangedForWall PAINT the tiles, so they need SettingsTabView's card
        // controls (and the tier badges additionally need App.Patreon). The state is correct
        // here; showing it stays with the view that owns the cards.

        /// <summary>The persisted flag behind a wall key. Unknown key = false.</summary>
        internal static bool IsWallFeatureOn(string key)
        {
            var s = CoreSettings.Current;
            return key switch
            {
                "flash" => s.FlashEnabled,
                "video" => s.MandatoryVideosEnabled,
                "subliminal" => s.SubliminalEnabled,
                "spiral" => s.SpiralEnabled,
                "pinkfilter" => s.PinkFilterEnabled,
                "bubbles" => s.BubblesEnabled,
                "lockcard" => s.LockCardEnabled,
                "bubblecount" => s.BubbleCountEnabled,
                "bouncingtext" => s.BouncingTextEnabled,
                "mindwipe" => s.MindWipeEnabled,
                "braindrain" => s.BrainDrainEnabled,
                _ => false,
            };
        }

        /// <summary>
        /// Sets a wall feature to an explicit state and persists it. The one seam every
        /// programmatic flip goes through - the wall card via <see cref="ToggleWallFeature"/>, and
        /// on WPF the lockdown Dose keeper too. Deliberately does NOT consult the session feature
        /// lock: the caller decides, exactly as in WPF.
        /// </summary>
        internal void SetWallFeature(string key, bool on)
        {
            var s = CoreSettings.Current;
            try
            {
                switch (key)
                {
                    case "flash": s.FlashEnabled = on; break;
                    case "video": s.MandatoryVideosEnabled = on; break;
                    case "subliminal": s.SubliminalEnabled = on; break;
                    case "spiral": s.SpiralEnabled = on; break;
                    case "pinkfilter": s.PinkFilterEnabled = on; break;
                    case "bubbles": s.BubblesEnabled = on; break;
                    case "lockcard": s.LockCardEnabled = on; break;
                    case "bubblecount": s.BubbleCountEnabled = on; break;
                    case "bouncingtext": s.BouncingTextEnabled = on; break;
                    case "mindwipe": s.MindWipeEnabled = on; break;
                    case "braindrain": s.BrainDrainEnabled = on; break;
                    default: return;   // unknown key: no write, no save
                }
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SetWallFeature({Key},{On}) failed", key, on);
            }
        }

        /// <summary>Flips one wall card, refusing while a session holds the feature lock.</summary>
        internal void ToggleWallFeature(string key)
        {
            if (RefuseIfSessionFeatureLocked($"card:{key}")) return;
            SetWallFeature(key, !IsWallFeatureOn(key));
        }

        // ---- still blocked -------------------------------------------------------------------

        // Pause needs the session engine itself (_sessionEngine.Pause/ResumeSession and its
        // PauseCount for the tooltip) plus App.Lockdown.IsActive for the refusal, neither on this
        // head. The middle third of it - the "costs XP" confirm over AppSettings.SkipPauseXpWarning
        // and Views/Dialogs/MessageDialog - would port today, but a confirm in front of a pause
        // that never happens is worse than a button that does nothing.
        private void BtnPauseSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // Empty, and not because anything is missing: CmbPresets is collapsed on both heads (see
        // item 1 in the header), so this handler cannot fire. Loading a preset for real would also
        // need WPF's LoadSettings() re-seed of every open editor, which on this head is spread
        // across the tab views rather than owned by the window.
        private void CmbPresets_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e) { }

    }
}
