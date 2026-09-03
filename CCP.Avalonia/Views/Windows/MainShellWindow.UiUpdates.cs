// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.UiUpdates.cs (2904 lines).
// Sorted member by member against the fifteen Core seams. One member is real; the blanket claim
// was wrong for it and is right for most of the rest, with three exceptions worth naming.
//
// WHAT IS REAL: BtnAccountChip_Click. WPF's body is ShowTab("appsettings") +
// AppSettingsTab.FocusSection("account"), and BOTH halves already ship here as
// OpenAppSettingsSection (MainShellWindow.Settings.cs:65) - "account" is one of the nine keys in
// AppSettingsTabView.SectionKeys. It routes through that helper rather than repeating the pair,
// which also means it gets the helper's Named<T> lookup: the generated AppSettingsTab field on
// this window is permanently null (AvaloniaXamlLoader.Load), so AppSettingsTab?.FocusSection(...)
// transcribed from WPF would have compiled, rendered, reviewed clean and done nothing.
//
// NOT BLOCKED, MOVED - the settings toggles. SliderMaster_Changed, SliderVideoVolume_Changed,
// SliderDuck_Changed, ChkAudioDuck_Changed, ChkExcludeBambiCloudDucking_Changed,
// CmbAudioOutputDevice_SelectionChanged, BtnAudioOutputRefresh_Click, BtnTestAudio_Click,
// PopulateAudioOutputDevices, ChkPerformanceMode_Changed, ChkAutoPerformance_Changed,
// CmbMotionLevel_SelectionChanged, ChkVideoHwDecode_Changed, ChkUnifiedOverlay_Changed,
// ChkPanicOverridesAll_Changed, ChkNoPanic_Changed, ChkWinStart_Click, ChkStartHidden_Click,
// BtnPauseKey_Click, BtnPanicKey_Click, BtnSelectStartupVideo_Click, BtnClearStartupVideo_Click and
// ChkOfflineMode_Changed were all "MainWindow owns the handler, the section relays to it" on WPF.
// On this head each section owns its own handler and its own _isLoading guard:
// Views/Controls/AppSettings/{Audio,Performance,General,Devices,Data}SettingsSection.axaml.cs.
// Adding any of them back here would be a SECOND writer for one setting, not a restoration.
//
// PURE AND PORTABLE, LEFT OUT ANYWAY because they are orphans on this head:
//   ModAwareLabel(english, locKey) - CoreMods.MakeModAware answers it, so it compiles today. Its
//     consumer, StudioTabView, already carries its own inlined twin
//     (Views/Tabs/StudioTabView.axaml.cs:1105-1131) precisely because a UserControl cannot reach a
//     private on the shell. A second public copy with no caller is duplication, not progress.
//   StripLeadingGlyph - same story, same file, already inlined there.
//   FormatFileSize - its only callers are the assets-folder rows, which are not ported.
//
// STILL OUT, by blocker:
//   App.Progression / the XP bar - UpdateLevelDisplay, UpdateXPBarLoginState, UpdateStatPills,
//     RefreshXPBarBonuses, GetBonusChipTooltip, StartStatPillUpdateTimer, XPBarTrack_ToolTipOpening,
//     RefreshAccountChip and the three AccountChip* brushes. CoreProgression is a seam, but the
//     chip and the pills read App.Account / App.Patreon tier state on top of it, and a chip that
//     paints the logged-out tier unconditionally is the same failure recorded for
//     UpdateSubscribeStarUI in MainShellWindow.SubscribeStar.cs.
//   The conditioning-time tracker - StartConditioningTimeTracker, StopConditioningTimeTracker,
//     SyncConditioningTimeToServerAsync. A server round trip with no seam.
//   UpdateUI / QueueModAwareSurfaceSweep / RefreshModAwareSurfaces / SweepStep /
//     EnsureModSweepWatchers / ApplyModFeatureNames / ApplyBimboJournalModVisibility - the sweep
//     walks named controls across every tab, most of which are unported; it is a pass over the
//     finished UI, so it lands last, not first.
//   UpdateUnlockablesVisibility / SetFeatureImageBlur - App.Unlockables plus a WPF BlurEffect.
//   The Intake Pass tile - RefreshIntakePassTile, SetIntakePassFace, ApplyIntakePassFaceState,
//     StartIntakePassCtaPulse, StopIntakePassCtaPulse, EnsureIntakePassHelpPopover,
//     BuildIntakePassHelpContent, CancelIntakePassSpin, StartIntakePassFlipLoop, HoldIntakePassFace,
//     RunIntakePassSpinPhase, FinishIntakePassSpin, IntakePassFace_MouseLeftButtonDown and their
//     eleven state flags. App.IntakePass, plus a WPF 3D card flip.
//   ImgLogo_MouseLeftButtonDown / ShowEasterEgg / TriggerStartupVideo - a media window each.
//   BtnManageAttention_Click, BtnAttentionStyle_Click, BtnSubliminalSettings_Click,
//     BtnManageMessages_Click, BtnViewLog_Click, BtnPrevImage_Click, BtnNextImage_Click,
//     BtnRefreshAssets_Click - each opens an unported window or drives the unported asset tree.
//   SetOfflineDisabled / UpdateOfflineModeUI / DisconnectNetworkServices - the toggle itself moved
//     (DataSettingsSection.axaml.cs:67, which persists OfflineMode and asks for the offline name),
//     but the TEARDOWN did not: App.Account, App.RemoteControl and the update checker are the
//     things a disconnect disconnects, and none of them exists here to disconnect. The greying
//     pass, SetOfflineDisabled, walks named controls on unported tabs.
//   BtnPickAssetsFolder_Click / CopyDirectoryRecursive - the picker itself already ships as
//     MainShellWindow.Settings.cs RequestPickAssetsFolder, called from SystemFeatureControl. What
//     is missing here is only the WPF handler shell around it and the copy-the-old-library
//     migration, which needs the assets tree to say what it copied.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The header's account chip. Opens the Settings door and scrolls it to Account - no popup
        /// and no reparenting, same as WPF (MainWindow.UiUpdates.cs). The scroll deliberately
        /// happens after the tab is shown, because a section can only be measured once visible;
        /// OpenAppSettingsSection keeps that order and swallows a failed scroll.
        /// </summary>
        private void BtnAccountChip_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => OpenAppSettingsSection("account");
    }
}
