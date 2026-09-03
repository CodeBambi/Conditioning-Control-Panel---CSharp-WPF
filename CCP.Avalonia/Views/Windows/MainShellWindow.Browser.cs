// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.Browser.cs (3084 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (66):
//   private bool _browserInitializing
//   private bool _browserCorePending
//   private async System.Threading.Tasks.Task InitializeBrowserAsync(…)
//   internal async void BrowserLoadingText_Click(…)
//   private void TearDownBrowserForReinit(…)
//   private void FocusBrowserSurface(…)
//   private async System.Threading.Tasks.Task InitAndNavigateAsync(…)
//   private async System.Threading.Tasks.Task NavigateWhenBrowserReadyAsync(…)
//   private void OpenUrlExternallyAfterBrowserFailure(…)
//   private void NotifyBrowserBlockedOffline(…)
//   private bool IsBrowserShowingKnownSite(…)
//   internal void SyncSiteRadiosToActiveMod(…)
//   internal async void BrowserSiteToggle_Click(…)
//   public bool NavigateToUrlInBrowser(…)
//   private async Task AutoPlayAndFullscreenVideoAsync(…)
//   internal void EndWebVideoTakeover(…)
//   private async Task AutoPlayBambiCloudPlaylistAsync(…)
//   private void OnBrowserWebMessageReceived(…)
//   private void HandleBrowserMediaMessage(…)
//   private void HandleAudioSyncVideoDetected(…)
//   private void HandleAudioSyncState(…)
//   private void HandleAudioSyncSeek(…)
//   private void HandleAudioSyncEnded(…)
//   private bool _hapticAudioSyncConnHooked
//   private void HookHapticAudioSyncRearm(…)
//   private void OnHapticConnectionChangedForAudioSync(…)
//   private void BtnDiscordTab_Click(…)
//   internal async void BtnDiscordTabLogin_Click(…)
//   private void UpdateDiscordTabUI(…)
//   internal void TxtProfileSearch_KeyDown(…)
//   internal void BtnProfileSearch_Click(…)
//   internal void BtnViewMyProfile_Click(…)
//   internal void BtnClearProfile_Click(…)
//   private void ClearProfileViewer(…)
//   private void ProfileDiscordHandle_Click(…)
//   internal void BtnProfileDiscord_Click(…)
//   internal async void BtnChangeDisplayName_Click(…)
//   internal async void BtnDeleteProfile_Click(…)
//   private bool SearchAndDisplayProfile(…)
//   private async Task RefreshAndSearchAsync(…)
//   private void DisplayOwnProfile(…)
//   private void DisplayProfileEntry(…)
//   private void ApplyProfileIdentityBadges(…)
//   private async Task RefreshProfileViewerAsync(…)
//   private void ResolveProfilePictureUnavailable(…)
//   private System.Windows.Media.Imaging.BitmapImage? LoadPatreonBadgeImage(…)
//   private void LoadProfileAchievementImages(…)
//   private string FormatNumber(…)
//   internal void BtnMuteBrowser_Click(…)
//   internal void SyncBrowserMuteIcon(…)
//   internal async void BtnPopOutBrowser_Click(…)
//   private void HandleBrowserFullscreenChanged(…)
//   private Window? _browserFsEscapeTarget
//   private EventHandler? _browserFsActivated
//   private EventHandler? _browserFsDeactivated
//   private System.Windows.Input.KeyEventHandler? _browserFsKeyDown
//   private void ArmFullscreenEscapes(…)
//   private void DisarmFullscreenEscapes(…)
//   internal void ExitBrowserFullscreenForTeardown(…)
//   public void EnterBrowserFullscreen(…)
//   private void ExitBrowserFullscreen(…)
//   private bool _remoteBrowserVideoActive
//   public void PlayHypnotubeFromRemote(…)
//   public void StopBrowserVideoFromRemote(…)
//   private void NavigateBrowserToCurrentSiteHome(…)
//   internal void BtnReloadBrowser_Click(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.Browser.cs; wired when they move to Core.
        private void BtnDiscordTab_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
