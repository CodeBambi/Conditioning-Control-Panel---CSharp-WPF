// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.ProfileBubble.cs (705 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (51):
//   private const int ProfileBubbleOpenDelayMs
//   private const int ProfileBubbleCloseGraceMs
//   private DispatcherTimer? _profileBubbleOpenTimer
//   private DispatcherTimer? _profileBubbleCloseTimer
//   private bool _profileBubbleWatchersOn
//   private string? _profileBubbleAvatarUrl
//   private ImageBrush? _profileBubblePhotoBrush
//   private DateTime _profileBubbleLastXpPulse
//   private DateTime _profileBubbleLastWobble
//   private DateTime _profileBubbleLastShimmer
//   private static readonly Color ProfileBubbleGold
//   private void InitializeProfileBubble(…)
//   private void CleanupProfileBubble(…)
//   private void RefreshProfileBubble(…)
//   private static readonly SolidColorBrush ProfileBubbleNeutralBrush
//   private static SolidColorBrush MakeFrozenBrush(…)
//   private async System.Threading.Tasks.Task LoadProfileBubblePhotoAsync(…)
//   private void RefreshProfileMenu(…)
//   private void ProfileBubble_MouseEnter(…)
//   private void ProfileBubble_MouseLeave(…)
//   private void ProfileBubblePopupRoot_MouseEnter(…)
//   private void ProfileBubblePopupRoot_MouseLeave(…)
//   private void OnProfileBubbleOpenTick(…)
//   private void OnProfileBubbleCloseTick(…)
//   private void OpenProfileBubbleMenu(…)
//   private void OnProfileBubblePopupClosed(…)
//   private CustomPopupPlacement[] PlaceProfileBubblePopup(…)
//   private void SubscribeProfileBubbleWatchers(…)
//   private void UnsubscribeProfileBubbleWatchers(…)
//   private void OnProfileBubbleHostDeactivated(…)
//   private void OnProfileBubbleHostStateChanged(…)
//   private void OnProfileBubbleWindowMouseDown(…)
//   private void BtnProfileBubble_Click(…)
//   private void ProfileMenuProfile_Click(…)
//   private void ProfileMenuPublicProfile_Click(…)
//   internal void OpenPublicProfilePage(…)
//   internal void RefreshProfileShareButton(…)
//   private void ProfileMenuAchievements_Click(…)
//   private void ProfileMenuSettings_Click(…)
//   private void ProfileMenuAccount_Click(…)
//   private void RunOnBubbleUi(…)
//   private void OnBubbleXPChanged(…)
//   private void OnBubbleLevelUp(…)
//   private void OnBubbleAchievementUnlocked(…)
//   private void OnBubbleFlashDisplayed(…)
//   private void OnBubbleSubliminalDisplayed(…)
//   private void OnBubbleAuthChanged(…)
//   private void PulseProfileBubble(…)
//   private void WobbleProfileBubble(…)
//   private void ShimmerProfileBubble(…)
//   private void FlashProfileBubbleGlow(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void BtnProfileBubble_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileBubblePopupRoot_MouseEnter(object? sender, global::Avalonia.Input.PointerEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileBubblePopupRoot_MouseLeave(object? sender, global::Avalonia.Input.PointerEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileBubble_MouseEnter(object? sender, global::Avalonia.Input.PointerEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileBubble_MouseLeave(object? sender, global::Avalonia.Input.PointerEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileMenuAccount_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileMenuAchievements_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileMenuProfile_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileMenuPublicProfile_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.ProfileBubble.cs; wired when they move to Core.
        private void ProfileMenuSettings_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
