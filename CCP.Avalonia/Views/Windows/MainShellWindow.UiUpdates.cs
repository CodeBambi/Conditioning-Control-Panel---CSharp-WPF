// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.UiUpdates.cs (2904 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (99):
//   private void UpdateUI(…)
//   private void UpdateLevelDisplay(…)
//   private void ApplyModFeatureNames(…)
//   private bool _modSweepQueued
//   private bool _modSweepWatchersHooked
//   private bool _presetsModDirty
//   private void QueueModAwareSurfaceSweep(…)
//   private void RefreshModAwareSurfaces(…)
//   private static void SweepStep(…)
//   private void EnsureModSweepWatchers(…)
//   internal static string ModAwareLabel(…)
//   private static string StripLeadingGlyph(…)
//   private void ApplyBimboJournalModVisibility(…)
//   private void UpdateXPBarLoginState(…)
//   private static SolidColorBrush AccountChipBrush(…)
//   private static readonly SolidColorBrush AccountChipTier1Brush
//   private static readonly SolidColorBrush AccountChipTier2Brush
//   private static readonly SolidColorBrush AccountChipNeutralBrush
//   private void RefreshAccountChip(…)
//   private void BtnAccountChip_Click(…)
//   private void UpdateStatPills(…)
//   private void RefreshXPBarBonuses(…)
//   private static string? GetBonusChipTooltip(…)
//   private void StartStatPillUpdateTimer(…)
//   private void StartConditioningTimeTracker(…)
//   private void StopConditioningTimeTracker(…)
//   private async Task SyncConditioningTimeToServerAsync(…)
//   private void UpdateUnlockablesVisibility(…)
//   private void SetFeatureImageBlur(…)
//   internal void SliderMaster_Changed(…)
//   internal void SliderVideoVolume_Changed(…)
//   internal void SliderDuck_Changed(…)
//   internal void ChkAudioDuck_Changed(…)
//   internal void ChkExcludeBambiCloudDucking_Changed(…)
//   private bool _testingAudio
//   internal async void BtnTestAudio_Click(…)
//   private bool _populatingAudioOutputs
//   private void PopulateAudioOutputDevices(…)
//   internal void CmbAudioOutputDevice_SelectionChanged(…)
//   internal void BtnAudioOutputRefresh_Click(…)
//   internal void ImgLogo_MouseLeftButtonDown(…)
//   private const int IntakePassFaceHoldMs
//   private static readonly int[] IntakePassHalfTurnMs
//   private const double IntakePassSkewDeg
//   private int _intakePassSpinGen
//   private bool _intakePassLoopRunning
//   private bool _intakePassShowingCard
//   private bool _intakePassLoadedHooked
//   private bool _intakePassStateHooked
//   private bool _intakePassVisibilityHooked
//   private bool _intakePassModHooked
//   private bool _intakePassHelpAttached
//   private static readonly TimeSpan IntakePassCtaBreath
//   private bool _intakePassCtaShouldPulse
//   private bool _intakePassCtaPulsing
//   internal void RefreshIntakePassTile(…)
//   private void SetIntakePassFace(…)
//   private void ApplyIntakePassFaceState(…)
//   private void StartIntakePassCtaPulse(…)
//   private void StopIntakePassCtaPulse(…)
//   private void EnsureIntakePassHelpPopover(…)
//   private static HelpContent BuildIntakePassHelpContent(…)
//   private void CancelIntakePassSpin(…)
//   private void StartIntakePassFlipLoop(…)
//   private void HoldIntakePassFace(…)
//   private void RunIntakePassSpinPhase(…)
//   private void FinishIntakePassSpin(…)
//   internal void IntakePassFace_MouseLeftButtonDown(…)
//   private async void ShowEasterEgg(…)
//   private void TriggerStartupVideo(…)
//   internal void BtnSelectStartupVideo_Click(…)
//   internal void BtnClearStartupVideo_Click(…)
//   internal void BtnManageAttention_Click(…)
//   internal void BtnAttentionStyle_Click(…)
//   internal void BtnSubliminalSettings_Click(…)
//   internal void BtnManageMessages_Click(…)
//   private void BtnPrevImage_Click(…)
//   private void BtnNextImage_Click(…)
//   internal void BtnPickAssetsFolder_Click(…)
//   private static void CopyDirectoryRecursive(…)
//   private static string FormatFileSize(…)
//   internal void BtnRefreshAssets_Click(…)
//   private void BtnViewLog_Click(…)
//   internal void ChkPanicOverridesAll_Changed(…)
//   internal void BtnPauseKey_Click(…)
//   internal void BtnPanicKey_Click(…)
//   internal void ChkNoPanic_Changed(…)
//   internal void ChkPerformanceMode_Changed(…)
//   internal void ChkAutoPerformance_Changed(…)
//   internal void CmbMotionLevel_SelectionChanged(…)
//   internal void ChkVideoHwDecode_Changed(…)
//   internal void ChkUnifiedOverlay_Changed(…)
//   internal void ChkOfflineMode_Changed(…)
//   private static void SetOfflineDisabled(…)
//   private void UpdateOfflineModeUI(…)
//   private void DisconnectNetworkServices(…)
//   internal void ChkWinStart_Click(…)
//   internal void ChkStartHidden_Click(…)
//   private void XPBarTrack_ToolTipOpening(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.UiUpdates.cs; wired when they move to Core.
        private void BtnAccountChip_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
